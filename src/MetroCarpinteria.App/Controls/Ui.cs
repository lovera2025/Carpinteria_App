using System.Windows;
using System.Windows.Controls;

namespace MetroCarpinteria.App.Controls;

/// <summary>
/// Propiedades adjuntas de la interfaz: texto de ejemplo, estado vacío y ayuda contextual.
/// <para>
/// Son propiedades adjuntas y no controles envolventes por dos motivos. Envolver un
/// <c>TextBox</c> en un <c>UserControl</c> rompería todas las rutas de binding existentes y
/// el recorrido con Tab; y un adorner no se dibuja cuando el control vive dentro de un
/// contenedor que se recicla, como las filas de un <c>DataGrid</c>. Leídas desde el
/// <c>ControlTemplate</c>, en cambio, funcionan en cualquier contexto.
/// </para>
/// </summary>
public static class Ui
{
    // --- Texto de ejemplo dentro de un campo vacío ---------------------------

    /// <summary>
    /// Texto tenue que se muestra mientras el campo está vacío y sin foco.
    /// <para>
    /// Antes esto se intentaba con <c>Tag</c> (<c>Tag="Buscar producto..."</c> en
    /// Inventario), pero no existía ningún estilo que leyera ese <c>Tag</c>, así que el
    /// texto nunca llegó a dibujarse. <c>Tag</c> además es de uso general y puede chocar
    /// con cualquier otra cosa; una propiedad tipada aparece en el autocompletado y no
    /// colisiona con nada.
    /// </para>
    /// </summary>
    public static readonly DependencyProperty PlaceholderProperty =
        DependencyProperty.RegisterAttached(
            "Placeholder",
            typeof(string),
            typeof(Ui),
            new PropertyMetadata(string.Empty));

    public static string GetPlaceholder(DependencyObject element) =>
        (string)element.GetValue(PlaceholderProperty);

    public static void SetPlaceholder(DependencyObject element, string value) =>
        element.SetValue(PlaceholderProperty, value);

    // --- Estado vacío de listas y grillas ------------------------------------

    /// <summary>
    /// Título de lo que se muestra cuando la lista no tiene filas.
    /// <para>
    /// Son nueve grillas en la app y ninguna decía nada al estar vacía: un inventario
    /// recién instalado mostraba encabezados y un rectángulo en blanco. Como propiedad
    /// adjunta son tres atributos por grilla en lugar de repetir el mismo bloque de
    /// superposición en cada vista.
    /// </para>
    /// </summary>
    public static readonly DependencyProperty EmptyTitleProperty =
        DependencyProperty.RegisterAttached(
            "EmptyTitle",
            typeof(string),
            typeof(Ui),
            new PropertyMetadata(string.Empty));

    public static string GetEmptyTitle(DependencyObject element) =>
        (string)element.GetValue(EmptyTitleProperty);

    public static void SetEmptyTitle(DependencyObject element, string value) =>
        element.SetValue(EmptyTitleProperty, value);

    /// <summary>Qué hacer a continuación. Un estado vacío sin salida no ayuda a nadie.</summary>
    public static readonly DependencyProperty EmptyMessageProperty =
        DependencyProperty.RegisterAttached(
            "EmptyMessage",
            typeof(string),
            typeof(Ui),
            new PropertyMetadata(string.Empty));

    public static string GetEmptyMessage(DependencyObject element) =>
        (string)element.GetValue(EmptyMessageProperty);

    public static void SetEmptyMessage(DependencyObject element, string value) =>
        element.SetValue(EmptyMessageProperty, value);

    public static readonly DependencyProperty EmptyIconProperty =
        DependencyProperty.RegisterAttached(
            "EmptyIcon",
            typeof(string),
            typeof(Ui),
            new PropertyMetadata("📋"));

    public static string GetEmptyIcon(DependencyObject element) =>
        (string)element.GetValue(EmptyIconProperty);

    public static void SetEmptyIcon(DependencyObject element, string value) =>
        element.SetValue(EmptyIconProperty, value);

    // --- Ayuda contextual ----------------------------------------------------

    /// <summary>
    /// Explicación del campo, para quien recién empieza a usar la app.
    /// Se muestra como un signo de pregunta al lado de la etiqueta y se puede
    /// apagar por completo desde Configuración con el modo experto.
    /// </summary>
    public static readonly DependencyProperty HelpProperty =
        DependencyProperty.RegisterAttached(
            "Help",
            typeof(string),
            typeof(Ui),
            new PropertyMetadata(string.Empty));

    public static string GetHelp(DependencyObject element) => (string)element.GetValue(HelpProperty);

    public static void SetHelp(DependencyObject element, string value) =>
        element.SetValue(HelpProperty, value);

    // --- Buscador de la sección ----------------------------------------------

