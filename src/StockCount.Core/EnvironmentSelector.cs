namespace StockCount.Core;

/// <summary>Only these two server-owned schema identifiers may appear in SQL.</summary>
public readonly record struct SelectedEnvironment(string Name, string Schema);

public static class EnvironmentSelector
{
    public static bool TryResolve(string? value, out SelectedEnvironment environment)
    {
        if (string.Equals(value, "Test", StringComparison.OrdinalIgnoreCase))
        {
            environment = new SelectedEnvironment("Test", "XTEST");
            return true;
        }
        if (string.Equals(value, "Production", StringComparison.OrdinalIgnoreCase))
        {
            environment = new SelectedEnvironment("Production", "COB");
            return true;
        }

        environment = default;
        return false;
    }

    public static string TablePrefix(SelectedEnvironment environment) =>
        environment.Schema switch
        {
            "XTEST" => "[XTEST]",
            "COB" => "[COB]",
            _ => throw new InvalidOperationException("Schema not allowed")
        };
}
