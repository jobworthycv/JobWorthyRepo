using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using DocumentFormat.OpenXml.Wordprocessing;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using ResumeScannerFB.Services;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text;
using UglyToad.PdfPig;
using static ResumeScannerFB.Model.WhatsAppWebhookRequest;

namespace ResumeScannerFB.Controllers
{
    [ApiController]
    [Route("api/whatsapp/webhook")]
    public class WhatsAppWebhookController : Controller
    {
        private readonly IConfiguration _config;

        private readonly GroqService _groqService;
        private readonly GoogleSheetService _googleSheetService;
        public WhatsAppWebhookController(IConfiguration config, GroqService groqService, GoogleSheetService googleSheetService)
        {
            _config = config;
            _groqService = groqService;
            _googleSheetService = googleSheetService;
        }
        [HttpPost("simulate")]
        public async Task<IActionResult> Simulate()
        {
            try
            {
                // Step 1: Load the file (PDF or DOCX) from the root directory
                string fileName = "Aniket_Muruskar_Resume.pdf"; // Change to .docx if needed
                string rootPath = Directory.GetCurrentDirectory();
                string filePath = Path.Combine(rootPath, fileName);

                string name = "User"; // You can later extract from resume or metadata
                string mobile = "8149938417";
                var (exists, count) = await _googleSheetService.CheckAndUpdateUserAsync(name, mobile);

                if (count > 1)
                {
                    await SendWhatsAppMessage(mobile, "⚠️ You have crossed the free resume scan limit. Please upgrade to continue.");
                    return Ok();
                }

                if (!System.IO.File.Exists(filePath))
                    return NotFound("Resume file not found at project root.");

                byte[] resumeBytes = await System.IO.File.ReadAllBytesAsync(filePath);

                // Step 2: Create a mock IFormFile
                string extension = Path.GetExtension(filePath).ToLower();
                var stream = new MemoryStream(resumeBytes);
                var formFile = new FormFile(stream, 0, resumeBytes.Length, "resume", $"resume{extension}");

                // Step 3: Extract text from resume
                string resumeText = extension switch
                {
                    ".pdf" => ExtractTextFromPdf(formFile, out PdfMetadata _),
                    ".docx" => ExtractTextFromDocx(formFile, out DocxMetadata _),
                    _ => throw new InvalidOperationException("Unsupported file type.")
                };

                // Step 4: Score or audit the resume
                string auditResult = await _groqService.GetResumeAuditAsync(resumeText);

                // Step 5: Insert the data into Google Sheet (name, mobile, date, count)
                await _googleSheetService.InsertRowAsync(new ResumeScanRecord
                {
                    Name = name,
                    Mobile = mobile,
                    Date = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"),
                    Count = count+1
                });

                return Ok(new
                {
                    message = "✅ Resume simulated and parsed successfully.",
                    result = auditResult
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"❌ Simulation failed: {ex.Message}");
            }
        }


        // ✅ Step 1: Webhook verification
        [HttpGet]
        public IActionResult Verify([FromQuery] string hub_mode, [FromQuery] string hub_challenge, [FromQuery] string hub_verify_token)
        {
            var verifyToken = _config["Meta:VerifyToken"];
            if (hub_mode == "subscribe" && hub_verify_token == verifyToken)
                return Ok(hub_challenge);
            return Unauthorized();
        }

        // ✅ Step 2: Handle incoming message
        [HttpPost]
        public async Task<IActionResult> ReceiveMessage([FromBody] dynamic body)
        {
            var message = body.entry[0].changes[0].value.messages[0];
            string from = message.from;
            string type = message.type;

            if (type == "document")
            {
                string mediaId = message.document.id;
                string fileName = message.document.filename;

                string fileUrl = await GetMediaUrl(mediaId);
                byte[] fileBytes = await DownloadFile(fileUrl);

                // Step 3: Create a MemoryStream from the byte array
                var stream = new MemoryStream(fileBytes);

                // Step 4: Create an IFormFile object from the stream
                var formFile = new FormFile(stream, 0, fileBytes.Length, "resume", fileName)
                {
                    Headers = new HeaderDictionary(),
                    ContentType = "application/octet-stream"
                };

                // Step 5: Extract text based on extension
                string resumeText;
                string extension = Path.GetExtension(fileName).ToLower();

                if (extension == ".pdf")
                {
                    resumeText = ExtractTextFromPdf(formFile, out var _);
                }
                else if (extension == ".docx")
                {
                    resumeText = ExtractTextFromDocx(formFile, out var _);
                }
                else
                {
                    throw new InvalidOperationException("Unsupported file type.");
                }

                // Step 6: Send to audit/scoring logic
                string auditResult = await _groqService.GetResumeAuditAsync(resumeText);

                await SendWhatsAppMessage(from, auditResult);
            }

            return Ok();
        }

        private async Task<string> GetMediaUrl(string mediaId)
        {
            var token = _config["Meta:AccessToken"];
            var client = new HttpClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            var res = await client.GetStringAsync($"https://graph.facebook.com/v19.0/{mediaId}");
            dynamic json = JsonConvert.DeserializeObject(res);
            return json.url;
        }

        private async Task<byte[]> DownloadFile(string url)
        {
            var token = _config["Meta:AccessToken"];
            var client = new HttpClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            return await client.GetByteArrayAsync(url);
        }

        private string ExtractTextFromPdf(IFormFile file, out PdfMetadata pdfMetadata)
        {
            using var stream = file.OpenReadStream();
            using var memoryStream = new MemoryStream();
            stream.CopyTo(memoryStream);
            memoryStream.Position = 0;

            var textBuilder = new StringBuilder();
            var fonts = new HashSet<string>();
            var fontSizes = new HashSet<double>();
            bool hasTables = false;
            bool hasColumns = false;

            using var document = PdfDocument.Open(memoryStream);
            foreach (var page in document.GetPages())
            {
                textBuilder.AppendLine(page.Text);

                // Analyze fonts and layout
                foreach (var word in page.GetWords())
                {
                    var font = word.FontName ?? "Unknown";
                    fonts.Add(font);
                    var fontSize = word.Letters.FirstOrDefault()?.FontSize ?? 0;
                    fontSizes.Add(fontSize);
                }

                // Detect tables (heuristic: check for grid-like text alignment)
                var wordsPerLine = page.GetWords().GroupBy(w => Math.Round(w.BoundingBox.Bottom, 1))
                    .Select(g => g.Count());
                if (wordsPerLine.Any(count => count > 10))
                    hasTables = true;

                // Detect columns (heuristic: check for horizontal spacing)
                var xPositions = page.GetWords().Select(w => w.BoundingBox.Left).Distinct();
                if (xPositions.Count() > 2 && xPositions.Max() - xPositions.Min() > 200)
                    hasColumns = true;
            }

            pdfMetadata = new PdfMetadata
            {
                Fonts = fonts.ToList(),
                FontSizes = fontSizes.ToList(),
                HasTables = hasTables,
                HasColumns = hasColumns,
                PageCount = document.NumberOfPages
            };

            return textBuilder.ToString();
        }

        private string ExtractTextFromDocx(IFormFile file, out DocxMetadata docxMetadata)
        {
            using var stream = file.OpenReadStream();
            using var memStream = new MemoryStream();
            stream.CopyTo(memStream);
            memStream.Position = 0;

            var fonts = new HashSet<string>();
            var fontSizes = new HashSet<double>();
            bool hasTables = false;
            bool hasColumns = false;
            int pageCountEstimate = 0;

            using var wordDoc = WordprocessingDocument.Open(memStream, false);
            var body = wordDoc.MainDocumentPart?.Document?.Body;
            var text = body?.InnerText ?? "";

            // Estimate page count (rough: 500 words per page)
            pageCountEstimate = (int)Math.Ceiling(text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length / 500.0);

            // Check for tables
            hasTables = body?.Descendants<DocumentFormat.OpenXml.Drawing.Table>().Any() ?? false;

            // Check for columns
            var sectionProps = body?.Descendants<SectionProperties>().FirstOrDefault();
            hasColumns = sectionProps?.Descendants<DocumentFormat.OpenXml.Wordprocessing.Columns>().Any(c => c.ColumnCount?.Value > 1) ?? false;

            // Extract font info from styles
            var stylesPart = wordDoc.MainDocumentPart?.StyleDefinitionsPart?.Styles;
            if (stylesPart != null)
            {
                foreach (var style in stylesPart.Descendants<Style>())
                {
                    var font = style.Descendants<RunFonts>().FirstOrDefault()?.Ascii?.Value;
                    var size = style.Descendants<DocumentFormat.OpenXml.Wordprocessing.FontSize>().FirstOrDefault()?.Val?.Value;
                    if (font != null) fonts.Add(font);
                    if (size != null && double.TryParse(size, out var fontSize)) fontSizes.Add(fontSize / 2.0); // Convert to points
                }
            }

            docxMetadata = new DocxMetadata
            {
                Fonts = fonts.ToList(),
                FontSizes = fontSizes.ToList(),
                HasTables = hasTables,
                HasColumns = hasColumns,
                PageCountEstimate = pageCountEstimate
            };

            return text;
        }

        private async Task SendWhatsAppMessage(string to, string message)
        {
            var token = _config["Meta:AccessToken"];
            var phoneNumberId = _config["Meta:PhoneNumberId"];
            var payload = new
            {
                messaging_product = "whatsapp",
                to = to,
                type = "text",
                text = new { body = message }
            };

            var client = new HttpClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var content = new StringContent(JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json");
            await client.PostAsync($"https://graph.facebook.com/v19.0/{phoneNumberId}/messages", content);
        }
    }
}
