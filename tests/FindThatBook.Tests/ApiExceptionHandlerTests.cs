using FindThatBook.Api;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;

namespace FindThatBook.Tests;

public class ApiExceptionHandlerTests
{
    private sealed class CapturingProblems : IProblemDetailsService
    {
        public ProblemDetailsContext? Written { get; private set; }

        public ValueTask WriteAsync(ProblemDetailsContext context)
        {
            Written = context;
            return ValueTask.CompletedTask;
        }

        public ValueTask<bool> TryWriteAsync(ProblemDetailsContext context)
        {
            Written = context;
            return ValueTask.FromResult(true);
        }
    }

    private static async Task<(int Status, string? Title)> Handle(Exception exception)
    {
        var problems = new CapturingProblems();
        var context = new DefaultHttpContext();

        await new ApiExceptionHandler(problems, NullLogger<ApiExceptionHandler>.Instance).TryHandleAsync(context, exception, default);

        return (context.Response.StatusCode, problems.Written?.ProblemDetails.Title);
    }

    [Fact]
    public async Task A_request_body_over_the_server_limit_is_a_413_not_a_500()
    {
        var (status, title) = await Handle(new BadHttpRequestException("Request body too large.", StatusCodes.Status413PayloadTooLarge));

        Assert.Equal(StatusCodes.Status413PayloadTooLarge, status);
        Assert.Equal("The request is too large.", title);
    }

    [Fact]
    public async Task Other_bad_requests_keep_their_client_error_status()
    {
        var (status, _) = await Handle(new BadHttpRequestException("Malformed.", StatusCodes.Status400BadRequest));

        Assert.Equal(StatusCodes.Status400BadRequest, status);
    }

    [Fact]
    public async Task An_unexpected_exception_is_still_a_500_without_details()
    {
        var (status, title) = await Handle(new InvalidOperationException("secret detail"));

        Assert.Equal(StatusCodes.Status500InternalServerError, status);
        Assert.Equal("An unexpected error occurred.", title);
    }
}
