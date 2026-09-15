using AIDocumentOrganizer.Web.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using AIDocumentOrganizer.Web.Models.Entities;

namespace AIDocumentOrganizer.Web.Controllers;

[Authorize]
public class DashboardController : Controller
{
    private readonly IDocumentService _docs;
    private readonly UserManager<ApplicationUser> _userMgr;

    public DashboardController(IDocumentService docs, UserManager<ApplicationUser> userMgr)
    {
        _docs = docs;
        _userMgr = userMgr;
    }

    public async Task<IActionResult> Index()
    {
        var userId = _userMgr.GetUserId(User)!;
        var vm = await _docs.GetDashboardDataAsync(userId);
        return View(vm);
    }
}
