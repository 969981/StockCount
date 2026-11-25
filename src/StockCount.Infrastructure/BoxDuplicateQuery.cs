using System.Security.Cryptography;
using System.Text;
using StockCount.Core;

namespace StockCount.Infrastructure;

/// <summary>
/// One box number may appear only once in the selected ERP schema's XCOUTD,
/// irrespective of count, site, warehouse, status or date. SQL equality follows
/// the actual BOXNUM_0 collation; the transactional range check is authoritative.
/// </summary>
public static class BoxDuplicateQuery
{
    public static string LabelProjection(SelectedEnvironment environment) => $"""
        CAST(CASE WHEN EXISTS (
            SELECT 1 FROM {EnvironmentSelector.TablePrefix(environment)}.[XCOUTD] D
            WHERE D.BOXNUM_0=L.PID_0
        ) THEN 1 ELSE 0 END AS bit) AS AlreadyRecordedInCount
        """;

    public static string LockedExists(SelectedEnvironment environment) => $"""
        SELECT CAST(CASE WHEN EXISTS (
            SELECT 1 FROM {EnvironmentSelector.TablePrefix(environment)}.[XCOUTD] D WITH (UPDLOCK,HOLDLOCK)
            WHERE D.BOXNUM_0=@BoxNumber
        ) THEN 1 ELSE 0 END AS bit);
        """;

    // No count number in the lock identity. The schema is validated against
    // the allowlist and separates XTEST from COB. The same canonical PID is
    // required by the write repository before any insert is permitted.
    public static string LockResource(SelectedEnvironment environment, string boxNumber)
    {
        var schema = EnvironmentSelector.TablePrefix(environment);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(schema + "\u001F" + boxNumber)));
        return "StockCount:Box:" + hash;
    }
}
