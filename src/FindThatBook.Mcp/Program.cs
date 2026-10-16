using FindThatBook.Infrastructure;
using FindThatBook.Mcp;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory,
});
builder.Logging.ClearProviders();
builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);
builder.Services.AddFindThatBook(builder.Configuration);
builder.Services.AddFindThatBookMcp().WithStdioServerTransport();

await builder.Build().RunAsync();
