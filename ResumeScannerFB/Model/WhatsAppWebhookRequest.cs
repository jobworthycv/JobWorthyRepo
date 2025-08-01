namespace ResumeScannerFB.Model
{
    public class WhatsAppWebhookRequest
    {
        public List<Entry> entry { get; set; }

        public class Entry
        {
            public List<Change> changes { get; set; }
        }

        public class Change
        {
            public Value value { get; set; }
        }

        public class Value
        {
            public List<Message> messages { get; set; }
        }

        public class Message
        {
            public string from { get; set; }
            public string type { get; set; }
            public Document document { get; set; }
        }

        public class Document
        {
            public string id { get; set; }
            public string filename { get; set; }
        }
        public class ResumeTestRequest
        {
            public string Phone { get; set; }
            public string MediaId { get; set; }
            public string FileName { get; set; }
        }
        public class PdfMetadata
        {
            public List<string> Fonts { get; set; }
            public List<double> FontSizes { get; set; }
            public bool HasTables { get; set; }
            public bool HasColumns { get; set; }
            public int PageCount { get; set; }
        }

        public class DocxMetadata
        {
            public List<string> Fonts { get; set; }
            public List<double> FontSizes { get; set; }
            public bool HasTables { get; set; }
            public bool HasColumns { get; set; }
            public int PageCountEstimate { get; set; }
        }
        public class ResumeScanRecord
        {
            public string Name { get; set; }
            public string Mobile { get; set; }
            public string Date { get; set; }
            public int Count { get; set; }
        }
    }
}
