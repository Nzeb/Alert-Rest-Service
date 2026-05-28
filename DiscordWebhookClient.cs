using System.Globalization;
using System.Net.Http.Json;

internal sealed class DiscordWebhookClient(HttpClient httpClient, ILogger<DiscordWebhookClient> logger)
{
    private const string WebhookUrlEnvironmentVariable = "DISCORD_WEBHOOK_URL";
    private const int MaxRetryAttempts = 5;
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(500);

    private readonly Uri _discordWebhookUri = ResolveDiscordWebhookUri();
    private DateTimeOffset _nextAllowedRequestUtc = DateTimeOffset.MinValue;

    public async Task SendWarningAsync(WarningMessage warning, CancellationToken cancellationToken)
    {
        var payload = new DiscordWebhookPayload(
            Content: $"Warning: {warning.Name}\n{warning.Description}\nEnqueuedAtUtc: {warning.EnqueuedAtUtc:O}");

        for (var attempt = 1; attempt <= MaxRetryAttempts; attempt++)
        {
            await WaitIfRateLimitedBeforeRequestAsync(cancellationToken);

            try
            {
                using var response = await httpClient.PostAsJsonAsync(_discordWebhookUri, payload, cancellationToken);
                UpdateRateLimitFromResponse(response.Headers);

                if (response.IsSuccessStatusCode)
                {
                    return;
                }

                var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
                var shouldRetry = attempt < MaxRetryAttempts
                    && IsTransientStatusCode(response.StatusCode);

                if (!shouldRetry)
                {
                    logger.LogError(
                        "Discord webhook failed after {Attempt} attempt(s). Status: {StatusCode}. Response: {ResponseBody}",
                        attempt,
                        (int)response.StatusCode,
                        responseBody);
                    return;
                }
            }
            catch (HttpRequestException ex) when (attempt < MaxRetryAttempts)
            {
                var delay = RetryDelay;
                logger.LogWarning(
                    ex,
                    "Discord webhook network failure on attempt {Attempt}/{MaxAttempts}. Waiting {DelayMs} ms before retry.",
                    attempt,
                    MaxRetryAttempts,
                    (int)delay.TotalMilliseconds);

                await Task.Delay(delay, cancellationToken);
            }
            catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested && attempt < MaxRetryAttempts)
            {
                var delay = RetryDelay;
                logger.LogWarning(
                    ex,
                    "Discord webhook timeout on attempt {Attempt}/{MaxAttempts}. Waiting {DelayMs} ms before retry.",
                    attempt,
                    MaxRetryAttempts,
                    (int)delay.TotalMilliseconds);

                await Task.Delay(delay, cancellationToken);
            }
        }
    }

    private async Task WaitIfRateLimitedBeforeRequestAsync(CancellationToken cancellationToken)
    {
        if (_nextAllowedRequestUtc <= DateTimeOffset.UtcNow)
        {
            return;
        }

        var delay = _nextAllowedRequestUtc - DateTimeOffset.UtcNow;
        if (delay <= TimeSpan.Zero)
        {
            return;
        }

        logger.LogWarning(
            "Discord rate limit active. Waiting {DelayMs} ms until {ResetAtUtc} before sending next request.",
            (int)delay.TotalMilliseconds,
            _nextAllowedRequestUtc);

        await Task.Delay(delay, cancellationToken);
    }

    private void UpdateRateLimitFromResponse(System.Net.Http.Headers.HttpResponseHeaders headers)
    {
        string? remainingRaw = null;
        if (headers.TryGetValues("x-ratelimit-remaining", out var remainingValues))
        {
            foreach (var value in remainingValues)
            {
                if (!string.IsNullOrWhiteSpace(value))
                {
                    remainingRaw = value;
                    break;
                }
            }
        }

        if (string.IsNullOrWhiteSpace(remainingRaw)
            || !int.TryParse(remainingRaw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var remaining))
        {
            return;
        }

        if (remaining > 0)
        {
            _nextAllowedRequestUtc = DateTimeOffset.MinValue;
            return;
        }

        string? resetRaw = null;
        if (headers.TryGetValues("x-ratelimit-reset", out var resetValues))
        {
            foreach (var value in resetValues)
            {
                if (!string.IsNullOrWhiteSpace(value))
                {
                    resetRaw = value;
                    break;
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(resetRaw)
            && long.TryParse(resetRaw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var resetEpochSeconds))
        {
            _nextAllowedRequestUtc = DateTimeOffset.FromUnixTimeSeconds(resetEpochSeconds);
            return;
        }

        _nextAllowedRequestUtc = DateTimeOffset.UtcNow + TimeSpan.FromMilliseconds(250);
    }

    private static bool IsTransientStatusCode(System.Net.HttpStatusCode statusCode)
    {
        return statusCode == System.Net.HttpStatusCode.RequestTimeout
            || statusCode == System.Net.HttpStatusCode.BadGateway
            || statusCode == System.Net.HttpStatusCode.ServiceUnavailable
            || statusCode == System.Net.HttpStatusCode.GatewayTimeout
            || (int)statusCode >= 500;
    }

    private static Uri ResolveDiscordWebhookUri()
    {
        var webhookUrl = Environment.GetEnvironmentVariable(WebhookUrlEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(webhookUrl))
        {
            throw new InvalidOperationException(
                $"Environment variable '{WebhookUrlEnvironmentVariable}' is required.");
        }

        if (!Uri.TryCreate(webhookUrl, UriKind.Absolute, out var webhookUri))
        {
            throw new InvalidOperationException(
                $"Environment variable '{WebhookUrlEnvironmentVariable}' must be a valid absolute URI.");
        }

        return webhookUri;
    }
}

internal sealed record DiscordWebhookPayload(string Content);
