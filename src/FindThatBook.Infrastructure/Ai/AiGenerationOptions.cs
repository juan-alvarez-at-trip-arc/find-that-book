using System.Text.RegularExpressions;

namespace FindThatBook.Infrastructure.Ai;

/// <summary>Generation settings sent with every AI request, whichever provider is selected: sampling (temperature, top-k, top-p), a seed and the reasoning effort. They are bound from the <c>Ai</c> configuration section (<c>Ai:Temperature</c>, <c>Ai:TopK</c>, <c>Ai:TopP</c>, <c>Ai:Seed</c>, <c>Ai:ReasoningEffort</c>). A value that is not set, or is invalid, is not sent, except the temperature, which falls back to <see cref="DefaultTemperature"/>. <see cref="Problems"/> lists the values that were rejected so the readiness check can report them.</summary>
public sealed partial class AiGenerationOptions
{
    /// <summary>The temperature sent when none is configured or the configured one is invalid. Zero keeps the answers as repeatable as the provider allows.</summary>
    public const double DefaultTemperature = 0;

    /// <summary>The highest temperature accepted.</summary>
    public const double MaxTemperature = 2;

    /// <summary>The configured temperature, from 0 to 2. A blank value counts as not set.</summary>
    public double? Temperature { get; set; }

    /// <summary>The configured top-k, the number of most likely tokens the model may pick from, 1 or more. A blank value counts as not set, and then the provider's own default applies. Some OpenAI-compatible gateways reject this parameter, so leave it unset for those.</summary>
    public int? TopK { get; set; }

    /// <summary>The configured top-p (nucleus sampling), greater than 0 and at most 1. A blank value counts as not set, and then the provider's own default applies.</summary>
    public double? TopP { get; set; }

    /// <summary>The configured seed, any integer. A blank value counts as not set. It asks the provider to repeat its choices for the same input, as far as the provider supports that. Support depends on the provider and the model, and a provider that does not support it may ignore it or reject it.</summary>
    public int? Seed { get; set; }

    /// <summary>The configured reasoning effort: a short word such as <c>minimal</c>, <c>low</c>, <c>medium</c> or <c>high</c>. The accepted words depend on the provider and the model, so only the shape is checked here (<see cref="Problems"/>). It is sent to an OpenAI-compatible gateway as <c>reasoning_effort</c> and to Gemini as <c>thinkingConfig.thinkingLevel</c>. A blank value counts as not set. A model that does not support reasoning settings may reject it, so leave it unset for those.</summary>
    public string? ReasoningEffort { get; set; }

    /// <summary>The temperature to send: the configured one when it is from 0 to 2, otherwise <see cref="DefaultTemperature"/>.</summary>
    public double EffectiveTemperature => Temperature is { } t && t >= 0 && t <= MaxTemperature ? t : DefaultTemperature;

    /// <summary>The top-k to send, or null when it is not set or is below 1.</summary>
    public int? EffectiveTopK => TopK is >= 1 ? TopK : null;

    /// <summary>The top-p to send, or null when it is not set or is outside 0 (exclusive) to 1.</summary>
    public double? EffectiveTopP => TopP is { } p && p > 0 && p <= 1 ? p : null;

    /// <summary>The seed to send, or null when it is not set.</summary>
    public int? EffectiveSeed => Seed;

    /// <summary>The reasoning effort to send: the configured word, trimmed and in lower case, or null when it is blank or is not a short word.</summary>
    public string? EffectiveReasoningEffort
    {
        get
        {
            var word = ReasoningEffort?.Trim().ToLowerInvariant();
            return !string.IsNullOrEmpty(word) && EffortWord().IsMatch(word) ? word : null;
        }
    }

    /// <summary>Lists the configured values that are invalid. Each message names the setting and never prints its value.</summary>
    /// <returns>One message per invalid value, empty when every configured value is valid.</returns>
    public IReadOnlyList<string> Problems()
    {
        var problems = new List<string>();
        if (Temperature is not null && EffectiveTemperature != Temperature)
            problems.Add($"Ai:Temperature must be between 0 and {MaxTemperature}.");
        if (TopK is not null && EffectiveTopK is null)
            problems.Add("Ai:TopK must be 1 or more.");
        if (TopP is not null && EffectiveTopP is null)
            problems.Add("Ai:TopP must be greater than 0 and at most 1.");
        if (!string.IsNullOrWhiteSpace(ReasoningEffort) && EffectiveReasoningEffort is null)
            problems.Add("Ai:ReasoningEffort must be a short lowercase word of letters, digits, hyphens or underscores (for example low, medium or high), starting with a letter and at most 20 characters.");
        return problems;
    }

    [GeneratedRegex("^[a-z][a-z0-9_-]{0,19}$")]
    private static partial Regex EffortWord();
}
