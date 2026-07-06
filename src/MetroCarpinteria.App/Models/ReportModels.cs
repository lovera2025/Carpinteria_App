namespace MetroCarpinteria.App.Models;

public sealed class ReportMetric
{
    public required string Label { get; init; }
    public required string Value { get; init; }
    public string? Detail { get; init; }
}

public sealed class ReportSection
{
    public required string Title { get; init; }
    public required string Icon { get; init; }
    public required IReadOnlyList<ReportMetric> Metrics { get; init; }
}
