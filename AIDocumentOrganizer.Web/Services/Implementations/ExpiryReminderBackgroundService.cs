using AIDocumentOrganizer.Web.Data;
using AIDocumentOrganizer.Web.Models.Entities;
using AIDocumentOrganizer.Web.Models.Enums;
using AIDocumentOrganizer.Web.Services.Interfaces;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace AIDocumentOrganizer.Web.Services.Implementations;

public class ExpiryReminderBackgroundService : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly ILogger<ExpiryReminderBackgroundService> _logger;
    private readonly TimeSpan _checkInterval = TimeSpan.FromHours(12);

    public ExpiryReminderBackgroundService(IServiceProvider services, ILogger<ExpiryReminderBackgroundService> logger)
    {
        _services = services;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Expiry Reminder Background Service started.");

        // Initial delay so the app fully starts before first check
        await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            await RunReminderCycleAsync(stoppingToken);
            await Task.Delay(_checkInterval, stoppingToken);
        }
    }

    private async Task RunReminderCycleAsync(CancellationToken ct)
    {
        _logger.LogInformation("Running expiry reminder cycle at {Time}", DateTime.UtcNow);

        using var scope = _services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var email = scope.ServiceProvider.GetRequiredService<IEmailService>();
        var userMgr = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        try
        {
            var today = DateTime.UtcNow.Date;
            var reminderThresholds = new[] { 30, 15, 7, 1, 0 };

            // Update expiry statuses first
            var allDocsWithExpiry = await db.Documents.Where(d => d.ExpiryDate.HasValue).ToListAsync(ct);
            foreach (var doc in allDocsWithExpiry)
            {
                var days = (doc.ExpiryDate!.Value.Date - today).TotalDays;
                var newStatus = days < 0 ? ExpiryStatus.Expired :
                                days <= 30 ? ExpiryStatus.ExpiringSoon : ExpiryStatus.Valid;
                if (doc.ExpiryStatus != newStatus) { doc.ExpiryStatus = newStatus; doc.UpdatedAt = DateTime.UtcNow; }
            }
            await db.SaveChangesAsync(ct);

            // Find documents needing reminders
            foreach (var threshold in reminderThresholds)
            {
                var targetDate = today.AddDays(threshold);

                var docsForReminder = await db.Documents
                    .Include(d => d.User)
                    .Where(d => d.ExpiryDate.HasValue
                        && d.ExpiryDate.Value.Date == targetDate
                        && d.User.EmailRemindersEnabled
                        && d.User.IsActive)
                    .ToListAsync(ct);

                foreach (var doc in docsForReminder)
                {
                    // Check if reminder for this level was already sent today
                    var alreadySent = await db.DocumentReminders.AnyAsync(
                        r => r.DocumentId == doc.Id
                          && r.DaysBefore == threshold
                          && r.Status == "Sent"
                          && r.ScheduledForDate.Date == today, ct);

                    if (alreadySent) continue;

                    // Check user preference
                    var prefDays = (doc.User.ReminderDaysBefore ?? "30,15,7,1,0")
                        .Split(',', StringSplitOptions.RemoveEmptyEntries)
                        .Select(s => int.TryParse(s.Trim(), out var n) ? n : -1)
                        .ToList();

                    if (!prefDays.Contains(threshold)) continue;

                    var reminder = new DocumentReminder
                    {
                        DocumentId = doc.Id,
                        DaysBefore = threshold,
                        ScheduledForDate = today,
                        Status = "Pending"
                    };
                    db.DocumentReminders.Add(reminder);

                    try
                    {
                        var userEmail = await userMgr.GetEmailAsync(doc.User);
                        if (!string.IsNullOrEmpty(userEmail))
                        {
                            await email.SendExpiryReminderAsync(
                                userEmail, doc.User.FullName, doc.Title, threshold, doc.ExpiryDate!.Value);
                        }

                        // In-app notification
                        db.Notifications.Add(new Notification
                        {
                            UserId = doc.UserId,
                            DocumentId = doc.Id,
                            Title = threshold == 0 ? "Document Expired" : $"Document Expiring in {threshold} Day(s)",
                            Message = threshold == 0
                                ? $"Your document \"{doc.Title}\" has expired today."
                                : $"Your document \"{doc.Title}\" expires in {threshold} day(s) on {doc.ExpiryDate!.Value.ToString("dd MMM yyyy")}.",
                            Type = threshold <= 7 ? "Danger" : threshold <= 15 ? "Warning" : "Info"
                        });

                        reminder.Status = "Sent";
                        reminder.SentAt = DateTime.UtcNow;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to send reminder for document {DocId}", doc.Id);
                        reminder.Status = "Failed";
                    }
                }

                await db.SaveChangesAsync(ct);
            }

            _logger.LogInformation("Expiry reminder cycle completed.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in expiry reminder cycle");
        }
    }
}
