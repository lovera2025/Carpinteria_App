using MetroCarpinteria.App.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace MetroCarpinteria.App.Data;

public class AppDbContext : DbContext
{
    private readonly string _databasePath;

    public AppDbContext(string databasePath)
    {
        _databasePath = databasePath;
    }

    public DbSet<Product> Products => Set<Product>();
    public DbSet<StockMovement> StockMovements => Set<StockMovement>();
    public DbSet<CashSession> CashSessions => Set<CashSession>();
    public DbSet<CashMovement> CashMovements => Set<CashMovement>();
    public DbSet<Project> Projects => Set<Project>();
    public DbSet<Employee> Employees => Set<Employee>();
    public DbSet<ProjectMaterial> ProjectMaterials => Set<ProjectMaterial>();
    public DbSet<ProjectAssignment> ProjectAssignments => Set<ProjectAssignment>();
    public DbSet<ProjectBudgetLine> ProjectBudgetLines => Set<ProjectBudgetLine>();
    public DbSet<Client> Clients => Set<Client>();
    public DbSet<ProjectPayment> ProjectPayments => Set<ProjectPayment>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.UseSqlite($"Data Source={_databasePath}");
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Product>(entity =>
        {
            entity.HasIndex(p => p.Name);
            entity.HasIndex(p => p.IsArchived);
            entity.Property(p => p.Name).HasMaxLength(200).IsRequired();
            entity.Property(p => p.Unit).HasMaxLength(30).IsRequired();
            entity.Property(p => p.CurrentStock).HasPrecision(18, 3);
            entity.Property(p => p.MinimumStock).HasPrecision(18, 3);
            entity.Property(p => p.CostPrice).HasPrecision(18, 2);
        });

