using System.Globalization;
using System.Text;

namespace MetroCarpinteria.App.Models;

/// <summary>
/// Reglas para reconocer cuándo dos nombres de cliente son el mismo.
/// </summary>
/// <remarks>
/// <para>
/// Hasta acá el cliente era texto libre tipeado en cada presupuesto, así que el mismo
/// señor figura como «Juan Pérez», «juan perez» y «  JUAN  PÉREZ ». Normalizar deja los
/// tres en la misma clave y permite juntarlos sin perder cómo se escribió cada uno: el
/// nombre que se muestra sigue siendo el que se tipeó.
/// </para>
/// <para>
/// La normalización es <b>determinista y exacta</b>, nunca aproximada. Es la única que se
/// puede aplicar sola, sin preguntar: «Juan Pérez» y «Juan Pérez h.» normalizan distinto
/// y quedan separados, porque bien pueden ser padre e hijo y fusionarlos mezclaría dos
/// historiales comerciales sin vuelta atrás. Lo difuso se revisa a mano.
/// </para>
/// </remarks>
public static class ClientRules
{
    /// <summary>
    /// La clave con la que se comparan dos nombres: sin acentos, sin puntuación, sin
    /// espacios de más y en mayúsculas.
    /// </summary>
    public static string Normalize(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return string.Empty;
        }

        // FormD separa la letra de su tilde, y así el filtro de abajo puede sacar la tilde
        // sin tocar la letra: «Pérez» y «Perez» terminan iguales.
        var decomposed = name.Trim().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        var lastWasSpace = false;

        foreach (var character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsWhiteSpace(character))
            {
                // Los espacios repetidos se colapsan en uno.
                if (!lastWasSpace && builder.Length > 0)
                {
                    builder.Append(' ');
                    lastWasSpace = true;
                }

                continue;
            }

            if (!char.IsLetterOrDigit(character))
            {
                // Puntos, comas y guiones se van: «S.A.» y «SA» son lo mismo.
                continue;
            }

            builder.Append(char.ToUpperInvariant(character));
            lastWasSpace = false;
        }

        return builder.ToString().TrimEnd().Normalize(NormalizationForm.FormC);
    }

    /// <summary>Deja el nombre listo para guardar tal como lo escribió el usuario.</summary>
    public static string CleanDisplayName(string? name) =>
        string.IsNullOrWhiteSpace(name)
            ? string.Empty
            : string.Join(' ', name.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries));

    /// <summary>
    /// De todas las formas en que se escribió un mismo cliente, con cuál se lo muestra.
    /// </summary>
    /// <remarks>
    /// Gana la que más veces se tipeó, porque es la que el taller reconoce. Ante un empate
    /// se prefiere la que está capitalizada como un nombre: si las tres variantes aparecen
    /// una vez, quedarse con «JUAN PÉREZ» hace que la ficha grite, y con «juan perez» que
    /// parezca a medio cargar.
    /// </remarks>
    public static string PickDisplayName(IEnumerable<string> variants) =>
        variants
            .Select(CleanDisplayName)
            .Where(v => v.Length > 0)
            .GroupBy(v => v, StringComparer.Ordinal)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => CapitalizationRank(group.Key))
            .ThenBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => group.Key)
            .FirstOrDefault() ?? string.Empty;

    /// <summary>Menor es mejor: mezclada, después minúsculas, y last el grito en mayúsculas.</summary>
    private static int CapitalizationRank(string name)
    {
        var letters = name.Where(char.IsLetter).ToList();

        if (letters.Count == 0)
        {
            return 3;
        }

        if (letters.Any(char.IsUpper) && letters.Any(char.IsLower))
        {
            return 0;
        }

        return letters.All(char.IsLower) ? 1 : 2;
    }
}
