namespace AssetMap.Entities;

public class PortfolioSnapshot
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid? AccountId { get; set; }
    public decimal TotalValue { get; set; }
    public DateTime Timestamp { get; set; }

    public Account? Account { get; set; }
    public User User { get; set; } = null!;
}
