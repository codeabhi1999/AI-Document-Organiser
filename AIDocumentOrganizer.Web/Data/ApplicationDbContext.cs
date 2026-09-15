using AIDocumentOrganizer.Web.Models.Entities;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace AIDocumentOrganizer.Web.Data;

public class ApplicationDbContext : IdentityDbContext<ApplicationUser>
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options) { }

    public DbSet<Document> Documents => Set<Document>();
    public DbSet<DocumentCategory> DocumentCategories => Set<DocumentCategory>();
    public DbSet<DocumentVersion> DocumentVersions => Set<DocumentVersion>();
    public DbSet<DocumentText> DocumentTexts => Set<DocumentText>();
    public DbSet<DocumentReminder> DocumentReminders => Set<DocumentReminder>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<AIProcessingLog> AIProcessingLogs => Set<AIProcessingLog>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        // Document
        builder.Entity<Document>(e =>
        {
            e.HasIndex(d => d.UserId);
            e.HasIndex(d => d.CategoryId);
            e.HasIndex(d => d.ExpiryDate);
            e.HasIndex(d => d.ProcessingStatus);
            e.HasOne(d => d.User)
             .WithMany(u => u.Documents)
             .HasForeignKey(d => d.UserId)
             .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(d => d.Category)
             .WithMany(c => c.Documents)
             .HasForeignKey(d => d.CategoryId)
             .OnDelete(DeleteBehavior.SetNull);
        });

        // DocumentVersion
        builder.Entity<DocumentVersion>(e =>
        {
            e.HasIndex(v => v.DocumentId);
            e.HasOne(v => v.Document)
             .WithMany(d => d.Versions)
             .HasForeignKey(v => v.DocumentId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        // DocumentText — one-to-one with DocumentVersion
        builder.Entity<DocumentText>(e =>
        {
            e.HasIndex(t => t.DocumentVersionId).IsUnique();
            e.HasOne(t => t.DocumentVersion)
             .WithOne(v => v.ExtractedText)
             .HasForeignKey<DocumentText>(t => t.DocumentVersionId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        // DocumentReminder
        builder.Entity<DocumentReminder>(e =>
        {
            e.HasIndex(r => r.DocumentId);
            e.HasIndex(r => r.ScheduledForDate);
            e.HasIndex(r => r.Status);
            e.HasOne(r => r.Document)
             .WithMany(d => d.Reminders)
             .HasForeignKey(r => r.DocumentId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        // Notification
        builder.Entity<Notification>(e =>
        {
            e.HasIndex(n => n.UserId);
            e.HasIndex(n => n.IsRead);
            e.HasOne(n => n.User)
             .WithMany(u => u.Notifications)
             .HasForeignKey(n => n.UserId)
             .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(n => n.Document)
             .WithMany(d => d.Notifications)
             .HasForeignKey(n => n.DocumentId)
             .OnDelete(DeleteBehavior.NoAction);
        });

        // AuditLog
        builder.Entity<AuditLog>(e =>
        {
            e.HasIndex(a => a.UserId);
            e.HasIndex(a => a.Timestamp);
            e.HasOne(a => a.User)
             .WithMany(u => u.AuditLogs)
             .HasForeignKey(a => a.UserId)
             .OnDelete(DeleteBehavior.SetNull);
        });

        // AIProcessingLog
        builder.Entity<AIProcessingLog>(e =>
        {
            e.HasIndex(l => l.DocumentId);
            e.HasOne(l => l.Document)
             .WithMany(d => d.AIProcessingLogs)
             .HasForeignKey(l => l.DocumentId)
             .OnDelete(DeleteBehavior.NoAction);
        });

        // Seed document categories
        builder.Entity<DocumentCategory>().HasData(
            new DocumentCategory { Id = 1,  Name = "Identity",         IconClass = "bi-person-badge",  ColorCode = "#4361ee", DisplayOrder = 1 },
            new DocumentCategory { Id = 2,  Name = "Government",       IconClass = "bi-building",      ColorCode = "#3a0ca3", DisplayOrder = 2 },
            new DocumentCategory { Id = 3,  Name = "Vehicle",          IconClass = "bi-car-front",     ColorCode = "#f72585", DisplayOrder = 3 },
            new DocumentCategory { Id = 4,  Name = "Insurance",        IconClass = "bi-shield-check",  ColorCode = "#7209b7", DisplayOrder = 4 },
            new DocumentCategory { Id = 5,  Name = "Education",        IconClass = "bi-mortarboard",   ColorCode = "#560bad", DisplayOrder = 5 },
            new DocumentCategory { Id = 6,  Name = "Financial",        IconClass = "bi-bank",          ColorCode = "#480ca8", DisplayOrder = 6 },
            new DocumentCategory { Id = 7,  Name = "Bills",            IconClass = "bi-receipt",       ColorCode = "#3f37c9", DisplayOrder = 7 },
            new DocumentCategory { Id = 8,  Name = "Property/Rental",  IconClass = "bi-house-door",    ColorCode = "#4895ef", DisplayOrder = 8 },
            new DocumentCategory { Id = 9,  Name = "Medical",          IconClass = "bi-heart-pulse",   ColorCode = "#4cc9f0", DisplayOrder = 9 },
            new DocumentCategory { Id = 10, Name = "Other",            IconClass = "bi-folder",        ColorCode = "#6c757d", DisplayOrder = 10 }
        );
    }
}
