using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace FindThatBook.Infrastructure.Telemetry;

/// <summary>Logs every outgoing HTTP request and its response with headers and bodies. All values pass through <see cref="SensitiveDataMasker"/> before they are logged.</summary>
/// <param name="log">Logger that receives the request and response entries.</param>
/// <param name="masker">Masks credentials in URLs, headers and bodies.</param>
/// <param name="maxBodyChars">The most body characters logged per message. Longer bodies are cut after masking.</param>
public sealed class HttpLoggingHandler(ILogger<HttpLoggingHandler> log, SensitiveDataMasker masker, int maxBodyChars = HttpLoggingHandler.DefaultMaxBodyChars) : DelegatingHandler
{
    /// <summary>The default limit of logged body characters per message.</summary>
    public const int DefaultMaxBodyChars = 8192;

    /// <summary>Logs the request, sends it, then logs the response or the failure. The caller can still read both bodies.</summary>
    /// <param name="request">The outgoing request.</param>
    /// <param name="cancellationToken">Token that cancels the request.</param>
    /// <returns>The response from the inner handler.</returns>
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var url = masker.MaskText(request.RequestUri?.ToString() ?? "");
        log.LogInformation("HTTP request {Method} {Url}\nheaders: {Headers}\nbody: {Body}",
            request.Method, url, FormatHeaders(request.Headers, request.Content?.Headers), await ReadBodyAsync(request.Content, cancellationToken));

        var started = Stopwatch.GetTimestamp();
        HttpResponseMessage response;
        try
        {
            response = await base.SendAsync(request, cancellationToken);
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "HTTP {Method} {Url} failed after {ElapsedMs} ms: {Error}",
                request.Method, url, Stopwatch.GetElapsedTime(started).TotalMilliseconds, masker.MaskText(ex.Message));
            throw;
        }

        log.LogInformation("HTTP response {StatusCode} for {Method} {Url} after {ElapsedMs} ms\nheaders: {Headers}\nbody: {Body}",
            (int)response.StatusCode, request.Method, url, Stopwatch.GetElapsedTime(started).TotalMilliseconds,
            FormatHeaders(response.Headers, response.Content.Headers), await ReadBodyAsync(response.Content, cancellationToken));
        return response;
    }

    /// <summary>Formats headers as one masked <c>name: value</c> pair per entry.</summary>
    /// <param name="headers">The message headers.</param>
    /// <param name="contentHeaders">The content headers, when the message has content.</param>
    /// <returns>The masked header text.</returns>
    private string FormatHeaders(IEnumerable<KeyValuePair<string, IEnumerable<string>>> headers, IEnumerable<KeyValuePair<string, IEnumerable<string>>>? contentHeaders)
        => string.Join("; ", headers.Concat(contentHeaders ?? [])
            .Select(h => $"{h.Key}: {masker.MaskHeader(h.Key, string.Join(", ", h.Value))}"));

    /// <summary>Reads a body without consuming it, masks it and cuts it to the size limit. Non-text bodies are summarized instead of logged.</summary>
    /// <param name="content">The message content, or null when there is none.</param>
    /// <param name="ct">Token that cancels the read.</param>
    /// <returns>The text to log.</returns>
    private async Task<string> ReadBodyAsync(HttpContent? content, CancellationToken ct)
    {
        if (content is null) return "";

        var mediaType = content.Headers.ContentType?.MediaType;
        if (mediaType is not null && !IsText(mediaType))
            return $"[binary {mediaType}, {content.Headers.ContentLength?.ToString() ?? "unknown"} bytes]";

        await content.LoadIntoBufferAsync();
        var body = masker.MaskText(await content.ReadAsStringAsync(ct));
        return body.Length > maxBodyChars ? $"{body[..maxBodyChars]}...[truncated {body.Length - maxBodyChars} chars]" : body;
    }

    /// <summary>Tells whether a media type carries readable text.</summary>
    /// <param name="mediaType">The media type without parameters.</param>
    /// <returns>True for text, JSON, XML and form bodies.</returns>
    private static bool IsText(string mediaType)
        => mediaType.StartsWith("text/", StringComparison.OrdinalIgnoreCase)
           || mediaType.EndsWith("json", StringComparison.OrdinalIgnoreCase)
           || mediaType.EndsWith("xml", StringComparison.OrdinalIgnoreCase)
           || mediaType.Equals("application/x-www-form-urlencoded", StringComparison.OrdinalIgnoreCase);
}
