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
}
