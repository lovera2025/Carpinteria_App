using MetroCarpinteria.App.Data;
using MetroCarpinteria.App.Data.Entities;
using MetroCarpinteria.App.Helpers;
using MetroCarpinteria.App.Models;
using Microsoft.EntityFrameworkCore;

namespace MetroCarpinteria.App.Services;

/// <summary>
/// La agenda de clientes del taller.
/// </summary>
/// <remarks>
/// Dos fichas nunca comparten <see cref="Client.NormalizedName"/>: lo garantiza un índice
/// único en la base y lo verifica este servicio antes de escribir, para poder dar un
/// mensaje en castellano en vez de dejar salir el error del motor.
/// </remarks>
public sealed class ClientService
{
    private readonly DatabaseService _databaseService;

    public ClientService(DatabaseService databaseService)
    {
        _databaseService = databaseService;
    }

    // --- Lectura -------------------------------------------------------------

    public IReadOnlyList<ClientListItem> GetClients(bool includeArchived = false, string? search = null)
    {
        using var context = _databaseService.CreateContext();

        var query = context.Clients.AsNoTracking().AsQueryable();

        if (!includeArchived)
        {
            query = query.Where(c => !c.IsArchived);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            // Se busca contra la clave normalizada: así «perez» encuentra a «Pérez».
            var term = ClientRules.Normalize(search);
            query = query.Where(c => EF.Functions.Like(c.NormalizedName, $"%{term}%"));
        }

        return Project(context, query).OrderBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    public ClientListItem? GetClient(int clientId)
    {
        using var context = _databaseService.CreateContext();
        return Project(context, context.Clients.AsNoTracking().Where(c => c.Id == clientId))
            .FirstOrDefault();
    }

    /// <summary>Sugerencias para el selector, ordenadas por quién vuelve más.</summary>
    public IReadOnlyList<ClientListItem> Search(string? term, int limit = 8)
    {
        var matches = GetClients(includeArchived: false, search: term);

        return matches
            .OrderByDescending(c => c.QuoteCount)
            .ThenBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase)
            .Take(limit)
            .ToList();
    }

    public IReadOnlyList<ClientProjectItem> GetClientProjects(int clientId)
    {
        using var context = _databaseService.CreateContext();
        var paidByProject = ReadPaidByProject(context);

        return context.Projects
            .AsNoTracking()
            .Where(p => p.ClientId == clientId)
            .Select(p => new { p.Id, p.Title, p.Status, p.Budget, p.QuotedAtUtc })
            .AsEnumerable()
            .Select(p => new ClientProjectItem
            {
                Id = p.Id,
                Title = p.Title,
                Status = p.Status,
                Budget = p.Budget,
                Paid = paidByProject.GetValueOrDefault(p.Id),
                QuotedAtLocal = ToLocalDate(p.QuotedAtUtc)
            })
            .OrderByDescending(p => p.QuotedAtLocal ?? DateTime.MinValue)
            .ToList();
    }

    // --- Alta y edición ------------------------------------------------------

