namespace MetroCarpinteria.SmokeTest;

/// <summary>
/// Aserciones mínimas para el runner casero. Antes cada test armaba su propio
/// <c>throw new InvalidOperationException</c>, así que los mensajes de error
/// salían con formatos distintos y muchas veces sin decir qué valor llegó.
/// </summary>
internal static class Assert
{
    public static void True(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    public static void False(bool condition, string message) => True(!condition, message);

    public static void Equal<T>(T actual, T expected, string label)
    {
        if (!EqualityComparer<T>.Default.Equals(actual, expected))
        {
            throw new InvalidOperationException($"{label}: esperaba «{expected}», llegó «{actual}».");
        }
    }

    /// <summary>Comparación de decimales con tolerancia, para lo que pasa por punto flotante.</summary>
    public static void Approximately(decimal actual, decimal expected, string label, decimal tolerance = 0.005m)
    {
        if (Math.Abs(actual - expected) > tolerance)
        {
            throw new InvalidOperationException(
                $"{label}: esperaba «{expected}» (±{tolerance}), llegó «{actual}».");
        }
    }

    public static void NotNull<T>(T? value, string label) where T : class
    {
        if (value is null)
        {
            throw new InvalidOperationException($"{label}: esperaba un valor, llegó null.");
        }
    }

    /// <summary>
    /// Verifica que la acción tire y que el mensaje contenga <paramref name="expectedFragment"/>.
    /// Chequear el fragmento importa: casi todos los errores de negocio son
    /// <see cref="InvalidOperationException"/>, así que sin eso un test puede pasar
    /// por la excepción equivocada.
    /// </summary>
    public static TException Throws<TException>(Action action, string expectedFragment)
        where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException ex)
        {
            if (!ex.Message.Contains(expectedFragment, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Tiró {typeof(TException).Name} pero el mensaje no menciona «{expectedFragment}»: {ex.Message}");
            }

            return ex;
        }

        throw new InvalidOperationException(
            $"Esperaba {typeof(TException).Name} con «{expectedFragment}», no tiró nada.");
    }

    public static InvalidOperationException Throws(Action action, string expectedFragment) =>
        Throws<InvalidOperationException>(action, expectedFragment);
}
