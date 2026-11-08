using System.Net.WebSockets;
using System.Text.Json;
using BalloonMesh.Server.Data;
using BalloonMesh.Server.Hubs;
using BalloonMesh.Server.Models;
using BalloonMesh.Server.Services;
using SvelteSharp;
using SvelteSharp.AspNetCore;
using SvelteSharp.Jint;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddSignalR()
    .AddJsonProtocol(options =>
        options.PayloadSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase);

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<DataStore>();
builder.Services.AddSingleton<FleetService>();
builder.Services.AddSingleton<PositionSolver>();
builder.Services.AddSingleton<NeighborStore>();
builder.Services.AddSingleton<GatewayRegistry>();
builder.Services.AddSingleton<MonitorNotifier>();
builder.Services.AddSingleton<IMonitorEvents>(serviceProvider => serviceProvider.GetRequiredService<MonitorNotifier>());
builder.Services.AddSingleton<FtmAggregator>();
builder.Services.AddSingleton<MeshReportRouter>();
builder.Services.AddSingleton<LedDispatchService>();
builder.Services.AddSingleton<CommandService>();
builder.Services.AddSingleton<SimulationService>();
builder.Services.AddHostedService(serviceProvider => serviceProvider.GetRequiredService<SimulationService>());
builder.Services.AddHostedService<AggregationWorker>();
builder.Services.AddHostedService<ShowRecoveryWorker>();
builder.Services.AddHostedService<LedDispatchWorker>();

var svelte = builder.Services.AddSvelteSharp(options =>
{
    options.ViewsPath = "Views";
});
svelte.UseJintForCompiler()
      .UseJintForSsr();

var app = builder.Build();

var dataStore = app.Services.GetRequiredService<DataStore>();
dataStore.Initialize();
dataStore.ResetRuntimeQueueState();

app.UseWebSockets(new WebSocketOptions
{
    KeepAliveInterval = TimeSpan.FromSeconds(30)
});

app.MapSvelteSharpAssets();
app.MapHub<MonitorHub>("/hubs/monitor");

app.Map("/ws/gateway", async context =>
{
    if (!context.WebSockets.IsWebSocketRequest)
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        await context.Response.WriteAsync("WebSocket upgrade required.");
        return;
    }

    using var socket = await context.WebSockets.AcceptWebSocketAsync();
    var registry = context.RequestServices.GetRequiredService<GatewayRegistry>();
    var router = context.RequestServices.GetRequiredService<MeshReportRouter>();
    var logger = context.RequestServices.GetRequiredService<ILoggerFactory>()
        .CreateLogger("GatewayWebSocket");
    await GatewayWebSocket.HandleAsync(socket, context, registry, router, logger,
        context.RequestAborted);
});

app.MapGet("/", () => Svelte.View(
    "Home",
    new HomePageModel(
        "Balloon Mesh",
        ServerInfo.Version,
        "ESP32-C3 RGB balloon fleet — live 2D map, FTM positioning and LED scheduling"),
    SvelteRenderMode.Client,
    new PageMetadata("Balloon Mesh", new Dictionary<string, string>
    {
        ["viewport"] = "width=device-width, initial-scale=1"
    })));

app.MapGet("/favicon.ico", () => Results.NoContent());

app.MapGet("/api/health", () => Results.Ok(new
{
    status = "ok",
    version = ServerInfo.Version,
    time = DateTimeOffset.UtcNow
}));

app.MapGet("/api/state", (FleetService fleet, GatewayRegistry gateways, SimulationService simulation) =>
    Results.Ok(fleet.GetState(gateways.Snapshot(), simulation.Running, ServerInfo.Version)));

app.MapGet("/api/balloons", (FleetService fleet) => Results.Ok(fleet.GetBalloons()));

app.MapGet("/api/balloons/{address}", (string address, FleetService fleet) =>
{
    if (!TryParseAddress(address, out var meshAddress))
    {
        return Results.BadRequest(new { error = "Invalid balloon address." });
    }
    var balloon = fleet.GetBalloon(meshAddress);
    return balloon is null ? Results.NotFound() : Results.Ok(balloon);
});

app.MapGet("/api/balloons/{address}/history", (string address, int? limit, FleetService fleet) =>
{
    if (!TryParseAddress(address, out var meshAddress))
    {
        return Results.BadRequest(new { error = "Invalid balloon address." });
    }
    return Results.Ok(fleet.GetHistory(meshAddress, limit ?? 100));
});

app.MapPost("/api/balloons/{address}/initial-position",
    (string address, InitialPositionRequest request, FleetService fleet) =>
    {
        if (!TryParseAddress(address, out var meshAddress))
        {
            return Results.BadRequest(new { error = "Invalid balloon address." });
        }
        try
        {
            return Results.Ok(fleet.SetInitialPosition(meshAddress, request.Lat, request.Lon));
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            return Results.BadRequest(new { error = exception.Message });
        }
    });

app.MapPost("/api/groups", (CreateGroupRequest request, FleetService fleet) =>
{
    try
    {
        return Results.Ok(fleet.CreateGroup(request.Name));
    }
    catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
    {
        return Results.BadRequest(new { error = exception.Message });
    }
});

