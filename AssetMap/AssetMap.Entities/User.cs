using AssetMap.Entities.Enums;

namespace AssetMap.Entities;

public class User
{
    public Guid Id { get; set; }
    public string Username { get; set; } = null!;
    public string PasswordHash { get; set; } = null!;
    public string BaseCurrency { get; set; } = "USD";
    public UserRole Role { get; set; }
    public DateTime CreatedAt { get; set; }

    public ICollection<Account> Accounts { get; set; } = [];
    public ICollection<ImportBatch> ImportBatches { get; set; } = [];
}
