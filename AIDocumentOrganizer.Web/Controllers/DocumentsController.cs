using AIDocumentOrganizer.Web.Data;
using AIDocumentOrganizer.Web.Models.Enums;
using AIDocumentOrganizer.Web.Services.Interfaces;
using AIDocumentOrganizer.Web.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using AIDocumentOrganizer.Web.Models.Entities;

namespace AIDocumentOrganizer.Web.Controllers;

[Authorize]
public class DocumentsController : Controller
{
    private readonly IDocumentService _docs;
    private readonly IAuditLogService _audit;
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userMgr;

    public DocumentsController(
        IDocumentService docs,
        IAuditLogService audit,
        ApplicationDbContext db,
        UserManager<ApplicationUser> userMgr)
    {
        _docs = docs;
        _audit = audit;
        _db = db;
        _userMgr = userMgr;
    }

    // ─────────── List ───────────

    [HttpGet]
    public async Task<IActionResult> Index(
        string? search, int? categoryId, string? expiryFilter,
        DocumentSortBy sortBy = DocumentSortBy.UploadDate, string sortDir = "desc", int page = 1)
    {
        var userId = _userMgr.GetUserId(User)!;
        var filter = new DocumentListViewModel
        {
            SearchTerm = search,
            CategoryId = categoryId,
            ExpiryFilter = expiryFilter,
            SortBy = sortBy,
            SortDir = sortDir,
            Page = page
        };
        var vm = await _docs.GetDocumentsAsync(userId, filter);
        return View(vm);
    }

    // ─────────── Upload ───────────

    [HttpGet]
    public async Task<IActionResult> Upload()
    {
        var vm = new DocumentUploadViewModel
        {
            Categories = await GetCategorySelectList()
        };
        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(20_000_000)]
    public async Task<IActionResult> Upload(DocumentUploadViewModel model)
    {
        model.Categories = await GetCategorySelectList();

        if (model.File is null)
        {
            ModelState.AddModelError("File", "Please select a file.");
            return View(model);
        }

        var (valid, error) = await _docs.ValidateFileAsync(model.File);
        if (!valid)
        {
            ModelState.AddModelError("File", error!);
            return View(model);
        }

        if (!ModelState.IsValid) return View(model);

        var userId = _userMgr.GetUserId(User)!;
        var document = await _docs.UploadDocumentAsync(userId, model);

        // Fire-and-forget AI processing in background
        _ = Task.Run(() => _docs.TriggerProcessingAsync(document.Id));

        TempData["Success"] = "Document uploaded successfully! AI is analyzing it in the background.";
        return RedirectToAction(nameof(Details), new { id = document.Id });
    }

    // ─────────── Details ───────────

    [HttpGet]
    public async Task<IActionResult> Details(Guid id)
    {
        var userId = _userMgr.GetUserId(User)!;
        var vm = await _docs.GetDocumentDetailsAsync(id, userId);
        if (vm is null) return NotFound();

        await _audit.LogAsync(userId, "View", "Document", id.ToString(), $"Viewed '{vm.Title}'");
        return View(vm);
    }

    // ─────────── Edit ───────────

    [HttpGet]
    public async Task<IActionResult> Edit(Guid id)
    {
        var userId = _userMgr.GetUserId(User)!;
        var doc = await _docs.GetDocumentEntityAsync(id, userId);
        if (doc is null) return NotFound();

        var vm = new DocumentEditViewModel
        {
            Id = doc.Id,
            Title = doc.Title,
            DocumentType = doc.DocumentType,
            HolderName = doc.HolderName,
            IssuingAuthority = doc.IssuingAuthority,
            IssueDate = doc.IssueDate,
            ExpiryDate = doc.ExpiryDate,
            VehicleNumber = doc.VehicleNumber,
            PolicyNumber = doc.PolicyNumber,
            Amount = doc.Amount,
            Notes = doc.Notes,
            Tags = doc.Tags,
            CategoryId = doc.CategoryId,
            Categories = await GetCategorySelectList()
        };
        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(20_000_000)]
    public async Task<IActionResult> Edit(DocumentEditViewModel model)
    {
        model.Categories = await GetCategorySelectList();

        if (model.NewFile is not null)
        {
            var (valid, error) = await _docs.ValidateFileAsync(model.NewFile);
            if (!valid) ModelState.AddModelError("NewFile", error!);
        }

        if (!ModelState.IsValid) return View(model);

        var userId = _userMgr.GetUserId(User)!;
        var success = await _docs.UpdateDocumentAsync(model, userId);
        if (!success) return NotFound();

        TempData["Success"] = "Document updated successfully.";
        return RedirectToAction(nameof(Details), new { id = model.Id });
    }

    // ─────────── Delete ───────────

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(Guid id)
    {
        var userId = _userMgr.GetUserId(User)!;
        var success = await _docs.DeleteDocumentAsync(id, userId);
        if (!success) return NotFound();

        TempData["Success"] = "Document deleted successfully.";
        return RedirectToAction(nameof(Index));
    }

    // ─────────── Download / Preview ───────────

    [HttpGet]
    public async Task<IActionResult> Download(Guid versionId)
    {
        var userId = _userMgr.GetUserId(User)!;
        var file = await _docs.GetDocumentFileAsync(versionId, userId);
        if (file is null) return NotFound();

        await _audit.LogAsync(userId, "Download", "DocumentVersion", versionId.ToString());
        return File(file.Value.FileStream, file.Value.ContentType, file.Value.FileName);
    }

    [HttpGet]
    public async Task<IActionResult> Preview(Guid versionId)
    {
        var userId = _userMgr.GetUserId(User)!;
        var file = await _docs.GetDocumentFileAsync(versionId, userId);
        if (file is null) return NotFound();

        await _audit.LogAsync(userId, "Preview", "DocumentVersion", versionId.ToString());
        // Inline: let the browser render PDF/image in-frame
        Response.Headers.Append("Content-Disposition", $"inline; filename=\"{file.Value.FileName}\"");
        return File(file.Value.FileStream, file.Value.ContentType);
    }

    // ─────────── Retry Processing ───────────

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RetryProcessing(Guid id)
    {
        var userId = _userMgr.GetUserId(User)!;
        if (!await _docs.DocumentBelongsToUserAsync(id, userId)) return Forbid();

        _ = Task.Run(() => _docs.RetryProcessingAsync(id, userId));
        TempData["Success"] = "AI reprocessing started. Refresh in a moment.";
        return RedirectToAction(nameof(Details), new { id });
    }

    // ─────────── Expiring Documents ───────────

    [HttpGet]
    public async Task<IActionResult> Expiring()
    {
        var userId = _userMgr.GetUserId(User)!;
        var filter = new DocumentListViewModel
        {
            ExpiryFilter = "expiringsoon",
            SortBy = DocumentSortBy.ExpiryDate,
            SortDir = "asc"
        };
        var vm = await _docs.GetDocumentsAsync(userId, filter);
        ViewBag.PageTitle = "Expiring Documents";
        return View("Index", vm);
    }

    // ─────────── Helpers ───────────

    private async Task<List<SelectListItem>> GetCategorySelectList()
    {
        return await _db.DocumentCategories
            .OrderBy(c => c.DisplayOrder)
            .Select(c => new SelectListItem { Value = c.Id.ToString(), Text = c.Name })
            .ToListAsync();
    }
}
