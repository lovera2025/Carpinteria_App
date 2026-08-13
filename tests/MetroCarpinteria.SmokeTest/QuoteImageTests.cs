using System.IO;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using MetroCarpinteria.App.Models;
using MetroCarpinteria.App.Services;
using Microsoft.Data.Sqlite;

namespace MetroCarpinteria.SmokeTest;

/// <summary>
/// Fotos de presupuesto: adjuntar, tope, duplicar, imprimir, respaldo. Corre en STA
/// porque comprimir JPEG usa tipos de WPF.
/// </summary>
internal static class QuoteImageTests
{
    public static void Run(Action<string, Action> run)
    {
        Exception? threadError = null;

        var thread = new Thread(() =>
        {
            try
            {
                RunOnSta(run);
            }
            catch (Exception ex)
            {
                threadError = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (threadError is not null)
        {
            run("Quote images STA bootstrap", () => throw threadError);
        }
    }

    private static void RunOnSta(Action<string, Action> run)
    {
        // Sin crear Application: si se deja viva en este hilo y después se muere el
        // hilo, las pruebas de UI que corren después heredan un dispatcher muerto y
        // se cuelgan. Comprimir JPEG alcanza con STA.

        using var fixture = TestFixture.CreateSeeded();
        var samples = Path.Combine(fixture.Paths.RootDirectory, "sample-photos");
        var kitchen = SampleJpeg.Write(samples, "cocina.jpg", Color.FromRgb(107, 68, 35), "Cocina similar");
        var wardrobe = SampleJpeg.Write(samples, "placard.jpg", Color.FromRgb(196, 165, 116), "Placard blanco");
        var table = SampleJpeg.Write(samples, "mesa.jpg", Color.FromRgb(61, 41, 20), "Mesa de roble");
        var shelf = SampleJpeg.Write(samples, "estante.jpg", Color.FromRgb(122, 101, 85), "Estante a medida");
        var fifth = SampleJpeg.Write(samples, "extra.jpg", Color.FromRgb(80, 80, 80), "De más");
        var notImage = Path.Combine(samples, "nota.txt");
        File.WriteAllText(notImage, "esto no es una foto");

        var images = AppHost.QuoteImageService;
        var quotes = AppHost.QuoteService;
        var inventory = AppHost.InventoryService;
        var quoteId = fixture.QuoteId;

        var stockBefore = inventory.GetProducts(false, false, null)
            .ToDictionary(p => p.Id, p => p.CurrentStock);
        var budgetBefore = quotes.GetDetail(quoteId)!.Budget;

        run("Fotos: adjuntar copia a quote-images y no mueve stock ni el precio", () =>
        {
            var item = images.AddFromFile(quoteId, kitchen, "Cocina similar en melamina");
            Assert.False(item.IsMissing, "la foto recién copiada tendría que existir.");
            Assert.True(File.Exists(item.FullPath), "tenía que copiarse a la carpeta de datos.");
            Assert.False(
                string.Equals(item.FullPath, kitchen, StringComparison.OrdinalIgnoreCase),
                "no puede guardarse la ruta original.");

            var detail = quotes.GetDetail(quoteId)!;
            Assert.Equal(detail.Images.Count, 1, "fotos en el detalle");
            Assert.Equal(detail.Budget ?? 0m, budgetBefore ?? 0m, "precio tras adjuntar");

            foreach (var product in inventory.GetProducts(false, false, null))
            {
                Assert.Equal(
                    product.CurrentStock,
                    stockBefore[product.Id],
                    $"stock de {product.Name} tras adjuntar");
            }
        });

        run("Fotos: tipo no soportado se rechaza", () =>
        {
            Assert.Throws(
                () => images.AddFromFile(quoteId, notImage),
                "JPG");
            Assert.Equal(images.List(quoteId).Count, 1, "sigue habiendo una sola foto");
        });

        run("Fotos: tope de 4", () =>
        {
            images.AddFromFile(quoteId, wardrobe, "Placard blanco");
            images.AddFromFile(quoteId, table, "Mesa de roble");
            images.AddFromFile(quoteId, shelf, "Estante");

            Assert.Equal(images.List(quoteId).Count, 4, "máximo alcanzado");
            Assert.Throws(() => images.AddFromFile(quoteId, fifth), "hasta 4");
        });

        run("Fotos: duplicar copia los archivos al id nuevo", () =>
        {
            var original = images.List(quoteId);
            var copyId = quotes.DuplicateQuote(quoteId);
            var copies = images.List(copyId);

            Assert.Equal(copies.Count, original.Count, "cantidad copiada");

            foreach (var (source, copy) in original.Zip(copies))
            {
                Assert.Equal(copy.Caption, source.Caption, "pie de foto");
                Assert.True(File.Exists(copy.FullPath), "archivo de la copia");
                Assert.False(
                    string.Equals(copy.FullPath, source.FullPath, StringComparison.OrdinalIgnoreCase),
                    "tienen que ser archivos distintos");
                Assert.True(File.Exists(source.FullPath), "el original no se toca");
            }
        });

        run("Fotos: archivo borrado a mano no explota al leer ni al imprimir", () =>
        {
            var first = images.List(quoteId)[0];
            File.Delete(first.FullPath);

            var detail = quotes.GetDetail(quoteId)
                ?? throw new InvalidOperationException("No se encontró el presupuesto.");

            Assert.True(detail.Images.Any(i => i.Id == first.Id && i.IsMissing), "tenía que marcarse como faltante.");
            Assert.False(
                detail.PrintableImages.Any(i => i.Id == first.Id),
                "una foto sin archivo no se imprime.");

            var client = AppHost.QuoteDocumentService.BuildClientQuote(detail, includeMaterialDetail: true);
            var text = new TextRange(client.ContentStart, client.ContentEnd).Text;
            if (!text.Contains("Referencias", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Con fotos restantes debía aparecer Referencias.");
            }

            if (text.Contains("Ganancia", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("El documento del cliente no puede mostrar ganancia.");
            }
        });

        run("Fotos: el documento del cliente las muestra y la hoja de costos no", () =>
        {
            var withPhotos = quotes.GetDetail(quoteId)!;
            var client = ToText(AppHost.QuoteDocumentService.BuildClientQuote(withPhotos, true));
            var cost = ToText(AppHost.QuoteDocumentService.BuildCostSheet(withPhotos));

            if (!client.Contains("Referencias", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("El presupuesto del cliente debía traer Referencias.");
            }

            if (cost.Contains("Referencias", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("La hoja de costos no puede mostrar las fotos.");
            }
        });

        run("Fotos: un presupuesto sin fotos imprime igual que antes", () =>
        {
            var plainId = quotes.CreateQuote("Sin fotos", "Cliente control", null).Id;
            quotes.AddInventoryLine(plainId, fixture.BoardProductId, 2m);
            quotes.SaveCalculation(plainId, quotes.GetDetail(plainId)!.MaterialsTotal, 2m, 30000m, BudgetRates.Defaults());

            var plain = quotes.GetDetail(plainId)!;
            var text = ToText(AppHost.QuoteDocumentService.BuildClientQuote(plain, includeMaterialDetail: true));

            if (text.Contains("Referencias", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Sin fotos no debía aparecer la sección Referencias.");
            }

            QuoteDocumentService.LayOut(AppHost.QuoteDocumentService.BuildClientQuote(plain, true), 794, 1123);
        });

        run("Fotos: aprobar un presupuesto con fotos sigue descontando stock", () =>
        {
            var product = inventory.CreateProduct("Melamina con foto", 10m, 1m, "Metro", 800m);
            var id = quotes.CreateQuote("Placard con foto", "Cliente foto", null).Id;
            quotes.AddInventoryLine(id, product.Id, 4m);
            quotes.SaveCalculation(id, 3200m, 2m, 25000m, BudgetRates.Defaults());
            images.AddFromFile(id, kitchen, "Referencia");

            var result = quotes.ApproveQuote(id);
            Assert.False(result.HasShortfalls, "no debía faltar stock.");
            Assert.Equal(
                inventory.GetProducts(false, false, "Melamina con foto").Single().CurrentStock,
                6m,
                "stock tras aprobar con fotos");
            Assert.Equal(images.List(id).Count, 1, "las fotos siguen después de aprobar");
        });

        run("Fotos: respaldo con sidecar recupera los archivos", () =>
        {
            var id = quotes.CreateQuote("Con respaldo", "Cliente backup", "Fotos").Id;
            quotes.AddInventoryLine(id, fixture.BoardProductId, 1m);
            quotes.SaveCalculation(id, quotes.GetDetail(id)!.MaterialsTotal, 1m, 20000m, BudgetRates.Defaults());
            var attached = images.AddFromFile(id, wardrobe, "Placard de muestra");
            var bytes = File.ReadAllBytes(attached.FullPath);

            var backup = AppHost.BackupService.CreateBackup();
            Assert.True(
                Directory.Exists(BackupService.ImagesSidecarPath(backup.FullPath)),
                "tenía que crearse la carpeta .images.");

            images.Remove(attached.Id);
            Assert.Equal(images.List(id).Count, 0, "foto quitada antes de restaurar");

            AppHost.BackupService.RestoreBackup(backup.FullPath);

            var restored = images.List(id).Single();
            Assert.Equal(restored.Caption, "Placard de muestra", "pie restaurado");
            Assert.False(restored.IsMissing, "el archivo tenía que volver.");
            Assert.Equal(File.ReadAllBytes(restored.FullPath).Length, bytes.Length, "tamaño restaurado");
        });

        run("Fotos: respaldo viejo solo .db no mezcla archivos de disco", () =>
        {
            var id = quotes.CreateQuote("Sin fotos al respaldar", "Cliente viejo", null).Id;
            quotes.AddInventoryLine(id, fixture.BoardProductId, 1m);
            quotes.SaveCalculation(id, quotes.GetDetail(id)!.MaterialsTotal, 1m, 20000m, BudgetRates.Defaults());

            Checkpoint(fixture.Paths.DatabasePath);

            var legacyPath = Path.Combine(
                fixture.Paths.BackupsDirectory,
                $"carpinteria_legacy_{Guid.NewGuid():N}.db");
            File.Copy(fixture.Paths.DatabasePath, legacyPath, overwrite: false);

            images.AddFromFile(id, table, "No debería sobrevivir");
            var decoyDir = Path.Combine(fixture.Paths.QuoteImagesDirectory, "9999");
            Directory.CreateDirectory(decoyDir);
            File.Copy(shelf, Path.Combine(decoyDir, $"{Guid.NewGuid():N}.jpg"));

            AppHost.BackupService.RestoreBackup(legacyPath);

            Assert.Equal(images.List(id).Count, 0, "el .db viejo no tenía fotos");
            Assert.False(
                Directory.Exists(decoyDir),
                "la carpeta señuelo no puede quedar mezclada con los ids restaurados.");
        });
    }

    private static void Checkpoint(string databasePath)
    {
        using var connection = new SqliteConnection($"Data Source={databasePath}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
        command.ExecuteNonQuery();
    }

    private static string ToText(FlowDocument document) =>
        new TextRange(document.ContentStart, document.ContentEnd).Text;
}
