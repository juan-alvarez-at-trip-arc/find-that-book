using FindThatBook.Api;
using FindThatBook.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.AddFindThatBook(builder.Configuration);
builder.AddFindThatBookTelemetry();

var app = builder.Build();

app.UseExceptionHandler();
app.UseDefaultFiles();
app.UseStaticFiles();
app.MapControllers();
app.MapFallbackToFile("index.html");

app.Run();

/// <summary>Entry point class of the API. It is declared public and partial so integration tests can start the app with <c>WebApplicationFactory&lt;Program&gt;</c>.</summary>
public partial class Program;
