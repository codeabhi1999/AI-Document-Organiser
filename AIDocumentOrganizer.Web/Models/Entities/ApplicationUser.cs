using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;

namespace AIDocumentOrganizer.Web.Models.Entities;

public class ApplicationUser : IdentityUser
{
    [Required, MaxLength(150)]
    public string FullName { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastLoginAt { get; set; }
    public bool IsActive { get; set; } = true;

    // Reminder preferences (comma-separated days before: "30,15,7,1,0")
    public string ReminderDaysBefore { get; set; } = "30,15,7,1,0";
    public bool EmailRemindersEnabled { get; set; } = true;

    public virtual ICollection<Document> Documents { get; set; } = new List<Document>();
    public virtual ICollection<Notification> Notifications { get; set; } = new List<Notification>();
    public virtual ICollection<AuditLog> AuditLogs { get; set; } = new List<AuditLog>();
}
