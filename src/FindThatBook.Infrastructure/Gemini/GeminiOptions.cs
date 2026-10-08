namespace FindThatBook.Infrastructure.Gemini;

public sealed class GeminiOptions
{
    public const string DefaultModel = "gemini-3.8-flash";

    public string? ApiKey { get; set; }
    public string Model { get; set; } = DefaultModel;
}
