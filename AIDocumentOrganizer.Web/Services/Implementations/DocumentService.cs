using System.Security.Cryptography;
using System.Text;
using AIDocumentOrganizer.Web.Data;
using AIDocumentOrganizer.Web.Models.Entities;
using AIDocumentOrganizer.Web.Models.Enums;
using AIDocumentOrganizer.Web.Services.Interfaces;
using AIDocumentOrganizer.Web.ViewModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AIDocumentOrganizer.Web.Services.Implementations;

public class AppSettings
{
    public int MaxFileSizeMB { get; set; } = 15;
    public string[] AllowedExtensions { get; set; } = [".pdf", ".jpg", ".jpeg", ".png"];
    public string SecureUploadPath { get; set; } = "App_Data/SecureUploads";
    public string SupportEmail { get; set; } = string.Empty;
}

public class DocumentService : IDocumentService
{
    private readonly ApplicationDbContext _db;
    private readonly IOcrService _ocr;
    private readonly IAiDocumentService _ai;
    private readonly IAuditLogService _audit;
    private readonly ILogger<DocumentService> _logger;
    private readonly AppSettings _settings;
    private readonly IWebHostEnvironment _env;
    private readonly IConfiguration _config;
    private readonly byte[] _encryptionKey;

    private static readonly Dictionary<string, byte[]> MagicBytes = new()
    {
        { ".pdf",  new byte[] { 0x25, 0x50, 0x44, 0x46 } },
        { ".jpg",  new byte[] { 0xFF, 0xD8, 0xFF } },
        { ".jpeg", new byte[] { 0xFF, 0xD8, 0xFF } },
        { ".png",  new byte[] { 0x89, 0x50, 0x4E, 0x47 } }
    };

    public DocumentService(
        ApplicationDbContext db,
        IOcrService ocr,
        IAiDocumentService ai,
        IAuditLogService audit,
        ILogger<DocumentService> logger,
        IOptions<AppSettings> settings,
        IWebHostEnvironment env,
        IConfiguration config)
    {
        _db = db;
        _ocr = ocr;
        _ai = ai;
        _audit = audit;
        _logger = logger;
        _settings = settings.Value;
        _env = env;
        _config = config;
        // Derive a stable 256-bit key from a config secret (or fallback for dev)
        var secret = config["AppSettings:EncryptionSecret"] ?? "DocOrganizer_Dev_Key_CHANGE_IN_PROD!";
        _encryptionKey = SHA256.HashData(Encoding.UTF8.GetBytes(secret));
    }

    // ─────────── File Validation ───────────

    public async Task<(bool Valid, string? Error)> ValidateFileAsync(IFormFile file)
    {
        if (file == null || file.Length == 0)
            return (false, "No file selected.");

        var maxBytes = _settings.MaxFileSizeMB * 1024 * 1024;
        if (file.Length > maxBytes)
            return (false, $"File exceeds maximum size of {_settings.MaxFileSizeMB} MB.");

        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!_settings.AllowedExtensions.Contains(ext))
            return (false, $"File type '{ext}' is not allowed. Accepted: {string.Join(", ", _settings.AllowedExtensions)}");

        // Magic byte validation
        if (MagicBytes.TryGetValue(ext, out var magic))
        {
            var buf = new byte[magic.Length];
            await file.OpenReadStream().ReadAsync(buf);
            if (!buf.Take(magic.Length).SequenceEqual(magic))
                return (false, "File content does not match its extension. Upload refused.");
        }