    /// <summary>
    /// Marca el buscador de la pantalla para que Ctrl+F sepa dónde poner el foco.
    /// Cada sección tiene el suyo y el atajo es global, así que el shell necesita
    /// alguna forma de encontrar el que corresponde a la vista activa.
    /// </summary>
    public static readonly DependencyProperty IsSectionSearchBoxProperty =
        DependencyProperty.RegisterAttached(
            "IsSectionSearchBox",
            typeof(bool),
            typeof(Ui),
            new PropertyMetadata(false));

    public static bool GetIsSectionSearchBox(DependencyObject element) =>
        (bool)element.GetValue(IsSectionSearchBoxProperty);

    public static void SetIsSectionSearchBox(DependencyObject element, bool value) =>
        element.SetValue(IsSectionSearchBoxProperty, value);

    /// <summary>
    /// Busca el buscador de la sección dentro del árbol visual dado.
    /// Devuelve null si la pantalla no tiene ninguno, que es un caso válido
    /// (Inicio y Acerca de no tienen búsqueda).
    /// </summary>
    public static TextBox? FindSectionSearchBox(DependencyObject? root)
    {
        if (root is null)
        {
            return null;
        }

        if (root is TextBox box && GetIsSectionSearchBox(box))
        {
            return box;
        }

        var count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var found = FindSectionSearchBox(System.Windows.Media.VisualTreeHelper.GetChild(root, i));
            if (found is not null)
            {
                return found;
            }
        }

