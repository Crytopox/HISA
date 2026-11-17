using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Text.Json;
using Hisa.Core.Abstractions;
using Hisa.Core.Models;
using Microsoft.Extensions.Logging;

namespace Hisa.Esi.Clients;

public sealed class EveCharacterResolver(IHttpClientFactory factory, ISettingsService settings,
    IEsiMetricsStore metrics, ILogger<EveCharacterResolver>? logger = null) : IEveCharacterResolver, IDisposable
{
    private readonly HttpClient http = factory.CreateClient(ClientName);
    private readonly HttpClient portraitHttp = factory.CreateClient(PortraitClientName);
    public const string ClientName = "eve-character-identities";
    public const string PortraitClientName = "eve-character-portraits";
    public sealed record CachedIdentity(EveCharacterIdentity Identity, DateTimeOffset Expires, string? ETag);
    private readonly ConcurrentDictionary<string,CachedIdentity> names = new();
    private readonly ConcurrentDictionary<string,(byte[]? Bytes,DateTimeOffset Expires)> portraits = new();
    private readonly ConcurrentDictionary<string,SemaphoreSlim> gates = new();
    private readonly SemaphoreSlim requests = new(4,4), persistence = new(1,1);
    private readonly SemaphoreSlim portraitRequests = new(4, 4);
    private bool loaded;
    private long retryUntil;
    private long portraitRetryUntil;

    public DateTimeOffset NameLookupRetryNotBefore => new(Interlocked.Read(ref retryUntil), TimeSpan.Zero);
    public DateTimeOffset PortraitRetryNotBefore => new(Interlocked.Read(ref portraitRetryUntil), TimeSpan.Zero);
    public async Task<EveCharacterIdentity> ResolveAsync(string server, long characterId, CancellationToken cancellationToken = default)
    {
        if (characterId <= 0) throw new ArgumentException("Character ID must be positive.");
        server = server.ToLowerInvariant();
        if (server != "tranquility") return new(server,characterId,null,$"Public name lookup unavailable for {server}");
        await LoadAsync(cancellationToken);
        var key = $"{server}:{characterId}"; var gate = gates.GetOrAdd(key,_ => new(1,1));
        await gate.WaitAsync(cancellationToken);
        try {
            names.TryGetValue(key,out var cached);
            if (cached is not null && cached.Expires > DateTimeOffset.UtcNow) return cached.Identity with {FromCache=true};
            if (DateTimeOffset.UtcNow.UtcTicks < Interlocked.Read(ref retryUntil)) return Fallback(cached,server,characterId,"ESI cooling down; retry later");
            await requests.WaitAsync(cancellationToken);
            try {
                using var request = new HttpRequestMessage(HttpMethod.Get,$"characters/{characterId}/");
                if (cached?.ETag is { } tag) request.Headers.TryAddWithoutValidation("If-None-Match",tag);
                using var response = await SendAsync(request,cancellationToken);
                var expires = Expiry(response,TimeSpan.FromDays(1));
                EveCharacterIdentity identity;
                if (response.StatusCode == HttpStatusCode.NotModified && cached is not null) identity = cached.Identity with {Status="Resolved",FromCache=false};
                else if (response.StatusCode == HttpStatusCode.NotFound) { identity = new(server,characterId,null,"Character not found"); expires=DateTimeOffset.UtcNow.AddMinutes(5); }
                else {
                    response.EnsureSuccessStatusCode();
                    using var json = JsonDocument.Parse(await ReadLimitedAsync(response,1024*1024,cancellationToken));
                    var name = json.RootElement.GetProperty("name").GetString();
                    if (string.IsNullOrWhiteSpace(name) || name.Length>256) throw new InvalidDataException("Invalid character name response.");
                    identity = new(server,characterId,name,"Resolved");
                }
                var entry = new CachedIdentity(identity,expires,response.Headers.ETag?.ToString() ?? cached?.ETag);
                if (response.Headers.CacheControl?.NoStore != true) { names[key]=entry; await PersistAsync(cancellationToken); }
                return identity;
            } catch (Exception ex) when (ex is HttpRequestException or JsonException or IOException or KeyNotFoundException || ex is OperationCanceledException && !cancellationToken.IsCancellationRequested) {
                var identity = Fallback(cached,server,characterId,cached?.Identity.Name is null?"Name unavailable (offline or ESI error)":"Name unavailable; cached data retained");
                names[key] = new(identity,DateTimeOffset.UtcNow.AddMinutes(1),cached?.ETag); return identity;
            }
            finally {requests.Release();}
        } finally {gate.Release();}
    }
    public async Task<long?> ResolveIdByNameAsync(string characterName, CancellationToken cancellationToken = default)
    {
        var name = characterName?.Trim() ?? "";
        if (name.Length == 0) return null;
        await LoadAsync(cancellationToken);
        foreach (var entry in names.Values)
        {
            if (entry.Identity.Name is { } cached &&
                cached.Equals(name, StringComparison.OrdinalIgnoreCase) &&
                entry.Identity.CharacterId > 0)
                return entry.Identity.CharacterId;
        }
        if (DateTimeOffset.UtcNow.UtcTicks < Interlocked.Read(ref retryUntil)) return null;
        await requests.WaitAsync(cancellationToken);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post,
                "https://esi.evetech.net/universe/ids/");
            request.Content = new StringContent(JsonSerializer.Serialize(new[] { name }), System.Text.Encoding.UTF8, "application/json");
            using var response = await SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode) return null;
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            if (!json.RootElement.TryGetProperty("characters", out var characters)) return null;
            foreach (var item in characters.EnumerateArray())
            {
                if (!item.TryGetProperty("id", out var idProp) || !item.TryGetProperty("name", out var nameProp))
                    continue;
                var id = idProp.GetInt64();
                var resolvedName = nameProp.GetString();
                if (id <= 0 || string.IsNullOrWhiteSpace(resolvedName)) continue;
                if (!resolvedName.Equals(name, StringComparison.OrdinalIgnoreCase)) continue;
                var key = $"tranquility:{id}";
                names[key] = new(new("tranquility", id, resolvedName, "Resolved"), DateTimeOffset.UtcNow.AddDays(1), null);
                await PersistAsync(cancellationToken);
                return id;
            }
            return null;
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or IOException ||
            ex is OperationCanceledException && !cancellationToken.IsCancellationRequested)
        {
            return null;
        }
        finally { requests.Release(); }
    }

    public async Task<byte[]?> GetPortraitAsync(string server,long characterId,CancellationToken cancellationToken = default)
    {
        if (characterId<=0 || server is not ("tranquility" or "singularity")) return null;
        var key=$"{server}:{characterId}:portrait"; var gate=gates.GetOrAdd(key,_=>new(1,1));await gate.WaitAsync(cancellationToken);
        try {
            if (portraits.TryGetValue(key,out var cached) && cached.Expires>DateTimeOffset.UtcNow) return cached.Bytes;
            if (DateTimeOffset.UtcNow < PortraitRetryNotBefore) return cached.Bytes;
            await portraitRequests.WaitAsync(cancellationToken);
            try {
                if (DateTimeOffset.UtcNow < PortraitRetryNotBefore) return cached.Bytes;
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                deadline.CancelAfter(portraitHttp.Timeout);
                using var request=new HttpRequestMessage(HttpMethod.Get,$"https://images.evetech.net/characters/{characterId}/portrait?tenant={server}&size=64");
                using var response = await portraitHttp.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
                UpdateCooldown(ref portraitRetryUntil, GetRetryDelay(response));
                response.EnsureSuccessStatusCode();
                var bytes=await ReadLimitedAsync(response,2*1024*1024,deadline.Token);
                if (response.Headers.CacheControl?.NoStore != true) portraits[key]=(bytes,Expiry(response,TimeSpan.FromDays(1)));
                return bytes;
            } catch(Exception ex) when(ex is HttpRequestException or IOException || ex is OperationCanceledException && !cancellationToken.IsCancellationRequested) {
                logger?.LogDebug(ex, "Image server portrait request failed for character {CharacterId}", characterId);
                portraits[key]=(cached.Bytes,DateTimeOffset.UtcNow.AddMinutes(1));return cached.Bytes;
            }
            finally {portraitRequests.Release();}
        } finally {gate.Release();}
    }
    private static EveCharacterIdentity Fallback(CachedIdentity? cached,string server,long id,string status) => cached is null ? new(server,id,null,status) : cached.Identity with {Status=status,FromCache=true};
    private async Task LoadAsync(CancellationToken ct)
    {
        await persistence.WaitAsync(ct);
        try {if (!loaded) {var saved=await settings.GetAsync<Dictionary<string,CachedIdentity>>("eve.character-identities.v1",ct);if(saved is not null)foreach(var pair in saved)names.TryAdd(pair.Key,pair.Value);loaded=true;}}
        finally {persistence.Release();}
    }
    private async Task PersistAsync(CancellationToken ct)
    {
        await persistence.WaitAsync(ct);try {await settings.SetAsync("eve.character-identities.v1",names.ToDictionary(p=>p.Key,p=>p.Value),ct);}finally{persistence.Release();}
    }
    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
    {
        var watch=Stopwatch.StartNew();
            // Recheck after waiting for another request, so a 429 stops queued lookups too.
            if(DateTimeOffset.UtcNow.UtcTicks<Interlocked.Read(ref retryUntil))throw new HttpRequestException("ESI cooldown is active.");
            var response=await http.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,ct);
            var delay = GetRetryDelay(response);
            var remain=Header(response,"X-ESI-Error-Limit-Remain");var reset=Header(response,"X-ESI-Error-Limit-Reset");
            if (remain==0) delay=TimeSpan.FromSeconds(Math.Max(1,reset??60));
            UpdateCooldown(ref retryUntil, delay);
            metrics.Add(new EsiRequestMetric {TimestampUtc=DateTimeOffset.UtcNow,Route=request.RequestUri?.ToString()??"characters",RateLimitGroup="characters.public",FromCache=false,StatusCode=(int)response.StatusCode,ErrorLimitRemain=remain,ErrorLimitResetSeconds=reset,RateLimitRemain=Header(response,"X-Ratelimit-Remaining"),RateLimitResetSeconds=Header(response,"X-Ratelimit-Reset"),ETag=response.Headers.ETag?.ToString(),Duration=watch.Elapsed,Message="character-identity"});
            return response;
    }
    private static TimeSpan? GetRetryDelay(HttpResponseMessage response)
    {
        var header = response.Headers.RetryAfter;
        var delay = header?.Delta ?? (header?.Date - DateTimeOffset.UtcNow);
        return delay ?? (response.StatusCode == HttpStatusCode.TooManyRequests ? TimeSpan.FromMinutes(1) : null);
    }

    private static void UpdateCooldown(ref long cooldown, TimeSpan? delay)
    {
        if (delay is not { } wait || wait <= TimeSpan.Zero) return;
        var now = DateTimeOffset.UtcNow;
        var until = wait >= DateTimeOffset.MaxValue - now ? DateTimeOffset.MaxValue.UtcTicks : now.Add(wait).UtcTicks;
        long previous;
        do
        {
            previous = Interlocked.Read(ref cooldown);
            if (previous >= until) return;
        } while (Interlocked.CompareExchange(ref cooldown, until, previous) != previous);
    }

    public void Dispose()
    {
        http.Dispose();
        portraitHttp.Dispose();
    }
    private static int? Header(HttpResponseMessage response,string key)=>response.Headers.TryGetValues(key,out var values)&&int.TryParse(values.FirstOrDefault(),out var n)?n:null;
    private static DateTimeOffset Expiry(HttpResponseMessage response,TimeSpan fallback)
    {
        if(response.Headers.CacheControl?.NoCache==true)return DateTimeOffset.UtcNow;
        var age=response.Headers.Age??TimeSpan.Zero;
        var duration=response.Headers.CacheControl?.MaxAge-age;
        return duration is { } d ? DateTimeOffset.UtcNow.Add(d<TimeSpan.Zero?TimeSpan.Zero:d) : response.Content.Headers.Expires??DateTimeOffset.UtcNow.Add(fallback);
    }
    private static async Task<byte[]> ReadLimitedAsync(HttpResponseMessage response,int limit,CancellationToken ct)
    {
        if(response.Content.Headers.ContentLength>limit)throw new IOException("Identity response is too large.");
        await using var input=await response.Content.ReadAsStreamAsync(ct);using var output=new MemoryStream();var buffer=new byte[8192];
        int count;while((count=await input.ReadAsync(buffer,ct))>0){if(output.Length+count>limit)throw new IOException("Identity response is too large.");output.Write(buffer,0,count);}return output.ToArray();
    }
}
