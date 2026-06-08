using AssetMap.Core.Models;
using AssetMap.Entities;

namespace AssetMap.Core.Services;

public interface IAccountService
{
    Task<List<AccountFullDto>> GetAllAsync(Guid userId, CancellationToken ct = default);
    Task<AccountFullDto?>      GetByIdAsync(Guid accountId, CancellationToken ct = default);
    Task<AccountFullDto>       CreateAsync(Guid userId, CreateAccountRequest req, CancellationToken ct = default);
    Task<bool>                 UpdateAsync(Guid accountId, UpdateAccountRequest req, CancellationToken ct = default);
    Task<bool>                 DeleteAsync(Guid accountId, CancellationToken ct = default);
}