        return null;
    }

    // --- Campo de plata ------------------------------------------------------

    /// <summary>
    /// Convierte un <see cref="TextBox"/> en un campo de importes: solo acepta números,
    /// pone los puntos de miles solo mientras se tipea, y alinea a la derecha.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Nace de un caso real: el 2026-09-07 Maximiliano cargó «arraial herrajes» en el
    /// campo Monto de Caja y «625800» en Motivo. La app contestó «Monto inválido» en la
    /// esquina opuesta de la pantalla y él probó cambiando el medio de pago, porque el
    /// mensaje no decía qué estaba mal. El formulario tenía dos cajones anchos y vacíos,
    /// uno abajo del otro, con dos etiquetas que empiezan igual —Monto y Motivo—.
    /// </para>
    /// <para>
    /// El arreglo de fondo es que <b>el campo de plata no se pueda confundir con uno de
    /// texto</b>: si no entran letras, el error es imposible de cometer, no solo fácil de
    /// explicar.
    /// </para>
    /// <para>
    /// <b>El punto es siempre separador de miles y la coma el decimal</b>, como se escribe
    /// la plata acá. Los miles los pone la app sola; el punto del teclado numérico escribe
    /// una coma, que es lo que la mano busca. Eso además evita que «1234.500» se guarde
    /// como 1.234,50, que es lo que pasaba antes sin avisar.
    /// </para>
    /// </remarks>
    public static readonly DependencyProperty MoneyBoxProperty =
        DependencyProperty.RegisterAttached(
            "MoneyBox",
            typeof(bool),
            typeof(Ui),
            new PropertyMetadata(false, OnMoneyBoxChanged));

    public static bool GetMoneyBox(DependencyObject element) =>
        (bool)element.GetValue(MoneyBoxProperty);

    public static void SetMoneyBox(DependencyObject element, bool value) =>
        element.SetValue(MoneyBoxProperty, value);

    /// <summary>Evita que el reformateo se dispare a sí mismo.</summary>
    private static bool _formattingMoney;

    private static void OnMoneyBoxChanged(DependencyObject element, DependencyPropertyChangedEventArgs e)
    {
        if (element is not TextBox box)
        {
            return;
        }

        box.PreviewTextInput -= OnMoneyInput;
        box.TextChanged -= OnMoneyTextChanged;
        DataObject.RemovePastingHandler(box, OnMoneyPaste);

        if (e.NewValue is not true)
        {
            return;
        }

        box.PreviewTextInput += OnMoneyInput;
        box.TextChanged += OnMoneyTextChanged;
        DataObject.AddPastingHandler(box, OnMoneyPaste);
        box.TextAlignment = TextAlignment.Right;
    }

    private static void OnMoneyInput(object sender, System.Windows.Input.TextCompositionEventArgs e)
    {
        if (sender is not TextBox box)
        {
            return;
        }

        var yaTieneComa = box.Text.Contains(',');

        // El punto del teclado numérico escribe una coma: es lo que la mano busca para los
        // decimales, y como los miles los pone la app, el punto no hace falta para nada más.
        if (e.Text == ".")
        {
            e.Handled = true;

            if (!yaTieneComa)
            {
                var posicion = box.CaretIndex;
                box.Text = box.Text.Insert(posicion, ",");
                box.CaretIndex = posicion + 1;
            }

            return;
        }

        // Un dígito siempre entra. La coma, solo si todavía no hay decimales.
        e.Handled = !(e.Text.Length == 1 && char.IsDigit(e.Text[0]))
                    && !(e.Text == "," && !yaTieneComa);
    }

    private static void OnMoneyPaste(object sender, DataObjectPastingEventArgs e)
    {
        // Pegar un importe copiado de otro lado tiene que funcionar; pegar una frase, no.
        if (sender is not TextBox
            || !e.DataObject.GetDataPresent(DataFormats.UnicodeText)
            || FormatMoneyInput(e.DataObject.GetData(DataFormats.UnicodeText) as string).Length == 0)
        {
            e.CancelCommand();
        }
    }

    private static void OnMoneyTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_formattingMoney || sender is not TextBox box)
        {
            return;
        }

        var formateado = FormatMoneyInput(box.Text);

        if (formateado == box.Text)
        {
            return;
        }

        // El cursor se guarda contando dígitos y no posiciones: al meter un punto de miles
        // el texto se corre, y sin esto el cursor salta al principio a mitad de un número.
        var digitosAntes = box.Text.Take(box.CaretIndex).Count(char.IsDigit);

        _formattingMoney = true;
        try
        {
            box.Text = formateado;
        }
        finally
        {
            _formattingMoney = false;
        }

        var posicion = 0;
        var vistos = 0;

        while (posicion < formateado.Length && vistos < digitosAntes)
        {
            if (char.IsDigit(formateado[posicion]))
            {
                vistos++;
            }

            posicion++;
        }

        box.CaretIndex = posicion;
    }

    /// <summary>
    /// Deja solo números, con los miles separados por punto y hasta dos decimales tras la
    /// coma. Es la forma en que se escribe la plata en Argentina.
    /// </summary>
    /// <remarks>
    /// Lo que devuelve tiene que poder leerlo <c>NumberInput.TryParseMoney</c> sin cambiar
    /// de valor: «625.800» son seiscientos veinticinco mil ochocientos, no 625,8.
    /// </remarks>
    public static string FormatMoneyInput(string? raw)
    {
        if (string.IsNullOrEmpty(raw))
        {
            return string.Empty;
        }

        var enteros = new System.Text.StringBuilder();
        var decimales = new System.Text.StringBuilder();
        var enDecimales = false;

        foreach (var c in raw)
        {
            if (char.IsDigit(c))
            {
                if (enDecimales)
                {
                    if (decimales.Length < 2)
                    {
                        decimales.Append(c);
                    }
                }
                else
                {
                    enteros.Append(c);
                }
            }
            else if (c == ',' && !enDecimales)
            {
                enDecimales = true;
            }

            // El punto se descarta siempre: es el separador de miles, y lo pone esta misma
            // función. Tomarlo como decimal hacía que no supiera releer lo que ella misma
            // escribía —«6.258» volvía a entrar como 6,25— y el número se destrozaba solo
            // al tipear la cifra siguiente.
        }

        // Los ceros de adelante no se guardan: «007» es 7.
        var parteEntera = enteros.ToString().TrimStart('0');

        if (parteEntera.Length == 0)
        {
            parteEntera = enteros.Length > 0 ? "0" : string.Empty;
        }

        if (parteEntera.Length == 0)
        {
            // Escribió la coma antes que ningún número: «,50» es medio peso.
            return enDecimales ? $"0,{decimales}" : string.Empty;
        }

        var conMiles = new System.Text.StringBuilder();

        for (var i = 0; i < parteEntera.Length; i++)
        {
            if (i > 0 && (parteEntera.Length - i) % 3 == 0)
            {
                conMiles.Append('.');
            }

            conMiles.Append(parteEntera[i]);
        }

        return enDecimales ? $"{conMiles},{decimales}" : conMiles.ToString();
    }

    // --- Tapar la plata cuando hay alguien mirando ---------------------------

    /// <summary>
    /// Si los importes están tapados ahora mismo.
    /// </summary>
    /// <remarks>
    /// <para>
    /// El estado vive acá, en un solo lugar, y no en un ViewModel: lo consulta el converter
    /// que arma cada celda, y así una vista lo respeta sola, sin depender de que alguien le
    /// pase el dato desde arriba.
    /// </para>
    /// <para>
    /// El primer intento fue difuminar zonas enteras y no servía por dos motivos. Un
    /// importe borroso todavía deja ver de cuántas cifras es, y sobre todo: tapando zonas
    /// se iba también lo que él quiere seguir mirando —cuáles trabajos terminó, de quién es
    /// cada uno—. Ahora se cambia el importe por puntos y el resto de la pantalla queda.
    /// </para>
    /// </remarks>
    public static bool IsPrivacyOn { get; private set; }

    /// <summary>Avisa que el modo cambió, para el botón que lo prende y lo apaga.</summary>
    public static event EventHandler? PrivacyChanged;

    /// <summary>
    /// Prende o apaga el modo privado.
    /// </summary>
    /// <remarks>
    /// No toca la pantalla por su cuenta: los importes los arma un converter, así que
    /// después de esto hay que recargar la sección para que las celdas se rearmen. Lo hace
    /// <c>MainViewModel.TogglePrivacy</c>.
    /// </remarks>
    public static void SetPrivacy(bool on)
    {
        if (IsPrivacyOn == on)
        {
            return;
        }

        IsPrivacyOn = on;
        PrivacyChanged?.Invoke(null, EventArgs.Empty);
    }
}
