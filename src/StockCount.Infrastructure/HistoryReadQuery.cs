using StockCount.Core;

namespace StockCount.Infrastructure;

/// <summary>Read-only history SQL; all user inputs are bound values. ROWID is numeric(38,0).</summary>
public static class HistoryReadQuery
{
    public static string Summary(SelectedEnvironment environment) => $"""
        SELECT CONVERT(varchar(40), MAX(D.ROWID)) AS UpperRowId,
               COUNT_BIG(*) AS TotalCount,
               COUNT_BIG(CASE WHEN (@BoxSearch IS NULL OR CHARINDEX(@BoxSearch,D.BOXNUM_0)>0)
                         AND (@Location IS NULL OR D.LOC_0=@Location) THEN 1 END) AS FilteredCount
        FROM {EnvironmentSelector.TablePrefix(environment)}.[XCOUTD] D
        WHERE D.CUNNUM_0=@CountNumber;
        """;

    public static string Page(SelectedEnvironment environment, bool hasBefore, bool withReceipts = false) => $"""
        SELECT TOP (@Take) CONVERT(varchar(40),D.ROWID) AS DetailId,
               D.CUNNUM_0 AS CountNumber,D.BOXNUM_0 AS BoxNumber,
               D.LOC_0 AS LocationCode,D.ITMREF_0 AS ItemCode,
               D.LOT_0 AS LotNumber,D.PCU_0 AS PackageUnit,
               D.QTYPCU_0 AS QuantityPcu,D.QTYSTU_0 AS QuantityStu,
               D.CREDATTIM_0 AS CreatedAt,D.UPDDATTIM_0 AS UpdatedAt
               {(withReceipts ? ", R.OldQtyStu AS LastCorrectionOldQuantity,R.SavedQtyStu AS LastCorrectionNewQuantity,R.CommittedAt AS LastCorrectionAtUtc" : "")}
        FROM {EnvironmentSelector.TablePrefix(environment)}.[XCOUTD] D
        {(withReceipts ? $"OUTER APPLY (SELECT TOP (1) OldQtyStu,SavedQtyStu,CommittedAt FROM {EnvironmentSelector.TablePrefix(environment)}.[StockCountOperationReceipt] WHERE CountNumber=D.CUNNUM_0 AND DetailId=CONVERT(nvarchar(40),D.ROWID) AND Operation=N'Update' ORDER BY ReceiptSequence DESC) R" : "")}
        WHERE D.CUNNUM_0=@CountNumber
          AND D.ROWID<=CONVERT(numeric(38,0),@UpperRowId)
          {(hasBefore ? "AND D.ROWID<CONVERT(numeric(38,0),@BeforeRowId)" : "")}
          AND (@BoxSearch IS NULL OR CHARINDEX(@BoxSearch,D.BOXNUM_0)>0)
          AND (@Location IS NULL OR D.LOC_0=@Location)
        ORDER BY D.ROWID DESC;
        """;
}
