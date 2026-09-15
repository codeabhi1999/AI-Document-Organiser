namespace AIDocumentOrganizer.Web.Models.Enums;

public enum ExpiryStatus
{
    Lifetime = 0,   // Document never expires (e.g. birth certificate)
    Valid = 1,       // Expires > 30 days away
    ExpiringSoon = 2,// Expires within 30 days
    Expired = 3      // Already expired
}

public enum ProcessingStatus
{
    Pending = 0,
    Processing = 1,
    Completed = 2,
    Failed = 3,
    NeedsReview = 4
}

public enum ReminderLevel
{
    ThirtyDays = 30,
    FifteenDays = 15,
    SevenDays = 7,
    OneDay = 1,
    OnExpiry = 0
}

public enum DocumentSortBy
{
    UploadDate,
    ExpiryDate,
    Name,
    Category
}
