using System.ComponentModel.DataAnnotations;
using AIDocumentOrganizer.Web.Models.Entities;
using AIDocumentOrganizer.Web.Models.Enums;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace AIDocumentOrganizer.Web.ViewModels;

// ─────────────────────────── Upload ───────────────────────────

public class DocumentUploadViewModel
{
    [Required(ErrorMessage = "Please select a file.")]
    [Display(Name = "Document File")]
    public IFormFile? File { get; set; }

    [Required, MaxLength(200)]
    [Display(Name = "Document Title")]
    public string Title { get; set; } = string.Empty;

    [Display(Name = "Category")]
    public int? CategoryId { get; set; }

    public List<SelectListItem> Categories { get; set; } = new();
}

// ─────────────────────────── List / Search ───────────────────────────

public class DocumentListViewModel
{
    public List<DocumentListItem> Documents { get; set; } = new();
    public string? SearchTerm { get; set; }
    public int? CategoryId { get; set; }
    public string? ExpiryFilter { get; set; }   // "all", "valid", "expiringsoon", "expired"
    public DocumentSortBy SortBy { get; set; } = DocumentSortBy.UploadDate;
    public string SortDir { get; set; } = "desc";
    public int Page { get; set; } = 1;
    public int TotalPages { get; set; }
    public int TotalCount { get; set; }
    public List<SelectListItem> Categories { get; set; } = new();
}

public class DocumentListItem
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string DocumentType { get; set; } = string.Empty;
    public string? DocumentNumberMasked { get; set; }
    public string? HolderName { get; set; }
    public string? CategoryName { get; set; }
    public string? CategoryColor { get; set; }
    public string? CategoryIcon { get; set; }
    public DateTime? ExpiryDate { get; set; }
    public int? DaysRemaining { get; set; }
    public ExpiryStatus ExpiryStatus { get; set; }
    public ProcessingStatus ProcessingStatus { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? Tags { get; set; }
}

// ─────────────────────────── Details ───────────────────────────

public class DocumentDetailsViewModel
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string DocumentType { get; set; } = string.Empty;
    public string? DocumentNumberMasked { get; set; }
    public string? HolderName { get; set; }
    public string? IssuingAuthority { get; set; }
    public DateTime? IssueDate { get; set; }
    public DateTime? ExpiryDate { get; set; }
    public int? DaysRemaining { get; set; }
    public ExpiryStatus ExpiryStatus { get; set; }
    public ProcessingStatus ProcessingStatus { get; set; }
    public string? ProcessingErrorMessage { get; set; }
    public string? VehicleNumber { get; set; }
    public string? PolicyNumber { get; set; }
    public decimal? Amount { get; set; }
    public string? Notes { get; set; }
    public string? Tags { get; set; }
    public string? CategoryName { get; set; }
    public string? CategoryColor { get; set; }
    public string? CategoryIcon { get; set; }
    public string? ExtractedText { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public string? ContentType { get; set; }
    public Guid? CurrentVersionId { get; set; }
    public List<DocumentVersionItem> Versions { get; set; } = new();
}

public class DocumentVersionItem
{
    public Guid Id { get; set; }
    public int VersionNumber { get; set; }
    public string OriginalFileName { get; set; } = string.Empty;
    public long FileSizeBytes { get; set; }
    public bool IsActive { get; set; }
    public string? ChangeSummary { get; set; }
    public DateTime UploadedAt { get; set; }
    public string ContentType { get; set; } = string.Empty;
}

// ─────────────────────────── Edit ───────────────────────────

public class DocumentEditViewModel
{
    public Guid Id { get; set; }

    [Required, MaxLength(200)]
    [Display(Name = "Document Title")]
    public string Title { get; set; } = string.Empty;

    [MaxLength(100)]
    [Display(Name = "Document Type")]
    public string DocumentType { get; set; } = string.Empty;

    [MaxLength(200)]
    [Display(Name = "Holder Name")]
    public string? HolderName { get; set; }

    [MaxLength(200)]
    [Display(Name = "Issuing Authority")]
    public string? IssuingAuthority { get; set; }

    [DataType(DataType.Date)]
    [Display(Name = "Issue Date")]
    public DateTime? IssueDate { get; set; }

    [DataType(DataType.Date)]
    [Display(Name = "Expiry Date")]
    public DateTime? ExpiryDate { get; set; }

    [Display(Name = "Document Number")]
    public string? DocumentNumber { get; set; }   // For editing — will be re-encrypted

    [MaxLength(50)]
    [Display(Name = "Vehicle Number")]
    public string? VehicleNumber { get; set; }

    [MaxLength(100)]
    [Display(Name = "Policy Number")]
    public string? PolicyNumber { get; set; }

    [Display(Name = "Amount")]
    public decimal? Amount { get; set; }

    [MaxLength(1000)]
    [Display(Name = "Notes")]
    public string? Notes { get; set; }

    [MaxLength(500)]
    [Display(Name = "Tags (comma-separated)")]
    public string? Tags { get; set; }

    [Display(Name = "Category")]
    public int? CategoryId { get; set; }

    [Display(Name = "Replace Document File (optional)")]
    public IFormFile? NewFile { get; set; }

    [MaxLength(500)]
    [Display(Name = "Change Summary")]
    public string? ChangeSummary { get; set; }

    public List<SelectListItem> Categories { get; set; } = new();
}

// ─────────────────────────── AI Assistant ───────────────────────────

public class AiAssistantViewModel
{
    public List<ChatMessage> History { get; set; } = new();
}

