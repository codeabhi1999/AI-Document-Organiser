using AIDocumentOrganizer.Web.Data;
using AIDocumentOrganizer.Web.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using AIDocumentOrganizer.Web.Models.Entities;

namespace AIDocumentOrganizer.Web.Controllers;

[Authorize(Roles = "Admin")]
public class AdminController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userMgr;
    private readonly IWebHostEnvironment _env;

    public AdminController(ApplicationDbContext db, UserManager<ApplicationUser> userMgr, IWebHostEnvironment env)
    {
        _db = db;
        _userMgr = userMgr;
        _env = env;
    }

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var totalUsers = await _db.Users.CountAsync();
        var totalDocs = await _db.Documents.CountAsync();
        var failedCount = await _db.AIProcessingLogs.CountAsync(l => !l.Success);
        var remindersSentToday = await _db.DocumentReminders
            .CountAsync(r => r.SentAt.HasValue && r.SentAt.Value.Date == DateTime.UtcNow.Date);

        // Storage calculation
        long storageBytes = 0;
        var versions = await _db.DocumentVersions.ToListAsync();
        storageBytes = versions.Sum(v => v.FileSizeBytes);

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
            .GroupBy(d => new { Name = d.Category != null ? d.Category.Name : "Other", Color = d.Category != null ? d.Category.ColorCode : "#6c757d", Icon = d.Category != null ? d.Category.IconClass : "bi-folder" })
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

    [HttpGet]
    public async Task<IActionResult> Users()
    {
        var users = await _db.Users.OrderByDescending(u => u.CreatedAt).ToListAsync();
        var userItems = new List<AdminUserItem>();
        foreach (var u in users)
        {
            var roles = (await _userMgr.GetRolesAsync(u)).ToList();
            var docCount = await _db.Documents.CountAsync(d => d.UserId == u.Id);
            userItems.Add(new AdminUserItem
            {
                Id = u.Id, FullName = u.FullName, Email = u.Email ?? "", IsActive = u.IsActive,
                DocumentCount = docCount, CreatedAt = u.CreatedAt, LastLoginAt = u.LastLoginAt, Roles = roles
            });
        }
        return View(userItems);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleUserActive(string userId)
    {
        var user = await _userMgr.FindByIdAsync(userId);
        if (user is null) return NotFound();
        user.IsActive = !user.IsActive;
        await _userMgr.UpdateAsync(user);
        TempData["Success"] = $"User {(user.IsActive ? "activated" : "deactivated")}.";
        return RedirectToAction(nameof(Users));
    }

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
}
