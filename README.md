# AI Document Organizer

A **production-ready, AI-powered personal document management system** built with **ASP.NET Core 8 MVC**, Entity Framework Core, SQL Server, ASP.NET Core Identity, and Google Gemini AI.

---

## ✨ Features

| Feature | Description |
|---|---|
| 🔒 **Secure Auth** | Register, Login, Forgot Password, Account Lockout, per-user isolation |
| 📄 **Smart Upload** | Drag-and-drop PDF/JPG/PNG upload with magic-byte file validation |
| 🤖 **AI Analysis** | Gemini AI extracts document number, holder name, dates, authority — structured JSON |
| 🔍 **OCR** | PdfPig (digital PDFs) + Tesseract (scanned images) |
| 📊 **Dashboard** | Expiry timeline, category breakdown, recent uploads, color-coded status |
| 🔔 **Smart Reminders** | Background service sends email + in-app alerts at 30/15/7/1/0 days before expiry |
| 💬 **AI Assistant** | Chat in natural language: "Show my expiring documents" |
| 📁 **Versioning** | Replace documents while keeping full version history |
| 🛡️ **Admin Panel** | User management, storage stats, AI/audit logs |
| 🔐 **Encryption** | AES-256 encrypted document numbers, masked in UI |

---

## 🚀 Quick Start

### Prerequisites
- .NET 8 SDK
- SQL Server LocalDB (comes with Visual Studio) or SQL Server
- (Optional) Gemini API key for AI analysis
- (Optional) Gmail / SMTP credentials for email reminders

### 1. Clone & Configure

```bash
git clone https://github.com/yourusername/AIDocumentOrganizer.git
cd "AI Document Organiser"
```

### 2. Configure `appsettings.json`

Open `AIDocumentOrganizer.Web/appsettings.json` and set:

```json
{
  "AiService": {
    "GeminiApiKey": "YOUR_GEMINI_API_KEY_HERE"
  },
  "EmailSettings": {
    "SmtpHost": "smtp.gmail.com",
    "SenderEmail": "your@gmail.com",
    "SenderPassword": "your_app_password"
  }
}
```

> **Never commit real secrets!** Use `dotnet user-secrets` or environment variables in production:
> ```bash
> dotnet user-secrets set "AiService:GeminiApiKey" "your-key"
> dotnet user-secrets set "EmailSettings:SenderPassword" "your-password"
> ```

### 3. Run

```bash
cd AIDocumentOrganizer.Web
dotnet run
```

The app will:
- Automatically apply EF Core migrations
- Seed the database with categories, admin and demo users
- Open at `https://localhost:5001`

---

## 🔑 Development Credentials

| Role | Email | Password |
|---|---|---|
| **Admin** | admin@docorganizer.com | Admin@123! |
| **User** | user@docorganizer.com | User@123! |

> Change these immediately in production!

---

## 🏗️ Project Structure

```
AI Document Organiser/
├── AIDocumentOrganizer.sln
├── AIDocumentOrganizer.Web/
│   ├── Controllers/         # Thin controllers
│   ├── Data/                # DbContext, DbInitializer
│   ├── Models/
│   │   ├── Entities/        # All EF Core entities
│   │   └── Enums/           # Status enums
│   ├── ViewModels/          # Strongly typed view models
│   ├── Services/
│   │   ├── Interfaces/      # IDocumentService, IOcrService, IAiDocumentService...
│   │   └── Implementations/ # DocumentService, OcrService, GeminiAiDocumentService...
│   ├── Views/               # All Razor views
│   ├── wwwroot/             # CSS design system + JavaScript
│   └── App_Data/SecureUploads/  # Encrypted file storage (auto-created)
└── AIDocumentOrganizer.Tests/   # xUnit tests
```

---

## 🗄️ Database Schema

| Table | Purpose |
|---|---|
| `AspNetUsers` | Identity users with profile & reminder prefs |
| `Documents` | Core document metadata + encrypted number |
| `DocumentCategories` | 10 pre-seeded categories |
| `DocumentVersions` | Version history per document |
| `DocumentTexts` | OCR extracted text per version |
| `DocumentReminders` | Per-document reminder tracking |
| `Notifications` | In-app user notifications |
| `AuditLogs` | Security audit trail |
| `AIProcessingLogs` | AI model usage tracking |

---

## 🤖 AI Integration

The app uses **Google Gemini 1.5 Flash** for:
1. **Document Classification** — Category + Type
2. **Data Extraction** — Number, holder, dates, authority, vehicle/policy number
3. **AI Assistant** — Natural language queries against your documents

### Fallback Strategy
- If no API key → Local NLP heuristics (regex patterns)
- If API fails → Exponential retry (up to 3 attempts) → Local heuristics
- Failed processing → Document retained, manual entry + Retry button

---

## 📧 Email Reminders

Reminders sent at: **30 days → 15 days → 7 days → 1 day → Expiry day**

Configure SMTP in appsettings. Works with Gmail App Passwords, Outlook, SendGrid, etc.

---

## 🧪 Running Tests

```bash
dotnet test AIDocumentOrganizer.Tests
```

Tests cover:
- Document ownership security
- Expiry status calculation
- File extension + magic byte validation
- AI JSON parsing + heuristic fallback

---

## 🚀 Deployment (Azure / IIS)

### Environment Variables (Production)
```
ASPNETCORE_ENVIRONMENT=Production
GEMINI_API_KEY=your-key
EMAIL_PASSWORD=your-smtp-password
ConnectionStrings__DefaultConnection=Server=...;Database=AIDocOrg;...
AppSettings__EncryptionSecret=a-strong-random-secret-64-chars-min
```

### Azure App Service

1. Deploy via Visual Studio Publish or GitHub Actions
2. Set connection string to Azure SQL
3. Set environment variables in App Service Configuration
4. Run `dotnet ef database update` or let auto-migration handle it

### IIS

1. Publish: `dotnet publish -c Release -o publish/`
2. Create IIS site pointing to `publish/`
3. Set App Pool to "No Managed Code"
4. Ensure `ASPNETCORE_MODULE` is installed

---

## 🔒 Security Notes

- Files stored in `App_Data/SecureUploads/{userId}/` — not publicly accessible
- Document numbers encrypted with AES-256, only masked versions shown in UI
- Every document operation checks user ownership
- Anti-forgery tokens on all POST forms
- Account lockout after 5 failed login attempts
- Audit logs for all significant actions

---

## 🛣️ Future Improvements

- [ ] Two-Factor Authentication (2FA)
- [ ] Document sharing with access controls
- [ ] Mobile app (MAUI/Flutter) with camera scan
- [ ] PDF password protection for downloads
- [ ] Bulk document upload
- [ ] Document templates (pre-fill common fields)
- [ ] Multi-language OCR (Hindi, Tamil, etc.)
- [ ] WhatsApp/Telegram reminder bot
- [ ] Azure Blob Storage integration
- [ ] OpenID Connect / Social login

---

## 📝 License

MIT — free to use, modify, and distribute.
