using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using AIDocumentOrganizer.Web.Models.Enums;

namespace AIDocumentOrganizer.Web.Models.Entities;

public class DocumentCategory
{
    [Key]
    public int Id { get; set; }

    [Required, MaxLength(80)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(60)]
    public string IconClass { get; set; } = "bi-folder";

    [MaxLength(20)]
    public string ColorCode { get; set; } = "#6c757d";

    public int DisplayOrder { get; set; }
    public bool IsSystemDefault { get; set; } = true;

    public virtual ICollection<Document> Documents { get; set; } = new List<Document>();
}

public class Document
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    public string UserId { get; set; } = string.Empty;

    public int? CategoryId { get; set; }

    [Required, MaxLength(200)]
    public string Title { get; set; } = string.Empty;

    [MaxLength(100)]
    public string DocumentType { get; set; } = string.Empty;   // Aadhaar, PAN, Passport, etc.

    // Sensitive fields — store encrypted, display masked
    [MaxLength(500)]
    public string? DocumentNumberEncrypted { get; set; }       // AES-256 encrypted

    [MaxLength(50)]
    public string? DocumentNumberMasked { get; set; }          // e.g. XXXX-XXXX-1234

    [MaxLength(200)]
    public string? HolderName { get; set; }

    [MaxLength(200)]
    public string? IssuingAuthority { get; set; }

    public DateTime? IssueDate { get; set; }
    public DateTime? ExpiryDate { get; set; }

    [MaxLength(50)]
    public string? VehicleNumber { get; set; }

    [MaxLength(100)]
    public string? PolicyNumber { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal? Amount { get; set; }

    [MaxLength(1000)]
    public string? Notes { get; set; }

    [MaxLength(500)]
    public string? Tags { get; set; }  // comma-separated

    public ProcessingStatus ProcessingStatus { get; set; } = ProcessingStatus.Pending;
    public ExpiryStatus ExpiryStatus { get; set; } = ExpiryStatus.Valid;

    [MaxLength(500)]
    public string? ProcessingErrorMessage { get; set; }

    public Guid? CurrentVersionId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    [ForeignKey(nameof(UserId))]
    public virtual ApplicationUser User { get; set; } = null!;

    [ForeignKey(nameof(CategoryId))]
    public virtual DocumentCategory? Category { get; set; }

    public virtual ICollection<DocumentVersion> Versions { get; set; } = new List<DocumentVersion>();
    public virtual ICollection<DocumentReminder> Reminders { get; set; } = new List<DocumentReminder>();
    public virtual ICollection<Notification> Notifications { get; set; } = new List<Notification>();
    public virtual ICollection<AIProcessingLog> AIProcessingLogs { get; set; } = new List<AIProcessingLog>();
}

public class DocumentVersion
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid DocumentId { get; set; }

    public int VersionNumber { get; set; } = 1;

    [Required, MaxLength(300)]
    public string OriginalFileName { get; set; } = string.Empty;

    [Required, MaxLength(600)]
    public string StoredFilePath { get; set; } = string.Empty;  // Relative to App_Data

    [MaxLength(100)]
    public string ContentType { get; set; } = string.Empty;

    public long FileSizeBytes { get; set; }

    [MaxLength(64)]
    public string? FileHashSha256 { get; set; }

    public bool IsActive { get; set; } = true;

    [MaxLength(500)]
    public string? ChangeSummary { get; set; }

    public DateTime UploadedAt { get; set; } = DateTime.UtcNow;

    [ForeignKey(nameof(DocumentId))]
    public virtual Document Document { get; set; } = null!;

    public virtual DocumentText? ExtractedText { get; set; }
}

public class DocumentText
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid DocumentVersionId { get; set; }

    public string? ExtractedText { get; set; }

    public float OcrConfidence { get; set; }

    public DateTime ExtractedAt { get; set; } = DateTime.UtcNow;

    [ForeignKey(nameof(DocumentVersionId))]
    public virtual DocumentVersion DocumentVersion { get; set; } = null!;
}

public class DocumentReminder
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid DocumentId { get; set; }

    public int DaysBefore { get; set; }  // 30, 15, 7, 1, 0

    public DateTime ScheduledForDate { get; set; }

    public DateTime? SentAt { get; set; }

    [MaxLength(20)]
    public string Status { get; set; } = "Pending";  // Pending, Sent, Failed, Skipped

    [ForeignKey(nameof(DocumentId))]
    public virtual Document Document { get; set; } = null!;
}

public class Notification
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    public string UserId { get; set; } = string.Empty;

    public Guid? DocumentId { get; set; }

    [Required, MaxLength(200)]
    public string Title { get; set; } = string.Empty;

    [Required, MaxLength(1000)]
    public string Message { get; set; } = string.Empty;

    [MaxLength(50)]
    public string Type { get; set; } = "Info";  // Info, Warning, Danger, Success

    public bool IsRead { get; set; } = false;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [ForeignKey(nameof(UserId))]
    public virtual ApplicationUser User { get; set; } = null!;

    [ForeignKey(nameof(DocumentId))]
    public virtual Document? Document { get; set; }
}

public class AuditLog
{
    [Key]
    public long Id { get; set; }

    public string? UserId { get; set; }

    [MaxLength(50)]
    public string Action { get; set; } = string.Empty;  // Upload, View, Download, Delete, Edit, Login, etc.

    [MaxLength(100)]
    public string? EntityName { get; set; }

    [MaxLength(100)]
    public string? EntityId { get; set; }

    [MaxLength(50)]
    public string? IpAddress { get; set; }

    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    [MaxLength(1000)]
    public string? Details { get; set; }

    [ForeignKey(nameof(UserId))]
    public virtual ApplicationUser? User { get; set; }
}

public class AIProcessingLog
{
    [Key]
    public long Id { get; set; }

    public Guid? DocumentId { get; set; }

    [MaxLength(100)]
    public string ModelName { get; set; } = string.Empty;

    public int PromptTokens { get; set; }
    public int ResponseTokens { get; set; }

    public bool Success { get; set; }

    [MaxLength(1000)]
    public string? ErrorMessage { get; set; }

    public string? RawResponse { get; set; }

    public DateTime ProcessedAt { get; set; } = DateTime.UtcNow;

    [ForeignKey(nameof(DocumentId))]
    public virtual Document? Document { get; set; }
}
