namespace MetroCarpinteria.App.Data.Entities;

public class CashMovement
{
    public int Id { get; set; }
    public int CashSessionId { get; set; }
    public CashSession CashSession { get; set; } = null!;
    public CashMovementType Type { get; set; }
    public decimal Amount { get; set; }
    public string Reason { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
}
