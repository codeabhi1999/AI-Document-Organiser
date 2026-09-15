using AIDocumentOrganizer.Web.Data;
using AIDocumentOrganizer.Web.Services.Interfaces;
using AIDocumentOrganizer.Web.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using AIDocumentOrganizer.Web.Models.Entities;
using AIDocumentOrganizer.Web.Models.Enums;

namespace AIDocumentOrganizer.Web.Controllers;

[Authorize]
public class AiAssistantController : Controller
{
    private readonly IAiDocumentService _ai;
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userMgr;
    private readonly ILogger<AiAssistantController> _logger;

    public AiAssistantController(
        IAiDocumentService ai,
        ApplicationDbContext db,
        UserManager<ApplicationUser> userMgr,
        ILogger<AiAssistantController> logger)
    {
        _ai = ai;
        _db = db;
        _userMgr = userMgr;
        _logger = logger;
    }

    [HttpGet]
    public IActionResult Index()
    {
        return View(new AiAssistantViewModel());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Chat([FromBody] AiChatRequest request)
    {
        if (!ModelState.IsValid || string.IsNullOrWhiteSpace(request.Message))
            return Json(new AiChatResponse { Success = false, Error = "Empty message." });

        var userId = _userMgr.GetUserId(User)!;

        // Build document context strictly scoped to this user
        var userDocs = await _db.Documents
            .Include(d => d.Category)
            .Where(d => d.UserId == userId)
            .Select(d => new DocumentSummaryForAi(
                d.Id,
                d.Title,
                d.DocumentType,
                d.Category != null ? d.Category.Name : "Other",
                d.HolderName,
                d.ExpiryDate,
                d.ExpiryDate.HasValue ? (int)(d.ExpiryDate.Value.Date - DateTime.UtcNow.Date).TotalDays : (int?)null,
                d.Tags
            ))
            .ToListAsync();

        try
        {
            var response = await _ai.ChatAsync(request.Message, userId, userDocs);
            return Json(new AiChatResponse { Success = true, Response = response });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AI assistant error for user {UserId}", userId);
            return Json(new AiChatResponse { Success = false, Error = "AI assistant is temporarily unavailable." });
        }
    }
}
