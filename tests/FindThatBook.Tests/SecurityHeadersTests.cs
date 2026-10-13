using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace FindThatBook.Tests;

public class SecurityHeadersTests
{
    private static async Task<HttpResponseMessage> Get(string path)
        => await new WebApplicationFactory<Program>().CreateClient().GetAsync(path);

    private static string Header(HttpResponseMessage response, string name)
        => response.Headers.TryGetValues(name, out var values) ? string.Join(",", values) : "";

    [Fact]
    public async Task Responses_carry_the_baseline_security_headers()
    {
        var response = await Get("/api/health");

        Assert.Equal("nosniff", Header(response, "X-Content-Type-Options"));
        Assert.Equal("DENY", Header(response, "X-Frame-Options"));
        Assert.Equal("no-referrer", Header(response, "Referrer-Policy"));
    }

    [Fact]
    public async Task The_content_security_policy_allows_only_own_scripts_and_open_library_covers()
    {
        var csp = Header(await Get("/api/health"), "Content-Security-Policy");

        Assert.Contains("default-src 'self'", csp);
        Assert.Contains("script-src 'self'", csp);
        Assert.DoesNotContain("script-src 'self' 'unsafe-inline'", csp);
        Assert.Contains("img-src 'self' data: https://covers.openlibrary.org", csp);
        Assert.Contains("frame-ancestors 'none'", csp);
        Assert.Contains("object-src 'none'", csp);
        Assert.Contains("base-uri 'self'", csp);
    }

    [Fact]
    public async Task The_static_landing_page_may_use_its_own_inline_script_and_styles()
    {
        var csp = Header(await Get("/landing.html"), "Content-Security-Policy");

        Assert.Contains("script-src 'self' 'unsafe-inline'", csp);
        Assert.Contains("style-src 'self' 'unsafe-inline'", csp);
        Assert.Contains("frame-ancestors 'none'", csp);
    }

    [Fact]
    public void Request_bodies_are_limited_to_a_few_kilobytes()
    {
        var factory = new WebApplicationFactory<Program>();

        var limit = factory.Services.GetRequiredService<IOptions<KestrelServerOptions>>().Value.Limits.MaxRequestBodySize;

        Assert.Equal(8192, limit);
    }
}
