namespace MatSplit.Web.Data.Entities;

/// <summary>
/// Uploaded receipt photo belonging to an expense. The binary lives on disk
/// under /data/receipts, only metadata is stored in the database.
/// </summary>
public class Receipt : AuditableEntity
{
    public long ExpenseId { get; set; }

    public string FileName { get; set; } = string.Empty;

    public string ContentType { get; set; } = "application/octet-stream";

    public long FileSizeBytes { get; set; }

    /// <summary>Path relative to the receipts root directory.</summary>
    public string StoragePath { get; set; } = string.Empty;

    public Expense? Expense { get; set; }
}
