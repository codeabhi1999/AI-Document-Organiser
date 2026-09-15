using AIDocumentOrganizer.Web.Data;
using AIDocumentOrganizer.Web.Models.Entities;
using AIDocumentOrganizer.Web.Services.Implementations;
using AIDocumentOrganizer.Web.Services.Interfaces;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// ─────────── Database ───────────
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// ─────────── Identity ───────────
builder.Services.AddIdentity<ApplicationUser, IdentityRole>(options =>
{
    options.Password.RequiredLength = 8;
    options.Password.RequireDigit = true;
    options.Password.RequireUppercase = true;
    options.Password.RequireNonAlphanumeric = true;
    options.SignIn.RequireConfirmedEmail = false;  // Set true in production
    options.Lockout.MaxFailedAccessAttempts = 5;
    options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
})
.AddEntityFrameworkStores<ApplicationDbContext>()
.AddDefaultTokenProviders();

// ─────────── Auth Cookies ───────────
builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/Account/Login";
    options.LogoutPath = "/Account/Logout";
    options.AccessDeniedPath = "/Account/AccessDenied";
    options.ExpireTimeSpan = TimeSpan.FromDays(7);
    options.SlidingExpiration = true;
    options.Cookie.HttpOnly = true;
    options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
    options.Cookie.SameSite = SameSiteMode.Lax;
});

// ─────────── Settings ───────────
var appSettings = builder.Configuration.GetSection("AppSettings").Get<AppSettings>() ?? new AppSettings();
builder.Services.AddSingleton(appSettings);
builder.Services.AddOptions<AppSettings>().Bind(builder.Configuration.GetSection("AppSettings"));

var aiSettings = builder.Configuration.GetSection("AiService").Get<AiServiceSettings>() ?? new AiServiceSettings();
// Override API key from environment variable if available
aiSettings.GeminiApiKey = builder.Configuration["AiService:GeminiApiKey"]
    ?? Environment.GetEnvironmentVariable("GEMINI_API_KEY")
    ?? aiSettings.GeminiApiKey;
builder.Services.AddSingleton(aiSettings);

var emailSettings = builder.Configuration.GetSection("EmailSettings").Get<EmailSettings>() ?? new EmailSettings();
emailSettings.SenderPassword = builder.Configuration["EmailSettings:SenderPassword"]
    ?? Environment.GetEnvironmentVariable("EMAIL_PASSWORD")
    ?? emailSettings.SenderPassword;
builder.Services.AddSingleton(emailSettings);

// ─────────── HTTP Client ───────────
builder.Services.AddHttpClient("GeminiClient");

// ─────────── Application Services ───────────
builder.Services.AddScoped<IOcrService, OcrService>();
builder.Services.AddScoped<IAiDocumentService, GeminiAiDocumentService>();
builder.Services.AddScoped<IDocumentService, DocumentService>();
builder.Services.AddScoped<IAuditLogService, AuditLogService>();
builder.Services.AddScoped<IEmailService, SmtpEmailService>();
builder.Services.AddHttpContextAccessor();

// ─────────── Background Services ───────────
builder.Services.AddHostedService<ExpiryReminderBackgroundService>();

// ─────────── MVC ───────────
builder.Services.AddControllersWithViews();

builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromHours(2);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});

var app = builder.Build();

// ─────────── Middleware Pipeline ───────────
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseSession();
app.UseAuthentication();
app.UseAuthorization();

// ─────────── Routes ───────────
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

// ─────────── DB Seed ───────────
using (var scope = app.Services.CreateScope())
{
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    await DbInitializer.SeedAsync(app.Services, logger);
}

app.Run();
