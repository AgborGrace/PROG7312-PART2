using System.Text.Json.Serialization;
using SmartX.Api.Services;

var builder = WebApplication.CreateBuilder(args);

// ---- Singletons: every request and the background processor share the same in-memory data structures ----
builder.Services.AddSingleton<PredictiveEngine>();
builder.Services.AddSingleton<SmartXDataStore>();   // TelemetryQueue, CriticalAlertQueue, CommandUndoStack, DeviceRegistry, ...
builder.Services.AddSingleton<IngestionPipeline>(); // Part 1: jagged arrays + List<T> + recursive validation

// Background worker that drains the queues (critical first, then FIFO) and detects disconnected nodes.
builder.Services.AddHostedService<TelemetryProcessorService>();

builder.Services.AddControllers().AddJsonOptions(options =>
{
    // Enums travel as readable strings ("Critical", "TurnOn") instead of numbers.
    options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy => policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader());
});

var app = builder.Build();

app.UseCors();

// Every /api request must carry a valid Firebase ID token (see FirebaseAuthMiddleware).
app.UseMiddleware<FirebaseAuthMiddleware>();

app.MapControllers();

// Public status page so opening the URL in a browser confirms the API is alive.
app.MapGet("/", () => Results.Ok(new
{
    service = "Smart-X Gateway API",
    status = "running",
    note = "All /api endpoints require a Firebase bearer token.",
    endpoints = new[]
    {
        "GET  /api/stream/snapshot",
        "GET  /api/devices?search=&category=&state=",
        "POST /api/devices/import        (multipart .txt)",
        "POST /api/sensors/register",
        "POST /api/sensors/{mac}/upload  (multipart file)",
        "POST /api/telemetry/ingest",
        "POST /api/commands | /api/commands/undo | GET /api/commands/history",
        "GET  /api/alerts | POST /api/alerts/disconnect/{mac}",
        "GET  /api/insights | POST /api/insights/search"
    }
}));

app.Run();
