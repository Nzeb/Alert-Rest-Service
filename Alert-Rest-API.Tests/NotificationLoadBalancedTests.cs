using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Alert_Rest_API.Tests;

public sealed class NotificationLoadBalancedTests(LoadBalancedNotificationApiFactory factory)
    : IClassFixture<LoadBalancedNotificationApiFactory>
{
    [Fact(Timeout = 300000)]
    [Trait("Category", "Load")]
    public async Task PostNotification_Handles10000ParallelRequests_WithDiscordRateLimitMock()
    {
        const int totalRequests = 10_000;
        const int warningRequests = 100;

        var client = factory.CreateClient();

        var requestTasks = Enumerable.Range(0, totalRequests)
            .Select(index => PostNotificationAsync(
                client,
                type: index < warningRequests ? "Warning" : "Info",
                name: $"Load-{index}"))
            .ToArray();

        var statusCodes = await Task.WhenAll(requestTasks);

        Assert.Equal(warningRequests, statusCodes.Count(code => code == HttpStatusCode.OK));
        Assert.Equal(totalRequests - warningRequests, statusCodes.Count(code => code == HttpStatusCode.Accepted));

        var processedAllWarningPosts = await factory.DiscordMock.WaitForSuccessfulPostsAsync(
            expectedCount: warningRequests,
            timeout: TimeSpan.FromSeconds(180));

        Assert.True(
            processedAllWarningPosts,
            $"Expected {warningRequests} successful Discord posts, but got {factory.DiscordMock.SuccessfulPosts}. Total mock posts: {factory.DiscordMock.TotalPosts}.");

        Assert.True(
            factory.DiscordMock.MaxSuccessfulPostsInWindow <= 5,
            $"Expected at most 5 successful posts per 2-second window, but saw {factory.DiscordMock.MaxSuccessfulPostsInWindow}.");
    }

    private static async Task<HttpStatusCode> PostNotificationAsync(HttpClient client, string type, string name)
    {
        var request = new
        {
            Type = type,
            Name = name,
            Description = "Load test notification"
        };

        using var response = await client.PostAsJsonAsync("/notification", request);
        return response.StatusCode;
    }
}

public sealed class LoadBalancedNotificationApiFactory : WebApplicationFactory<Program>
{
    public DiscordRateLimitedMockHandler DiscordMock => Services.GetRequiredService<DiscordRateLimitedMockHandler>();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        Environment.SetEnvironmentVariable("DISCORD_WEBHOOK_URL", "https://discord.mock.local/webhook");

        builder.ConfigureServices(services =>
        {
            services.AddSingleton<DiscordRateLimitedMockHandler>();
            services.AddHttpClient("DiscordWebhookClient")
                .ConfigurePrimaryHttpMessageHandler(sp => sp.GetRequiredService<DiscordRateLimitedMockHandler>());
        });
    }
}

public sealed class DiscordRateLimitedMockHandler : HttpMessageHandler
{
    private const int LimitPerWindow = 5;
    private static readonly TimeSpan WindowDuration = TimeSpan.FromSeconds(2);

    private readonly object _syncLock = new();
    private DateTimeOffset _windowStartUtc = DateTimeOffset.UtcNow;
    private int _successfulInCurrentWindow;
    private int _maxSuccessfulInWindow;
    private int _totalPosts;
    private int _successfulPosts;

    public int TotalPosts
    {
        get
        {
            lock (_syncLock)
            {
                return _totalPosts;
            }
        }
    }

    public int SuccessfulPosts
    {
        get
        {
            lock (_syncLock)
            {
                return _successfulPosts;
            }
        }
    }

    public int MaxSuccessfulPostsInWindow
    {
        get
        {
            lock (_syncLock)
            {
                return _maxSuccessfulInWindow;
            }
        }
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var nowUtc = DateTimeOffset.UtcNow;
        var windowResetUtc = nowUtc;
        var remaining = 0;
        var isAllowed = false;

        lock (_syncLock)
        {
            if (nowUtc >= _windowStartUtc + WindowDuration)
            {
                _windowStartUtc = nowUtc;
                _successfulInCurrentWindow = 0;
            }

            _totalPosts++;
            if (_successfulInCurrentWindow < LimitPerWindow)
            {
                _successfulInCurrentWindow++;
                _successfulPosts++;
                if (_successfulInCurrentWindow > _maxSuccessfulInWindow)
                {
                    _maxSuccessfulInWindow = _successfulInCurrentWindow;
                }

                isAllowed = true;
            }

            remaining = Math.Max(LimitPerWindow - _successfulInCurrentWindow, 0);
            windowResetUtc = _windowStartUtc + WindowDuration;
        }

        var resetEpochSeconds = (long)Math.Ceiling(windowResetUtc.ToUnixTimeMilliseconds() / 1000d);

        var response = new HttpResponseMessage(isAllowed ? HttpStatusCode.NoContent : HttpStatusCode.TooManyRequests)
        {
            Content = new StringContent(isAllowed ? string.Empty : "rate_limited")
        };

        response.Headers.TryAddWithoutValidation("x-ratelimit-limit", LimitPerWindow.ToString(CultureInfo.InvariantCulture));
        response.Headers.TryAddWithoutValidation("x-ratelimit-remaining", remaining.ToString(CultureInfo.InvariantCulture));
        response.Headers.TryAddWithoutValidation("x-ratelimit-reset", resetEpochSeconds.ToString(CultureInfo.InvariantCulture));

        return Task.FromResult(response);
    }

    public async Task<bool> WaitForSuccessfulPostsAsync(int expectedCount, TimeSpan timeout)
    {
        var timeoutAt = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < timeoutAt)
        {
            if (SuccessfulPosts >= expectedCount)
            {
                return true;
            }

            await Task.Delay(100);
        }

        return SuccessfulPosts >= expectedCount;
    }
}