        modelBuilder.Entity<StockMovement>(entity =>
        {
            entity.HasIndex(m => m.ProductId);
            entity.HasIndex(m => m.CreatedAtUtc);
            entity.Property(m => m.Quantity).HasPrecision(18, 3);
            entity.Property(m => m.Reason).HasMaxLength(500).IsRequired();
            entity.HasOne(m => m.Product)
                .WithMany()
                .HasForeignKey(m => m.ProductId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<CashSession>(entity =>
        {
            entity.HasIndex(s => s.ClosedAtUtc);
            entity.HasIndex(s => s.OpenedAtUtc);
            entity.Property(s => s.OpeningAmount).HasPrecision(18, 2);
            entity.Property(s => s.ClosingExpectedAmount).HasPrecision(18, 2);
            entity.Property(s => s.ClosingCountedAmount).HasPrecision(18, 2);
            entity.Property(s => s.Difference).HasPrecision(18, 2);
            entity.Property(s => s.OpeningNotes).HasMaxLength(500);
            entity.Property(s => s.ClosingNotes).HasMaxLength(500);
        });

        modelBuilder.Entity<CashMovement>(entity =>
        {
            entity.HasIndex(m => m.CashSessionId);
            entity.HasIndex(m => m.CreatedAtUtc);
            entity.Property(m => m.Amount).HasPrecision(18, 2);
            entity.Property(m => m.Reason).HasMaxLength(500).IsRequired();
            entity.HasOne(m => m.CashSession)
                .WithMany(s => s.Movements)
                .HasForeignKey(m => m.CashSessionId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Project>(entity =>
        {
            entity.HasIndex(p => p.Status);
            entity.HasIndex(p => p.IsArchived);
            entity.HasIndex(p => p.Title);
            entity.Property(p => p.Title).HasMaxLength(200).IsRequired();
            entity.Property(p => p.ClientName).HasMaxLength(200).IsRequired();
            entity.Property(p => p.Description).HasMaxLength(2000);
            entity.Property(p => p.Budget).HasPrecision(18, 2);
            entity.Property(p => p.QuotedMaterialsCost).HasPrecision(18, 2);
            entity.Property(p => p.EstimatedDays).HasPrecision(18, 2);
            entity.Property(p => p.DailyRate).HasPrecision(18, 2);
            entity.Property(p => p.WastePercent).HasPrecision(9, 4);
            entity.Property(p => p.ToolWearPercent).HasPrecision(9, 4);
            entity.Property(p => p.OverheadPercent).HasPrecision(9, 4);
            entity.Property(p => p.ProfitPercent).HasPrecision(9, 4);
            entity.Property(p => p.VatPercent).HasPrecision(9, 4);
            entity.Property(p => p.DiscountValue).HasPrecision(18, 2);
            entity.HasIndex(p => p.ClientId);

            // SetNull y no Cascade: archivar o borrar una ficha de cliente no puede
            // llevarse puestos los presupuestos que se le entregaron. El nombre queda
            // igual en ClientName.
            entity.HasOne(p => p.Client)
                .WithMany(c => c.Projects)
                .HasForeignKey(p => p.ClientId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<ProjectPayment>(entity =>
        {
            entity.HasIndex(p => p.ProjectId);
            entity.HasIndex(p => p.CreatedAtUtc);
            entity.Property(p => p.Amount).HasPrecision(18, 2);
            entity.Property(p => p.Notes).HasMaxLength(500);
            entity.HasOne(p => p.Project)
                .WithMany(p => p.Payments)
                .HasForeignKey(p => p.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Client>(entity =>
        {
            entity.HasIndex(c => c.NormalizedName).IsUnique();
            entity.HasIndex(c => c.IsArchived);
            entity.Property(c => c.Name).HasMaxLength(200).IsRequired();
            entity.Property(c => c.NormalizedName).HasMaxLength(200).IsRequired();
            entity.Property(c => c.Phone).HasMaxLength(50);
            entity.Property(c => c.Email).HasMaxLength(200);
            entity.Property(c => c.TaxId).HasMaxLength(20);
            entity.Property(c => c.Address).HasMaxLength(300);
            entity.Property(c => c.Notes).HasMaxLength(2000);
        });

        modelBuilder.Entity<Employee>(entity =>
        {
            entity.HasIndex(e => e.FullName);
            entity.HasIndex(e => e.IsArchived);
            entity.Property(e => e.FullName).HasMaxLength(200).IsRequired();
            entity.Property(e => e.Phone).HasMaxLength(50);
            entity.Property(e => e.Role).HasMaxLength(100);
        });

        modelBuilder.Entity<ProjectMaterial>(entity =>
        {
            entity.HasIndex(m => m.ProjectId);
            entity.HasIndex(m => m.ProductId);
            entity.Property(m => m.Quantity).HasPrecision(18, 3);
            entity.HasOne(m => m.Project).WithMany(p => p.Materials).HasForeignKey(m => m.ProjectId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(m => m.Product).WithMany().HasForeignKey(m => m.ProductId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ProjectAssignment>(entity =>
        {
            entity.HasIndex(a => a.ProjectId);
            entity.HasIndex(a => a.EmployeeId);
            entity.HasIndex(a => new { a.ProjectId, a.EmployeeId }).IsUnique();
            entity.Property(a => a.Notes).HasMaxLength(500);
            entity.HasOne(a => a.Project).WithMany(p => p.Assignments).HasForeignKey(a => a.ProjectId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(a => a.Employee).WithMany(e => e.Assignments).HasForeignKey(a => a.EmployeeId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ProjectBudgetLine>(entity =>
        {
            entity.HasIndex(l => l.ProjectId);
            entity.HasIndex(l => l.ProductId);
            entity.Property(l => l.Description).HasMaxLength(200).IsRequired();
            entity.Property(l => l.Unit).HasMaxLength(30).IsRequired();
            entity.Property(l => l.Quantity).HasPrecision(18, 3);
            entity.Property(l => l.AppliedQuantity).HasPrecision(18, 3);
            entity.Property(l => l.UnitCost).HasPrecision(18, 2);
            entity.Ignore(l => l.LineTotal);
            entity.Ignore(l => l.PendingQuantity);
            entity.HasOne(l => l.Project).WithMany(p => p.BudgetLines).HasForeignKey(l => l.ProjectId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(l => l.Product).WithMany().HasForeignKey(l => l.ProductId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
