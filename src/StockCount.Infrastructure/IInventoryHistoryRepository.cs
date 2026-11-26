using StockCount.Contracts;
using StockCount.Core;

namespace StockCount.Infrastructure;

public interface IInventoryHistoryRepository
{
    Task<CountHistoryPage?> GetHistoryAsync(SelectedEnvironment environment,
        HistoryQuery query, HistoryCursor? cursor, int size, CancellationToken cancellationToken);
}
