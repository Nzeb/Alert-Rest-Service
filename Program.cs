using System.Threading.Channels;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<WarningQueue>();
builder.Services.AddHostedService<WarningQueueListener>();
builder.Services.AddHttpClient<DiscordWebhookClient>();

var app = builder.Build();

app.MapPost("/notification", (JsonElement requestBody, WarningQueue warningQueue, ILogger<Program> logger) =>
{
    var type = GetStringPropertyCaseInsensitive(requestBody, "Type");
    if (!string.Equals(type?.Trim(), "Warning", StringComparison.OrdinalIgnoreCase))
    {
        return Results.Accepted();
    }

    var warning = new WarningMessage(
        PayloadJson: requestBody.GetRawText(),
        EnqueuedAtUtc: DateTimeOffset.UtcNow);

    warningQueue.TryEnqueue(warning);

    logger.LogWarning("Warning notification queued. EnqueuedAtUtc: {EnqueuedAtUtc}", warning.EnqueuedAtUtc);

    return Results.Ok();
});

app.Run();

static string? GetStringPropertyCaseInsensitive(JsonElement json, string propertyName)
{
    if (json.ValueKind != JsonValueKind.Object)
    {
        return null;
    }

    foreach (var property in json.EnumerateObject())
    {
        if (!string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
        {
            continue;
        }

        return property.Value.ValueKind == JsonValueKind.String
            ? property.Value.GetString()
            : property.Value.ToString();
    }

    return null;
}

internal sealed record WarningMessage(string PayloadJson, DateTimeOffset EnqueuedAtUtc);

internal sealed class WarningQueue
{
    private readonly Channel<WarningMessage> _channel = Channel.CreateUnbounded<WarningMessage>(
        new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false
        });

    public bool TryEnqueue(WarningMessage warning)
    {
        return _channel.Writer.TryWrite(warning);
    }

    public IAsyncEnumerable<WarningMessage> ListenAsync(CancellationToken cancellationToken)
    {
        return _channel.Reader.ReadAllAsync(cancellationToken);
    }
}

internal sealed class WarningQueueListener(
    WarningQueue warningQueue,
    DiscordWebhookClient discordWebhookClient,
    ILogger<WarningQueueListener> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var warning in warningQueue.ListenAsync(stoppingToken))
            {
                logger.LogWarning("Warning notification processed from queue. EnqueuedAtUtc: {EnqueuedAtUtc}", warning.EnqueuedAtUtc);

                try
                {
                    await discordWebhookClient.SendWarningAsync(warning, stoppingToken);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to send warning notification to Discord webhook.");
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Graceful shutdown
        }
    }
}

public partial class Program
{
}
