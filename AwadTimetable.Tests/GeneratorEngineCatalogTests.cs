using AwadTimetable.Core.Enums;
using AwadTimetable.Core.Services;

namespace AwadTimetable.Tests;

public class GeneratorEngineCatalogTests
{
    [Fact]
    public void Catalog_OffersThreeEngines()
    {
        var engines = GeneratorEngineCatalog.All.Select(e => e.Engine).ToList();

        Assert.Equal(3, engines.Count);
        Assert.Contains(GeneratorEngine.CpSat, engines);
        Assert.Contains(GeneratorEngine.Fet, engines);
        Assert.Contains(GeneratorEngine.Asc, engines);
    }

    [Fact]
    public void Catalog_DefaultEngine_IsCpSat_AndAvailable()
    {
        var defaultInfo = GeneratorEngineCatalog.Get(GeneratorEngine.CpSat);

        Assert.True(defaultInfo.IsAvailable);
        Assert.Contains("CP-SAT", defaultInfo.DisplayName);
    }

    [Fact]
    public void Catalog_EveryEngineHasNameAndDescription()
    {
        foreach (var info in GeneratorEngineCatalog.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(info.DisplayName));
            Assert.False(string.IsNullOrWhiteSpace(info.Description));
        }
    }
}
