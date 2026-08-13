using MetroCarpinteria.App.Data.Entities;
using MetroCarpinteria.App.Models;
using MetroCarpinteria.App.Services;

namespace MetroCarpinteria.SmokeTest;

/// <summary>
/// La agenda de clientes. Lo que más se protege acá es la fusión: juntar dos fichas mezcla
/// dos historiales comerciales y no hay forma de deshacerlo con un botón.
/// </summary>
internal static class ClientTests
{
    public static void Run(
        Action<string, Action> run,
        ClientService clients,
        QuoteService quotes,
        InventoryService inventory)
    {
        run("Clientes: no se pueden crear dos fichas con el mismo nombre", () =>
        {
            clients.Create("Carpintería del Sur", "3777-111111");

            // Ni escrito distinto: la clave de comparación ignora acentos y mayúsculas.
            Assert.Throws(() => clients.Create("CARPINTERIA DEL SUR"), "Ya existe");
        });

        run("Clientes: pedir uno que no existe lo crea al vuelo", () =>
        {
            // Es lo que hace el selector al cotizar: obligar a dar de alta un cliente antes
            // de poder pasar un precio rompe el flujo del taller.
            var created = clients.GetOrCreate("Vecino de enfrente");
            var again = clients.GetOrCreate("  vecino  de  enfrente ");

            Assert.Equal(again.Id, created.Id, "tendría que devolver la misma ficha");
            Assert.Equal(again.Name, "Vecino de enfrente", "nombre conservado");
        });

        run("Clientes: al cotizar, teléfono y email quedan en la ficha", () =>
        {
            var created = clients.SaveFromQuote(
                "Familia Acosta", "3777-555666", "acosta@ejemplo.com");

            Assert.Equal(created.Phone, "3777-555666", "teléfono guardado");
            Assert.Equal(created.Email, "acosta@ejemplo.com", "email guardado");

            // Un segundo presupuesto de la misma persona a menudo no vuelve a tipear
            // el contacto: el vacío no puede borrar lo que ya estaba.
            var again = clients.SaveFromQuote("familia acosta", null, "  ");
            Assert.Equal(again.Id, created.Id, "la misma ficha");
            Assert.Equal(again.Phone, "3777-555666", "teléfono conservado");
            Assert.Equal(again.Email, "acosta@ejemplo.com", "email conservado");

            var updated = clients.SaveFromQuote("Familia Acosta", "3777-111222", null);
            Assert.Equal(updated.Phone, "3777-111222", "teléfono actualizado");
            Assert.Equal(updated.Email, "acosta@ejemplo.com", "email intacto al no venir");
        });

        run("Clientes: buscar encuentra sin acentos y sin importar mayúsculas", () =>
        {
            clients.Create("Estudio Ramírez");

            Assert.Equal(clients.Search("ramirez").Count, 1, "buscando sin acento");
            Assert.Equal(clients.Search("RAMÍREZ").Count, 1, "buscando en mayúsculas");
            Assert.Equal(clients.Search("zzz").Count, 0, "buscando algo que no está");
        });

        run("Clientes: la ficha trae el historial con lo facturado y el saldo", () =>
        {
            var client = clients.Create("Cliente con historia");
            var productId = inventory.CreateProduct("Tabla historial", 100m, 0m, "Metro", 500m).Id;

            // Uno aprobado y uno todavía en presupuesto.
            var approvedId = NewQuote(quotes, productId, "Trabajo aprobado", client.Id);
            quotes.ApproveQuote(approvedId);
            NewQuote(quotes, productId, "Trabajo cotizado", client.Id);

            var detail = clients.GetClient(client.Id)
                ?? throw new InvalidOperationException("No se encontró la ficha.");

            Assert.Equal(detail.QuoteCount, 2, "presupuestos del cliente");
            Assert.Equal(detail.ApprovedCount, 1, "trabajos aprobados");

            // Facturado cuenta lo aprobado, no lo cotizado: un presupuesto que el cliente
            // todavía no aceptó no es plata del taller.
            Assert.True(detail.Invoiced > 0, "tendría que haber algo facturado.");
            Assert.Equal(detail.Balance, detail.Invoiced, "sin cobros, el saldo es todo lo facturado");
            Assert.Equal(clients.GetClientProjects(client.Id).Count, 2, "trabajos en el historial");
        });

        run("Clientes: no se borra una ficha con historial", () =>
        {
            var withHistory = clients.GetClients().First(c => c.Name == "Cliente con historia");

            Assert.Throws(() => clients.Delete(withHistory.Id), "trabajo");
            Assert.NotNull(clients.DescribeDeleteBlock(withHistory.Id), "tendría que explicar por qué");

            // Una sin historial sí.
            var empty = clients.Create("Ficha vacía");
            Assert.True(clients.DescribeDeleteBlock(empty.Id) is null, "una ficha sin trabajos se puede borrar.");
            clients.Delete(empty.Id);
        });

        run("Clientes: se proponen los parecidos, nunca se fusionan solos", () =>
        {
            var father = clients.Create("Ricardo Fontana");
            var son = clients.Create("Ricardo Fontana h.");

            var candidates = clients.FindDuplicateCandidates();
            var pair = candidates.FirstOrDefault(c =>
                (c.Left.Id == father.Id && c.Right.Id == son.Id)
                || (c.Left.Id == son.Id && c.Right.Id == father.Id));

            Assert.NotNull(pair, "el par tendría que aparecer como candidato");

            // Pero siguen siendo dos fichas: pueden ser padre e hijo.
            Assert.Equal(clients.GetClients().Count(c => c.Name.StartsWith("Ricardo Fontana")), 2, "fichas");
        });

        run("Clientes: dos fichas con el mismo teléfono se proponen aunque el nombre no se parezca", () =>
        {
            // Es el caso del apodo: «El Gordo» y «Miguel Sosa» con el mismo número.
            clients.Create("El Gordo", "3777-999888");
            clients.Create("Miguel Sosa", "3777 999888");

            var pair = clients.FindDuplicateCandidates()
                .FirstOrDefault(c => c.Reason.Contains("teléfono", StringComparison.OrdinalIgnoreCase));

            Assert.NotNull(pair, "el mismo teléfono tendría que proponer el par");
        });

        run("Clientes: un par descartado no se vuelve a proponer", () =>
        {
            var candidates = clients.FindDuplicateCandidates();
            Assert.True(candidates.Count > 0, "la prueba necesita algún candidato.");

            var dismissed = candidates[0].PairKey;
            var remaining = clients.FindDuplicateCandidates([dismissed]);

            Assert.False(
                remaining.Any(c => c.PairKey == dismissed),
                "el par descartado no tendría que volver a aparecer.");
        });

        run("Clientes: fusionar mueve el historial y conserva el nombre de cada presupuesto", () =>
        {
            // El nombre escrito en un presupuesto entregado no puede cambiar porque después
            // se corrigió una ficha: es la instantánea de lo que se firmó.
            var productId = inventory.CreateProduct("Tabla fusión", 100m, 0m, "Metro", 500m).Id;

            var target = clients.Create("Mueblería Norte", "3777-222222");
            var source = clients.Create("Muebleria Norte SA", email: "norte@ejemplo.com");

            var targetQuoteId = NewQuote(quotes, productId, "Trabajo del destino", target.Id, "Mueblería Norte");
            var sourceQuoteId = NewQuote(quotes, productId, "Trabajo del origen", source.Id, "Muebleria Norte SA");

            var moved = clients.Merge(source.Id, target.Id);
            Assert.Equal(moved, 1, "trabajos reasignados");

            var merged = clients.GetClient(target.Id)!;
            Assert.Equal(merged.QuoteCount, 2, "trabajos tras fusionar");

            // El dato de contacto que solo tenía la de origen no se pierde.
            Assert.Equal(merged.Email, "norte@ejemplo.com", "email traído del origen");
            Assert.Equal(merged.Phone, "3777-222222", "el teléfono del destino no se pisa");

            // La de origen queda archivada, no borrada: una fusión equivocada se puede revisar.
            var archived = clients.GetClients(includeArchived: true).Single(c => c.Id == source.Id);
            Assert.True(archived.IsArchived, "la ficha de origen tendría que quedar archivada.");

            // Y cada presupuesto conserva cómo se escribió el nombre.
            Assert.Equal(
                quotes.GetDetail(sourceQuoteId)!.ClientName,
                "Muebleria Norte SA",
                "nombre del presupuesto de origen");

            Assert.Equal(
                quotes.GetDetail(targetQuoteId)!.ClientName,
                "Mueblería Norte",
                "nombre del presupuesto de destino");
        });

        run("Clientes: no se fusiona una ficha consigo misma", () =>
        {
            var client = clients.GetClients().First();
            Assert.Throws(() => clients.Merge(client.Id, client.Id), "distintas");
        });

        run("Clientes: renombrar sobre un nombre ya usado avisa que se fusione", () =>
        {
            var first = clients.Create("Taller Uno");
            clients.Create("Taller Dos");

            Assert.Throws(
                () => clients.Update(first.Id, "Taller Dos", null, null, null, null, null),
                "fusiona");
        });

        run("Clientes: el parecido se mide, no se adivina", () =>
        {
            Assert.Approximately(
                (decimal)ClientRules.Similarity("Juan Pérez", "Juan Perez"), 1m, "sin acento", 0.001m);

            // Un sufijo corto sobre un nombre corto no llega al umbral de parecido: por eso
            // el prefijo compartido es un criterio aparte y no un adorno. Con uno solo de
            // los tres, «Juan Pérez h.» se escapaba.
            Assert.True(
                ClientRules.Similarity("Juan Pérez", "Juan Pérez h.") < ClientRules.SimilarityThreshold,
                "la prueba documenta que el parecido solo no alcanza para este caso.");

            Assert.True(
                ClientRules.SharesLongPrefix("Juan Pérez", "Juan Pérez h."),
                "el prefijo compartido tendría que proponer el par igual.");

            Assert.True(
                ClientRules.Similarity("Juan Pérez", "Marta Gómez") < ClientRules.SimilarityThreshold,
                "dos nombres distintos no tendrían que proponerse.");

            Assert.False(
                ClientRules.SharesLongPrefix("Juan Pérez", "Marta Gómez"),
                "dos nombres distintos tampoco comparten prefijo.");

            Assert.True(ClientRules.SamePhone("3777-412207", "3777 412207"), "el mismo número escrito distinto");
            Assert.False(ClientRules.SamePhone("123", "123"), "un número demasiado corto no alcanza.");
        });
    }

    private static int NewQuote(
        QuoteService quotes,
        int productId,
        string title,
        int clientId,
        string? clientName = null)
    {
        var id = quotes.CreateQuote(title, clientName ?? $"Cliente {clientId}", null).Id;

        quotes.AssignClient(id, clientId);
        quotes.AddInventoryLine(id, productId, 2m);
        quotes.SaveCalculation(id, 1000m, 1m, 20000m, BudgetRates.Defaults());

        return id;
    }
}
