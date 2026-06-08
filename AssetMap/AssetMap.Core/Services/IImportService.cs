namespace AssetMap.Core.Services;

public interface IImportService
{
    /// <summary>
    /// Importuje transakce z CSV souboru do zadaného účtu.
    /// </summary>
    Task<ImportResult> ImportCsvAsync(
        Guid   accountId,
        Guid   userId,
        string fileName,
        Stream csvStream,
        CancellationToken ct = default);
}

public class ImportResult
{
    public Guid   ImportBatchId { get; set; }
    public string DetectedFormat { get; set; } = "generic";
    public int    TotalRows    { get; set; }
    public int    SuccessCount { get; set; }
    public int    ErrorCount   { get; set; }
    public List<ImportRowError> Errors { get; set; } = [];
}

public record ImportRowError(int Row, string Message);
