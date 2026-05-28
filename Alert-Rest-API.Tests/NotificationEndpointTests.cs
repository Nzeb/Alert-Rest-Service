using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Alert_Rest_API.Tests;

public sealed class NotificationEndpointTests(NotificationApiFactory factory)
    : IClassFixture<NotificationApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task PostNotification_ReturnsAccepted_ForNonWarningType()
    {
        var request = new
        {
            Type = "Info",
            Name = "Health Ping",
            Description = "Service heartbeat"
        };

        var response = await _client.PostAsJsonAsync("/notification", request);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
    }

    [Fact]
    public async Task PostNotification_ReturnsOk_ForWarningType()
    {
        var request = new
        {
            Type = "Warning",
            Name = "Backup Failure",
            Description = "The backup failed due to a database problem"
        };

        var response = await _client.PostAsJsonAsync("/notification", request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}

public sealed class NotificationApiFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        Environment.SetEnvironmentVariable("DISCORD_WEBHOOK_URL", "https://example.invalid/webhook");

        builder.ConfigureServices(services =>
        {
            var warningListenerRegistration = services.FirstOrDefault(descriptor =>
                descriptor.ServiceType == typeof(IHostedService)
                && descriptor.ImplementationType?.Name == "WarningQueueListener");

            if (warningListenerRegistration is not null)
            {
                services.Remove(warningListenerRegistration);
            }
        });
    }
}
