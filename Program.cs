var builder = WebApplication.CreateBuilder(args);

var app = builder.Build();

app.MapPost("/notification", (NotificationRequest request, ILogger<Program> logger) =>
{
    if (!string.Equals(request.Type?.Trim(), "Warning", StringComparison.OrdinalIgnoreCase))
    {
        return Results.Accepted();
    }

    logger.LogWarning(
        "Warning notification received. Name: {Name}, Description: {Description}",
        request.Name,
        request.Description);

    return Results.Ok();
});

app.Run();

internal sealed record NotificationRequest(string? Type, string? Name, string? Description);
