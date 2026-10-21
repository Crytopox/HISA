using Hisa.Core.Models;
using Hisa.Services.Probes;

namespace Hisa.Services.Tests;

public sealed class EveSettingsIdentityTests
{
    [Fact]
    public async Task DiscoveryUsesSiblingFilenamesWithoutDecodingAndGroupsProfilesByServer()
    {
        var root=Path.Combine(Path.GetTempPath(),"hisa-identity-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        try{
            var a=Path.Combine(root,"settings_A");var b=Path.Combine(root,"settings_B");Directory.CreateDirectory(a);Directory.CreateDirectory(b);
            foreach(var dir in new[]{a,b}){await File.WriteAllBytesAsync(Path.Combine(dir,"core_user_123.dat"),[0]);await File.WriteAllBytesAsync(Path.Combine(dir,"core_char_90000001.dat"),[255]);await File.WriteAllBytesAsync(Path.Combine(dir,"core_char_bad.dat"),[255]);}
            var accounts=new[]{new EveSettingsAccount(Path.Combine(a,"core_user_123.dat"),"tq_tranquility","A",123),new(Path.Combine(b,"core_user_123.dat"),"tq_tranquility","B",123)};
            var service=new EveSettingsIdentityService(new ProbeMemorySettings());var found=await service.DiscoverCharactersAsync(accounts);
            Assert.Equal(2,found.Count);Assert.All(found,c=>Assert.Equal(90000001,c.CharacterId));Assert.All(found,c=>Assert.Equal("tranquility",c.Server));
            Assert.Equal(2,Assert.Single(service.GroupAccounts(accounts)).Files.Count);
            Assert.Equal("tranquility|123",EveSettingsIdentity.GroupKey(accounts[0]));
            Assert.Equal(2,service.GroupAccounts([accounts[0],accounts[1] with{Installation="sisi_singularity"}]).Count);
            var unknown=accounts.Select((x,i)=>x with{Installation="Custom",Path=Path.Combine(root,"install"+i,"profile","core_user_123.dat")}).ToArray();Assert.Equal(2,service.GroupAccounts(unknown).Count);
            Assert.Equal(new byte[]{255},await File.ReadAllBytesAsync(found[0].Path));
        }finally{Directory.Delete(root,true);}
    }
    [Fact]
    public async Task LinksPersistPerGroupAndServerAndPreserveExistingAliases()
    {
        var settings=new ProbeMemorySettings();await settings.SetAsync("probes.discovery.v1",new EveDiscoveryPreferences(["custom"],new Dictionary<string,string>{{"file","My account"}}));
        var service=new EveSettingsIdentityService(settings);
        await service.SaveLinksAsync("tranquility|123","tranquility",[90000001,90000001]);await service.SaveLinksAsync("singularity|123","singularity",[90000001]);
        Assert.Equal(2,(await new EveSettingsIdentityService(settings).LoadLinksAsync()).Count);
        await service.SaveLinksAsync("tranquility|123","tranquility",[]);var remaining=Assert.Single(await service.LoadLinksAsync());Assert.Equal("singularity",remaining.Server);
        Assert.Equal("My account",(await settings.GetAsync<EveDiscoveryPreferences>("probes.discovery.v1"))!.Aliases["file"]);
    }
}
