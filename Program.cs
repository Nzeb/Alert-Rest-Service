using System.Collections.Concurrent;

var builder = WebApplication.CreateBuilder(args);

var warningQueue = new ConcurrentQueue<WarningMessage>();

var app = builder.Build();

app.MapPost("/notification", (NotificationRequest request, ILogger<Program> logger) =>
{
    if (!string.Equals(request.Type?.Trim(), "Warning", StringComparison.OrdinalIgnoreCase))
    {
        return Results.Accepted();
    }

    var warning = new WarningMessage(
        Name: request.Name?.Trim() ?? string.Empty,
        Description: request.Description?.Trim() ?? string.Empty,
        EnqueuedAtUtc: DateTimeOffset.UtcNow);

    warningQueue.Enqueue(warning);

    logger.LogWarning(
        "Warning notification queued. Name: {Name}, Description: {Description}",
        warning.Name,
        warning.Description);

    return Results.Ok();
});

app.Run();

internal sealed record NotificationRequest(string? Type, string? Name, string? Description);

internal sealed record WarningMessage(string Name, string Description, DateTimeOffset EnqueuedAtUtc);
