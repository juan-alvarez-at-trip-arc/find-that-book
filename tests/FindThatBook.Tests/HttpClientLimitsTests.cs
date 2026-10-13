using FindThatBook.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace FindThatBook.Tests;

public class HttpClientLimitsTests
{
    [Theory]
    [InlineData("anything")]
    [InlineData("IBookCatalog")]
    [InlineData("IAiClient")]
    public void Every_outgoing_http_client_caps_the_response_size(string clientName)
    {
        var factory = new WebApplicationFactory<Program>();

        var client = factory.Services.GetRequiredService<IHttpClientFactory>().CreateClient(clientName);

        Assert.Equal(ServiceCollectionExtensions.MaxResponseBytes, client.MaxResponseContentBufferSize);
    }

    [Fact]
    public void The_response_cap_is_small_enough_to_matter_and_large_enough_for_catalog_records()
    {
        Assert.InRange(ServiceCollectionExtensions.MaxResponseBytes, 512 * 1024, 8 * 1024 * 1024);
    }
}
