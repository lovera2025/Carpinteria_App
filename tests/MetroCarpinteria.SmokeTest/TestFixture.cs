using System.IO;
using MetroCarpinteria.App.Data.Entities;
using MetroCarpinteria.App.Models;
using MetroCarpinteria.App.Services;

namespace MetroCarpinteria.SmokeTest;

/// <summary>
/// Carpeta de datos descartable con contenido conocido.
/// <para>
/// Existe por dos motivos. El primero: antes los tests de UI llamaban
/// <c>AppHost.Initialize()</c> sin argumentos, así que instanciaban los ViewModels
/// contra <c>Documentos/MetroCarpinteria</c> — la base real del taller — y al cargar
/// un presupuesto sin desglose disparaban <c>AutoCalculate</c>, que <b>graba</b>.
/// Cada corrida de la suite modificaba datos de producción.
/// </para>
/// <para>
/// El segundo: varios tests dependían de que el inventario de esa base tuviera
/// productos. En una máquina limpia fallaban sin que hubiera ninguna regresión.
/// Con datos sembrados verifican siempre lo mismo.
/// </para>
/// </summary>
internal sealed class TestFixture : IDisposable
{
    private TestFixture(AppPaths paths)
    {
        Paths = paths;
    }

    public AppPaths Paths { get; }

    /// <summary>Ids de lo sembrado, para que los tests no tengan que buscarlos por nombre.</summary>
    public int LowStockProductId { get; private set; }
    public int BoardProductId { get; private set; }
    public int ScrewProductId { get; private set; }
    public int EmployeeId { get; private set; }
    public int QuoteId { get; private set; }
    public int ActiveProjectId { get; private set; }

    /// <summary>
    /// Crea la carpeta temporal, deja <see cref="AppHost"/> apuntando ahí y siembra los datos.
    /// El reset previo permite crear varias fixtures dentro del mismo proceso.
    /// </summary>
    public static TestFixture CreateSeeded()
    {
        var root = Path.Combine(Path.GetTempPath(), $"MetroCarpinteriaFixture_{Guid.NewGuid():N}");
        var paths = new AppPaths(root);

        AppHost.ResetForTests();
        AppHost.Initialize(paths);

        var fixture = new TestFixture(paths);
        fixture.Seed();
        return fixture;
    }

    private void Seed()
    {
        var inventory = AppHost.InventoryService;
        var quotes = AppHost.QuoteService;

        // Inventario: tres activos, uno deliberadamente bajo el mínimo.
        // Los valores son los del caso 9 < 15, que es el que fallaba cuando la
        // comparación de stock se hacía en SQL sobre columnas TEXT ('9.0' > '15.0').
        BoardProductId = inventory.CreateProduct("Tabla de roble", 40m, 5m, "Metro", 1200m).Id;
        ScrewProductId = inventory.CreateProduct("Tornillo 3x30", 500m, 100m, "Pieza", 15m).Id;
        LowStockProductId = inventory.CreateProduct("Cola vinílica", 9m, 15m, "Litro", 900m).Id;

        EmployeeId = AppHost.EmployeeService.Create("Empleado de prueba", "11-5555-0000", "Oficial").Id;

        // Un presupuesto completo: dos líneas y el cálculo ya guardado, para que
        // LoadDetail encuentre un desglose y no tenga que recalcular nada.
        QuoteId = quotes.CreateQuote("Mesada de cocina", "Cliente de prueba", "Roble macizo").Id;
        quotes.AddInventoryLine(QuoteId, BoardProductId, 10m, 1200m);
        quotes.AddLooseLine(QuoteId, "Bisagra importada", "Unidad", 8m, 250m, saveToCatalog: false);
        quotes.SaveCalculation(QuoteId, 14000m, 3m, 30000m, BudgetRates.Defaults());

        // Condiciones comerciales y una seña: es el caso completo, el que hace falta para
        // revisar el pie del presupuesto y el saldo sin tener que provocarlos a mano.
        quotes.SaveCommercialTerms(QuoteId, new CommercialTerms
        {
            DiscountMode = DiscountMode.Percentage,
            DiscountValue = 10m,
            VatPercent = 21m
        });

        AppHost.PaymentService.RegisterPayment(
            QuoteId, PaymentKind.Deposit, 50000m, PaymentMethod.Transfer, "Adelanto por transferencia");

        ActiveProjectId = AppHost.ProjectService
            .Create("Placard empotrado", "Cliente en curso", "Melamina blanca", 250000m, ProjectStatus.InProgress)
            .Id;

        // Una caja abierta y cerrada, para que el historial y los reportes tengan
        // algo que mostrar sin dejar una sesión abierta que condicione otros tests.
        var cash = AppHost.CashRegisterService;
        cash.OpenSession(5000m, "Apertura de prueba");
        cash.RegisterMovement(CashMovementType.Income, 12000m, "Cobro de prueba");
        cash.RegisterMovement(CashMovementType.Expense, 2000m, "Gasto de prueba");
        cash.CloseSession(15000m, "Cierre de prueba");
    }

    public void Dispose()
    {
        AppHost.ResetForTests();
        TryDeleteDirectory(Paths.RootDirectory);
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
            // Una carpeta temporal que quedó tomada por SQLite no es motivo
            // para hacer fallar la suite entera; el SO la limpia después.
        }
    }
}
