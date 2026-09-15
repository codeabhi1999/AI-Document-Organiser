using AIDocumentOrganizer.Web.Models.Enums;

namespace AIDocumentOrganizer.Web.ViewModels;

public class DashboardViewModel
{
    public int TotalDocuments { get; set; }
    public int ExpiringThisMonth { get; set; }
    public int ExpiredDocuments { get; set; }
    public int AddedThisMonth { get; set; }
    public int UnreadNotifications { get; set; }

    public List<DashboardDocumentItem> UpcomingExpiries { get; set; } = new();
    public List<DashboardDocumentItem> RecentUploads { get; set; } = new();
    public List<CategoryStat> CategoryStats { get; set; } = new();
}

public class DashboardDocumentItem
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string DocumentType { get; set; } = string.Empty;
    public string? CategoryName { get; set; }
    public string? CategoryColor { get; set; }
    public string? CategoryIcon { get; set; }
    public DateTime? ExpiryDate { get; set; }
    public int? DaysRemaining { get; set; }
    public ExpiryStatus ExpiryStatus { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class CategoryStat
{
    public string Name { get; set; } = string.Empty;
    public string ColorCode { get; set; } = string.Empty;
    public string IconClass { get; set; } = string.Empty;
    public int Count { get; set; }
}
