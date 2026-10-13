namespace FindThatBook.Tests;

public class ComposeFileTests
{
    private static string[] ComposeLines()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "docker-compose.yml"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return File.ReadAllLines(Path.Combine(dir.FullName, "docker-compose.yml"));
    }

    private static string[] JaegerLines()
        => ComposeLines().SkipWhile(l => l.TrimEnd() != "  jaeger:").Skip(1).TakeWhile(l => !l.StartsWith("  ") || l.StartsWith("   ")).ToArray();

    [Fact]
    public void Jaeger_publishes_only_the_ui_port_and_only_on_localhost()
    {
        var ports = JaegerLines().Select(l => l.Trim()).Where(l => l.StartsWith("- \"") && l.Contains(':')).ToList();

        Assert.NotEmpty(ports);
        Assert.All(ports, p => Assert.StartsWith("- \"127.0.0.1:", p));
        Assert.Single(ports);
        Assert.Contains("16686", ports[0]);
    }

    [Fact]
    public void Jaeger_image_is_pinned_to_a_version()
    {
        var image = JaegerLines().Select(l => l.Trim()).Single(l => l.StartsWith("image:"));

        Assert.DoesNotContain(":latest", image);
        Assert.Matches(@"image: \S+:\d+\.\d+\.\d+$", image);
    }
}
