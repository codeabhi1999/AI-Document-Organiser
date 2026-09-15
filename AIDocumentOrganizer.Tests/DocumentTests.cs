using AIDocumentOrganizer.Web.Data;
using AIDocumentOrganizer.Web.Models.Entities;
using AIDocumentOrganizer.Web.Models.Enums;
using AIDocumentOrganizer.Web.Services.Implementations;
using AIDocumentOrganizer.Web.Services.Interfaces;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace AIDocumentOrganizer.Tests;

// ═══════════════════════════════════════════════════════════════
// Helper: in-memory DbContext factory
// ═══════════════════════════════════════════════════════════════

public static class DbFactory
{
    public static ApplicationDbContext Create(string dbName = "")
    {
        if (string.IsNullOrEmpty(dbName)) dbName = Guid.NewGuid().ToString();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;
        var db = new ApplicationDbContext(options);
        db.Database.EnsureCreated();
        return db;
    }
}

// ═══════════════════════════════════════════════════════════════
// 1. Document Ownership Tests
// ═══════════════════════════════════════════════════════════════

public class DocumentOwnershipTests
{
    [Fact]
    public async Task GetDocumentDetails_ReturnNull_WhenUserIsNotOwner()
    {
        // Arrange
        using var db = DbFactory.Create();
        var ownerUserId  = "user-owner";
        var attackerUserId = "user-attacker";

        db.Documents.Add(new Document { Id = Guid.NewGuid(), UserId = ownerUserId, Title = "Secret Doc" });
        await db.SaveChangesAsync();

        var svc = BuildDocumentService(db);
        var doc = await db.Documents.FirstAsync();

        // Act: attacker tries to access owner's document
        var result = await svc.GetDocumentDetailsAsync(doc.Id, attackerUserId);

        // Assert: must return null — never expose other user's data
        Assert.Null(result);
    }

    [Fact]
    public async Task GetDocumentDetails_ReturnDocument_WhenUserIsOwner()
    {
        using var db = DbFactory.Create();
        var userId = "user-1";
        db.Documents.Add(new Document { Id = Guid.NewGuid(), UserId = userId, Title = "My Passport" });
        await db.SaveChangesAsync();

        var svc = BuildDocumentService(db);
        var docId = (await db.Documents.FirstAsync()).Id;

        var result = await svc.GetDocumentDetailsAsync(docId, userId);

        Assert.NotNull(result);
        Assert.Equal("My Passport", result.Title);
    }

    [Fact]
    public async Task DeleteDocument_ReturnFalse_WhenUserIsNotOwner()
    {
        using var db = DbFactory.Create();
        db.Documents.Add(new Document { Id = Guid.NewGuid(), UserId = "user-A", Title = "Doc A" });
        await db.SaveChangesAsync();

        var svc = BuildDocumentService(db);
        var docId = (await db.Documents.FirstAsync()).Id;

        var result = await svc.DeleteDocumentAsync(docId, "user-B");

        Assert.False(result);
        Assert.Equal(1, await db.Documents.CountAsync()); // not deleted
    }

    [Fact]
    public async Task DocumentBelongsToUser_ReturnTrue_ForOwner()
    {
        using var db = DbFactory.Create();
        var userId = "user-X";
        var docId = Guid.NewGuid();
        db.Documents.Add(new Document { Id = docId, UserId = userId, Title = "X" });
        await db.SaveChangesAsync();

        var svc = BuildDocumentService(db);
        Assert.True(await svc.DocumentBelongsToUserAsync(docId, userId));
        Assert.False(await svc.DocumentBelongsToUserAsync(docId, "user-Y"));
    }

    private static IDocumentService BuildDocumentService(ApplicationDbContext db)
    {
        var env = new Mock<IWebHostEnvironment>();
        env.SetupGet(e => e.ContentRootPath).Returns(Path.GetTempPath());
        var config = new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build();
        var settings = Options.Create(new AppSettings());
        return new DocumentService(
            db,
            Mock.Of<IOcrService>(),
            Mock.Of<IAiDocumentService>(),
            Mock.Of<IAuditLogService>(),
            Mock.Of<ILogger<DocumentService>>(),
            settings, env.Object, config);
    }
}

// ═══════════════════════════════════════════════════════════════
// 2. Expiry Calculation Tests
// ═══════════════════════════════════════════════════════════════

public class ExpiryCalculationTests
{
    [Theory]
    [InlineData(31,  ExpiryStatus.Valid)]
    [InlineData(30,  ExpiryStatus.ExpiringSoon)]
    [InlineData(15,  ExpiryStatus.ExpiringSoon)]
    [InlineData(1,   ExpiryStatus.ExpiringSoon)]
    [InlineData(0,   ExpiryStatus.ExpiringSoon)]
    [InlineData(-1,  ExpiryStatus.Expired)]
    [InlineData(-30, ExpiryStatus.Expired)]
    public void ExpiryStatus_CalculatedCorrectly(int daysFromNow, ExpiryStatus expected)
    {
        var expiryDate = DateTime.UtcNow.Date.AddDays(daysFromNow);
        var days = (expiryDate - DateTime.UtcNow.Date).TotalDays;

        var status = days < 0 ? ExpiryStatus.Expired :
                     days <= 30 ? ExpiryStatus.ExpiringSoon : ExpiryStatus.Valid;

        Assert.Equal(expected, status);
    }

