using AssetMap.Core.Models;

namespace AssetMap.Core.Services;

public interface ITransactionService
{
    Task<TransactionDto> AddAsync(CreateTransactionRequest req, CancellationToken ct = default);
    Task<List<TransactionDto>> GetForAccountAsync(Guid accountId, int limit = 50, CancellationToken ct = default);
}
