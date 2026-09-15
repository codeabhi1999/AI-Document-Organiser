using AIDocumentOrganizer.Web.Models.Entities;
using AIDocumentOrganizer.Web.Services.Interfaces;
using AIDocumentOrganizer.Web.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace AIDocumentOrganizer.Web.Controllers;

public class AccountController : Controller
{
    private readonly UserManager<ApplicationUser> _userMgr;
    private readonly SignInManager<ApplicationUser> _signInMgr;
    private readonly IAuditLogService _audit;
    private readonly ILogger<AccountController> _logger;

    public AccountController(
        UserManager<ApplicationUser> userMgr,
        SignInManager<ApplicationUser> signInMgr,
        IAuditLogService audit,
        ILogger<AccountController> logger)
    {
        _userMgr = userMgr;
        _signInMgr = signInMgr;
        _audit = audit;
        _logger = logger;
    }

    // ─────────── Register ───────────

    [HttpGet]
    public IActionResult Register() => User.Identity?.IsAuthenticated == true ? RedirectToAction("Index", "Dashboard") : View();

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Register(RegisterViewModel model)
    {
        if (!ModelState.IsValid) return View(model);

        var user = new ApplicationUser
        {
            UserName = model.Email,
            Email = model.Email,
            FullName = model.FullName,
            EmailConfirmed = true // Skip email confirm for now
        };

        var result = await _userMgr.CreateAsync(user, model.Password);
        if (result.Succeeded)
        {
            await _userMgr.AddToRoleAsync(user, "User");
            await _signInMgr.SignInAsync(user, isPersistent: false);
            await _audit.LogAsync(user.Id, "Register");
            TempData["Success"] = "Welcome! Your account has been created.";
            return RedirectToAction("Index", "Dashboard");
        }

        foreach (var error in result.Errors)
            ModelState.AddModelError(string.Empty, error.Description);
        return View(model);
    }

    // ─────────── Login ───────────

    [HttpGet]
    public IActionResult Login(string? returnUrl = null)
    {
        ViewBag.ReturnUrl = returnUrl;
        return User.Identity?.IsAuthenticated == true ? RedirectToAction("Index", "Dashboard") : View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginViewModel model, string? returnUrl = null)
    {
        ViewBag.ReturnUrl = returnUrl;
        if (!ModelState.IsValid) return View(model);

        var result = await _signInMgr.PasswordSignInAsync(model.Email, model.Password, model.RememberMe, lockoutOnFailure: true);

        if (result.Succeeded)
        {
            var user = await _userMgr.FindByEmailAsync(model.Email);
            if (user is not null)
            {
                user.LastLoginAt = DateTime.UtcNow;
                await _userMgr.UpdateAsync(user);
                await _audit.LogAsync(user.Id, "Login");
            }
            return LocalRedirect(string.IsNullOrEmpty(returnUrl) ? "/Dashboard" : returnUrl);
        }

        if (result.IsLockedOut)
        {
            ModelState.AddModelError(string.Empty, "Account locked after too many failed attempts. Try again in 15 minutes.");
            return View(model);
        }

        ModelState.AddModelError(string.Empty, "Invalid email or password.");
        return View(model);
    }

    // ─────────── Logout ───────────

    [HttpPost]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        var userId = _userMgr.GetUserId(User);
        await _signInMgr.SignOutAsync();
        if (userId is not null) await _audit.LogAsync(userId, "Logout");
        return RedirectToAction("Index", "Home");
    }

    // ─────────── Forgot Password ───────────

    [HttpGet]
    public IActionResult ForgotPassword() => View();

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ForgotPassword(ForgotPasswordViewModel model)
    {
        if (!ModelState.IsValid) return View(model);
        // In production: generate token, send email
        TempData["Info"] = "If an account with that email exists, a reset link has been sent.";
        return View("ForgotPasswordConfirmation");
    }

    // ─────────── Profile ───────────

    [HttpGet]
    [Authorize]
    public async Task<IActionResult> Profile()
    {
        var user = await _userMgr.GetUserAsync(User);
        if (user is null) return NotFound();

        return View(new ProfileViewModel
        {
            FullName = user.FullName,
            Email = user.Email,
            EmailRemindersEnabled = user.EmailRemindersEnabled,
            ReminderDaysBefore = user.ReminderDaysBefore
        });
    }

    [HttpPost]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Profile(ProfileViewModel model)
    {
        if (!ModelState.IsValid) return View(model);

        var user = await _userMgr.GetUserAsync(User);
        if (user is null) return NotFound();

        user.FullName = model.FullName;
        user.EmailRemindersEnabled = model.EmailRemindersEnabled;
        user.ReminderDaysBefore = model.ReminderDaysBefore;
        await _userMgr.UpdateAsync(user);

        if (!string.IsNullOrWhiteSpace(model.CurrentPassword) && !string.IsNullOrWhiteSpace(model.NewPassword))
        {
            var result = await _userMgr.ChangePasswordAsync(user, model.CurrentPassword, model.NewPassword);
            if (!result.Succeeded)
            {
                foreach (var e in result.Errors) ModelState.AddModelError(string.Empty, e.Description);
                return View(model);
            }
        }

        await _audit.LogAsync(user.Id, "ProfileUpdate");
        TempData["Success"] = "Profile updated successfully.";
        return RedirectToAction(nameof(Profile));
    }

    [HttpGet]
    public IActionResult AccessDenied() => View();
}
