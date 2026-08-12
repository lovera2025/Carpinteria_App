using MetroCarpinteria.App.Models;
using MetroCarpinteria.App.Services;

namespace MetroCarpinteria.SmokeTest;

/// <summary>
/// Contratos de los componentes compartidos: avisos flotantes y diálogos.
/// </summary>
internal static class ComponentTests
{
    public static void Run(Action<string, Action> run)
    {
        run("Avisos: se apilan, se descartan y no crecen sin límite", () =>
        {
            var service = new NotificationService();

            service.Success("Producto creado.");
            service.Warning("Stock insuficiente.");
            Assert.Equal(service.Items.Count, 2, "avisos en pantalla");

            service.Dismiss(service.Items[0]);
            Assert.Equal(service.Items.Count, 1, "avisos tras descartar uno");

            // Más de cuatro a la vez tapan la pantalla en vez de informar.
            for (var i = 0; i < 10; i++)
            {
                service.Info($"Aviso {i}");
            }

            Assert.True(service.Items.Count <= 4, $"quedaron {service.Items.Count} avisos apilados.");

            // El más viejo es el que se va: los últimos son los que el usuario está mirando.
            Assert.Equal(service.Items[^1].Message, "Aviso 9", "último aviso");

            service.Clear();
            Assert.Equal(service.Items.Count, 0, "avisos tras limpiar");
        });

        run("Avisos: un error dura más y trae el detalle para copiar", () =>
        {
            var service = new NotificationService();
            service.Error("No se pudo guardar.", new InvalidOperationException("detalle técnico"));

            var item = service.Items[0];
            Assert.Equal(item.Kind, ToastKind.Error, "tipo de aviso");
            Assert.True(item.HasDetail, "un error tendría que traer el detalle.");
            Assert.True(item.Detail.Contains("detalle técnico"), "faltaba el detalle de la excepción.");

            // Un error necesita más tiempo en pantalla que un "guardado": hay algo que leer.
            var info = new ToastItem { Kind = ToastKind.Info, Message = "x" };
            Assert.True(item.Duration > info.Duration, "el error debería durar más que un aviso común.");
        });

        run("Diálogos: sin capa visual montada no se cuelgan esperando respuesta", () =>
        {
            // Es el caso de los tests, que instancian pantallas sueltas. Sin esta salida,
            // un ConfirmAsync quedaría esperando para siempre un TaskCompletionSource que
            // nadie completa, y la suite se colgaría en vez de fallar.
            var service = new DialogService { HasHost = false };

            var task = service.ConfirmAsync("Eliminar", "¿Seguro?");
            Assert.True(task.IsCompleted, "sin capa visual la respuesta tiene que ser inmediata.");
        });

        run("Diálogos: la respuesta del usuario llega a quien preguntó", () =>
        {
            var service = new DialogService { HasHost = true };

            var task = service.ConfirmAsync("Eliminar producto", "No se puede deshacer.", isDestructive: true);
            Assert.False(task.IsCompleted, "tendría que estar esperando la respuesta.");

            Assert.NotNull(service.Current, "el diálogo tendría que estar visible");
            Assert.Equal(service.Current!.Kind, DialogKind.Danger, "tipo de diálogo");

            service.Complete(true);

            Assert.True(task.IsCompleted, "la respuesta no llegó.");
            Assert.True(task.Result, "se confirmó pero llegó un no.");
            Assert.True(service.Current is null, "el diálogo tendría que haberse cerrado.");
        });

        run("Diálogos: no se abren dos a la vez", () =>
        {
            // El segundo taparía al primero y el usuario respondería sin saber a qué.
            var service = new DialogService { HasHost = true };

            var first = service.ConfirmAsync("Primero", "…");
            var second = service.ConfirmAsync("Segundo", "…");

            Assert.False(first.IsCompleted, "el primero no debía cerrarse solo.");
            Assert.True(second.IsCompleted, "el segundo tenía que rebotar.");
            Assert.Equal(service.Current!.Title, "Primero", "diálogo visible");

            service.Complete(false);
        });
    }
}
