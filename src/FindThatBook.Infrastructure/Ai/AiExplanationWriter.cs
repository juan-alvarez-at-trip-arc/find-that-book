using System.Text.Json;
using System.Text.Json.Nodes;
using FindThatBook.Application.Abstractions;
using FindThatBook.Application.Models;

namespace FindThatBook.Infrastructure.Ai;

public sealed class AiExplanationWriter(IAiClient ai) : IExplanationWriter
{
    public async Task<ExplanationOutcome> WriteAsync(IReadOnlyList<MatchedBook> matches, CancellationToken ct)
    {
        var facts = JsonSerializer.Serialize(matches.Select((m, i) =>
        {
            var fact = new Dictionary<string, object?>
            {
                ["index"] = i,
                ["title"] = m.Book.Title,
                ["primaryAuthors"] = m.Book.PrimaryAuthors,
                ["contributors"] = m.Book.Contributors,
                ["matchKind"] = m.Tier.ToString(),
                ["exactTitle"] = m.ExactTitle,
                ["matchedAuthor"] = m.MatchedAuthor,
                ["matchedAuthorRole"] = m.AuthorMatch.ToString(),
                ["authorDetailsAvailable"] = m.Book.Enriched,
            };
            // The year is shown only when it matches the reader's year; otherwise the model cannot misstate it.
            if (m.YearMatches)
            {
                fact["yearMatchesQuery"] = true;
                fact["firstPublishYear"] = m.Book.FirstPublishYear;
            }
            return fact;
        }));

        var prompt = $"""
            For each book below, write ONE sentence (at most 30 words) explaining why it matches the reader's search.
            Use ONLY the facts given. Do not add outside knowledge. Say whether the matched author is the primary author or only a contributor, and mention other contributors when present. Mention the publication year only when yearMatchesQuery is true, and never comment on a year that did not match.
            matchKind values: ExactTitlePrimaryAuthor, ExactTitleContributorAuthor, NearTitleAuthor, AuthorOnly (no title was given), TitleOnly (title matched, no matching author in the query).
            Return a JSON array with one object per book: index and explanation.
            Books: {facts}
            """;

        var text = await ai.GenerateJsonAsync(prompt, AiSchemas.Explanations, ct);
        try
        {
            using var doc = JsonDocument.Parse(text);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
                throw new AiUnavailableException("AI provider did not return an array.");

            var byIndex = new Dictionary<int, string>();
            foreach (var item in doc.RootElement.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object) continue;
                if (!item.TryGetProperty("index", out var i) || !i.TryGetInt32(out var index)) continue;
                if (!item.TryGetProperty("explanation", out var e) || e.ValueKind != JsonValueKind.String) continue;
                var explanation = e.GetString()?.Trim();
                if (!string.IsNullOrEmpty(explanation)) byIndex[index] = explanation;
            }

            var result = new List<string>();
            for (var i = 0; i < matches.Count; i++)
            {
                if (!byIndex.TryGetValue(i, out var explanation))
                    throw new AiUnavailableException($"AI provider returned no explanation for result {i}.");
                result.Add(explanation);
            }
            return new ExplanationOutcome(result, true);
        }
        catch (JsonException ex)
        {
            throw new AiUnavailableException("AI provider returned malformed JSON.", ex);
        }
    }
}
