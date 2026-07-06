namespace MetroCarpinteria.App.Data.Entities;

public class CashSession
{
    public int Id { get; set; }
    public decimal OpeningAmount { get; set; }
    public decimal? ClosingExpectedAmount { get; set; }
    public decimal? ClosingCountedAmount { get; set; }
    public decimal? Difference { get; set; }
    public string? OpeningNotes { get; set; }
    public string? ClosingNotes { get; set; }
    public DateTime OpenedAtUtc { get; set; }
    public DateTime? ClosedAtUtc { get; set; }
    public ICollection<CashMovement> Movements { get; set; } = [];

    public bool IsOpen => ClosedAtUtc is null;
}
