namespace FindThatBook.Api;

/// <summary>Adds browser security headers to every response.</summary>
public static class SecurityHeadersExtensions
{
    private const string LandingPath = "/landing.html";

    private const string AppPolicy =
        "default-src 'self'; script-src 'self'; style-src 'self'; style-src-attr 'unsafe-inline'; "
        + "img-src 'self' data: https://covers.openlibrary.org https://archive.org https://*.archive.org; "
        + "font-src 'self'; connect-src 'self'; object-src 'none'; base-uri 'self'; form-action 'self'; frame-ancestors 'none'";

    private const string PermissionsPolicy = "camera=(), microphone=(), geolocation=(), payment=(), usb=(), interest-cohort=()";

    private const string LandingPolicy =
        "default-src 'self'; script-src 'self' 'unsafe-inline'; style-src 'self' 'unsafe-inline'; "
        + "img-src 'self' data:; object-src 'none'; base-uri 'self'; form-action 'self'; frame-ancestors 'none'";

    /// <summary>Sets <c>X-Content-Type-Options</c>, <c>X-Frame-Options</c>, <c>Referrer-Policy</c>, a <c>Permissions-Policy</c> that switches off device features the app never uses, and a <c>Content-Security-Policy</c> when the response starts, so the exception handler clearing the headers of a failed request cannot remove them. The static landing page gets a policy that allows its own inline script and styles, and every other response gets a policy without <c>unsafe-inline</c> for scripts.</summary>
    /// <param name="app">The application pipeline.</param>
    /// <returns>The same <paramref name="app"/> instance, for chaining.</returns>
    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app)
        => app.Use((context, next) =>
        {
            context.Response.OnStarting(() =>
            {
                var headers = context.Response.Headers;
                headers["X-Content-Type-Options"] = "nosniff";
                headers["X-Frame-Options"] = "DENY";
                headers["Referrer-Policy"] = "no-referrer";
                headers["Permissions-Policy"] = PermissionsPolicy;
                headers["Content-Security-Policy"] = context.Request.Path.Equals(LandingPath, StringComparison.OrdinalIgnoreCase) ? LandingPolicy : AppPolicy;
                return Task.CompletedTask;
            });
            return next(context);
        });
}