app.MapDelete("/api/groups/{id:int}", (int id, FleetService fleet) =>
    fleet.DeleteGroup(id)
        ? Results.NoContent()
        : Results.BadRequest(new { error = "Group is not empty, is the all-balloons group, or does not exist." }));

app.MapPost("/api/balloons/{address}/group",
    (string address, AssignGroupRequest request, FleetService fleet) =>
    {
        if (!TryParseAddress(address, out var meshAddress))
        {
            return Results.BadRequest(new { error = "Invalid balloon address." });
        }
        try
        {
            return Results.Ok(fleet.AssignBalloonToGroup(meshAddress, request.GroupId));
        }
        catch (ArgumentException exception)
        {
            return Results.BadRequest(new { error = exception.Message });
        }
    });

app.MapGet("/api/anchors", (FleetService fleet) => Results.Ok(fleet.GetAnchors()));

app.MapPut("/api/anchors/{id:int}", (int id, AnchorUpdateRequest request, FleetService fleet) =>
{
    if (id is < 1 or > 255)
    {
        return Results.BadRequest(new { error = "Anchor id must be 1..255." });
    }
    try
    {
        return Results.Ok(fleet.UpsertAnchor((byte)id, request.Name ?? string.Empty,
            request.Lat, request.Lon, request.AltM, request.TimeValid, request.PositionValid));
    }
    catch (ArgumentException exception)
    {
        return Results.BadRequest(new { error = exception.Message });
    }
});

app.MapGet("/api/groups", (FleetService fleet) => Results.Ok(fleet.GetGroups()));

app.MapGet("/api/gateways", (GatewayRegistry gateways) => Results.Ok(gateways.Snapshot()));

app.MapGet("/api/show", (DataStore store, int? limit) =>
    Results.Ok(store.GetShowSteps(limit ?? 50)));

app.MapDelete("/api/show/{id:long}", async (long id, CommandService service, CancellationToken cancellationToken) =>
{
    var result = await service.CancelShowStepAsync(id, cancellationToken);
    return result is null ? Results.NotFound() : Results.Ok(result);
});

app.MapPost("/api/commands/led-set", async (LedSetRequest request, CommandService service,
    CancellationToken cancellationToken) =>
{
    try
    {
        return Results.Ok(await service.ScheduleAsync(request, cancellationToken));
    }
    catch (ArgumentException exception)
    {
        return Results.BadRequest(new { error = exception.Message });
    }
});

app.MapPost("/api/commands/now", async (LedNowRequest request, CommandService service,
    CancellationToken cancellationToken) =>
{
    try
    {
        return Results.Ok(await service.SetNowAsync(request, cancellationToken));
    }
    catch (ArgumentException exception)
    {
        return Results.BadRequest(new { error = exception.Message });
    }
});

app.MapPost("/api/commands/cancel", async (LedCancelRequest request, CommandService service,
    CancellationToken cancellationToken) =>
{
    try
    {
        return Results.Ok(await service.CancelAsync(request, cancellationToken));
    }
    catch (ArgumentException exception)
    {
        return Results.BadRequest(new { error = exception.Message });
    }
});

app.MapPost("/api/commands/reset", async (LedResetRequest request, CommandService service,
    CancellationToken cancellationToken) =>
{
    try
    {
        return Results.Ok(await service.ResetAsync(request, cancellationToken));
    }
    catch (ArgumentException exception)
    {
        return Results.BadRequest(new { error = exception.Message });
    }
});

app.MapPost("/api/demo/start", async (SimulationService simulation, CancellationToken cancellationToken) =>
{
    await simulation.SetRunningAsync(true, cancellationToken);
    return Results.Ok(new { running = true });
});

app.MapPost("/api/demo/stop", async (SimulationService simulation, CancellationToken cancellationToken) =>
{
    await simulation.SetRunningAsync(false, cancellationToken);
    return Results.Ok(new { running = false });
});

app.MapPost("/api/gateway/{id}/events",
    (string id, GatewayEventsRequest request, GatewayRegistry registry, MeshReportRouter router) =>
    {
        registry.Touch(id, request.Firmware);
        var accepted = 0;
        foreach (var message in request.Events)
        {
            accepted += router.Handle(message, id);
        }
        return Results.Accepted(value: new { accepted });
    });

app.MapGet("/api/gateway/{id}/commands",
    async (string id, int? waitMs, GatewayRegistry registry, CancellationToken cancellationToken) =>
    {
        registry.Touch(id);
        var wait = Math.Clamp(waitMs ?? 10_000, 0, 30_000);
        var first = await registry.ReadCommandAsync(id, wait, cancellationToken);
        if (first is null)
        {
            return Results.NoContent();
        }

        var commands = new List<GatewayCommandDto> { first.ToDto() };
        while (commands.Count < 32 && registry.TryReadCommand(id, out var next) && next is not null)
        {
            commands.Add(next.ToDto());
        }
        return Results.Ok(new GatewayCommandsRequest(commands));
    });

app.Run();

static bool TryParseAddress(string text, out ushort address)
{
    if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
    {
        return ushort.TryParse(text[2..], System.Globalization.NumberStyles.HexNumber,
            System.Globalization.CultureInfo.InvariantCulture, out address);
    }
    return ushort.TryParse(text, out address);
}

public partial class Program;
