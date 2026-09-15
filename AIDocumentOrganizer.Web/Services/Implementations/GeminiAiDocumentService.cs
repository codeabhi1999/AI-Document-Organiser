using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using AIDocumentOrganizer.Web.Services.Interfaces;
using AIDocumentOrganizer.Web.ViewModels;

namespace AIDocumentOrganizer.Web.Services.Implementations;

public class AiServiceSettings
{
    public string Provider { get; set; } = "Gemini";
    public string GeminiApiKey { get; set; } = string.Empty;
    public string GeminiModel { get; set; } = "gemini-1.5-flash";
    public string GeminiBaseUrl { get; set; } = "https://generativelanguage.googleapis.com/v1beta/models";
    public int MaxRetries { get; set; } = 2;
    public int TimeoutSeconds { get; set; } = 30;
}

public class GeminiAiDocumentService : IAiDocumentService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly AiServiceSettings _settings;
    private readonly ILogger<GeminiAiDocumentService> _logger;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public GeminiAiDocumentService(
        IHttpClientFactory httpClientFactory,
        AiServiceSettings settings,
        ILogger<GeminiAiDocumentService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _settings = settings;
        _logger = logger;
    }

    // ─────────── Document Analysis ───────────

    public async Task<AiExtractionResult> AnalyzeDocumentAsync(string extractedText, string originalFileName)
    {
        var prompt = BuildExtractionPrompt(extractedText, originalFileName);

        if (string.IsNullOrWhiteSpace(_settings.GeminiApiKey))
        {
            _logger.LogWarning("No Gemini API key configured. Using local heuristic classification.");
            return LocalHeuristicAnalysis(extractedText, originalFileName);
        }

        for (var attempt = 0; attempt <= _settings.MaxRetries; attempt++)
        {
            try
            {
                var raw = await CallGeminiAsync(prompt);
                var result = ParseExtractionResponse(raw);
                if (result is not null) return result with { RawResponse = raw };
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Gemini API attempt {Attempt} failed", attempt + 1);
                if (attempt == _settings.MaxRetries)
                    return LocalHeuristicAnalysis(extractedText, originalFileName) with
                    {
                        Error = $"AI API unavailable after {_settings.MaxRetries + 1} attempts. Used local heuristic."
                    };
                await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt)));
            }
        }

        return LocalHeuristicAnalysis(extractedText, originalFileName);
    }

    // ─────────── AI Chat / Assistant ───────────

    public async Task<string> ChatAsync(string userMessage, string userId, List<DocumentSummaryForAi> userDocuments)
    {
        var contextSb = new StringBuilder();
        contextSb.AppendLine("You are a helpful personal document assistant. Only answer based on the documents listed below.");
        contextSb.AppendLine("Never reveal other users' data. Answer concisely and helpfully.");
        contextSb.AppendLine();
        contextSb.AppendLine("USER'S DOCUMENTS:");
        foreach (var doc in userDocuments)
        {
            contextSb.AppendLine($"- [{doc.Category ?? "Other"}] {doc.Title} (Type: {doc.DocumentType})" +
                $" Holder: {doc.HolderName ?? "N/A"}" +
                $" Expiry: {(doc.ExpiryDate.HasValue ? doc.ExpiryDate.Value.ToString("dd MMM yyyy") : "No Expiry")}" +
                $" DaysLeft: {(doc.DaysRemaining.HasValue ? doc.DaysRemaining.Value.ToString() : "N/A")}" +
                $" Tags: {doc.Tags ?? ""}");
        }
        contextSb.AppendLine();
        contextSb.AppendLine($"USER QUESTION: {userMessage}");

        if (string.IsNullOrWhiteSpace(_settings.GeminiApiKey))
            return AnswerLocallyFromDocuments(userMessage, userDocuments);

        try
        {
            return await CallGeminiAsync(contextSb.ToString());
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "AI chat failed, using local response");
            return AnswerLocallyFromDocuments(userMessage, userDocuments);
        }
    }

    // ─────────── Gemini API Call ───────────

    private async Task<string> CallGeminiAsync(string prompt)
    {
        var client = _httpClientFactory.CreateClient("GeminiClient");
        client.Timeout = TimeSpan.FromSeconds(_settings.TimeoutSeconds);

        var url = $"{_settings.GeminiBaseUrl}/{_settings.GeminiModel}:generateContent?key={_settings.GeminiApiKey}";

        var body = new
        {
            contents = new[]
            {
                new { parts = new[] { new { text = prompt } } }
            },
            generationConfig = new
            {
                temperature = 0.1,
                maxOutputTokens = 1024
            }
        };

        var json = JsonSerializer.Serialize(body);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await client.PostAsync(url, content);
        response.EnsureSuccessStatusCode();

        var raw = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(raw);

        return doc.RootElement
            .GetProperty("candidates")[0]
            .GetProperty("content")
            .GetProperty("parts")[0]
            .GetProperty("text")
            .GetString() ?? string.Empty;
    }

    // ─────────── Prompt Building ───────────

    private static string BuildExtractionPrompt(string text, string fileName)
    {
        var docText = string.IsNullOrWhiteSpace(text)
            ? "[No text extracted - classify from filename only]"
            : text.Length > 3000 ? text[..3000] : text;

        return
            "You are a document analysis AI. Analyze the document text and extract structured information.\n" +
            "Return ONLY a valid JSON object with NO markdown code fences, NO explanation text.\n\n" +
            $"Document filename: {fileName}\n" +
            $"Document text:\n{docText}\n\n" +
            "Return this exact JSON structure (use null for unknown fields):\n" +
            "{\n" +
            "  \"category\": \"<Identity|Government|Vehicle|Insurance|Education|Financial|Bills|Property/Rental|Medical|Other>\",\n" +
            "  \"documentType\": \"<specific type e.g. Aadhaar Card, PAN Card, Passport, Driving License, Vehicle Insurance, etc.>\",\n" +
            "  \"documentNumber\": \"<the actual number or null>\",\n" +
            "  \"holderName\": \"<full name or null>\",\n" +
            "  \"issueDate\": \"<YYYY-MM-DD or null>\",\n" +
            "  \"expiryDate\": \"<YYYY-MM-DD or null>\",\n" +
            "  \"issuingAuthority\": \"<authority name or null>\",\n" +
            "  \"vehicleNumber\": \"<vehicle registration number or null>\",\n" +
            "  \"policyNumber\": \"<policy or reference number or null>\",\n" +
            "  \"amount\": null,\n" +
            "  \"importantNotes\": \"<brief notes or null>\",\n" +
            "  \"tags\": [\"tag1\", \"tag2\"],\n" +
            "  \"confidenceScore\": 0.9\n" +
            "}";
    }

    // ─────────── JSON Parsing ───────────

    private AiExtractionResult? ParseExtractionResponse(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        try
        {
            // Strip any accidental markdown fences
            var cleaned = Regex.Replace(raw.Trim(), @"^```json?\s*|```\s*$", "", RegexOptions.Multiline).Trim();

            using var doc = JsonDocument.Parse(cleaned);
            var root = doc.RootElement;

            DateTime? ParseDate(string key)
            {
                if (!root.TryGetProperty(key, out var el) || el.ValueKind == JsonValueKind.Null) return null;
                return DateTime.TryParse(el.GetString(), out var d) ? d : null;
            }

            var tags = root.TryGetProperty("tags", out var tagsEl) && tagsEl.ValueKind == JsonValueKind.Array
                ? tagsEl.EnumerateArray().Select(t => t.GetString() ?? "").Where(t => !string.IsNullOrEmpty(t)).ToList()
                : new List<string>();

            decimal? amount = null;
            if (root.TryGetProperty("amount", out var amtEl) && amtEl.ValueKind != JsonValueKind.Null)
                amount = amtEl.TryGetDecimal(out var a) ? a : null;

            double confidence = 0.7;
            if (root.TryGetProperty("confidenceScore", out var confEl))
                confidence = confEl.TryGetDouble(out var c) ? c : 0.7;

            return new AiExtractionResult(
                Success: true,
                Category: root.TryGetProperty("category", out var cat) ? cat.GetString() : null,
                DocumentType: root.TryGetProperty("documentType", out var dtype) ? dtype.GetString() : null,
                DocumentNumber: root.TryGetProperty("documentNumber", out var dnum) ? dnum.GetString() : null,
                HolderName: root.TryGetProperty("holderName", out var hname) ? hname.GetString() : null,
                IssueDate: ParseDate("issueDate"),
                ExpiryDate: ParseDate("expiryDate"),
                IssuingAuthority: root.TryGetProperty("issuingAuthority", out var auth) ? auth.GetString() : null,
                VehicleNumber: root.TryGetProperty("vehicleNumber", out var vnum) ? vnum.GetString() : null,
                PolicyNumber: root.TryGetProperty("policyNumber", out var pnum) ? pnum.GetString() : null,
                Amount: amount,
                ImportantNotes: root.TryGetProperty("importantNotes", out var notes) ? notes.GetString() : null,
                Tags: tags,
                ConfidenceScore: confidence,
                Error: null,
                RawResponse: raw
            );
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse AI JSON response: {Raw}", raw);
            return null;
        }
    }

    // ─────────── Local Heuristic Fallback ───────────

    private static AiExtractionResult LocalHeuristicAnalysis(string text, string fileName)
    {
        var combined = (fileName + " " + text).ToLower();

        var category = "Other";
        var docType = "Document";

        if (Regex.IsMatch(combined, @"aadhaar|aadhar|uid\b|uidai")) { category = "Identity"; docType = "Aadhaar Card"; }
        else if (Regex.IsMatch(combined, @"\bpan\b|permanent account")) { category = "Identity"; docType = "PAN Card"; }
        else if (Regex.IsMatch(combined, @"passport")) { category = "Identity"; docType = "Passport"; }
        else if (Regex.IsMatch(combined, @"driving licen|dl\b|driver.*licen")) { category = "Vehicle"; docType = "Driving License"; }
        else if (Regex.IsMatch(combined, @"vehicle.*insurance|motor.*insurance|car.*insurance")) { category = "Insurance"; docType = "Vehicle Insurance"; }
        else if (Regex.IsMatch(combined, @"health.*insurance|mediclaim|medical.*insurance")) { category = "Insurance"; docType = "Health Insurance"; }
        else if (Regex.IsMatch(combined, @"life.*insurance|term.*plan")) { category = "Insurance"; docType = "Life Insurance"; }
        else if (Regex.IsMatch(combined, @"rc\b|registration.*certific|vehicle.*reg")) { category = "Vehicle"; docType = "Vehicle RC"; }
        else if (Regex.IsMatch(combined, @"birth.*certific")) { category = "Government"; docType = "Birth Certificate"; }
        else if (Regex.IsMatch(combined, @"rent.*agreement|lease.*agreement|rental")) { category = "Property/Rental"; docType = "Rent Agreement"; }
        else if (Regex.IsMatch(combined, @"marksheet|degree|certificate|diploma|admit.*card")) { category = "Education"; docType = "Education Certificate"; }
        else if (Regex.IsMatch(combined, @"bank.*statement|account.*statement")) { category = "Financial"; docType = "Bank Statement"; }
        else if (Regex.IsMatch(combined, @"electricity|water.*bill|gas.*bill|telephone.*bill|internet.*bill")) { category = "Bills"; docType = "Utility Bill"; }
        else if (Regex.IsMatch(combined, @"prescription|medical.*report|lab.*report|discharge.*summary")) { category = "Medical"; docType = "Medical Document"; }

        // Try extract dates
        DateTime? expiryDate = null;
        var dateMatches = Regex.Matches(text, @"\b(\d{2}[/-]\d{2}[/-]\d{4}|\d{4}[/-]\d{2}[/-]\d{2})\b");
        if (dateMatches.Count > 0 && DateTime.TryParse(dateMatches[^1].Value, out var lastDate))
            expiryDate = lastDate;

        var tags = new List<string> { category.ToLower() };
        if (Regex.IsMatch(combined, @"insur")) tags.Add("insurance");
        if (Regex.IsMatch(combined, @"vehicle|car|bike|motor")) tags.Add("vehicle");

        return new AiExtractionResult(
            Success: true,
            Category: category,
            DocumentType: docType,
            DocumentNumber: null,
            HolderName: null,
            IssueDate: null,
            ExpiryDate: expiryDate,
            IssuingAuthority: null,
            VehicleNumber: null,
            PolicyNumber: null,
            Amount: null,
            ImportantNotes: "Document was classified using local heuristics. Please review and edit details.",
            Tags: tags,
            ConfidenceScore: 0.4,
            Error: null,
            RawResponse: null
        );
    }

    private static string AnswerLocallyFromDocuments(string question, List<DocumentSummaryForAi> docs)
    {
        var q = question.ToLower();
        var now = DateTime.UtcNow;

        if (Regex.IsMatch(q, @"expir(e|ing|es).*month|month.*expir"))
        {
            var expThisMonth = docs.Where(d => d.ExpiryDate.HasValue
                && d.ExpiryDate.Value.Year == now.Year && d.ExpiryDate.Value.Month == now.Month).ToList();
            if (!expThisMonth.Any()) return "None of your documents expire this month.";
            return "Documents expiring this month:\n" + string.Join("\n", expThisMonth.Select(d =>
                $"• {d.Title} — {d.ExpiryDate!.Value:dd MMM yyyy} ({d.DaysRemaining} days remaining)"));
        }

        if (Regex.IsMatch(q, @"expir(e|ing|es).*soon|upcoming.*expir|due.*soon"))
        {
            var soon = docs.Where(d => d.DaysRemaining.HasValue && d.DaysRemaining.Value >= 0 && d.DaysRemaining.Value <= 60)
                .OrderBy(d => d.DaysRemaining).ToList();
            if (!soon.Any()) return "No documents expiring within the next 60 days.";
            return "Documents expiring soon:\n" + string.Join("\n", soon.Select(d =>
                $"• {d.Title} — {d.ExpiryDate!.Value:dd MMM yyyy} ({d.DaysRemaining} days left)"));
        }

        if (Regex.IsMatch(q, @"insurance"))
        {
            var ins = docs.Where(d => d.Category == "Insurance" || (d.Tags?.Contains("insurance") == true)).ToList();
            if (!ins.Any()) return "No insurance documents found.";
            return "Your insurance documents:\n" + string.Join("\n", ins.Select(d =>
                $"• {d.Title} ({d.DocumentType}) — {(d.ExpiryDate.HasValue ? "Expires: " + d.ExpiryDate.Value.ToString("dd MMM yyyy") : "No expiry")}"));
        }

        if (Regex.IsMatch(q, @"vehicle|car|bike|motor"))
        {
            var veh = docs.Where(d => d.Category == "Vehicle" || (d.Tags?.Contains("vehicle") == true)).ToList();
            if (!veh.Any()) return "No vehicle documents found.";
            return "Your vehicle documents:\n" + string.Join("\n", veh.Select(d =>
                $"• {d.Title} ({d.DocumentType}) — {(d.ExpiryDate.HasValue ? "Expires: " + d.ExpiryDate.Value.ToString("dd MMM yyyy") : "No expiry")}"));
        }

        if (Regex.IsMatch(q, @"summarize|summary|all.*document|list.*document"))
        {
            if (!docs.Any()) return "You have no documents uploaded yet.";
            return $"You have {docs.Count} document(s):\n" + string.Join("\n", docs.Select(d =>
                $"• [{d.Category ?? "Other"}] {d.Title} — {(d.ExpiryDate.HasValue ? "Expires: " + d.ExpiryDate.Value.ToString("dd MMM yyyy") : "No expiry")}"));
        }

        return "I can help you find information about your documents. Try asking: \"Which documents expire next month?\", \"Show my insurance documents\", or \"Summarize all my documents\".";
    }
}
