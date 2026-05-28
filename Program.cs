using System.Threading.Channels;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<WarningQueue>();
builder.Services.AddHostedService<WarningQueueListener>();
builder.Services.AddHttpClient<DiscordWebhookClient>();

var app = builder.Build();

app.MapPost("/notification", (NotificationRequest request, WarningQueue warningQueue, ILogger<Program> logger) =>
{
    if (!string.Equals(request.Type?.Trim(), "Warning", StringComparison.OrdinalIgnoreCase))
    {
        return Results.Accepted();
    }

    var warning = new WarningMessage(
        Name: request.Name?.Trim() ?? string.Empty,
        Description: request.Description?.Trim() ?? string.Empty,
        EnqueuedAtUtc: DateTimeOffset.UtcNow);

    warningQueue.TryEnqueue(warning);

    logger.LogWarning(
        "Warning notification queued. Name: {Name}, Description: {Description}",
        warning.Name,
        warning.Description);

    return Results.Ok();
});

app.Run();

internal sealed record NotificationRequest(string? Type, string? Name, string? Description);

internal sealed record WarningMessage(string Name, string Description, DateTimeOffset EnqueuedAtUtc);

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
                logger.LogWarning(
                    "Warning notification processed from queue. Name: {Name}, Description: {Description}, EnqueuedAtUtc: {EnqueuedAtUtc}",
                    warning.Name,
                    warning.Description,
                    warning.EnqueuedAtUtc);

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
