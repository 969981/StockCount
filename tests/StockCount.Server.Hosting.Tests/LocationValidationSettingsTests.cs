using StockCount.Server.Hosting;
using Xunit;

namespace StockCount.Server.Hosting.Tests;

public sealed class LocationValidationSettingsTests
{
    [Fact]
    public void ExistingJsonDefaultsOffAndIndependentFlagsPersist()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var store = new SettingsStore(root);
            File.WriteAllText(store.SettingsPath, "{\"sqlDatabase\":\"sagex3\"}");
            var settings = store.Load();
            Assert.False(settings.TestCheckSite);
            Assert.False(settings.TestCheckWarehouse);
            Assert.False(settings.ProductionCheckSite);
            Assert.False(settings.ProductionCheckWarehouse);
            settings.SchemaReviewed = true;
            settings.EnableTestWrites = true;
            settings.CopyLocationValidationFrom(new ServerSettings
            {
                TestCheckWarehouse = true, ProductionCheckSite = true
            });
            store.Save(settings);
            var saved = store.Load();
            Assert.False(saved.TestCheckSite);
            Assert.True(saved.TestCheckWarehouse);
            Assert.True(saved.ProductionCheckSite);
            Assert.False(saved.ProductionCheckWarehouse);
            Assert.True(saved.SchemaReviewed);
            Assert.True(saved.EnableTestWrites);
            Assert.False(saved.EnableProductionWrites);
        }
        finally { Directory.Delete(root, true); }
    }
}