public class ChatMessage
{
    public string Role { get; set; } = "user";  // "user" or "assistant"
    public string Content { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

public class AiChatRequest
{
    [Required, MaxLength(1000)]
    public string Message { get; set; } = string.Empty;
}

public class AiChatResponse
{
    public bool Success { get; set; }
    public string Response { get; set; } = string.Empty;
    public string? Error { get; set; }
}

// ─────────────────────────── Admin ───────────────────────────

public class AdminDashboardViewModel
{
    public int TotalUsers { get; set; }
    public int TotalDocuments { get; set; }
    public long TotalStorageBytes { get; set; }
    public int FailedProcessingCount { get; set; }
    public int RemindersSentToday { get; set; }
    public List<AdminUserItem> RecentUsers { get; set; } = new();
    public List<AdminAiLogItem> RecentAiLogs { get; set; } = new();
    public List<CategoryStat> CategoryStats { get; set; } = new();
}

public class AdminUserItem
{
    public string Id { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public int DocumentCount { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? LastLoginAt { get; set; }
    public List<string> Roles { get; set; } = new();
}

public class AdminAiLogItem
{
    public long Id { get; set; }
    public string? DocumentTitle { get; set; }
    public string ModelName { get; set; } = string.Empty;
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public int TotalTokens { get; set; }
    public DateTime ProcessedAt { get; set; }
}

public class AdminCategoryItem
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string IconClass { get; set; } = "bi-folder";
    public string ColorCode { get; set; } = "#6c757d";
    public int DisplayOrder { get; set; }
    public bool IsSystemDefault { get; set; }
    public int DocumentCount { get; set; }
}

public class AdminCategoryEditViewModel
{
    public int Id { get; set; }

    [Required, MaxLength(80)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(60)]
    public string IconClass { get; set; } = "bi-folder";

    [MaxLength(20)]
    public string ColorCode { get; set; } = "#4361ee";

    public int DisplayOrder { get; set; } = 1;
}

public class AdminDocumentsViewModel
{
    public string? Search { get; set; }
    public int? CategoryId { get; set; }
    public ProcessingStatus? ProcessingStatus { get; set; }
    public ExpiryStatus? ExpiryStatus { get; set; }
    public int Page { get; set; } = 1;
    public int TotalPages { get; set; }
    public int TotalCount { get; set; }
    public List<SelectListItem> Categories { get; set; } = new();
    public List<AdminDocumentItem> Documents { get; set; } = new();
}

public class AdminDocumentItem
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string DocumentType { get; set; } = string.Empty;
    public string? DocumentNumberMasked { get; set; }
    public string? HolderName { get; set; }
    public string? CategoryName { get; set; }
    public string? CategoryColor { get; set; }
    public string? CategoryIcon { get; set; }
    public long FileSizeBytes { get; set; }
    public ProcessingStatus ProcessingStatus { get; set; }
    public ExpiryStatus ExpiryStatus { get; set; }
    public DateTime? ExpiryDate { get; set; }
    public string OwnerName { get; set; } = string.Empty;
    public string OwnerEmail { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public int VersionCount { get; set; }
}

public class AdminSystemHealthViewModel
{
    public string DotNetVersion { get; set; } = string.Empty;
    public string OsDescription { get; set; } = string.Empty;
    public string MachineName { get; set; } = string.Empty;
    public DateTime ServerTimeUtc { get; set; }
    public string EnvironmentName { get; set; } = string.Empty;

    public long TotalStorageBytes { get; set; }
    public long PhysicalStorageBytes { get; set; }
    public int TotalVersions { get; set; }
    public int MaxFileSizeMB { get; set; }
    public string[] AllowedExtensions { get; set; } = Array.Empty<string>();

    public string GeminiModel { get; set; } = string.Empty;
    public bool HasApiKey { get; set; }
    public int TotalAiRequests { get; set; }
    public int SuccessfulAiRequests { get; set; }
    public int FailedAiRequests { get; set; }
    public int TotalAiTokens { get; set; }

    public int TotalUsers { get; set; }
    public int TotalDocuments { get; set; }
    public int TotalCategories { get; set; }
    public int TotalReminders { get; set; }
    public int TotalAuditLogs { get; set; }
    public int TotalNotifications { get; set; }
}

public class AdminBroadcastViewModel
{
    [Required, MaxLength(150)]
    public string Title { get; set; } = string.Empty;

    [Required, MaxLength(1000)]
    public string Message { get; set; } = string.Empty;

    [MaxLength(30)]
    public string Type { get; set; } = "Info"; // Info, Warning, Danger, Success

    public List<AdminBroadcastItem> RecentBroadcasts { get; set; } = new();
}

public class AdminBroadcastItem
{
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string Type { get; set; } = "Info";
    public DateTime CreatedAt { get; set; }
    public int RecipientCount { get; set; }
}

public class AdminUsersViewModel
{
    public string? Search { get; set; }
    public string? RoleFilter { get; set; }
    public string? StatusFilter { get; set; }
    public int TotalUsers { get; set; }
    public int ActiveUsers { get; set; }
    public int InactiveUsers { get; set; }
    public int AdminCount { get; set; }
    public List<AdminUserItem> Users { get; set; } = new();
}

public class NotificationViewModel
{
    public List<NotificationItem> Notifications { get; set; } = new();
    public int UnreadCount { get; set; }
}

public class NotificationItem
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string Type { get; set; } = "Info";
    public bool IsRead { get; set; }
    public Guid? DocumentId { get; set; }
    public string? DocumentTitle { get; set; }
    public DateTime CreatedAt { get; set; }
}

