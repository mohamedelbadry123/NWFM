namespace Workflow.Infrastructure.Services;

using System.Net;
using System.Net.Http.Headers;
using System.Net.Mail;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Workflow.Application.Integrations;
using Workflow.Domain.Entities;

internal sealed class WorkflowIntegrationTransport
{
    public const int MaxResponseBytes = 262144;
    public async Task<IntegrationResult> SendHttpAsync(WorkflowIntegrationConnection connection,
        Dictionary<string, string> credentials, HttpActivityConfiguration config, Dictionary<string, JsonElement> variables,
        string operationKey, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(config.TimeoutSeconds, 1, 120)));
        try
        {
            if (config.Protocol == "Sms")
            {
                variables = new(variables);
                variables["smsTo"] = JsonSerializer.SerializeToElement(IntegrationValueMapper.Render(config.SmsTo, variables));
                variables["smsMessage"] = JsonSerializer.SerializeToElement(IntegrationValueMapper.Render(config.SmsMessage, variables));
            }
            var baseUri = new Uri(connection.Address, UriKind.Absolute);
            var uri = new Uri(baseUri, IntegrationValueMapper.Render(config.Path, variables));
            if (uri.Scheme != baseUri.Scheme || uri.Host != baseUri.Host || uri.Port != baseUri.Port || !string.IsNullOrEmpty(uri.UserInfo))
                return new(false, null, "", "Request URL must remain on the configured connection origin.");
            if (uri.Scheme != "https" && !(connection.AllowPrivateNetwork && uri.Scheme == "http"))
                return new(false, null, "", "HTTPS is required unless this connection explicitly allows a private test network.");
            var query = string.Join("&", config.Query.Select(p => Uri.EscapeDataString(p.Key) + "=" + Uri.EscapeDataString(IntegrationValueMapper.Render(p.Value, variables))));
            if (query.Length > 0) uri = new UriBuilder(uri) { Query = uri.Query.TrimStart('?') + (uri.Query.Length > 0 ? "&" : "") + query }.Uri;
            using var handler = CreateHandler(connection.AllowPrivateNetwork);
            using var client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
            using var request = new HttpRequestMessage(new HttpMethod(config.Method.ToUpperInvariant()), uri);
            foreach (var (name, value) in config.Headers)
            {
                if (new[] { "Authorization", "Proxy-Authorization", "Cookie", "Host", "Content-Length" }.Contains(name, StringComparer.OrdinalIgnoreCase))
                    return new(false, null, "", $"Header '{name}' must be configured through the connection or request body settings.");
                request.Headers.Add(name, IntegrationValueMapper.Render(value, variables));
            }
            if (!string.IsNullOrWhiteSpace(config.IdempotencyHeader)) request.Headers.Add(config.IdempotencyHeader, operationKey);
            if (config.Body is not null)
            {
                var body = config.Protocol == "Soap" ? SoapMessage.Render(config.Body, variables) : config.ContentType.Contains("json", StringComparison.OrdinalIgnoreCase)
                    ? IntegrationValueMapper.RenderJson(config.Body, variables) : IntegrationValueMapper.Render(config.Body, variables);
                if (Encoding.UTF8.GetByteCount(body) > MaxResponseBytes) return new(false, null, "", "Request body exceeds 256 KB.");
                request.Content = new StringContent(body, Encoding.UTF8, config.Protocol == "Soap" ? config.SoapVersion == "1.2" ? "application/soap+xml" : "text/xml" : config.ContentType);
                if (config.Protocol == "Soap")
                {
                    if (config.SoapVersion == "1.2") request.Content.Headers.ContentType!.Parameters.Add(new NameValueHeaderValue("action", "\"" + config.SoapAction.Replace("\"", "") + "\""));
                    else request.Headers.Add("SOAPAction", "\"" + config.SoapAction.Replace("\"", "") + "\"");
                }
            }
            string Credential(string key) => credentials.GetValueOrDefault(key) ?? throw new InvalidOperationException($"Connection credential '{key}' is missing.");
            switch (connection.Authentication)
            {
                case "Basic": request.Headers.Authorization = new("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes(Credential("username") + ":" + Credential("password")))); break;
                case "Bearer": request.Headers.Authorization = new("Bearer", Credential("token")); break;
                case "ApiKey":
                    var keyName = credentials.GetValueOrDefault("header") ?? "X-Api-Key";
                    if (credentials.GetValueOrDefault("location") == "query")
                        request.RequestUri = new UriBuilder(uri) { Query = uri.Query.TrimStart('?') + (uri.Query.Length > 0 ? "&" : "") + Uri.EscapeDataString(keyName) + "=" + Uri.EscapeDataString(Credential("apiKey")) }.Uri;
                    else request.Headers.Add(keyName, Credential("apiKey"));
                    break;
                case "OAuth2":
                    var tokenUri = new Uri(Credential("tokenUrl"));
                    if (tokenUri.Scheme != "https") return new(false, null, "", "OAuth token endpoints require HTTPS.");
                    using (var tokenRequest = new HttpRequestMessage(HttpMethod.Post, tokenUri)
                    { Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["grant_type"] = "client_credentials", ["client_id"] = Credential("clientId"), ["client_secret"] = Credential("clientSecret"), ["scope"] = credentials.GetValueOrDefault("scope") ?? "" }) })
                    using (var tokenResponse = await client.SendAsync(tokenRequest, HttpCompletionOption.ResponseHeadersRead, timeout.Token))
                    {
                        if (!tokenResponse.IsSuccessStatusCode) return new(false, (int)tokenResponse.StatusCode, "", "OAuth token acquisition failed.", (int)tokenResponse.StatusCode is 408 or 429 or >= 500);
                        using var tokenJson = JsonDocument.Parse(await ReadBoundedAsync(tokenResponse.Content, timeout.Token));
                        request.Headers.Authorization = new("Bearer", tokenJson.RootElement.GetProperty("access_token").GetString());
                    }
                    break;
            }
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            var responseBody = await ReadBoundedAsync(response.Content, timeout.Token);
            var secrets = credentials.Where(p => p.Key is "token" or "password" or "apiKey" or "clientSecret").Select(p => p.Value)
                .Append(request.Headers.Authorization?.Parameter ?? "").Where(v => v.Length > 0).ToArray();
            string Redact(string text) { foreach (var secret in secrets) text = text.Replace(secret, "[redacted]", StringComparison.Ordinal); return text; }
            responseBody = Redact(responseBody);
            var responseHeaders = response.Headers.Concat(response.Content.Headers)
                .Where(h => !new[] { "Set-Cookie", "Authorization", "Proxy-Authorization" }.Contains(h.Key, StringComparer.OrdinalIgnoreCase))
                .ToDictionary(h => h.Key.ToLowerInvariant(), h => Redact(string.Join(",", h.Value)));
            var status = (int)response.StatusCode;
            var success = config.SuccessStatusCodes.Length > 0 ? config.SuccessStatusCodes.Contains(status) : response.IsSuccessStatusCode;
            // Request diagnostics deliberately omit headers, body values and query values.
            var summary = JsonSerializer.Serialize(new { method = request.Method.Method, host = uri.Host, protocol = config.Protocol, contentType = request.Content?.Headers.ContentType?.MediaType, requestBytes = request.Content?.Headers.ContentLength, operationId = operationKey });
            if (config.Protocol == "Soap" && SoapMessage.HasFault(responseBody)) return new(false, status, responseBody, "SOAP service returned a Fault.", false, Headers: responseHeaders, RequestSummary: summary);
            return new(success, status, responseBody, success ? null : $"HTTP request returned status {status}.", status is 408 or 429 || status >= 500, Headers: responseHeaders, RequestSummary: summary);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return new(false, null, "", "HTTP request timed out.", true, true); }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) when (ex is HttpRequestException or IOException or SocketException) { return new(false, null, "", "The service could not be reached or its response exceeded the permitted size.", true); }
        catch (Exception ex) when (ex is FormatException or InvalidOperationException or JsonException or ArgumentException or KeyNotFoundException or System.Xml.XmlException)
        { return new(false, null, "", "Request configuration or response format is invalid. Check the connection and mappings."); }
    }

    internal static SocketsHttpHandler CreateHandler(bool allowPrivate) => new()
    {
        AllowAutoRedirect = false, UseProxy = false, AutomaticDecompression = DecompressionMethods.None,
        ConnectCallback = async (context, ct) =>
        {
            var addresses = await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, ct);
            if (addresses.Length == 0 || addresses.Any(a => !IsAllowedAddress(a, allowPrivate)))
                throw new HttpRequestException("Destination network is not allowed.");
            var socket = new Socket(SocketType.Stream, ProtocolType.Tcp);
            try { await socket.ConnectAsync(addresses, context.DnsEndPoint.Port, ct); return new NetworkStream(socket, ownsSocket: true); }
            catch { socket.Dispose(); throw; }
        }
    };
    internal static bool IsAllowedAddress(IPAddress address, bool allowPrivate)
    {
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        var bytes = address.GetAddressBytes();
        if (address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any) || address.IsIPv6Multicast || address.IsIPv6LinkLocal) return false;
        if (bytes.Length == 4 && (bytes[0] == 169 && bytes[1] == 254 || bytes[0] >= 224 || bytes[0] == 0)) return false;
        if (allowPrivate) return true;
        if (IPAddress.IsLoopback(address) || address.IsIPv6SiteLocal) return false;
        return bytes.Length == 4
            ? !(bytes[0] == 10 || bytes[0] == 172 && bytes[1] is >= 16 and <= 31 || bytes[0] == 192 && bytes[1] == 168 || bytes[0] == 100 && bytes[1] is >= 64 and <= 127)
            : (bytes[0] & 0xfe) != 0xfc;
    }
    private static async Task<string> ReadBoundedAsync(HttpContent content, CancellationToken ct)
    {
        await using var stream = await content.ReadAsStreamAsync(ct);
        using var buffer = new MemoryStream(); var bytes = new byte[8192];
        int count;
        while ((count = await stream.ReadAsync(bytes, ct)) > 0)
        { if (buffer.Length + count > MaxResponseBytes) throw new IOException("Response too large."); await buffer.WriteAsync(bytes.AsMemory(0, count), ct); }
        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    public async Task<IntegrationResult> SendEmailAsync(WorkflowIntegrationConnection connection, Dictionary<string, string> credentials,
        EmailActivityConfiguration config, Dictionary<string, JsonElement> variables, string operationKey, CancellationToken ct)
    {
        try
        {
            using var client = new SmtpClient(connection.Address, connection.Port) { EnableSsl = connection.UseTls, UseDefaultCredentials = false, Timeout = 60000 };
            if (connection.Authentication == "Basic") client.Credentials = new NetworkCredential(credentials.GetValueOrDefault("username"), credentials.GetValueOrDefault("password"));
            using var message = new MailMessage { From = new MailAddress(credentials.GetValueOrDefault("fromAddress") ?? ""),
                Subject = IntegrationValueMapper.Render(config.Subject, variables), Body = IntegrationValueMapper.Render(config.Body, variables), IsBodyHtml = config.IsHtml };
            foreach (var address in SplitAddresses(IntegrationValueMapper.Render(config.To, variables))) message.To.Add(address);
            foreach (var address in SplitAddresses(IntegrationValueMapper.Render(config.Cc, variables))) message.CC.Add(address);
            foreach (var address in SplitAddresses(IntegrationValueMapper.Render(config.Bcc, variables))) message.Bcc.Add(address);
            if (message.To.Count + message.CC.Count + message.Bcc.Count == 0) return new(false, null, "", "At least one email recipient is required.");
            message.Headers.Add("X-Workflow-Operation", operationKey);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(TimeSpan.FromSeconds(60));
            await client.SendMailAsync(message, timeout.Token);
            return new(true, null, "", null);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return new(false, null, "", "Email send timed out; the delivery outcome may be uncertain.", true, true); }
        catch (OperationCanceledException) { throw; }
        catch (SmtpException) { return new(false, null, "", "SMTP delivery failed. Check mail server availability and credentials.", true); }
        catch (Exception ex) when (ex is ArgumentException or FormatException or InvalidOperationException) { return new(false, null, "", "Email connection, recipient or template configuration is invalid."); }
    }
    internal static IEnumerable<string> SplitAddresses(string addresses) => addresses.Split([',', ';'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
}
