namespace MetroCarpinteria.App.Helpers;

/// <summary>
/// Armado de frases en castellano para los mensajes que lee el usuario.
/// </summary>
/// <remarks>
/// Está acá y no repetido en cada servicio porque los mensajes de "no se puede eliminar"
/// los arman tres módulos distintos y tienen que sonar igual: un "1 movimiento(s)" al
/// lado de un "3 movimientos" delata que nadie leyó el texto.
/// </remarks>
public static class Phrases
{
    /// <summary>«1 movimiento» / «3 movimientos», eligiendo la forma correcta.</summary>
    public static string Count(int quantity, string singular, string plural) =>
        quantity == 1 ? $"1 {singular}" : $"{quantity} {plural}";

    /// <summary>«a», «a y b», «a, b y c».</summary>
    public static string JoinWithAnd(IReadOnlyList<string> parts) => parts.Count switch
    {
        0 => string.Empty,
        1 => parts[0],
        _ => $"{string.Join(", ", parts.Take(parts.Count - 1))} y {parts[^1]}"
    };
}
