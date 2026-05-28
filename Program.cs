var builder = WebApplication.CreateBuilder(args);

var app = builder.Build();

app.MapPost("/notification", (ILogger<Program> logger) =>
{
    logger.LogInformation("Notification endpoint was called.");
    return Results.NoContent();
});

app.Run();
