using FindThatBook.Application.Telemetry;
using FindThatBook.Infrastructure.Telemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace FindThatBook.Api;

/// <summary>OpenTelemetry registration for the API.</summary>
public static class TelemetryExtensions
{
    /// <summary>The service name reported to the collector unless <c>OTEL_SERVICE_NAME</c> overrides it.</summary>
    public const string ServiceName = "find-that-book";

    /// <summary>Tells whether telemetry is on, which is when <c>Api:Telemetry:Enabled</c> is true or <c>OTEL_EXPORTER_OTLP_ENDPOINT</c> is set.</summary>
    /// <param name="config">Application configuration.</param>
    /// <returns>True when traces, metrics and logs should be exported.</returns>
    public static bool IsTelemetryEnabled(IConfiguration config)
        => config.GetValue<bool>("Api:Telemetry:Enabled") || !string.IsNullOrWhiteSpace(config["OTEL_EXPORTER_OTLP_ENDPOINT"]);

    /// <summary>Exports traces, metrics and logs over OTLP and, unless <c>Api:Telemetry:LogHttpBodies</c> is false, logs masked HTTP request and response bodies. Does nothing when telemetry is off.</summary>
    /// <param name="builder">The application builder.</param>
    /// <returns>The same <paramref name="builder"/> instance, for chaining.</returns>
    public static WebApplicationBuilder AddFindThatBookTelemetry(this WebApplicationBuilder builder)
    {
        if (!IsTelemetryEnabled(builder.Configuration)) return builder;

        builder.Services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(ServiceName))
            .WithTracing(tracing => tracing
                .AddSource(FindThatBookTelemetry.SourceName)
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddOtlpExporter())
            .WithMetrics(metrics => metrics
                .AddMeter(FindThatBookTelemetry.MeterName)
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddRuntimeInstrumentation()
                .AddOtlpExporter());

        builder.Logging.AddOpenTelemetry(logging =>
        {
            logging.IncludeScopes = true;
            logging.IncludeFormattedMessage = true;
            logging.AddOtlpExporter();
        });

        if (builder.Configuration.GetValue("Api:Telemetry:LogHttpBodies", true))
            builder.Services.AddHttpBodyLogging();

        return builder;
    }
}
