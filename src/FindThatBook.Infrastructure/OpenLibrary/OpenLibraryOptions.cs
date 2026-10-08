namespace FindThatBook.Infrastructure.OpenLibrary;

public sealed class OpenLibraryOptions
{
    public string BaseUrl { get; set; } = "https://openlibrary.org/";
    public int MaxRetries { get; set; } = 2;
    public int RetryDelayMs { get; set; } = 300;
}
