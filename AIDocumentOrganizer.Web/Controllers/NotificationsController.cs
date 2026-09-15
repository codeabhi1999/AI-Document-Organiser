using AIDocumentOrganizer.Web.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using AIDocumentOrganizer.Web.Models.Entities;

namespace AIDocumentOrganizer.Web.Controllers;

[Authorize]
public class NotificationsController : Controller
{
    private readonly IDocumentService _docs;
    private readonly UserManager<ApplicationUser> _userMgr;

    public NotificationsController(IDocumentService docs, UserManager<ApplicationUser> userMgr)
    {
        _docs = docs;
        _userMgr = userMgr;
    }

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var userId = _userMgr.GetUserId(User)!;
        var notifications = await _docs.GetUserNotificationsAsync(userId, 50);
        var vm = new ViewModels.NotificationViewModel
        {
            Notifications = notifications,
            UnreadCount = notifications.Count(n => !n.IsRead)
        };
        await _docs.MarkNotificationsReadAsync(userId);
        return View(vm);
    }
}
