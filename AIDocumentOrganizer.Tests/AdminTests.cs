using System.Security.Claims;
using AIDocumentOrganizer.Web.Controllers;
using AIDocumentOrganizer.Web.Data;
using AIDocumentOrganizer.Web.Models.Entities;
using AIDocumentOrganizer.Web.Services.Implementations;
using AIDocumentOrganizer.Web.Services.Interfaces;
using AIDocumentOrganizer.Web.ViewModels;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace AIDocumentOrganizer.Tests;

public class AdminTests
{
    private static (AdminController Controller, ApplicationDbContext Db, Mock<UserManager<ApplicationUser>> UserMgr) CreateController(string currentAdminId = "admin-1")
    {
        var db = DbFactory.Create();

        var store = new Mock<IUserStore<ApplicationUser>>();
        var userMgr = new Mock<UserManager<ApplicationUser>>(store.Object, null!, null!, null!, null!, null!, null!, null!, null!);

        var env = new Mock<IWebHostEnvironment>();
        env.Setup(e => e.ContentRootPath).Returns(AppDomain.CurrentDomain.BaseDirectory);
        env.Setup(e => e.EnvironmentName).Returns("Testing");

        var docService = new Mock<IDocumentService>();
        var audit = new Mock<IAuditLogService>();

        var appSettings = new AppSettings { MaxFileSizeMB = 15, AllowedExtensions = new[] { ".pdf", ".jpg" } };
        var aiSettings = new AiServiceSettings { GeminiModel = "gemini-1.5-flash", GeminiApiKey = "test-key" };

        var controller = new AdminController(db, userMgr.Object, env.Object, docService.Object, audit.Object, appSettings, aiSettings);

        // Mock ControllerContext with User identity
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, currentAdminId),
            new(ClaimTypes.Name, "admin@test.com"),
            new(ClaimTypes.Role, "Admin")
        };
        var identity = new ClaimsIdentity(claims, "TestAuth");
        var userPrincipal = new ClaimsPrincipal(identity);

        var httpContext = new DefaultHttpContext { User = userPrincipal };
        var tempData = new TempDataDictionary(httpContext, Mock.Of<ITempDataProvider>());

        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
        controller.TempData = tempData;

        return (controller, db, userMgr);
    }

    [Fact]
    public async Task CreateCategory_AddsCategory_WhenValid()
    {
        var (controller, db, _) = CreateController();

        var model = new AdminCategoryEditViewModel
        {
            Name = "Tax Records",
            IconClass = "bi-file-text",
            ColorCode = "#10b981",
            DisplayOrder = 15
        };

        var result = await controller.CreateCategory(model) as RedirectToActionResult;

        Assert.NotNull(result);
        Assert.Equal("Categories", result.ActionName);

        var cat = await db.DocumentCategories.FirstOrDefaultAsync(c => c.Name == "Tax Records");
        Assert.NotNull(cat);
        Assert.False(cat.IsSystemDefault);
        Assert.Equal("#10b981", cat.ColorCode);
    }

    [Fact]
    public async Task CreateCategory_RejectsDuplicateName()
    {
        var (controller, db, _) = CreateController();

        db.DocumentCategories.Add(new DocumentCategory { Name = "Legal", IconClass = "bi-bank", ColorCode = "#000", DisplayOrder = 1 });
        await db.SaveChangesAsync();

        var model = new AdminCategoryEditViewModel { Name = "Legal" };

        var result = await controller.CreateCategory(model) as RedirectToActionResult;

        Assert.NotNull(result);
        Assert.Equal("Categories", result.ActionName);
        Assert.Contains("already exists", controller.TempData["Error"]?.ToString());
    }

    [Fact]
    public async Task DeleteCategory_PreventsDeletingSystemDefault()
    {
        var (controller, db, _) = CreateController();

        var defaultCat = await db.DocumentCategories.FirstAsync(c => c.IsSystemDefault);

        var result = await controller.DeleteCategory(defaultCat.Id) as RedirectToActionResult;

        Assert.NotNull(result);
        Assert.Contains("System default categories cannot be deleted", controller.TempData["Error"]?.ToString());
    }

    [Fact]
    public async Task ToggleUserActive_PreventsSelfDeactivation()
    {
        var currentAdminId = "admin-current";
        var (controller, _, userMgr) = CreateController(currentAdminId);

        var selfUser = new ApplicationUser { Id = currentAdminId, UserName = "admin", Email = "admin@test.com", IsActive = true };
        userMgr.Setup(m => m.FindByIdAsync(currentAdminId)).ReturnsAsync(selfUser);

        var result = await controller.ToggleUserActive(currentAdminId) as RedirectToActionResult;

        Assert.NotNull(result);
        Assert.Equal("Users", result.ActionName);
        Assert.Contains("cannot deactivate your own account", controller.TempData["Error"]?.ToString());
    }

    [Fact]
    public async Task SendBroadcast_CreatesNotifications_ForAllActiveUsers()
    {
        var (controller, db, _) = CreateController();

        var user1 = new ApplicationUser { Id = "u1", UserName = "u1", Email = "u1@test.com", FullName = "User 1", IsActive = true };
        var user2 = new ApplicationUser { Id = "u2", UserName = "u2", Email = "u2@test.com", FullName = "User 2", IsActive = true };
        var inactiveUser = new ApplicationUser { Id = "u3", UserName = "u3", Email = "u3@test.com", FullName = "User 3", IsActive = false };

        db.Users.AddRange(user1, user2, inactiveUser);
        await db.SaveChangesAsync();

        var model = new AdminBroadcastViewModel
        {
            Title = "Maintenance Window",
            Message = "Servers will restart at midnight.",
            Type = "Warning"
        };

        var result = await controller.SendBroadcast(model) as RedirectToActionResult;

        Assert.NotNull(result);
        Assert.Equal("Broadcast", result.ActionName);

        var notifications = await db.Notifications.ToListAsync();
        Assert.Equal(2, notifications.Count);
        Assert.Contains(notifications, n => n.UserId == "u1");
        Assert.Contains(notifications, n => n.UserId == "u2");
        Assert.DoesNotContain(notifications, n => n.UserId == "u3");
    }

    [Fact]
    public async Task ExportUsers_ReturnsCsvFile()
    {
        var (controller, db, userMgr) = CreateController();

        var u = new ApplicationUser { Id = "u1", UserName = "u1", Email = "u1@test.com", FullName = "Alice Doe", IsActive = true };
        db.Users.Add(u);
        await db.SaveChangesAsync();

        userMgr.Setup(m => m.GetRolesAsync(It.IsAny<ApplicationUser>())).ReturnsAsync(new List<string> { "User" });

        var fileResult = await controller.ExportUsers() as FileContentResult;

        Assert.NotNull(fileResult);
        Assert.Equal("text/csv", fileResult.ContentType);
        var csvContent = System.Text.Encoding.UTF8.GetString(fileResult.FileContents);
        Assert.Contains("Alice Doe", csvContent);
        Assert.Contains("u1@test.com", csvContent);
    }
}
