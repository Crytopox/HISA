using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Text.Json;
using Hisa.Core.Abstractions;
using Hisa.Core.Models;

namespace Hisa.Esi.Clients;

public sealed class EveCharacterResolver(IHttpClientFactory factory, ISettingsService settings, IEsiMetricsStore metrics) : IEveCharacterResolver, IDisposable
{
    private readonly HttpClient http = factory.CreateClient(ClientName);
    public const string ClientName = "eve-character-identities";
    public sealed record CachedIdentity(EveCharacterIdentity Identity, DateTimeOffset Expires, string? ETag);
    private readonly ConcurrentDictionary<string,CachedIdentity> names = new();
    private readonly ConcurrentDictionary<string,(byte[]? Bytes,DateTimeOffset Expires)> portraits = new();
    private readonly ConcurrentDictionary<string,SemaphoreSlim> gates = new();
    private readonly SemaphoreSlim requests = new(4,4), persistence = new(1,1);
    private bool loaded;
    private long retryUntil;
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
    public async Task<byte[]?> GetPortraitAsync(string server,long characterId,CancellationToken cancellationToken = default)
    {
        if (characterId<=0 || server is not ("tranquility" or "singularity")) return null;
        var key=$"{server}:{characterId}:portrait"; var gate=gates.GetOrAdd(key,_=>new(1,1));await gate.WaitAsync(cancellationToken);
        try {
            if (portraits.TryGetValue(key,out var cached) && cached.Expires>DateTimeOffset.UtcNow) return cached.Bytes;
            if (DateTimeOffset.UtcNow.UtcTicks<Interlocked.Read(ref retryUntil)) return cached.Bytes;
            await requests.WaitAsync(cancellationToken);
            try {
                using var request=new HttpRequestMessage(HttpMethod.Get,$"https://images.evetech.net/characters/{characterId}/portrait?tenant={server}&size=64");
                using var response=await SendAsync(request,cancellationToken);response.EnsureSuccessStatusCode();
                var bytes=await ReadLimitedAsync(response,2*1024*1024,cancellationToken);
                if (response.Headers.CacheControl?.NoStore != true) portraits[key]=(bytes,Expiry(response,TimeSpan.FromDays(1)));
                return bytes;
            } catch(Exception ex) when(ex is HttpRequestException or IOException || ex is OperationCanceledException && !cancellationToken.IsCancellationRequested) {
                portraits[key]=(cached.Bytes,DateTimeOffset.UtcNow.AddMinutes(1));return cached.Bytes;
            }
            finally {requests.Release();}
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
            var delay=response.Headers.RetryAfter?.Delta ?? (response.Headers.RetryAfter?.Date-DateTimeOffset.UtcNow);
            var remain=Header(response,"X-ESI-Error-Limit-Remain");var reset=Header(response,"X-ESI-Error-Limit-Reset");
            if (remain==0) delay=TimeSpan.FromSeconds(Math.Max(1,reset??60));
            if ((int)response.StatusCode==429 && delay is null)delay=TimeSpan.FromMinutes(1);
            if(delay is { } wait && wait>TimeSpan.Zero) {
                var now=DateTimeOffset.UtcNow;var until=wait>=DateTimeOffset.MaxValue-now?DateTimeOffset.MaxValue.UtcTicks:now.Add(wait).UtcTicks;
                long old;do {old=Interlocked.Read(ref retryUntil);if(old>=until)break;}while(Interlocked.CompareExchange(ref retryUntil,until,old)!=old);
            }
            metrics.Add(new EsiRequestMetric {TimestampUtc=DateTimeOffset.UtcNow,Route=request.RequestUri?.ToString()??"characters",RateLimitGroup="characters.public",FromCache=false,StatusCode=(int)response.StatusCode,ErrorLimitRemain=remain,ErrorLimitResetSeconds=reset,RateLimitRemain=Header(response,"X-Ratelimit-Remaining"),RateLimitResetSeconds=Header(response,"X-Ratelimit-Reset"),ETag=response.Headers.ETag?.ToString(),Duration=watch.Elapsed,Message="character-identity"});
            return response;
    }
    public void Dispose() => http.Dispose();
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
