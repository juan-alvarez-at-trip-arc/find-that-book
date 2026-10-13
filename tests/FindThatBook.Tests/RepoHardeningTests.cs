using System.Text.RegularExpressions;
using FindThatBook.Tests.Support;

namespace FindThatBook.Tests;

public class RepoHardeningTests
{
    [Fact]
    public void Dockerfile_base_images_are_pinned_by_digest()
    {
        var froms = RepoFiles.Lines("Dockerfile").Where(l => l.StartsWith("FROM ")).ToList();

        Assert.Equal(3, froms.Count);
        Assert.All(froms, l => Assert.Matches(@"@sha256:[0-9a-f]{64}", l));
    }

    [Fact]
    public void Workflow_actions_are_pinned_to_a_commit()
    {
        var uses = Directory.GetFiles(RepoFiles.Path(".github/workflows"), "*.yml")
            .SelectMany(File.ReadAllLines)
            .Select(l => l.Trim())
            .Where(l => l.StartsWith("- uses:") || l.StartsWith("uses:"))
            .ToList();

        Assert.NotEmpty(uses);
        Assert.All(uses, l => Assert.Matches(@"uses: \S+@[0-9a-f]{40}( #.*)?$", l));
    }

    [Fact]
    public void Npm_installs_do_not_run_dependency_install_scripts()
    {
        var installs = RepoFiles.Lines("Dockerfile").Concat(RepoFiles.Lines(".github/workflows/ci.yml"))
            .Where(l => l.Contains("npm ci"))
            .ToList();

        Assert.Equal(2, installs.Count);
        Assert.All(installs, l => Assert.Contains("--ignore-scripts", l));
    }

    [Fact]
    public void Dependabot_watches_the_compose_file_too()
    {
        Assert.Contains("package-ecosystem: docker-compose", RepoFiles.Text(".github/dependabot.yml"));
    }

    [Fact]
    public void Ci_audits_nuget_and_npm_dependencies()
    {
        var ci = RepoFiles.Text(".github/workflows/ci.yml");

        Assert.Contains("dotnet list FindThatBook.sln package --vulnerable --include-transitive", ci);
        Assert.Contains("npm audit --audit-level=high", ci);
    }

    [Theory]
    [InlineData(".env")]
    [InlineData(".github")]
    public void Dockerignore_keeps_secrets_and_ci_files_out_of_the_build_context(string entry)
        => Assert.Contains(entry, RepoFiles.Lines(".dockerignore").Select(l => l.Trim()));

    [Fact]
    public void Index_html_has_no_inline_script_so_the_csp_needs_no_unsafe_inline()
    {
        var html = RepoFiles.Text("web/index.html");

        var inline = Regex.Matches(html, @"<script(?<attrs>[^>]*)>", RegexOptions.IgnoreCase)
            .Where(m => !m.Groups["attrs"].Value.Contains("src=") && !m.Groups["attrs"].Value.Contains("application/ld+json"));

        Assert.Empty(inline);
        Assert.True(File.Exists(RepoFiles.Path("web/public/theme-init.js")));
    }
}