    public Client Create(
        string name,
        string? phone = null,
        string? email = null,
        string? taxId = null,
        string? address = null,
        string? notes = null)
    {
        var display = ClientRules.CleanDisplayName(name);
        var normalized = ClientRules.Normalize(name);

        if (normalized.Length == 0)
        {
            throw new InvalidOperationException("El nombre del cliente es obligatorio.");
        }

        using var context = _databaseService.CreateContext();

        if (context.Clients.FirstOrDefault(c => c.NormalizedName == normalized) is { } existing)
        {
            throw new InvalidOperationException(
                $"Ya existe un cliente que se llama «{existing.Name}».");
        }

        var now = DateTime.UtcNow;
        var client = new Client
        {
            Name = display,
            NormalizedName = normalized,
            Phone = Trim(phone),
            Email = Trim(email),
            TaxId = Trim(taxId),
            Address = Trim(address),
            Notes = Trim(notes),
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        context.Clients.Add(client);
        context.SaveChanges();
        return client;
    }

    /// <summary>
    /// Devuelve la ficha que corresponda al nombre, creándola si hace falta.
    /// </summary>
    /// <remarks>
    /// Es lo que usa el selector al cotizar. Obligar a dar de alta un cliente antes de
    /// poder cotizar rompe el flujo del taller: llega alguien, se le pasa un precio, y
    /// recién si acepta importa quién es.
    /// </remarks>
    public Client GetOrCreate(string name)
    {
        var normalized = ClientRules.Normalize(name);

        if (normalized.Length == 0)
        {
            throw new InvalidOperationException("El nombre del cliente es obligatorio.");
        }

        using (var context = _databaseService.CreateContext())
        {
            if (context.Clients.FirstOrDefault(c => c.NormalizedName == normalized) is { } existing)
            {
                // Si estaba archivado y vuelve a aparecer, vuelve a la lista.
                if (existing.IsArchived)
                {
                    existing.IsArchived = false;
                    existing.UpdatedAtUtc = DateTime.UtcNow;
                    context.SaveChanges();
                }

                return existing;
            }
        }

        return Create(name);
    }

    public void Update(
        int clientId,
        string name,
        string? phone,
        string? email,
        string? taxId,
        string? address,
        string? notes)
    {
        var display = ClientRules.CleanDisplayName(name);
        var normalized = ClientRules.Normalize(name);

        if (normalized.Length == 0)
        {
            throw new InvalidOperationException("El nombre del cliente es obligatorio.");
        }

        using var context = _databaseService.CreateContext();
        var client = context.Clients.FirstOrDefault(c => c.Id == clientId)
            ?? throw new InvalidOperationException("Cliente no encontrado.");

        if (context.Clients.FirstOrDefault(c => c.NormalizedName == normalized && c.Id != clientId) is { } other)
        {
            throw new InvalidOperationException(
                $"Ya existe otro cliente que se llama «{other.Name}». Si son el mismo, fusionalos.");
        }

        client.Name = display;
        client.NormalizedName = normalized;
        client.Phone = Trim(phone);
        client.Email = Trim(email);
        client.TaxId = Trim(taxId);
        client.Address = Trim(address);
        client.Notes = Trim(notes);
        client.UpdatedAtUtc = DateTime.UtcNow;

        context.SaveChanges();
    }

    public void SetArchived(int clientId, bool archived)
    {
        using var context = _databaseService.CreateContext();
        var client = context.Clients.FirstOrDefault(c => c.Id == clientId)
            ?? throw new InvalidOperationException("Cliente no encontrado.");

        client.IsArchived = archived;
        client.UpdatedAtUtc = DateTime.UtcNow;
        context.SaveChanges();
    }

    /// <summary>
    /// Por qué no se puede borrar la ficha, o <c>null</c> si se puede.
    /// </summary>
    public string? DescribeDeleteBlock(int clientId)
    {
        using var context = _databaseService.CreateContext();
        var projects = context.Projects.Count(p => p.ClientId == clientId);

        return projects == 0
            ? null
            : $"No se puede eliminar: tiene {Phrases.Count(projects, "trabajo", "trabajos")} " +
              "en el historial. Archivalo en su lugar.";
    }

    public void Delete(int clientId)
    {
        if (DescribeDeleteBlock(clientId) is { } reason)
        {
            throw new InvalidOperationException(reason);
        }

        using var context = _databaseService.CreateContext();
        var client = context.Clients.FirstOrDefault(c => c.Id == clientId)
            ?? throw new InvalidOperationException("Cliente no encontrado.");

        context.Clients.Remove(client);
        context.SaveChanges();
    }

    // --- Duplicados ----------------------------------------------------------

    /// <summary>
    /// Pares de fichas que podrían ser la misma persona.
    /// </summary>
    /// <remarks>
    /// Nunca fusiona: propone. La migración v5 ya juntó las coincidencias exactas tras
    /// normalizar, que son las únicas seguras; lo que queda acá es parecido, y el parecido
    /// se decide mirando el historial de cada lado.
    /// </remarks>
    public IReadOnlyList<ClientDuplicateCandidate> FindDuplicateCandidates(
        IReadOnlyCollection<string>? dismissedPairs = null)
    {
        var clients = GetClients(includeArchived: false);
        var candidates = new List<ClientDuplicateCandidate>();

        for (var i = 0; i < clients.Count; i++)
        {
            for (var j = i + 1; j < clients.Count; j++)
            {
                var left = clients[i];
                var right = clients[j];

                var key = ClientDuplicateCandidate.BuildPairKey(left.Id, right.Id);
                if (dismissedPairs?.Contains(key) == true)
                {
                    continue;
                }

                if (Compare(left, right) is { } candidate)
                {
                    candidates.Add(candidate);
                }
            }
        }

        return candidates.OrderByDescending(c => c.Similarity).ToList();
    }

    private static ClientDuplicateCandidate? Compare(ClientListItem left, ClientListItem right)
    {
        // El teléfono manda: dos fichas con el mismo número son casi seguro la misma
        // persona anotada dos veces, aunque los nombres no se parezcan en nada.
        if (ClientRules.SamePhone(left.Phone, right.Phone))
        {
            return new ClientDuplicateCandidate
            {
                Left = left,
                Right = right,
                Reason = $"Mismo teléfono: {left.Phone}",
                Similarity = 1
            };
        }

        var similarity = ClientRules.Similarity(left.Name, right.Name);

        if (similarity >= ClientRules.SimilarityThreshold)
        {
            return new ClientDuplicateCandidate
            {
                Left = left,
                Right = right,
                Reason = "Nombres muy parecidos",
                Similarity = similarity
            };
        }

        if (ClientRules.SharesLongPrefix(left.Name, right.Name))
        {
            return new ClientDuplicateCandidate
            {
                Left = left,
                Right = right,
                Reason = "Empiezan igual",
                Similarity = similarity
            };
        }

        return null;
    }

    /// <summary>
    /// Pasa el historial de una ficha a otra y archiva la de origen.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Se reasigna <see cref="Project.ClientId"/> pero <b>no</b> se toca
    /// <see cref="Project.ClientName"/>: ese texto es cómo se escribió el nombre en el
    /// presupuesto que se entregó, y un papel ya firmado no puede cambiar porque después
    /// se corrigió una ficha.
    /// </para>
    /// <para>
    /// La de origen se archiva en vez de borrarse, así una fusión equivocada se puede
    /// revisar: los datos siguen ahí.
    /// </para>
    /// </remarks>
    public int Merge(int sourceId, int targetId)
    {
        if (sourceId == targetId)
        {
            throw new InvalidOperationException("Elegí dos fichas distintas para fusionar.");
        }

        using var context = _databaseService.CreateContext();
        using var transaction = context.Database.BeginTransaction();

        try
        {
            var source = context.Clients.FirstOrDefault(c => c.Id == sourceId)
                ?? throw new InvalidOperationException("No se encontró la ficha de origen.");

            var target = context.Clients.FirstOrDefault(c => c.Id == targetId)
                ?? throw new InvalidOperationException("No se encontró la ficha de destino.");

            var moved = 0;

            foreach (var project in context.Projects.Where(p => p.ClientId == sourceId))
            {
                project.ClientId = targetId;
                project.UpdatedAtUtc = DateTime.UtcNow;
                moved++;
            }

            // Los datos de contacto que la de destino no tenga se completan con los de la
            // otra: si no, fusionar puede hacer perder el único teléfono cargado.
            target.Phone ??= source.Phone;
            target.Email ??= source.Email;
            target.TaxId ??= source.TaxId;
            target.Address ??= source.Address;
            target.Notes = MergeNotes(target.Notes, source.Notes, source.Name);

            target.UpdatedAtUtc = DateTime.UtcNow;

            source.IsArchived = true;
            source.UpdatedAtUtc = DateTime.UtcNow;

            context.SaveChanges();
            transaction.Commit();

            return moved;
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    private static string? MergeNotes(string? target, string? source, string sourceName)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return target;
        }

        var fromSource = $"(de «{sourceName}») {source.Trim()}";

        return string.IsNullOrWhiteSpace(target)
            ? fromSource
            : $"{target.Trim()}{Environment.NewLine}{fromSource}";
    }

    // --- Internos ------------------------------------------------------------

    /// <summary>
    /// Arma la ficha con su historial.
    /// </summary>
    /// <remarks>
    /// Los importes se agrupan y suman <b>en memoria</b>, con <c>AsEnumerable</c> antes de
    /// cualquier cuenta: las columnas de dinero son TEXT, y SQLite no sabe sumarlas —ni
    /// querríamos que lo intente, porque las concatenaría como texto—.
    /// </remarks>
    private static IEnumerable<ClientListItem> Project(AppDbContext context, IQueryable<Client> query)
    {
        var clients = query.AsEnumerable().ToList();

        if (clients.Count == 0)
        {
            return [];
        }

        var ids = clients.Select(c => c.Id).ToHashSet();

        var projects = context.Projects
            .AsNoTracking()
            .Where(p => p.ClientId != null && ids.Contains(p.ClientId.Value))
            .Select(p => new { p.Id, p.ClientId, p.Status, p.Budget, p.QuotedAtUtc })
            .AsEnumerable()
            .ToList();

        var paidByProject = ReadPaidByProject(context);

        return clients.Select(client =>
        {
            var own = projects.Where(p => p.ClientId == client.Id).ToList();

            // Facturado cuenta lo aprobado, no lo cotizado: un presupuesto que el cliente
            // todavía no aceptó no es plata del taller.
            var approved = own
                .Where(p => p.Status is not ProjectStatus.Quote and not ProjectStatus.Rejected)
                .ToList();

            var invoiced = approved.Sum(p => p.Budget ?? 0m);
            var paid = approved.Sum(p => paidByProject.GetValueOrDefault(p.Id));

            return new ClientListItem
            {
                Id = client.Id,
                Name = client.Name,
                Phone = client.Phone,
                Email = client.Email,
                TaxId = client.TaxId,
                Address = client.Address,
                Notes = client.Notes,
                IsArchived = client.IsArchived,
                QuoteCount = own.Count,
                ApprovedCount = approved.Count,
                Invoiced = invoiced,
                Balance = Math.Max(0m, invoiced - paid),
                LastQuotedAtLocal = own
                    .Select(p => ToLocalDate(p.QuotedAtUtc))
                    .Where(d => d.HasValue)
                    .DefaultIfEmpty(null)
                    .Max()
            };
        });
    }

    private static Dictionary<int, decimal> ReadPaidByProject(AppDbContext context) =>
        context.ProjectPayments
            .AsNoTracking()
            .Select(x => new { x.ProjectId, x.Amount })
            .AsEnumerable()
            .GroupBy(x => x.ProjectId)
            .ToDictionary(group => group.Key, group => group.Sum(x => x.Amount));

    private static string? Trim(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static DateTime? ToLocalDate(DateTime? utc) => utc.HasValue
        ? DateTime.SpecifyKind(utc.Value, DateTimeKind.Utc).ToLocalTime().Date
        : null;
}
