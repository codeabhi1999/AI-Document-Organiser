using System.Net;
using System.Net.Mail;
using AIDocumentOrganizer.Web.Services.Interfaces;

namespace AIDocumentOrganizer.Web.Services.Implementations;

public class EmailSettings
{
    public string SmtpHost { get; set; } = string.Empty;
    public int SmtpPort { get; set; } = 587;
    public bool EnableSsl { get; set; } = true;
    public string SenderEmail { get; set; } = string.Empty;
    public string SenderName { get; set; } = "AI Document Organizer";
    public string SenderPassword { get; set; } = string.Empty;
}

public class SmtpEmailService : IEmailService
{
    private readonly EmailSettings _settings;
    private readonly ILogger<SmtpEmailService> _logger;

    public SmtpEmailService(EmailSettings settings, ILogger<SmtpEmailService> logger)
    {
        _settings = settings;
        _logger = logger;
    }

    public async Task SendAsync(string to, string subject, string htmlBody)
    {
        if (string.IsNullOrWhiteSpace(_settings.SenderEmail) || string.IsNullOrWhiteSpace(_settings.SenderPassword))
        {
            _logger.LogWarning("Email not configured. Skipping send to {To}: {Subject}", to, subject);
            return;
        }

        try
        {
            using var msg = new MailMessage
            {
                From = new MailAddress(_settings.SenderEmail, _settings.SenderName),
                Subject = subject,
                Body = htmlBody,
                IsBodyHtml = true
            };
            msg.To.Add(to);

            using var smtp = new SmtpClient(_settings.SmtpHost, _settings.SmtpPort)
            {
                EnableSsl = _settings.EnableSsl,
                Credentials = new NetworkCredential(_settings.SenderEmail, _settings.SenderPassword)
            };

            await smtp.SendMailAsync(msg);
            _logger.LogInformation("Email sent to {To}: {Subject}", to, subject);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send email to {To}", to);
            // Don't rethrow — email failure should not break the application
        }
    }

    public Task SendExpiryReminderAsync(string to, string userName, string documentTitle, int daysRemaining, DateTime expiryDate)
    {
        var urgencyColor = daysRemaining <= 7 ? "#e63946" : daysRemaining <= 15 ? "#f4a261" : "#2a9d8f";
        var urgencyText = daysRemaining == 0 ? "expires TODAY" : daysRemaining < 0 ? "has EXPIRED" : $"expires in {daysRemaining} day(s)";

        var html = $"""
        <!DOCTYPE html>
        <html>
        <head><meta charset="utf-8"></head>
        <body style="font-family: 'Segoe UI', Arial, sans-serif; background:#f8f9fa; margin:0; padding:20px;">
          <div style="max-width:520px; margin:0 auto; background:#fff; border-radius:12px; overflow:hidden; box-shadow:0 4px 20px rgba(0,0,0,0.1);">
            <div style="background:linear-gradient(135deg,#4361ee,#7209b7); padding:30px; text-align:center;">
              <h1 style="color:#fff; margin:0; font-size:22px;">📄 Document Reminder</h1>
              <p style="color:rgba(255,255,255,0.85); margin:8px 0 0;">AI Document Organizer</p>
            </div>
            <div style="padding:30px;">
              <p style="font-size:16px; color:#343a40;">Hi <strong>{userName}</strong>,</p>
              <p style="font-size:15px; color:#495057;">Your document <strong>"{documentTitle}"</strong> <span style="color:{urgencyColor}; font-weight:700;">{urgencyText}</span>.</p>
              <div style="background:#f8f9fa; border-left:4px solid {urgencyColor}; border-radius:8px; padding:16px; margin:20px 0;">
                <p style="margin:0; font-size:14px; color:#6c757d;">Expiry Date</p>
                <p style="margin:4px 0 0; font-size:18px; font-weight:700; color:{urgencyColor};">{expiryDate:dd MMMM yyyy}</p>
              </div>
              <p style="font-size:14px; color:#6c757d;">Please renew this document to avoid any issues. Login to AI Document Organizer to view and manage your documents.</p>
            </div>
            <div style="background:#f8f9fa; padding:16px; text-align:center; border-top:1px solid #dee2e6;">
              <p style="margin:0; font-size:12px; color:#adb5bd;">AI Document Organizer · Sent automatically</p>
            </div>
          </div>
        </body>
        </html>
        """;

        var subject = daysRemaining <= 0
            ? $"⚠️ EXPIRED: {documentTitle}"
            : $"🔔 Reminder: \"{documentTitle}\" {urgencyText}";

        return SendAsync(to, subject, html);
    }
}
