using Hisa.Core.Models;
using Hisa.Services.Probes;

namespace Hisa.Services.Tests;

public sealed class ProbeFormationLibraryTests
{
    [Fact]
    public async Task LibraryAndJsonPreserveDoublePrecisionAndCustomRadii()
    {
        var service = new ProbeFormationLibrary(new ProbeMemorySettings());
        var f = ProbeFormationMath.Create("雪") with { Probes = [new(new(.123456789123456, 149597870700.125, -5), 1234.56789)] };
        await service.SaveAsync(new(1, "meters", [f]));
        var loaded = await service.LoadAsync(); Assert.Equal(f.Probes, loaded.Formations[0].Probes);
        var imported = service.Import(service.Export([f]), new(1, "meters", []), FormationImportAction.RenamedCopy);
        Assert.Equal(f.Probes, imported.Formations[0].Probes); Assert.NotEqual(f.Id, imported.Formations[0].Id);
    }
    [Fact]
    public void ImportValidatesWholeFileAndResolvesNamesWithoutMutatingCurrentLibrary()
    {
        var service = new ProbeFormationLibrary(new ProbeMemorySettings()); var f = ProbeFormationMath.Create(); var current = new FormationLibrary(1, "meters", [f]);
        var json = service.Export([f]);
        Assert.Single(service.Import(json, current, FormationImportAction.Skip).Formations);
        Assert.Equal(f.Id, Assert.Single(service.Import(json, current, FormationImportAction.Replace).Formations).Id);
        Assert.Equal(2, service.Import(json, current, FormationImportAction.RenamedCopy).Formations.Count);
        Assert.Throws<ArgumentException>(() => service.Import(json.Replace("\"meters\"", "\"AU\""), current, FormationImportAction.Replace));
        Assert.Single(current.Formations);
    }
}
