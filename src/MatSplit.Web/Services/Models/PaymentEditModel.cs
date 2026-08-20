namespace MatSplit.Web.Services.Models;

/// <summary>
/// Input model for <see cref="PaymentService.SavePaymentAsync"/>.
/// <see cref="Id"/> = 0 inserts, everything else updates.
/// </summary>
public sealed class PaymentEditModel
{
    public long Id { get; set; }

    public long GroupId { get; set; }

    public long FromUserId { get; set; }

    public long ToUserId { get; set; }

    public long AmountCents { get; set; }

    public DateTime PaymentDate { get; set; } = DateTime.UtcNow.Date;

    public string? Note { get; set; }
}
