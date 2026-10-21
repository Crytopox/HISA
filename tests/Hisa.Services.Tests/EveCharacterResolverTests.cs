using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using Hisa.Esi.Clients;
using Hisa.Esi.Telemetry;

namespace Hisa.Services.Tests;

public sealed class EveCharacterResolverTests
{
    private sealed class Handler(Func<HttpRequestMessage,CancellationToken,Task<HttpResponseMessage>> send):HttpMessageHandler
    {
        public int Calls;public readonly ConcurrentBag<string> Urls=[];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct){Interlocked.Increment(ref Calls);Urls.Add(request.RequestUri!.ToString());return await send(request,ct);}
    }
    private sealed class Factory(Handler handler):IHttpClientFactory
    {public HttpClient CreateClient(string name)=>new(handler,false){BaseAddress=new("https://esi.evetech.net/latest/"),Timeout=TimeSpan.FromSeconds(2)};}
    private static HttpResponseMessage Name(string name="Pilot",int maxAge=3600)
    {var r=new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent("{\"name\":\""+name+"\"}")};r.Headers.CacheControl=new(){MaxAge=TimeSpan.FromSeconds(maxAge)};r.Headers.ETag=new("\"test\"");return r;}
    [Fact]
    public async Task NamesDeduplicatePersistAndKeepUnsupportedServersSeparate()
    {
        using var handler=new Handler(async(_,ct)=>{await Task.Delay(30,ct);return Name();});var settings=new ProbeMemorySettings();var metrics=new EsiMetricsStore();
        using var resolver=new EveCharacterResolver(new Factory(handler),settings,metrics);
        var names=await Task.WhenAll(Enumerable.Range(0,10).Select(_=>resolver.ResolveAsync("tranquility",90000001)));
        Assert.All(names,n=>Assert.Equal("Pilot",n.Name));Assert.Equal(1,handler.Calls);Assert.Single(metrics.Snapshot());
        using var reloaded=new EveCharacterResolver(new Factory(handler),settings,metrics);Assert.Equal("Pilot",(await reloaded.ResolveAsync("tranquility",90000001)).Name);Assert.Equal(1,handler.Calls);
        Assert.Null((await resolver.ResolveAsync("singularity",90000001)).Name);Assert.Null((await resolver.ResolveAsync("unknown",90000001)).Name);Assert.Equal(1,handler.Calls);
    }
    [Fact]
    public async Task CacheRevalidationAndOfflineFallbackPreserveNames()
    {
        var call=0;using var handler=new Handler((request,_)=>{
            call++;if(call==1)return Task.FromResult(Name(maxAge:0));
            Assert.Contains("\"test\"",request.Headers.IfNoneMatch.Select(t=>t.ToString()));
            if(call==2){var r=new HttpResponseMessage(HttpStatusCode.NotModified);r.Headers.CacheControl=new(){MaxAge=TimeSpan.Zero};return Task.FromResult(r);}
            throw new HttpRequestException("offline");
        });
        using var resolver=new EveCharacterResolver(new Factory(handler),new ProbeMemorySettings(),new EsiMetricsStore());
        Assert.Equal("Pilot",(await resolver.ResolveAsync("tranquility",1)).Name);Assert.Equal("Pilot",(await resolver.ResolveAsync("tranquility",1)).Name);
        var offline=await resolver.ResolveAsync("tranquility",1);Assert.Equal("Pilot",offline.Name);Assert.True(offline.FromCache);Assert.Equal(3,handler.Calls);
    }
    [Fact]
    public async Task ThrottlingStopsFurtherRequestsAndMissingCharactersAndPortraitsRemainUsable()
    {
        using var handler=new Handler((request,_)=>{
            if(request.RequestUri!.AbsolutePath.Contains("90000001"))return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            var r=new HttpResponseMessage((HttpStatusCode)429);r.Headers.RetryAfter=new RetryConditionHeaderValue(TimeSpan.FromMinutes(5));return Task.FromResult(r);
        });
        using var resolver=new EveCharacterResolver(new Factory(handler),new ProbeMemorySettings(),new EsiMetricsStore());
        var missing=await resolver.ResolveAsync("tranquility",90000001);Assert.Null(missing.Name);Assert.Contains("not found",missing.Status);
        await resolver.ResolveAsync("tranquility",90000002);await resolver.ResolveAsync("tranquility",90000003);Assert.Equal(2,handler.Calls);
        Assert.Null(await resolver.GetPortraitAsync("tranquility",90000002));Assert.Equal(2,handler.Calls);
    }
    [Fact]
    public async Task ConcurrencyCancellationAndPortraitTenantAreControlled()
    {
        var active=0;var max=0;using var handler=new Handler(async(request,ct)=>{
            var n=Interlocked.Increment(ref active);lock(typeof(EveCharacterResolverTests)){max=Math.Max(max,n);}try{await Task.Delay(30,ct);return request.RequestUri!.Host=="images.evetech.net"?new(HttpStatusCode.OK){Content=new ByteArrayContent([1,2,3])}:Name();}finally{Interlocked.Decrement(ref active);}
        });
        using var resolver=new EveCharacterResolver(new Factory(handler),new ProbeMemorySettings(),new EsiMetricsStore());
        await Task.WhenAll(Enumerable.Range(1,16).Select(i=>resolver.ResolveAsync("tranquility",i)));Assert.InRange(max,1,4);
        Assert.Equal(new byte[]{1,2,3},await resolver.GetPortraitAsync("singularity",90000001));Assert.Contains(handler.Urls,u=>u.Contains("tenant=singularity"));
        using var cts=new CancellationTokenSource();cts.Cancel();await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>resolver.ResolveAsync("tranquility",999,cts.Token));
    }
}
