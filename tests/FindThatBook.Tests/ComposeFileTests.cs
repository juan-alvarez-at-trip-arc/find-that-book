using FindThatBook.Tests.Support;

namespace FindThatBook.Tests;

public class ComposeFileTests
{
    private static string[] ServiceLines(string service)
        => RepoFiles.Lines("docker-compose.yml").SkipWhile(l => l.TrimEnd() != $"  {service}:").Skip(1).TakeWhile(l => !l.StartsWith("  ") || l.StartsWith("   ")).ToArray();

    [Fact]
    public void Jaeger_publishes_only_the_ui_port_and_only_on_localhost()
    {
        var ports = ServiceLines("jaeger").Select(l => l.Trim()).Where(l => l.StartsWith("- \"") && l.Contains(':')).ToList();

        Assert.NotEmpty(ports);
        Assert.All(ports, p => Assert.StartsWith("- \"127.0.0.1:", p));
        Assert.Single(ports);
        Assert.Contains("16686", ports[0]);
    }

    [Fact]
    public void Jaeger_image_is_pinned_to_a_version()
    {
        var image = ServiceLines("jaeger").Select(l => l.Trim()).Single(l => l.StartsWith("image:"));

        Assert.DoesNotContain(":latest", image);
        Assert.Matches(@"image: \S+:\d+\.\d+\.\d+(@sha256:[0-9a-f]{64})?$", image);
    }

    [Fact]
    public void Jaeger_image_is_pinned_by_digest()
    {
        var image = ServiceLines("jaeger").Select(l => l.Trim()).Single(l => l.StartsWith("image:"));

        Assert.Matches(@"@sha256:[0-9a-f]{64}$", image);
    }

    [Fact]
    public void Jaeger_does_not_enable_the_unused_zipkin_listener()
    {
        var lines = ServiceLines("jaeger");

        Assert.DoesNotContain(lines, l => l.Contains("ZIPKIN", StringComparison.OrdinalIgnoreCase) || l.Contains("9411"));
    }

    [Theory]
    [InlineData("app")]
    [InlineData("jaeger")]
    public void Containers_drop_privileges(string service)
    {
        var lines = ServiceLines(service).Select(l => l.Trim()).ToList();

        Assert.Contains("read_only: true", lines);
        Assert.Contains("cap_drop: [ALL]", lines);
        Assert.Contains("security_opt: [\"no-new-privileges:true\"]", lines);
    }
}