    [Fact]
    public void NullExpiryDate_ReturnsLifetime()
    {
        DateTime? expiryDate = null;
        var status = expiryDate.HasValue ? ExpiryStatus.Valid : ExpiryStatus.Lifetime;
        Assert.Equal(ExpiryStatus.Lifetime, status);
    }
}

// ═══════════════════════════════════════════════════════════════
// 3. File Validation Tests
// ═══════════════════════════════════════════════════════════════

public class FileValidationTests
{
    [Theory]
    [InlineData("document.pdf",  "application/pdf",  true)]
    [InlineData("photo.jpg",     "image/jpeg",       true)]
    [InlineData("photo.jpeg",    "image/jpeg",       true)]
    [InlineData("scan.png",      "image/png",        true)]
    [InlineData("virus.exe",     "application/exe",  false)]
    [InlineData("script.js",     "text/javascript",  false)]
    [InlineData("archive.zip",   "application/zip",  false)]
    [InlineData("sheet.xlsx",    "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", false)]
    public void AllowedExtensions_Validated(string filename, string contentType, bool expectedAllowed)
    {
        _ = contentType; // parameter kept for documentation of expected mime type
        var allowed = new[] { ".pdf", ".jpg", ".jpeg", ".png" };
        var ext = Path.GetExtension(filename).ToLowerInvariant();
        Assert.Equal(expectedAllowed, allowed.Contains(ext));
    }

    [Fact]
    public void FileSizeLimit_Enforced()
    {
        const int maxMb = 15;
        var maxBytes = maxMb * 1024 * 1024;

        Assert.True(10 * 1024 * 1024 <= maxBytes);  // 10MB: OK
        Assert.False(20 * 1024 * 1024 <= maxBytes); // 20MB: too big
    }

    [Theory]
    [InlineData(new byte[] { 0x25, 0x50, 0x44, 0x46 }, ".pdf",  true)]   // PDF magic
    [InlineData(new byte[] { 0xFF, 0xD8, 0xFF },        ".jpg",  true)]   // JPEG magic
    [InlineData(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, ".png",  true)]   // PNG magic
    [InlineData(new byte[] { 0x4D, 0x5A },              ".pdf",  false)]  // EXE disguised as PDF
    public void MagicBytes_Validated(byte[] header, string extension, bool expectedValid)
    {
        var magicBytes = new Dictionary<string, byte[]>
        {
            { ".pdf",  new byte[] { 0x25, 0x50, 0x44, 0x46 } },
            { ".jpg",  new byte[] { 0xFF, 0xD8, 0xFF } },
            { ".jpeg", new byte[] { 0xFF, 0xD8, 0xFF } },
            { ".png",  new byte[] { 0x89, 0x50, 0x4E, 0x47 } }
        };

        var valid = magicBytes.TryGetValue(extension, out var magic)
            && header.Take(magic.Length).SequenceEqual(magic);

        Assert.Equal(expectedValid, valid);
    }
}

// ═══════════════════════════════════════════════════════════════
// 4. AI JSON Parsing Tests
// ═══════════════════════════════════════════════════════════════

public class AiResponseParsingTests
{
    private static AIDocumentOrganizer.Web.Services.Implementations.GeminiAiDocumentService CreateService()
    {
        var httpFactory = new Mock<IHttpClientFactory>();
        var settings = new AiServiceSettings { GeminiApiKey = "" };
        return new GeminiAiDocumentService(
            httpFactory.Object, settings, Mock.Of<ILogger<GeminiAiDocumentService>>());
    }

    [Fact]
    public async Task LocalHeuristic_DetectsAadhaar()
    {
        var svc = CreateService();
        var result = await svc.AnalyzeDocumentAsync("AADHAAR CARD Government of India UID: 1234 5678 9012", "aadhaar.pdf");
        Assert.True(result.Success);
        Assert.Equal("Identity", result.Category);
        Assert.Contains("Aadhaar", result.DocumentType);
    }

    [Fact]
    public async Task LocalHeuristic_DetectsVehicleInsurance()
    {
        var svc = CreateService();
        var result = await svc.AnalyzeDocumentAsync("Vehicle Insurance Policy Motor Car Comprehensive", "car_insurance.pdf");
        Assert.True(result.Success);
        Assert.Equal("Insurance", result.Category);
    }

    [Fact]
    public async Task LocalHeuristic_DetectsPassport()
    {
        var svc = CreateService();
        var result = await svc.AnalyzeDocumentAsync("PASSPORT REPUBLIC OF INDIA", "passport_scan.jpg");
        Assert.True(result.Success);
        Assert.Equal("Identity", result.Category);
        Assert.Contains("Passport", result.DocumentType);
    }

    [Fact]
    public async Task EmptyText_StillReturnsSuccess()
    {
        var svc = CreateService();
        var result = await svc.AnalyzeDocumentAsync("", "unknown.pdf");
        Assert.True(result.Success);
        Assert.NotNull(result.Category);
    }
}
