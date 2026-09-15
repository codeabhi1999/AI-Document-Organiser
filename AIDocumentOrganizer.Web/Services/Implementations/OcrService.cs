using AIDocumentOrganizer.Web.Services.Interfaces;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace AIDocumentOrganizer.Web.Services.Implementations;

public class OcrService : IOcrService
{
    private readonly ILogger<OcrService> _logger;
    private readonly IConfiguration _config;

    public OcrService(ILogger<OcrService> logger, IConfiguration config)
    {
        _logger = logger;
        _config = config;
    }

    public async Task<OcrResult> ExtractTextAsync(string filePath, string contentType)
    {
        try
        {
            if (contentType == "application/pdf" || filePath.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
                return await ExtractFromPdfAsync(filePath);

            return await ExtractFromImageAsync(filePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "OCR extraction failed for {FilePath}", filePath);
            return new OcrResult(string.Empty, 0f, false, ex.Message);
        }
    }

    private static Task<OcrResult> ExtractFromPdfAsync(string filePath)
    {
        var sb = new System.Text.StringBuilder();
        try
        {
            using var pdf = PdfDocument.Open(filePath);
            foreach (Page page in pdf.GetPages())
            {
                var text = page.Text;
                if (!string.IsNullOrWhiteSpace(text))
                    sb.AppendLine(text);
            }

            var extracted = sb.ToString().Trim();
            var confidence = string.IsNullOrWhiteSpace(extracted) ? 0.1f : 0.9f;
            return Task.FromResult(new OcrResult(extracted, confidence, true));
        }
        catch
        {
            // If PdfPig fails (e.g. scanned PDF), return empty so AI still tries
            return Task.FromResult(new OcrResult(string.Empty, 0.1f, true, "PDF text extraction failed; scanned document."));
        }
    }

    private Task<OcrResult> ExtractFromImageAsync(string filePath)
    {
        // Attempt Tesseract OCR
        try
        {
            var tessDataPath = _config["OcrSettings:TesseractDataPath"] ?? "tessdata";
            var fullTessData = Path.IsPathRooted(tessDataPath)
                ? tessDataPath
                : Path.Combine(AppContext.BaseDirectory, tessDataPath);

            if (!Directory.Exists(fullTessData))
            {
                // No tessdata found — return descriptive error so AI can still work with filename heuristics
                _logger.LogWarning("Tesseract data path not found at {Path}. Skipping image OCR.", fullTessData);
                return Task.FromResult(new OcrResult(string.Empty, 0f, true,
                    "Tesseract not configured. AI will classify from file name only."));
            }

            using var engine = new Tesseract.TesseractEngine(fullTessData, "eng", Tesseract.EngineMode.Default);
            using var img = Tesseract.Pix.LoadFromFile(filePath);
            using var page = engine.Process(img);
            var text = page.GetText().Trim();
            var confidence = page.GetMeanConfidence();
            return Task.FromResult(new OcrResult(text, confidence, true));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Tesseract OCR failed for {FilePath}", filePath);
            return Task.FromResult(new OcrResult(string.Empty, 0f, true,
                $"Image OCR failed: {ex.Message}. AI will classify from file name only."));
        }
    }
}
