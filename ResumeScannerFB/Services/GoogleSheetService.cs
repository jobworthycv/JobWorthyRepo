using Google.Apis.Auth.OAuth2;
using Google.Apis.Services;
using Google.Apis.Sheets.v4;
using Google.Apis.Sheets.v4.Data;
using System.Text;
using static ResumeScannerFB.Model.WhatsAppWebhookRequest;

public class GoogleSheetService
{
    private readonly SheetsService _service;
    private readonly string _spreadsheetId = "1ZWba9g1CEXzDFRfiRL8Pn8CR4nQ7OVQvXJ_DYUwflck"; // from sheet URL

    public GoogleSheetService(IWebHostEnvironment env)
    {
        var credentialPath = Path.Combine(env.ContentRootPath, "credentials.json");
        GoogleCredential credential;

        using (var stream = new FileStream(credentialPath, FileMode.Open, FileAccess.Read))
        {
            credential = GoogleCredential.FromStream(stream)
                .CreateScoped(SheetsService.Scope.Spreadsheets);
        }

        _service = new SheetsService(new BaseClientService.Initializer()
        {
            HttpClientInitializer = credential,
            ApplicationName = "Resume Scanner",
        });
    }
    public async Task InsertRowAsync(ResumeScanRecord record)
    {
        var range = "Sheet1!A:D"; // Adjust as needed
        var valueRange = new ValueRange
        {
            Values = new List<IList<object>>
        {
            new List<object> { record.Name, record.Mobile, record.Date, record.Count }
        }
        };

        var appendRequest = _service.Spreadsheets.Values.Append(valueRange, _spreadsheetId, range);
        appendRequest.ValueInputOption = SpreadsheetsResource.ValuesResource.AppendRequest.ValueInputOptionEnum.USERENTERED;

        await appendRequest.ExecuteAsync();
    }
    public async Task<(bool Exists, int Count)> CheckAndUpdateUserAsync(string name, string mobile)
    {
        var range = "Sheet1!A2:D"; // assuming header is in row 1
        var request = _service.Spreadsheets.Values.Get(_spreadsheetId, range);
        var response = await request.ExecuteAsync();
        var values = response.Values;

        if (values == null || values.Count == 0)
            return (false, 0);

        int rowIndex = 2;
        foreach (var row in values)
        {
            if (row.Count >= 2 && row[1].ToString() == mobile)
            {
                int count = row.Count >= 4 ? int.Parse(row[3].ToString()) : 0;

                if (count >= 1)
                    return (true, count);

                // Update count
                count += 1;
                var updateRequest = new ValueRange
                {
                    Values = new List<IList<object>> { new List<object> { row[0], row[1], DateTime.Now.ToString("yyyy-MM-dd"), count } }
                };

                var update = _service.Spreadsheets.Values.Update(updateRequest, _spreadsheetId, $"A{rowIndex}:D{rowIndex}");
                update.ValueInputOption = SpreadsheetsResource.ValuesResource.UpdateRequest.ValueInputOptionEnum.RAW;
                await update.ExecuteAsync();

                return (true, count);
            }

            rowIndex++;
        }

        // New entry
        var appendRequest = new ValueRange
        {
            Values = new List<IList<object>> {
                new List<object> { name, mobile, DateTime.Now.ToString("yyyy-MM-dd"), 1 }
            }
        };

        var append = _service.Spreadsheets.Values.Append(appendRequest, _spreadsheetId, "Sheet1!A:D");
        append.ValueInputOption = SpreadsheetsResource.ValuesResource.AppendRequest.ValueInputOptionEnum.RAW;
        await append.ExecuteAsync();

        return (false, 1);
    }
}
