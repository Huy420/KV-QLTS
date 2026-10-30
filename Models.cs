using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;

namespace KieuViet;

public class Asset
{
    public int Id { get; set; }
    [MaxLength(80)] public string Code { get; set; } = "";
    [MaxLength(250)] public string Name { get; set; } = "";
    [MaxLength(120)] public string Category { get; set; } = "";
    [MaxLength(150)] public string Department { get; set; } = "";
    [MaxLength(150)] public string Location { get; set; } = "";
    [MaxLength(150)] public string Custodian { get; set; } = "";
    [MaxLength(150)] public string Serial { get; set; } = "";
    public DateTime? PurchaseDate { get; set; }
    public decimal Cost { get; set; }
    [MaxLength(40)] public string Status { get; set; } = "Đang sử dụng";
    [MaxLength(2000)] public string Notes { get; set; } = "";
    public bool Archived { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    [Timestamp] public byte[] Version { get; set; } = [];
}
public class InventorySession
{
    public int Id { get; set; }
    [MaxLength(250)] public string Name { get; set; } = "";
    [MaxLength(150)] public string Department { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ClosedAt { get; set; }
}
public class InventoryItem
{
    public int Id { get; set; }
    public int SessionId { get; set; }
    public InventorySession Session { get; set; } = null!;
    public int AssetId { get; set; }
    public Asset Asset { get; set; } = null!;
    [MaxLength(80)] public string Code { get; set; } = "";
    [MaxLength(250)] public string Name { get; set; } = "";
    [MaxLength(150)] public string ExpectedLocation { get; set; } = "";
    [MaxLength(150)] public string ExpectedCustodian { get; set; } = "";
    [MaxLength(150)] public string ActualLocation { get; set; } = "";
    [MaxLength(40)] public string Result { get; set; } = "Chưa kiểm kê";
    [MaxLength(2000)] public string Notes { get; set; } = "";
    public DateTime? CheckedAt { get; set; }
    [MaxLength(100)] public string CheckedBy { get; set; } = "";
    [Timestamp] public byte[] Version { get; set; } = [];
}
public class AppUser
{
    public int Id { get; set; }
    [MaxLength(100)] public string Username { get; set; } = "";
    [MaxLength(150)] public string DisplayName { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    [MaxLength(30)] public string Role { get; set; } = "Viewer";
}
public class AuditEntry
{
    public int Id { get; set; }
    public DateTime At { get; set; } = DateTime.UtcNow;
    [MaxLength(100)] public string User { get; set; } = "";
    [MaxLength(100)] public string Action { get; set; } = "";
    [MaxLength(4000)] public string Detail { get; set; } = "";
}
public class AssetDb(DbContextOptions<AssetDb> options) : DbContext(options), IDataProtectionKeyContext
{
    public DbSet<DataProtectionKey> DataProtectionKeys { get; set; } = null!;
    public DbSet<Asset> Assets => Set<Asset>();
    public DbSet<InventorySession> Sessions => Set<InventorySession>();
    public DbSet<InventoryItem> Items => Set<InventoryItem>();
    public DbSet<AppUser> Users => Set<AppUser>();
    public DbSet<AuditEntry> Audit => Set<AuditEntry>();
    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Asset>().HasIndex(x => x.Code).IsUnique();
        b.Entity<Asset>().Property(x => x.Cost).HasPrecision(18, 2);
        b.Entity<AppUser>().HasIndex(x => x.Username).IsUnique();
        b.Entity<InventoryItem>().HasIndex(x => new { x.SessionId, x.AssetId }).IsUnique();
        b.Entity<InventoryItem>().HasOne(x => x.Asset).WithMany().OnDelete(DeleteBehavior.Restrict);
        b.Entity<Asset>().Property(x => x.UpdatedAt).HasConversion(v => v, v => DateTime.SpecifyKind(v, DateTimeKind.Utc));
        b.Entity<InventorySession>().Property(x => x.CreatedAt).HasConversion(v => v, v => DateTime.SpecifyKind(v, DateTimeKind.Utc));
        b.Entity<InventorySession>().Property(x => x.ClosedAt).HasConversion(v => v, v => v.HasValue ? DateTime.SpecifyKind(v.Value, DateTimeKind.Utc) : (DateTime?)null);
        b.Entity<InventoryItem>().Property(x => x.CheckedAt).HasConversion(v => v, v => v.HasValue ? DateTime.SpecifyKind(v.Value, DateTimeKind.Utc) : (DateTime?)null);
        b.Entity<AuditEntry>().Property(x => x.At).HasConversion(v => v, v => DateTime.SpecifyKind(v, DateTimeKind.Utc));
        b.Entity<Asset>().ToTable(t => t.HasCheckConstraint("CK_Asset_Cost", "[Cost] >= 0"));
    }
}
public record LoginInput(string Username, string Password);
public record UserInput(string Username, string DisplayName, string Password, string Role);
public record AssetInput(string Code, string Name, string Category, string Department, string Location,
    string Custodian, string Serial, DateTime? PurchaseDate, decimal Cost, string Status, string Notes, string? Version);
public record SessionInput(string Name, string Department);
public record CheckInput(string Result, string ActualLocation, string Notes, string Version);
