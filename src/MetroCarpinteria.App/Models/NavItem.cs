namespace MetroCarpinteria.App.Models;

public enum NavigationSection
{
    Home,
    Inventory,
    CashRegister,
    Quotes,
    Clients,
    Projects,
    Staff,
    Reports,
    Settings,
    About
}

public sealed class NavItem
{
    public required NavigationSection Section { get; init; }
    public required string Title { get; init; }
    public required string Icon { get; init; }
}

public sealed class DashboardCard
{
    public required string Title { get; init; }
    public required string Value { get; init; }
    public required string Description { get; init; }
    public required string AccentColor { get; init; }
}
