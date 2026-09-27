namespace Workflow.Application.Integrations;

using System.Text.Json;
using System.Text.RegularExpressions;

public static class IntegrationConfigurationRules
{
    public static IReadOnlyList<string> Validate(string kind, string json)
    {
        var errors = new List<string>();
        void Check(bool valid, string message) { if (!valid) errors.Add(message); }
        void Attempts(int attempts, int delay) { Check(attempts is >= 1 and <= 10, "Maximum attempts must be between 1 and 10."); Check(delay is >= 1 and <= 3600, "Retry delay must be between 1 and 3600 seconds."); }
        void Mappings(Dictionary<string, string>? mappings)
        {
            Check(mappings is not null && mappings.All(p => Regex.IsMatch(p.Key, @"^[A-Za-z_][A-Za-z0-9_]*$") && !string.IsNullOrWhiteSpace(p.Value)), "Each response mapping needs a variable key and a response path.");
        }
        try
        {
            if (kind == "Http")
            {
                var c = IntegrationJson.Read<HttpActivityConfiguration>(json);
                Check(c.Protocol is "Rest" or "Soap" or "Sms", "Select REST, SOAP or SMS.");
                if (c.Protocol == "Soap")
                {
                    Check(c.SoapVersion is "1.1" or "1.2", "Select SOAP 1.1 or SOAP 1.2.");
                    Check(c.Method == "POST", "SOAP requests use POST.");
                    Check(!c.SoapAction.Contains('\r') && !c.SoapAction.Contains('\n'), "SOAP action must not contain line breaks.");
                    var xml = SoapMessage.Parse(c.Body ?? "");
                    Check(xml.Root?.Name.LocalName == "Envelope" && xml.Root.Name.NamespaceName == (c.SoapVersion == "1.2" ? "http://www.w3.org/2003/05/soap-envelope" : "http://schemas.xmlsoap.org/soap/envelope/"), "Provide a SOAP envelope matching the selected version.");
                    Mappings(c.XmlOutputMappings);
                    foreach (var path in c.XmlOutputMappings.Values) System.Xml.XPath.XPathExpression.Compile(path);
                }
                if (c.Protocol == "Sms") Check(!string.IsNullOrWhiteSpace(c.SmsTo) && !string.IsNullOrWhiteSpace(c.SmsMessage), "SMS requires a recipient and message.");
                Check(c.ConnectionId != Guid.Empty, "Select an HTTP connection.");
                Check(new[] { "GET", "POST", "PUT", "PATCH", "DELETE", "HEAD", "OPTIONS" }.Contains(c.Method), "Select a supported HTTP method.");
                Check(!string.IsNullOrWhiteSpace(c.Path), "Enter an endpoint path.");
                Check(c.TimeoutSeconds is >= 1 and <= 120, "Request timeout must be between 1 and 120 seconds.");
                Attempts(c.MaxAttempts, c.RetryDelaySeconds); Mappings(c.OutputMappings);
                Check(c.SuccessStatusCodes is not null && c.SuccessStatusCodes.All(s => s is >= 200 and <= 599), "Success status codes must be between 200 and 599.");
                Check(c.Headers is not null && c.Query is not null, "Headers and query parameters must be objects.");
                Check(c.Headers is null || c.Headers.All(h => !string.IsNullOrWhiteSpace(h.Key) && h.Value is not null && !h.Key.Contains('\r') && !h.Key.Contains('\n') && !h.Value.Contains('\r') && !h.Value.Contains('\n')), "Headers must not contain line breaks.");
                Check(!string.IsNullOrWhiteSpace(c.ErrorOutcome) && !string.IsNullOrWhiteSpace(c.TimeoutOutcome), "Error and timeout outcomes are required.");
                Check(c.MaxAttempts <= 1 || c.Method is "GET" or "HEAD" or "OPTIONS" || !string.IsNullOrWhiteSpace(c.IdempotencyHeader), "Retrying a request that changes data requires an idempotency header.");
                if (c.Protocol != "Soap" && c.ContentType == "application/json" && !string.IsNullOrWhiteSpace(c.Body)) { using var document = JsonDocument.Parse(c.Body); }
            }
            else if (kind == "Webhook")
            {
                var c = IntegrationJson.Read<EventActivityConfiguration>(json);
                Check(c.ConnectionId != Guid.Empty, "Select a webhook connection.");
                Check(!string.IsNullOrWhiteSpace(c.EventKey) && c.EventKey.Length <= 200, "Event key must contain 1 to 200 characters.");
                Check(!string.IsNullOrWhiteSpace(c.CorrelationVariable), "Select a correlation variable.");
                Check(c.TimeoutSeconds is >= 1 and <= 31536000, "Event timeout must be between one second and one year.");
                Check(!string.IsNullOrWhiteSpace(c.TimeoutOutcome), "A timeout outcome is required."); Mappings(c.OutputMappings);
            }
            else
            {
                var c = IntegrationJson.Read<EmailActivityConfiguration>(json);
                Check(c.Channels is "InApp" or "Email" or "Email, InApp" or "InApp, Email", "Select valid notification channels.");
                Check(c.FailurePolicy is "Continue" or "FailWorkflow" or "Retry", "Select a valid failure policy.");
                Check(c.RecipientUserIds is not null, "Recipient user IDs must be an array.");
                if (c.Channels?.Contains("Email") == true)
                {
                    Check(c.ConnectionId != Guid.Empty, "Select an SMTP connection.");
                    Check(!string.IsNullOrWhiteSpace(c.To) || !string.IsNullOrWhiteSpace(c.Cc) || !string.IsNullOrWhiteSpace(c.Bcc) || c.RecipientUserIds?.Length > 0, "Select at least one email recipient.");
                    Check(!string.IsNullOrWhiteSpace(c.Subject), "An email subject is required."); Attempts(c.MaxAttempts, c.RetryDelaySeconds);
                }
                else Check(c.FailurePolicy != "Retry", "Retry requires the Email channel; in-app notifications are stored immediately.");
            }
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or System.Xml.XmlException or System.Xml.XPath.XPathException) { errors.Add("Integration configuration contains an invalid value, request body or XML mapping."); }
        return errors;
    }
}
