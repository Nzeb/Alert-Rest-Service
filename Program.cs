var builder = WebApplication.CreateBuilder(args);

var app = builder.Build();

app.MapPost("/notification", (NotificationRequest request, ILogger<Program> logger) =>
{
    logger.LogInformation(
        "Notification endpoint was called. Type: {Type}, Name: {Name}, Description: {Description}",
        request.Type,
        request.Name,
        request.Description);

    return Results.NoContent();
});

app.Run();

internal sealed record NotificationRequest(string? Type, string? Name, string? Description);
