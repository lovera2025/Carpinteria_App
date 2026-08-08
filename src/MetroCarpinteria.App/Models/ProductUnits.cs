namespace MetroCarpinteria.App.Models;

public static class ProductUnits
{
    public const string Unit = "Unidad";
    public const string Piece = "Pieza";
    public const string Meter = "Metro";
    public const string SquareMeter = "Metro cuadrado";
    public const string Kilogram = "Kilogramo";
    public const string Liter = "Litro";

    public static IReadOnlyList<string> All { get; } =
    [
        Unit,
        Piece,
        Meter,
        SquareMeter,
        Kilogram,
        Liter
    ];

    private static readonly Dictionary<string, string> LegacyMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["u"] = Unit,
        ["pza"] = Piece,
        ["m"] = Meter,
        ["m²"] = SquareMeter,
        ["m2"] = SquareMeter,
        ["kg"] = Kilogram,
        ["l"] = Liter,
    };

    public static string Normalize(string? unit)
    {
        if (string.IsNullOrWhiteSpace(unit))
        {
            return Unit;
        }

        var trimmed = unit.Trim();
        return LegacyMap.TryGetValue(trimmed, out var mapped) ? mapped : trimmed;
    }

    private static readonly Dictionary<string, string> Abbreviations = new(StringComparer.OrdinalIgnoreCase)
    {
        [Unit] = "u.",
        [Piece] = "pza.",
        [Meter] = "m",
        [SquareMeter] = "m²",
        [Kilogram] = "kg",
        [Liter] = "L",
    };

    /// <summary>
    /// Forma corta para listas y tablas: <c>Metro cuadrado</c> → <c>m²</c>. Una unidad
    /// que el usuario escribió a mano se devuelve tal cual.
    /// </summary>
    public static string Abbreviate(string? unit)
    {
        var normalized = Normalize(unit);
        return Abbreviations.TryGetValue(normalized, out var shortForm) ? shortForm : normalized;
    }
}
