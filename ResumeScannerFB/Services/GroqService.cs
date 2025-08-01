using RestSharp;
using System.Text.Json;

namespace ResumeScannerFB.Services
{

    public class GroqService
    {
        private readonly IConfiguration _config;
        private readonly RestClient _client;

        public GroqService(IConfiguration config)
        {
            _config = config;
            var endpoint = _config["GroqAI:Endpoint"];
            _client = new RestClient(endpoint);
        }

        public async Task<string> GetResumeAuditAsync(string resumeText)
        {
            var apiKey = _config["GroqAI:ApiKey"];
            var model = _config["GroqAI:Model"];

            var request = new RestRequest();
            request.AddHeader("Authorization", $"Bearer {apiKey}");
            request.AddHeader("Content-Type", "application/json");

            var body = new
            {
                model = model,
                messages = new[]
                {
                new { role = "system", content = "You are a professional resume auditor. You will give audit in 3 sections: ✅ Strengths, ⚠️ Weaknesses, 🛠️ Recommendations in new line and add the proper emogies and icons for each bullet points. Add Hello {Name of Resume} and Give the Audit Score out of 100 with proper emogies" },
                new { role = "user", content = $"Please analyze this resume:\n\n{resumeText}" }
            },
                temperature = 0.3
            };

            request.AddJsonBody(body);

            var response = await _client.PostAsync(request);

            if (!response.IsSuccessful)
            {
                throw new Exception($"Groq API Error: {response.Content}");
            }

            var json = JsonDocument.Parse(response.Content);
            var content = json.RootElement
                              .GetProperty("choices")[0]
                              .GetProperty("message")
                              .GetProperty("content")
                              .GetString();

            return content;
        }
    }
}
