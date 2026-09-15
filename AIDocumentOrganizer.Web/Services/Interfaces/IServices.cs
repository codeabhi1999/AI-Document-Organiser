using AIDocumentOrganizer.Web.Models.Entities;
using AIDocumentOrganizer.Web.Models.Enums;
using AIDocumentOrganizer.Web.ViewModels;

namespace AIDocumentOrganizer.Web.Services.Interfaces;

public interface IDocumentService
{
    // Upload & Validation
    Task<(bool Valid, string? Error)> ValidateFileAsync(IFormFile file);
    Task<Document> UploadDocumentAsync(string userId, DocumentUploadViewModel model);
    Task TriggerProcessingAsync(Guid documentId);
    Task RetryProcessingAsync(Guid documentId, string userId);

    // Queries — always scoped to userId for security
    Task<DocumentListViewModel> GetDocumentsAsync(string userId, DocumentListViewModel filter);
    Task<DocumentDetailsViewModel?> GetDocumentDetailsAsync(Guid documentId, string userId);
    Task<Document?> GetDocumentEntityAsync(Guid documentId, string userId);
    Task<DashboardViewModel> GetDashboardDataAsync(string userId);

    // Mutations
    Task<bool> UpdateDocumentAsync(DocumentEditViewModel model, string userId);
    Task<bool> DeleteDocumentAsync(Guid documentId, string userId);

    // File Access
    Task<(Stream FileStream, string ContentType, string FileName)?> GetDocumentFileAsync(Guid versionId, string userId);
    Task<bool> DocumentBelongsToUserAsync(Guid documentId, string userId);

    // Notifications & Expiry
    Task UpdateExpiryStatusesAsync();
    Task<List<NotificationItem>> GetUserNotificationsAsync(string userId, int take = 20);
    Task MarkNotificationsReadAsync(string userId);
}

public interface IOcrService
{
    Task<OcrResult> ExtractTextAsync(string filePath, string contentType);
}

public record OcrResult(string Text, float Confidence, bool Success, string? Error = null);

public interface IAiDocumentService
{
    Task<AiExtractionResult> AnalyzeDocumentAsync(string extractedText, string originalFileName);
    Task<string> ChatAsync(string userMessage, string userId, List<DocumentSummaryForAi> userDocuments);
}

public record AiExtractionResult(
    bool Success,
    string? Category,
    string? DocumentType,
    string? DocumentNumber,
    string? HolderName,
    DateTime? IssueDate,
    DateTime? ExpiryDate,
    string? IssuingAuthority,
    string? VehicleNumber,
    string? PolicyNumber,
    decimal? Amount,
    string? ImportantNotes,
    List<string>? Tags,
    double ConfidenceScore,
    string? Error,
    string? RawResponse
);

public record DocumentSummaryForAi(
    Guid Id,
    string Title,
    string DocumentType,
    string? Category,
    string? HolderName,
    DateTime? ExpiryDate,
    int? DaysRemaining,
    string? Tags
);

public interface IEmailService
{
    Task SendAsync(string to, string subject, string htmlBody);
    Task SendExpiryReminderAsync(string to, string userName, string documentTitle, int daysRemaining, DateTime expiryDate);
}

public interface IAuditLogService
{
    Task LogAsync(string? userId, string action, string? entityName = null, string? entityId = null, string? details = null, string? ip = null);
}
