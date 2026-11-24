using System.Text.Json;
using StockCount.Server.Hosting;
using Xunit;

namespace StockCount.Server.Hosting.Tests;

public sealed class SettingsStoreTests
{
    [Fact]
    public void SavesCamelCaseJsonBesideDesktopExeAndReadsItAgain()
    {
        var dir = Path.Combine(Path.GetTempPath(), "stockcount-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var store = new SettingsStore(dir);
            store.Save(new ServerSettings { ApiPort = 5140, SqlServer = "localhost" });
            Assert.True(File.Exists(Path.Combine(dir, "server-settings.json")));
            var json = File.ReadAllText(store.SettingsPath);
            Assert.Contains("\"apiPort\": 5140", json);
            Assert.DoesNotContain("sqlPassword", json, StringComparison.OrdinalIgnoreCase);
            Assert.Equal("localhost", store.Load().SqlServer);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void DefaultsToApiAutostartButNeverToUnreviewedErpWrites()
    {
        var settings = new ServerSettings();
        Assert.True(settings.AutoStartApi);
        Assert.Equal("192.168.1.1,1433", settings.SqlServer);
        Assert.Equal("PDA", settings.EffectiveX3AuditUser);
        Assert.False(settings.SchemaReviewed);
        Assert.False(settings.EnableTestWrites);
        Assert.False(settings.ProductionSchemaReviewed);
        Assert.False(settings.EnableProductionWrites);
    }

    [Fact]
    public void NewWriteDraftPreselectsFourWithoutChangingEffectiveSettings()
    {
        var saved = new ServerSettings();
        var draft = saved.CreateWriteDraft();
        Assert.True(draft.SchemaReviewed && draft.EnableTestWrites &&
                    draft.ProductionSchemaReviewed && draft.EnableProductionWrites);
        Assert.False(saved.SchemaReviewed || saved.EnableTestWrites ||
                     saved.ProductionSchemaReviewed || saved.EnableProductionWrites);
        draft.Validate();
        Assert.Equal("PDA", draft.EffectiveX3AuditUser);
        var connection = saved.Copy();
        connection.AllowLan = true;
        connection.AllowKeylessLan = true;
        connection.Validate();
        Assert.False(connection.EnableProductionWrites);
    }

    [Theory]
    [InlineData("", "PDA")]
    [InlineData("   ", "PDA")]
    [InlineData("USR1", "USR1")]
    public void OptionalAuditCodeResolvesWithoutBlockingWriteSettings(string value, string expected)
    {
        var settings = new ServerSettings
        {
            SchemaReviewed = true, EnableTestWrites = true,
            ProductionSchemaReviewed = true, EnableProductionWrites = true,
            X3AuditUser = value
        };
        settings.Validate();
        Assert.Equal(expected, settings.EffectiveX3AuditUser);
    }

    [Fact]
    public void InvalidCustomAuditCodeStillRejected()
    {
        var settings = new ServerSettings
        {
            SchemaReviewed = true, EnableTestWrites = true, X3AuditUser = "TOOLONG"
        };
        Assert.Throws<InvalidOperationException>(() => settings.Validate());
    }

    [Fact]
    public void SchemaApprovalIsNotInheritedAfterDatabaseIdentityChange()
    {
        var previous = new ServerSettings { SqlServer = "SERVER-A", SchemaReviewed = true, ProductionSchemaReviewed = true };
        var next = new ServerSettings { SqlServer = "SERVER-B", SchemaReviewed = true, ProductionSchemaReviewed = true,
            EnableTestWrites = true, EnableProductionWrites = true };
        Assert.True(next.DatabaseIdentityChangedFrom(previous, newPassword: false));
        next.RevokeWriteApprovals();
        Assert.False(next.EnableTestWrites);
        Assert.False(next.EnableProductionWrites);
        Assert.False(next.SchemaReviewed);
        Assert.False(next.ProductionSchemaReviewed);
    }

    [Fact]
    public void WriteRequiresIndependentSchemaReview()
    {
        var settings = new ServerSettings { EnableProductionWrites = true, X3AuditUser = "PDA" };
        Assert.Throws<InvalidOperationException>(() => settings.Validate());
    }

    [Fact]
    public void ReadingMalformedLocalJsonIsAnErrorNotSilentDefaults()
    {
        var dir = Path.Combine(Path.GetTempPath(), "stockcount-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var store = new SettingsStore(dir);
            File.WriteAllText(store.SettingsPath, "{ invalid");
            Assert.Throws<JsonException>(() => store.Load());
        }
        finally { Directory.Delete(dir, recursive: true); }
    }
}
