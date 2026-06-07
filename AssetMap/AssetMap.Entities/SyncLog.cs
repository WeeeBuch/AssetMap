using AssetMap.Entities.Enums;

namespace AssetMap.Entities;

public class SyncLog
{
    public Guid Id { get; set; }
    public DateTime Timestamp { get; set; }
    public SyncLogType Type { get; set; }
    public Guid? TargetId { get; set; }
    public SyncLogStatus Status { get; set; }
    public string? Message { get; set; }
}