        return (true, null);
    }

    // ─────────── Upload ───────────

    public async Task<Document> UploadDocumentAsync(string userId, DocumentUploadViewModel model)
    {
        var file = model.File!;
        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        var safeGuid = Guid.NewGuid().ToString("N");
        var safeName = $"{safeGuid}{ext}";

        var uploadDir = Path.Combine(_env.ContentRootPath, _settings.SecureUploadPath, userId);
        Directory.CreateDirectory(uploadDir);
        var physicalPath = Path.Combine(uploadDir, safeName);

        using (var fs = new FileStream(physicalPath, FileMode.Create))
            await file.CopyToAsync(fs);

        // Compute SHA-256 hash
        var hash = await ComputeFileHashAsync(physicalPath);

        var relPath = Path.Combine(_settings.SecureUploadPath, userId, safeName)
                          .Replace("\\", "/");

        var document = new Document
        {
            UserId = userId,
            CategoryId = model.CategoryId,
            Title = model.Title,
            ProcessingStatus = ProcessingStatus.Pending
        };
        _db.Documents.Add(document);

        var version = new DocumentVersion
        {
            DocumentId = document.Id,
            VersionNumber = 1,
            OriginalFileName = Path.GetFileName(file.FileName),
            StoredFilePath = relPath,
            ContentType = file.ContentType,
            FileSizeBytes = file.Length,
            FileHashSha256 = hash,
            IsActive = true
        };
        _db.DocumentVersions.Add(version);

        document.CurrentVersionId = version.Id;
        await _db.SaveChangesAsync();

        await _audit.LogAsync(userId, "Upload", "Document", document.Id.ToString(),
            $"Uploaded '{model.Title}'");

        return document;
    }

    public async Task TriggerProcessingAsync(Guid documentId)
    {
        var doc = await _db.Documents
            .Include(d => d.Versions).ThenInclude(v => v.ExtractedText)
            .FirstOrDefaultAsync(d => d.Id == documentId);
        if (doc is null) return;

        var version = doc.Versions.FirstOrDefault(v => v.IsActive);
        if (version is null) return;

        doc.ProcessingStatus = ProcessingStatus.Processing;
        await _db.SaveChangesAsync();

        try
        {
            var physPath = Path.Combine(_env.ContentRootPath, version.StoredFilePath.Replace("/", "\\"));

            // OCR
            var ocrResult = await _ocr.ExtractTextAsync(physPath, version.ContentType);

            // Save extracted text
            if (version.ExtractedText is null)
            {
                _db.DocumentTexts.Add(new DocumentText
                {
                    DocumentVersionId = version.Id,
                    ExtractedText = ocrResult.Text,
                    OcrConfidence = ocrResult.Confidence
                });
            }
            else
            {
                version.ExtractedText.ExtractedText = ocrResult.Text;
                version.ExtractedText.OcrConfidence = ocrResult.Confidence;
            }

            // AI Analysis
            var aiResult = await _ai.AnalyzeDocumentAsync(ocrResult.Text, version.OriginalFileName);

            // Log AI usage
            _db.AIProcessingLogs.Add(new AIProcessingLog
            {
                DocumentId = documentId,
                ModelName = "gemini-1.5-flash",
                Success = aiResult.Success,
                ErrorMessage = aiResult.Error,
                RawResponse = aiResult.RawResponse,
                ProcessedAt = DateTime.UtcNow
            });

            if (aiResult.Success)
            {
                // Map category by name
                if (!string.IsNullOrWhiteSpace(aiResult.Category))
                {
                    var cat = await _db.DocumentCategories
                        .FirstOrDefaultAsync(c => EF.Functions.Like(c.Name, aiResult.Category));
                    if (cat is not null) doc.CategoryId = cat.Id;
                }

                doc.DocumentType = aiResult.DocumentType ?? doc.DocumentType;
                doc.HolderName = aiResult.HolderName;
                doc.IssuingAuthority = aiResult.IssuingAuthority;
                doc.IssueDate = aiResult.IssueDate;
                doc.ExpiryDate = aiResult.ExpiryDate;
                doc.VehicleNumber = aiResult.VehicleNumber;
                doc.PolicyNumber = aiResult.PolicyNumber;
                doc.Amount = aiResult.Amount;
                doc.Notes = aiResult.ImportantNotes;
                doc.Tags = aiResult.Tags is not null ? string.Join(",", aiResult.Tags) : null;

                if (!string.IsNullOrWhiteSpace(aiResult.DocumentNumber))
                {
                    doc.DocumentNumberEncrypted = Encrypt(aiResult.DocumentNumber);
                    doc.DocumentNumberMasked = MaskDocumentNumber(aiResult.DocumentNumber);
                }

                doc.ProcessingStatus = ProcessingStatus.NeedsReview;
                doc.ExpiryStatus = CalculateExpiryStatus(doc.ExpiryDate);
            }
            else
            {
                doc.ProcessingStatus = ProcessingStatus.Failed;
                doc.ProcessingErrorMessage = aiResult.Error;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing document {DocId}", documentId);
            doc.ProcessingStatus = ProcessingStatus.Failed;
            doc.ProcessingErrorMessage = "Processing failed. Please retry or enter details manually.";
        }

        doc.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
    }

    public async Task RetryProcessingAsync(Guid documentId, string userId)
    {
        if (!await DocumentBelongsToUserAsync(documentId, userId)) return;
        await TriggerProcessingAsync(documentId);
    }

    // ─────────── Queries ───────────

    public async Task<DocumentListViewModel> GetDocumentsAsync(string userId, DocumentListViewModel filter)
    {
        var query = _db.Documents
            .Include(d => d.Category)
            .Where(d => d.UserId == userId)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(filter.SearchTerm))
        {
            var term = filter.SearchTerm.ToLower();
            query = query.Where(d =>
                d.Title.ToLower().Contains(term) ||
                d.DocumentType.ToLower().Contains(term) ||
                (d.HolderName != null && d.HolderName.ToLower().Contains(term)) ||
                (d.Tags != null && d.Tags.ToLower().Contains(term)) ||
                (d.DocumentNumberMasked != null && d.DocumentNumberMasked.ToLower().Contains(term)));
        }

        if (filter.CategoryId.HasValue)
            query = query.Where(d => d.CategoryId == filter.CategoryId);

        if (!string.IsNullOrWhiteSpace(filter.ExpiryFilter))
        {
            query = filter.ExpiryFilter.ToLower() switch
            {
                "valid" => query.Where(d => d.ExpiryStatus == ExpiryStatus.Valid || d.ExpiryStatus == ExpiryStatus.Lifetime),
                "expiringsoon" => query.Where(d => d.ExpiryStatus == ExpiryStatus.ExpiringSoon),
                "expired" => query.Where(d => d.ExpiryStatus == ExpiryStatus.Expired),
                _ => query
            };
        }

        query = (filter.SortBy, filter.SortDir) switch
        {
            (DocumentSortBy.ExpiryDate, "asc") => query.OrderBy(d => d.ExpiryDate),
            (DocumentSortBy.ExpiryDate, _) => query.OrderByDescending(d => d.ExpiryDate),
            (DocumentSortBy.Name, "asc") => query.OrderBy(d => d.Title),
            (DocumentSortBy.Name, _) => query.OrderByDescending(d => d.Title),
            (DocumentSortBy.Category, _) => query.OrderBy(d => d.Category!.Name),
            _ => filter.SortDir == "asc" ? query.OrderBy(d => d.CreatedAt) : query.OrderByDescending(d => d.CreatedAt)
        };

        const int pageSize = 12;
        var total = await query.CountAsync();
        var docs = await query.Skip((filter.Page - 1) * pageSize).Take(pageSize).ToListAsync();

        filter.TotalCount = total;
        filter.TotalPages = (int)Math.Ceiling(total / (double)pageSize);
        filter.Documents = docs.Select(MapToListItem).ToList();
        filter.Categories = await GetCategorySelectListAsync();
        return filter;
    }

    public async Task<DocumentDetailsViewModel?> GetDocumentDetailsAsync(Guid documentId, string userId)
    {
        var doc = await _db.Documents
            .Include(d => d.Category)
            .Include(d => d.Versions).ThenInclude(v => v.ExtractedText)
            .FirstOrDefaultAsync(d => d.Id == documentId && d.UserId == userId);

        if (doc is null) return null;

        var activeVersion = doc.Versions.FirstOrDefault(v => v.IsActive);

        return new DocumentDetailsViewModel
        {
            Id = doc.Id,
            Title = doc.Title,
            DocumentType = doc.DocumentType,
            DocumentNumberMasked = doc.DocumentNumberMasked,
            HolderName = doc.HolderName,
            IssuingAuthority = doc.IssuingAuthority,
            IssueDate = doc.IssueDate,
            ExpiryDate = doc.ExpiryDate,
            DaysRemaining = doc.ExpiryDate.HasValue ? (int)(doc.ExpiryDate.Value.Date - DateTime.UtcNow.Date).TotalDays : null,
            ExpiryStatus = doc.ExpiryStatus,
            ProcessingStatus = doc.ProcessingStatus,
            ProcessingErrorMessage = doc.ProcessingErrorMessage,
            VehicleNumber = doc.VehicleNumber,
            PolicyNumber = doc.PolicyNumber,
            Amount = doc.Amount,
            Notes = doc.Notes,
            Tags = doc.Tags,
            CategoryName = doc.Category?.Name,
            CategoryColor = doc.Category?.ColorCode,
            CategoryIcon = doc.Category?.IconClass,
            ExtractedText = activeVersion?.ExtractedText?.ExtractedText,
            CreatedAt = doc.CreatedAt,
            UpdatedAt = doc.UpdatedAt,
            ContentType = activeVersion?.ContentType,
            CurrentVersionId = doc.CurrentVersionId,
            Versions = doc.Versions.OrderByDescending(v => v.VersionNumber).Select(v => new DocumentVersionItem
            {
                Id = v.Id,
                VersionNumber = v.VersionNumber,
                OriginalFileName = v.OriginalFileName,
                FileSizeBytes = v.FileSizeBytes,
                IsActive = v.IsActive,
                ChangeSummary = v.ChangeSummary,
                UploadedAt = v.UploadedAt,
                ContentType = v.ContentType
            }).ToList()
        };
    }

    public async Task<Document?> GetDocumentEntityAsync(Guid documentId, string userId) =>
        await _db.Documents.FirstOrDefaultAsync(d => d.Id == documentId && d.UserId == userId);

    public async Task<DashboardViewModel> GetDashboardDataAsync(string userId)
    {
        var now = DateTime.UtcNow;
        var monthStart = new DateTime(now.Year, now.Month, 1);
        var monthEnd = monthStart.AddMonths(1);

        var docs = await _db.Documents.Include(d => d.Category)
            .Where(d => d.UserId == userId).ToListAsync();

        var upcoming = docs
            .Where(d => d.ExpiryDate.HasValue && d.ExpiryDate.Value >= now)
            .OrderBy(d => d.ExpiryDate)
            .Take(5).Select(MapToDashItem).ToList();

        var recent = docs.OrderByDescending(d => d.CreatedAt).Take(6).Select(MapToDashItem).ToList();

        var catStats = docs
            .GroupBy(d => new { Name = d.Category?.Name ?? "Other", Color = d.Category?.ColorCode ?? "#6c757d", Icon = d.Category?.IconClass ?? "bi-folder" })
            .Select(g => new CategoryStat { Name = g.Key.Name, ColorCode = g.Key.Color, IconClass = g.Key.Icon, Count = g.Count() })
            .OrderByDescending(x => x.Count).ToList();

        return new DashboardViewModel
        {
            TotalDocuments = docs.Count,
            ExpiringThisMonth = docs.Count(d => d.ExpiryDate >= now && d.ExpiryDate < now.AddDays(30)),
            ExpiredDocuments = docs.Count(d => d.ExpiryStatus == ExpiryStatus.Expired),
            AddedThisMonth = docs.Count(d => d.CreatedAt >= monthStart && d.CreatedAt < monthEnd),
            UnreadNotifications = await _db.Notifications.CountAsync(n => n.UserId == userId && !n.IsRead),
            UpcomingExpiries = upcoming,
            RecentUploads = recent,
            CategoryStats = catStats
        };
    }

    // ─────────── Mutations ───────────

    public async Task<bool> UpdateDocumentAsync(DocumentEditViewModel model, string userId)
    {
        var doc = await _db.Documents
            .Include(d => d.Versions)
            .FirstOrDefaultAsync(d => d.Id == model.Id && d.UserId == userId);
        if (doc is null) return false;

        doc.Title = model.Title;
        doc.DocumentType = model.DocumentType;
        doc.HolderName = model.HolderName;
        doc.IssuingAuthority = model.IssuingAuthority;
        doc.IssueDate = model.IssueDate;
        doc.ExpiryDate = model.ExpiryDate;
        doc.VehicleNumber = model.VehicleNumber;
        doc.PolicyNumber = model.PolicyNumber;
        doc.Amount = model.Amount;
        doc.Notes = model.Notes;
        doc.Tags = model.Tags;
        doc.CategoryId = model.CategoryId;

        if (!string.IsNullOrWhiteSpace(model.DocumentNumber))
        {
            doc.DocumentNumberEncrypted = Encrypt(model.DocumentNumber);
            doc.DocumentNumberMasked = MaskDocumentNumber(model.DocumentNumber);
        }

        // Handle new version upload
        if (model.NewFile is not null)
        {
            var ext = Path.GetExtension(model.NewFile.FileName).ToLowerInvariant();
            var safeGuid = Guid.NewGuid().ToString("N");
            var safeName = $"{safeGuid}{ext}";
            var uploadDir = Path.Combine(_env.ContentRootPath, _settings.SecureUploadPath, userId);
            Directory.CreateDirectory(uploadDir);
            var physPath = Path.Combine(uploadDir, safeName);

            using (var fs = new FileStream(physPath, FileMode.Create))
                await model.NewFile.CopyToAsync(fs);

            var hash = await ComputeFileHashAsync(physPath);
            var relPath = Path.Combine(_settings.SecureUploadPath, userId, safeName).Replace("\\", "/");
            var nextVersion = doc.Versions.Any() ? doc.Versions.Max(v => v.VersionNumber) + 1 : 1;

            // Deactivate old
            foreach (var v in doc.Versions) v.IsActive = false;

            var newVer = new DocumentVersion
            {
                DocumentId = doc.Id,
                VersionNumber = nextVersion,
                OriginalFileName = Path.GetFileName(model.NewFile.FileName),
                StoredFilePath = relPath,
                ContentType = model.NewFile.ContentType,
                FileSizeBytes = model.NewFile.Length,
                FileHashSha256 = hash,
                IsActive = true,
                ChangeSummary = model.ChangeSummary
            };
            _db.DocumentVersions.Add(newVer);
            doc.CurrentVersionId = newVer.Id;
        }

        doc.ExpiryStatus = CalculateExpiryStatus(doc.ExpiryDate);
        if (doc.ProcessingStatus == ProcessingStatus.NeedsReview || doc.ProcessingStatus == ProcessingStatus.Failed)
            doc.ProcessingStatus = ProcessingStatus.Completed;

        doc.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        await _audit.LogAsync(userId, "Edit", "Document", doc.Id.ToString(), $"Edited '{doc.Title}'");
        return true;
    }

    public async Task<bool> DeleteDocumentAsync(Guid documentId, string userId)
    {
        var doc = await _db.Documents
            .Include(d => d.Versions)
            .FirstOrDefaultAsync(d => d.Id == documentId && d.UserId == userId);
        if (doc is null) return false;

        // Delete physical files
        foreach (var ver in doc.Versions)
        {
            var physPath = Path.Combine(_env.ContentRootPath, ver.StoredFilePath.Replace("/", "\\"));
            if (File.Exists(physPath)) File.Delete(physPath);
        }

        _db.Documents.Remove(doc);
        await _db.SaveChangesAsync();
        await _audit.LogAsync(userId, "Delete", "Document", documentId.ToString(), $"Deleted '{doc.Title}'");
        return true;
    }

    // ─────────── File Access ───────────

    public async Task<(Stream FileStream, string ContentType, string FileName)?> GetDocumentFileAsync(Guid versionId, string userId)
    {
        var version = await _db.DocumentVersions
            .Include(v => v.Document)
            .FirstOrDefaultAsync(v => v.Id == versionId && v.Document.UserId == userId);

        if (version is null) return null;

        var physPath = Path.Combine(_env.ContentRootPath, version.StoredFilePath.Replace("/", "\\"));
        if (!File.Exists(physPath)) return null;

        return (File.OpenRead(physPath), version.ContentType, version.OriginalFileName);
    }

    public async Task<bool> DocumentBelongsToUserAsync(Guid documentId, string userId) =>
        await _db.Documents.AnyAsync(d => d.Id == documentId && d.UserId == userId);

    // ─────────── Expiry & Notifications ───────────

    public async Task UpdateExpiryStatusesAsync()
    {
        var docs = await _db.Documents.Where(d => d.ExpiryDate.HasValue).ToListAsync();
        foreach (var doc in docs)
        {
            var newStatus = CalculateExpiryStatus(doc.ExpiryDate);
            if (doc.ExpiryStatus != newStatus)
            {
                doc.ExpiryStatus = newStatus;
                doc.UpdatedAt = DateTime.UtcNow;
            }
        }
        await _db.SaveChangesAsync();
    }

    public async Task<List<NotificationItem>> GetUserNotificationsAsync(string userId, int take = 20)
    {
        return await _db.Notifications
            .Include(n => n.Document)
            .Where(n => n.UserId == userId)
            .OrderByDescending(n => n.CreatedAt)
            .Take(take)
            .Select(n => new NotificationItem
            {
                Id = n.Id,
                Title = n.Title,
                Message = n.Message,
                Type = n.Type,
                IsRead = n.IsRead,
                DocumentId = n.DocumentId,
                DocumentTitle = n.Document != null ? n.Document.Title : null,
                CreatedAt = n.CreatedAt
            }).ToListAsync();
    }

    public async Task MarkNotificationsReadAsync(string userId)
    {
        var unread = await _db.Notifications.Where(n => n.UserId == userId && !n.IsRead).ToListAsync();
        foreach (var n in unread) n.IsRead = true;
        await _db.SaveChangesAsync();
    }

    // ─────────── Helpers ───────────

    private static ExpiryStatus CalculateExpiryStatus(DateTime? expiryDate)
    {
        if (!expiryDate.HasValue) return ExpiryStatus.Lifetime;
        var days = (expiryDate.Value.Date - DateTime.UtcNow.Date).TotalDays;
        return days < 0 ? ExpiryStatus.Expired :
               days <= 30 ? ExpiryStatus.ExpiringSoon :
               ExpiryStatus.Valid;
    }

    private static string MaskDocumentNumber(string number)
    {
        if (string.IsNullOrWhiteSpace(number) || number.Length <= 4) return "****";
        return new string('X', number.Length - 4) + number[^4..];
    }

    private string Encrypt(string plainText)
    {
        using var aes = Aes.Create();
        aes.Key = _encryptionKey;
        aes.GenerateIV();
        var enc = aes.CreateEncryptor();
        var bytes = Encoding.UTF8.GetBytes(plainText);
        var cipher = enc.TransformFinalBlock(bytes, 0, bytes.Length);
        return Convert.ToBase64String(aes.IV) + ":" + Convert.ToBase64String(cipher);
    }

    private static async Task<string> ComputeFileHashAsync(string filePath)
    {
        using var fs = File.OpenRead(filePath);
        var hashBytes = await SHA256.HashDataAsync(fs);
        return Convert.ToHexString(hashBytes).ToLower();
    }

    private static DocumentListItem MapToListItem(Document doc) => new()
    {
        Id = doc.Id,
        Title = doc.Title,
        DocumentType = doc.DocumentType,
        DocumentNumberMasked = doc.DocumentNumberMasked,
        HolderName = doc.HolderName,
        CategoryName = doc.Category?.Name,
        CategoryColor = doc.Category?.ColorCode,
        CategoryIcon = doc.Category?.IconClass,
        ExpiryDate = doc.ExpiryDate,
        DaysRemaining = doc.ExpiryDate.HasValue ? (int)(doc.ExpiryDate.Value.Date - DateTime.UtcNow.Date).TotalDays : null,
        ExpiryStatus = doc.ExpiryStatus,
        ProcessingStatus = doc.ProcessingStatus,
        CreatedAt = doc.CreatedAt,
        Tags = doc.Tags
    };

    private static DashboardDocumentItem MapToDashItem(Document doc) => new()
    {
        Id = doc.Id,
        Title = doc.Title,
        DocumentType = doc.DocumentType,
        CategoryName = doc.Category?.Name,
        CategoryColor = doc.Category?.ColorCode,
        CategoryIcon = doc.Category?.IconClass,
        ExpiryDate = doc.ExpiryDate,
        DaysRemaining = doc.ExpiryDate.HasValue ? (int)(doc.ExpiryDate.Value.Date - DateTime.UtcNow.Date).TotalDays : null,
        ExpiryStatus = doc.ExpiryStatus,
        CreatedAt = doc.CreatedAt
    };

    private async Task<List<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem>> GetCategorySelectListAsync() =>
        await _db.DocumentCategories.OrderBy(c => c.DisplayOrder)
            .Select(c => new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem { Value = c.Id.ToString(), Text = c.Name })
            .ToListAsync();
}
