using System.Runtime.InteropServices;
using System.Security.Claims;
using System.Text;
using AIDocumentOrganizer.Web.Data;
using AIDocumentOrganizer.Web.Models.Entities;
using AIDocumentOrganizer.Web.Models.Enums;
using AIDocumentOrganizer.Web.Services.Implementations;
using AIDocumentOrganizer.Web.Services.Interfaces;
using AIDocumentOrganizer.Web.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace AIDocumentOrganizer.Web.Controllers;

[Authorize(Roles = "Admin")]
public class AdminController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userMgr;
    private readonly IWebHostEnvironment _env;
    private readonly IDocumentService _docService;
    private readonly IAuditLogService _audit;
    private readonly AppSettings _appSettings;
    private readonly AiServiceSettings _aiSettings;

    public AdminController(
        ApplicationDbContext db,
        UserManager<ApplicationUser> userMgr,
        IWebHostEnvironment env,
        IDocumentService docService,
        IAuditLogService audit,
        AppSettings appSettings,
        AiServiceSettings aiSettings)
    {
        _db = db;
        _userMgr = userMgr;
        _env = env;
        _docService = docService;
        _audit = audit;
        _appSettings = appSettings;
        _aiSettings = aiSettings;
    }

    private string CurrentUserId => User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;

    // ─────────────────────────── Dashboard ───────────────────────────

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var totalUsers = await _db.Users.CountAsync();
        var totalDocs = await _db.Documents.CountAsync();
        var failedCount = await _db.AIProcessingLogs.CountAsync(l => !l.Success);
        var remindersSentToday = await _db.DocumentReminders
            .CountAsync(r => r.SentAt.HasValue && r.SentAt.Value.Date == DateTime.UtcNow.Date);

        // Storage calculation
        var versions = await _db.DocumentVersions.ToListAsync();
        var storageBytes = versions.Sum(v => v.FileSizeBytes);

        var recentUsers = await _db.Users.OrderByDescending(u => u.CreatedAt).Take(10).ToListAsync();
        var userItems = new List<AdminUserItem>();
        foreach (var u in recentUsers)
        {
            var roles = (await _userMgr.GetRolesAsync(u)).ToList();
            var docCount = await _db.Documents.CountAsync(d => d.UserId == u.Id);
            userItems.Add(new AdminUserItem
            {
                Id = u.Id,
                FullName = u.FullName,
                Email = u.Email ?? "",
                IsActive = u.IsActive,
                DocumentCount = docCount,
                CreatedAt = u.CreatedAt,
                LastLoginAt = u.LastLoginAt,
                Roles = roles
            });
        }

        var catStats = await _db.Documents
            .Include(d => d.Category)
            .GroupBy(d => new
            {
                Name = d.Category != null ? d.Category.Name : "Other",
                Color = d.Category != null ? d.Category.ColorCode : "#6c757d",
                Icon = d.Category != null ? d.Category.IconClass : "bi-folder"
            })
            .Select(g => new CategoryStat { Name = g.Key.Name, ColorCode = g.Key.Color, IconClass = g.Key.Icon, Count = g.Count() })
            .OrderByDescending(x => x.Count).ToListAsync();

        var aiLogs = await _db.AIProcessingLogs
            .Include(l => l.Document)
            .OrderByDescending(l => l.ProcessedAt)
            .Take(15)
            .Select(l => new AdminAiLogItem
            {
                Id = l.Id,
                DocumentTitle = l.Document != null ? l.Document.Title : "Deleted",
                ModelName = l.ModelName,
                Success = l.Success,
                ErrorMessage = l.ErrorMessage,
                TotalTokens = l.PromptTokens + l.ResponseTokens,
                ProcessedAt = l.ProcessedAt
            }).ToListAsync();

        return View(new AdminDashboardViewModel
        {
            TotalUsers = totalUsers,
            TotalDocuments = totalDocs,
            TotalStorageBytes = storageBytes,
            FailedProcessingCount = failedCount,
            RemindersSentToday = remindersSentToday,
            RecentUsers = userItems,
            CategoryStats = catStats,
            RecentAiLogs = aiLogs
        });
    }

    // ─────────────────────────── User Management ───────────────────────────

    [HttpGet]
    public async Task<IActionResult> Users(string? search, string? roleFilter, string? statusFilter)
    {
        var query = _db.Users.AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim().ToLower();
            query = query.Where(u => u.FullName.ToLower().Contains(s) || (u.Email != null && u.Email.ToLower().Contains(s)));
        }

        if (statusFilter == "active")
        {
            query = query.Where(u => u.IsActive);
        }
        else if (statusFilter == "inactive")
        {
            query = query.Where(u => !u.IsActive);
        }

        var users = await query.OrderByDescending(u => u.CreatedAt).ToListAsync();
        var userItems = new List<AdminUserItem>();
        int adminCount = 0;

        foreach (var u in users)
        {
            var roles = (await _userMgr.GetRolesAsync(u)).ToList();
            if (roles.Contains("Admin")) adminCount++;

            if (!string.IsNullOrWhiteSpace(roleFilter))
            {
                if (roleFilter == "Admin" && !roles.Contains("Admin")) continue;
                if (roleFilter == "User" && roles.Contains("Admin")) continue;
            }

            var docCount = await _db.Documents.CountAsync(d => d.UserId == u.Id);
            userItems.Add(new AdminUserItem
            {
                Id = u.Id,
                FullName = u.FullName,
                Email = u.Email ?? "",
                IsActive = u.IsActive,
                DocumentCount = docCount,
                CreatedAt = u.CreatedAt,
                LastLoginAt = u.LastLoginAt,
                Roles = roles
            });
        }

        var totalAllUsers = await _db.Users.CountAsync();
        var totalActive = await _db.Users.CountAsync(u => u.IsActive);

        var model = new AdminUsersViewModel
        {
            Search = search,
            RoleFilter = roleFilter,
            StatusFilter = statusFilter,
            TotalUsers = totalAllUsers,
            ActiveUsers = totalActive,
            InactiveUsers = totalAllUsers - totalActive,
            AdminCount = adminCount,
            Users = userItems
        };

        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleUserActive(string userId)
    {
        var user = await _userMgr.FindByIdAsync(userId);
        if (user is null) return NotFound();

        if (user.Id == CurrentUserId)
        {
            TempData["Error"] = "You cannot deactivate your own account.";
            return RedirectToAction(nameof(Users));
        }

        user.IsActive = !user.IsActive;
        await _userMgr.UpdateAsync(user);

        await _audit.LogAsync(CurrentUserId, "ToggleUserActive", "User", user.Id,
            $"User {user.Email} is now {(user.IsActive ? "active" : "inactive")}.");

        TempData["Success"] = $"User {user.FullName} is now {(user.IsActive ? "activated" : "deactivated")}.";
        return RedirectToAction(nameof(Users));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleUserRole(string userId)
    {
        var user = await _userMgr.FindByIdAsync(userId);
        if (user is null) return NotFound();

        if (user.Id == CurrentUserId)
        {
            TempData["Error"] = "You cannot change your own administrator role.";
            return RedirectToAction(nameof(Users));
        }

        var isCurrentlyAdmin = await _userMgr.IsInRoleAsync(user, "Admin");
        if (isCurrentlyAdmin)
        {
            await _userMgr.RemoveFromRoleAsync(user, "Admin");
            await _audit.LogAsync(CurrentUserId, "RevokeAdminRole", "User", user.Id, $"Admin role revoked for {user.Email}");
            TempData["Success"] = $"Admin role revoked for {user.FullName}.";
        }
        else
        {
            await _userMgr.AddToRoleAsync(user, "Admin");
            await _audit.LogAsync(CurrentUserId, "GrantAdminRole", "User", user.Id, $"Admin role granted to {user.Email}");
            TempData["Success"] = $"Admin role granted to {user.FullName}.";
        }

        return RedirectToAction(nameof(Users));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ResetUserPassword(string userId, string newPassword)
    {
        if (string.IsNullOrWhiteSpace(newPassword) || newPassword.Length < 8)
        {
            TempData["Error"] = "Password must be at least 8 characters long and meet complexity requirements.";
            return RedirectToAction(nameof(Users));
        }

        var user = await _userMgr.FindByIdAsync(userId);
        if (user is null) return NotFound();

        var token = await _userMgr.GeneratePasswordResetTokenAsync(user);
        var result = await _userMgr.ResetPasswordAsync(user, token, newPassword);

        if (result.Succeeded)
        {
            await _audit.LogAsync(CurrentUserId, "ResetPassword", "User", user.Id, $"Password reset by Admin for {user.Email}");
            TempData["Success"] = $"Password successfully reset for {user.FullName}.";
        }
        else
        {
            TempData["Error"] = string.Join("; ", result.Errors.Select(e => e.Description));
        }

        return RedirectToAction(nameof(Users));
    }

    // ─────────────────────────── All Documents Explorer ───────────────────────────

    [HttpGet]
    public async Task<IActionResult> Documents(
        string? search,
        int? categoryId,
        ProcessingStatus? processingStatus,
        ExpiryStatus? expiryStatus,
        int page = 1)
    {
        const int pageSize = 15;
        var query = _db.Documents
            .Include(d => d.User)
            .Include(d => d.Category)
            .Include(d => d.Versions)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim().ToLower();
            query = query.Where(d => d.Title.ToLower().Contains(s)
                || (d.HolderName != null && d.HolderName.ToLower().Contains(s))
                || d.User.FullName.ToLower().Contains(s)
                || (d.User.Email != null && d.User.Email.ToLower().Contains(s))
                || d.DocumentType.ToLower().Contains(s));
        }

        if (categoryId.HasValue)
        {
            query = query.Where(d => d.CategoryId == categoryId.Value);
        }

        if (processingStatus.HasValue)
        {
            query = query.Where(d => d.ProcessingStatus == processingStatus.Value);
        }

        if (expiryStatus.HasValue)
        {
            query = query.Where(d => d.ExpiryStatus == expiryStatus.Value);
        }

        var totalCount = await query.CountAsync();
        var docs = await query
            .OrderByDescending(d => d.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(d => new AdminDocumentItem
            {
                Id = d.Id,
                Title = d.Title,
                DocumentType = d.DocumentType,
                DocumentNumberMasked = d.DocumentNumberMasked,
                HolderName = d.HolderName,
                CategoryName = d.Category != null ? d.Category.Name : "Other",
                CategoryColor = d.Category != null ? d.Category.ColorCode : "#6c757d",
                CategoryIcon = d.Category != null ? d.Category.IconClass : "bi-folder",
                FileSizeBytes = d.Versions.OrderByDescending(v => v.VersionNumber).Select(v => v.FileSizeBytes).FirstOrDefault(),
                ProcessingStatus = d.ProcessingStatus,
                ExpiryStatus = d.ExpiryStatus,
                ExpiryDate = d.ExpiryDate,
                OwnerName = d.User.FullName,
                OwnerEmail = d.User.Email ?? "",
                CreatedAt = d.CreatedAt,
                VersionCount = d.Versions.Count
            })
            .ToListAsync();

        var categories = await _db.DocumentCategories
            .OrderBy(c => c.DisplayOrder)
            .Select(c => new SelectListItem { Value = c.Id.ToString(), Text = c.Name })
            .ToListAsync();

        var model = new AdminDocumentsViewModel
        {
            Search = search,
            CategoryId = categoryId,
            ProcessingStatus = processingStatus,
            ExpiryStatus = expiryStatus,
            Page = page,
            TotalPages = (int)Math.Ceiling(totalCount / (double)pageSize),
            TotalCount = totalCount,
            Categories = categories,
            Documents = docs
        };

        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ReprocessDocument(Guid documentId)
    {
        var doc = await _db.Documents.FindAsync(documentId);
        if (doc is null) return NotFound();

        await _docService.TriggerProcessingAsync(documentId);
        await _audit.LogAsync(CurrentUserId, "Reprocess", "Document", documentId.ToString(), $"Reprocessing triggered for '{doc.Title}'");

        TempData["Success"] = $"AI extraction and OCR re-triggered for \"{doc.Title}\".";
        return RedirectToAction(nameof(Documents));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteDocument(Guid documentId)
    {
        var doc = await _db.Documents.Include(d => d.Versions).FirstOrDefaultAsync(d => d.Id == documentId);
        if (doc is null) return NotFound();

        var title = doc.Title;
        // Physically delete version files
        foreach (var v in doc.Versions)
        {
            var physPath = Path.Combine(_env.ContentRootPath, v.StoredFilePath.Replace("/", "\\"));
            if (System.IO.File.Exists(physPath))
            {
                try { System.IO.File.Delete(physPath); } catch { /* best effort */ }
            }
        }

        _db.Documents.Remove(doc);
        await _db.SaveChangesAsync();

        await _audit.LogAsync(CurrentUserId, "AdminDelete", "Document", documentId.ToString(), $"Admin deleted document '{title}'");

        TempData["Success"] = $"Document \"{title}\" deleted successfully.";
        return RedirectToAction(nameof(Documents));
    }

    // ─────────────────────────── Category Management ───────────────────────────

    [HttpGet]
    public async Task<IActionResult> Categories()
    {
        var categories = await _db.DocumentCategories
            .Include(c => c.Documents)
            .OrderBy(c => c.DisplayOrder)
            .Select(c => new AdminCategoryItem
            {
                Id = c.Id,
                Name = c.Name,
                IconClass = c.IconClass,
                ColorCode = c.ColorCode,
                DisplayOrder = c.DisplayOrder,
                IsSystemDefault = c.IsSystemDefault,
                DocumentCount = c.Documents.Count
            })
            .ToListAsync();

        return View(categories);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateCategory(AdminCategoryEditViewModel model)
    {
        if (!ModelState.IsValid)
        {
            TempData["Error"] = "Invalid category data. Please fill in all required fields.";
            return RedirectToAction(nameof(Categories));
        }

        var exists = await _db.DocumentCategories.AnyAsync(c => c.Name.ToLower() == model.Name.Trim().ToLower());
        if (exists)
        {
            TempData["Error"] = $"A category named '{model.Name}' already exists.";
            return RedirectToAction(nameof(Categories));
        }

        var cat = new DocumentCategory
        {
            Name = model.Name.Trim(),
            IconClass = string.IsNullOrWhiteSpace(model.IconClass) ? "bi-folder" : model.IconClass.Trim(),
            ColorCode = string.IsNullOrWhiteSpace(model.ColorCode) ? "#4361ee" : model.ColorCode.Trim(),
            DisplayOrder = model.DisplayOrder,
            IsSystemDefault = false
        };

        _db.DocumentCategories.Add(cat);
        await _db.SaveChangesAsync();

        await _audit.LogAsync(CurrentUserId, "CreateCategory", "DocumentCategory", cat.Id.ToString(), $"Created category '{cat.Name}'");
        TempData["Success"] = $"Category '{cat.Name}' created successfully.";
        return RedirectToAction(nameof(Categories));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EditCategory(AdminCategoryEditViewModel model)
    {
        if (!ModelState.IsValid)
        {
            TempData["Error"] = "Invalid category data.";
            return RedirectToAction(nameof(Categories));
        }

        var cat = await _db.DocumentCategories.FindAsync(model.Id);
        if (cat is null) return NotFound();

        cat.Name = model.Name.Trim();
        cat.IconClass = string.IsNullOrWhiteSpace(model.IconClass) ? "bi-folder" : model.IconClass.Trim();
        cat.ColorCode = string.IsNullOrWhiteSpace(model.ColorCode) ? "#4361ee" : model.ColorCode.Trim();
        cat.DisplayOrder = model.DisplayOrder;

        await _db.SaveChangesAsync();

        await _audit.LogAsync(CurrentUserId, "EditCategory", "DocumentCategory", cat.Id.ToString(), $"Updated category '{cat.Name}'");
        TempData["Success"] = $"Category '{cat.Name}' updated successfully.";
        return RedirectToAction(nameof(Categories));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteCategory(int id)
    {
        var cat = await _db.DocumentCategories.Include(c => c.Documents).FirstOrDefaultAsync(c => c.Id == id);
        if (cat is null) return NotFound();

        if (cat.IsSystemDefault)
        {
            TempData["Error"] = "System default categories cannot be deleted.";
            return RedirectToAction(nameof(Categories));
        }

        // Reassign existing documents in this category to default 'Other' category (Id 10) or null
        var otherCategory = await _db.DocumentCategories.FirstOrDefaultAsync(c => c.Name == "Other");
        int? fallbackId = otherCategory?.Id;

        foreach (var doc in cat.Documents)
        {
            doc.CategoryId = fallbackId;
        }

        _db.DocumentCategories.Remove(cat);
        await _db.SaveChangesAsync();

        await _audit.LogAsync(CurrentUserId, "DeleteCategory", "DocumentCategory", id.ToString(), $"Deleted category '{cat.Name}'");
        TempData["Success"] = $"Category '{cat.Name}' deleted. Associated documents reassigned.";
        return RedirectToAction(nameof(Categories));
    }

    // ─────────────────────────── System Health & Maintenance ───────────────────────────

    [HttpGet]
    public async Task<IActionResult> SystemHealth()
    {
        var versions = await _db.DocumentVersions.ToListAsync();
        var totalDbBytes = versions.Sum(v => v.FileSizeBytes);

        // Physical folder size
        long physicalBytes = 0;
        var uploadDir = Path.Combine(_env.ContentRootPath, _appSettings.SecureUploadPath.Replace("/", "\\"));
        if (Directory.Exists(uploadDir))
        {
            var dirInfo = new DirectoryInfo(uploadDir);
            physicalBytes = dirInfo.EnumerateFiles("*", SearchOption.AllDirectories).Sum(fi => fi.Length);
        }

        // AI processing stats
        var totalAi = await _db.AIProcessingLogs.CountAsync();
        var successAi = await _db.AIProcessingLogs.CountAsync(l => l.Success);
        var failedAi = totalAi - successAi;
        var totalTokens = await _db.AIProcessingLogs.SumAsync(l => (int?)(l.PromptTokens + l.ResponseTokens)) ?? 0;

        var model = new AdminSystemHealthViewModel
        {
            DotNetVersion = RuntimeInformation.FrameworkDescription,
            OsDescription = RuntimeInformation.OSDescription,
            MachineName = Environment.MachineName,
            ServerTimeUtc = DateTime.UtcNow,
            EnvironmentName = _env.EnvironmentName,

            TotalStorageBytes = totalDbBytes,
            PhysicalStorageBytes = physicalBytes,
            TotalVersions = versions.Count,
            MaxFileSizeMB = _appSettings.MaxFileSizeMB,
            AllowedExtensions = _appSettings.AllowedExtensions,

            GeminiModel = _aiSettings.GeminiModel,
            HasApiKey = !string.IsNullOrWhiteSpace(_aiSettings.GeminiApiKey),
            TotalAiRequests = totalAi,
            SuccessfulAiRequests = successAi,
            FailedAiRequests = failedAi,
            TotalAiTokens = totalTokens,

            TotalUsers = await _db.Users.CountAsync(),
            TotalDocuments = await _db.Documents.CountAsync(),
            TotalCategories = await _db.DocumentCategories.CountAsync(),
            TotalReminders = await _db.DocumentReminders.CountAsync(),
            TotalAuditLogs = await _db.AuditLogs.CountAsync(),
            TotalNotifications = await _db.Notifications.CountAsync()
        };

        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> TriggerExpiryCheck()
    {
        await _docService.UpdateExpiryStatusesAsync();
        await _audit.LogAsync(CurrentUserId, "TriggerExpiryCheck", "System", null, "Admin manually triggered expiry status update.");
        TempData["Success"] = "Document expiry statuses and reminders refreshed successfully.";
        return RedirectToAction(nameof(SystemHealth));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RetryFailedAiJobs()
    {
        var failedDocs = await _db.Documents
            .Where(d => d.ProcessingStatus == ProcessingStatus.Failed)
            .ToListAsync();

        foreach (var doc in failedDocs)
        {
            await _docService.TriggerProcessingAsync(doc.Id);
        }

        await _audit.LogAsync(CurrentUserId, "RetryFailedAiJobs", "System", null, $"Retried {failedDocs.Count} failed AI processing jobs.");
        TempData["Success"] = $"Scheduled {failedDocs.Count} failed document(s) for AI reprocessing.";
        return RedirectToAction(nameof(SystemHealth));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> PurgeOldAuditLogs(int daysOld = 90)
    {
        var cutoff = DateTime.UtcNow.AddDays(-daysOld);
        var oldLogs = await _db.AuditLogs.Where(a => a.Timestamp < cutoff).ToListAsync();
        var count = oldLogs.Count;

        if (count > 0)
        {
            _db.AuditLogs.RemoveRange(oldLogs);
            await _db.SaveChangesAsync();
        }

        await _audit.LogAsync(CurrentUserId, "PurgeAuditLogs", "System", null, $"Purged {count} audit logs older than {daysOld} days.");
        TempData["Success"] = $"Successfully purged {count} audit log(s) older than {daysOld} days.";
        return RedirectToAction(nameof(SystemHealth));
    }

    // ─────────────────────────── Broadcast Notifications ───────────────────────────

    [HttpGet]
    public async Task<IActionResult> Broadcast()
    {
        // Recent broadcasts: notifications grouped by Title, Message, Type and minute
        var recentNotifications = await _db.Notifications
            .OrderByDescending(n => n.CreatedAt)
            .Take(100)
            .ToListAsync();

        var recent = recentNotifications
            .GroupBy(n => new { n.Title, n.Message, n.Type, DateMinute = new DateTime(n.CreatedAt.Year, n.CreatedAt.Month, n.CreatedAt.Day, n.CreatedAt.Hour, n.CreatedAt.Minute, 0) })
            .Select(g => new AdminBroadcastItem
            {
                Title = g.Key.Title,
                Message = g.Key.Message,
                Type = g.Key.Type,
                CreatedAt = g.Max(n => n.CreatedAt),
                RecipientCount = g.Count()
            })
            .OrderByDescending(b => b.CreatedAt)
            .Take(10)
            .ToList();

        return View(new AdminBroadcastViewModel
        {
            RecentBroadcasts = recent
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SendBroadcast(AdminBroadcastViewModel model)
    {
        if (!ModelState.IsValid)
        {
            TempData["Error"] = "Title and message are required for broadcasting.";
            return RedirectToAction(nameof(Broadcast));
        }

        var activeUsers = await _db.Users.Where(u => u.IsActive).ToListAsync();
        if (!activeUsers.Any())
        {
            TempData["Error"] = "No active users found to broadcast to.";
            return RedirectToAction(nameof(Broadcast));
        }

        var now = DateTime.UtcNow;
        var notifications = activeUsers.Select(u => new Notification
        {
            UserId = u.Id,
            Title = model.Title.Trim(),
            Message = model.Message.Trim(),
            Type = model.Type,
            IsRead = false,
            CreatedAt = now
        }).ToList();

        _db.Notifications.AddRange(notifications);
        await _db.SaveChangesAsync();

        await _audit.LogAsync(CurrentUserId, "BroadcastNotification", "Notification", null,
            $"Broadcast '{model.Title}' sent to {activeUsers.Count} active users.");

        TempData["Success"] = $"Broadcast announcement successfully sent to {activeUsers.Count} active users!";
        return RedirectToAction(nameof(Broadcast));
    }

    // ─────────────────────────── Logs ───────────────────────────

    [HttpGet]
    public async Task<IActionResult> AuditLogs(int page = 1)
    {
        const int pageSize = 50;
        var query = _db.AuditLogs.Include(a => a.User).OrderByDescending(a => a.Timestamp);
        var total = await query.CountAsync();
        var logs = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
        ViewBag.Page = page;
        ViewBag.TotalPages = (int)Math.Ceiling(total / (double)pageSize);
        return View(logs);
    }

    [HttpGet]
    public async Task<IActionResult> AiLogs(int page = 1)
    {
        const int pageSize = 50;
        var query = _db.AIProcessingLogs.Include(l => l.Document).OrderByDescending(l => l.ProcessedAt);
        var total = await query.CountAsync();
        var logs = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
        ViewBag.Page = page;
        ViewBag.TotalPages = (int)Math.Ceiling(total / (double)pageSize);
        return View(logs);
    }

    // ─────────────────────────── CSV Exports ───────────────────────────

    [HttpGet]
    public async Task<IActionResult> ExportUsers()
    {
        var users = await _db.Users.OrderByDescending(u => u.CreatedAt).ToListAsync();
        var sb = new StringBuilder();
        sb.AppendLine("Id,FullName,Email,Roles,IsActive,DocumentCount,CreatedAt,LastLoginAt");

        foreach (var u in users)
        {
            var roles = string.Join(";", await _userMgr.GetRolesAsync(u));
            var docCount = await _db.Documents.CountAsync(d => d.UserId == u.Id);
            sb.AppendLine($"\"{u.Id}\",\"{EscapeCsv(u.FullName)}\",\"{EscapeCsv(u.Email ?? "")}\",\"{roles}\",{u.IsActive},{docCount},\"{u.CreatedAt:yyyy-MM-dd HH:mm}\",\"{u.LastLoginAt:yyyy-MM-dd HH:mm}\"");
        }

        var bytes = Encoding.UTF8.GetBytes(sb.ToString());
        return File(bytes, "text/csv", $"users_{DateTime.UtcNow:yyyyMMdd_HHmm}.csv");
    }

    [HttpGet]
    public async Task<IActionResult> ExportDocuments()
    {
        var docs = await _db.Documents
            .Include(d => d.User)
            .Include(d => d.Category)
            .Include(d => d.Versions)
            .OrderByDescending(d => d.CreatedAt)
            .ToListAsync();

        var sb = new StringBuilder();
        sb.AppendLine("Id,Title,DocumentType,OwnerName,OwnerEmail,Category,ProcessingStatus,ExpiryStatus,ExpiryDate,FileSizeKB,CreatedAt");

        foreach (var d in docs)
        {
            var sizeKb = (d.Versions.OrderByDescending(v => v.VersionNumber).Select(v => v.FileSizeBytes).FirstOrDefault() / 1024.0).ToString("0.0");
            sb.AppendLine($"\"{d.Id}\",\"{EscapeCsv(d.Title)}\",\"{EscapeCsv(d.DocumentType)}\",\"{EscapeCsv(d.User?.FullName ?? "")}\",\"{EscapeCsv(d.User?.Email ?? "")}\",\"{EscapeCsv(d.Category?.Name ?? "Other")}\",\"{d.ProcessingStatus}\",\"{d.ExpiryStatus}\",\"{d.ExpiryDate:yyyy-MM-dd}\",{sizeKb},\"{d.CreatedAt:yyyy-MM-dd HH:mm}\"");
        }

        var bytes = Encoding.UTF8.GetBytes(sb.ToString());
        return File(bytes, "text/csv", $"documents_{DateTime.UtcNow:yyyyMMdd_HHmm}.csv");
    }

    [HttpGet]
    public async Task<IActionResult> ExportAuditLogs()
    {
        var logs = await _db.AuditLogs.Include(a => a.User).OrderByDescending(a => a.Timestamp).Take(2000).ToListAsync();
        var sb = new StringBuilder();
        sb.AppendLine("Id,Timestamp,UserEmail,Action,EntityName,EntityId,IpAddress,Details");

        foreach (var l in logs)
        {
            sb.AppendLine($"{l.Id},\"{l.Timestamp:yyyy-MM-dd HH:mm:ss}\",\"{EscapeCsv(l.User?.Email ?? l.UserId ?? "")}\",\"{EscapeCsv(l.Action)}\",\"{EscapeCsv(l.EntityName ?? "")}\",\"{EscapeCsv(l.EntityId ?? "")}\",\"{EscapeCsv(l.IpAddress ?? "")}\",\"{EscapeCsv(l.Details ?? "")}\"");
        }

        var bytes = Encoding.UTF8.GetBytes(sb.ToString());
        return File(bytes, "text/csv", $"audit_logs_{DateTime.UtcNow:yyyyMMdd_HHmm}.csv");
    }

    private static string EscapeCsv(string val) => val.Replace("\"", "\"\"");
}
