using System.Text.Json.Serialization;
using MassTransit;
using ReplayTool.Application;
using ReplayTool.Application.Interfaces;
using ReplayTool.Application.UseCases;
using ReplayTool.API.Workers;
using ReplayTool.Domain.Events;
using ReplayTool.Infrastructure.Database;
using ReplayTool.Infrastructure.Messaging;
using ReplayTool.Infrastructure.Storage;

var builder = WebApplication.CreateBuilder(args);

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
    options.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
});

var storageRoot = builder.Configuration["STORAGE_ROOT"] ?? "./cases";
var jobServiceDb = builder.Configuration["JOBSERVICE_DB"]
    ?? "Host=localhost;Database=jobservice;Username=postgres;Password=postgres";
var allowRemoteDb = bool.TryParse(builder.Configuration["REPLAY_ALLOW_REMOTE_DB"], out var remoteFlag) && remoteFlag;
var rabbitHost = builder.Configuration["RabbitMQ:Host"] ?? "localhost";

// SAFETY: the tool writes directly to a DB and publishes to a broker, so both must
// default to LOCAL and refuse a non-local target unless explicitly overridden — a
// captured prod scenario must never be replayed into a real environment.
if (!allowRemoteDb && !LocalTargetGuard.IsLocalHost(rabbitHost))
    throw new InvalidOperationException(
        $"The configured RabbitMQ host '{rabbitHost}' is not local. " +
        "Set REPLAY_ALLOW_REMOTE_DB=true to override this safety guard.");

builder.Services.AddScoped<IFileStorage, LocalFileStorage>();
builder.Services.AddScoped<IJobServiceRepository>(_ => new JobServiceRepository(jobServiceDb));
builder.Services.AddScoped(sp => new CreateCaseUseCase(sp.GetRequiredService<IFileStorage>(), storageRoot));
builder.Services.AddScoped(sp => new GetCaseUseCase(sp.GetRequiredService<IFileStorage>(), storageRoot));
builder.Services.AddScoped(sp => new ListCasesUseCase(sp.GetRequiredService<IFileStorage>(), storageRoot));
builder.Services.AddScoped(sp => new UploadTypeFileUseCase(sp.GetRequiredService<IFileStorage>(), storageRoot));
builder.Services.AddScoped(sp => new ListTypeFilesUseCase(sp.GetRequiredService<IFileStorage>(), storageRoot));
builder.Services.AddScoped(sp => new DeleteTypeFileUseCase(sp.GetRequiredService<IFileStorage>(), storageRoot));
builder.Services.AddScoped(sp => new ParseTypeFilesUseCase(sp.GetRequiredService<IFileStorage>(), storageRoot));
builder.Services.AddScoped(sp => new InsertCustomerOrdersUseCase(
    sp.GetRequiredService<IFileStorage>(), storageRoot,
    sp.GetRequiredService<IJobServiceRepository>(),
    jobServiceDb, allowRemoteDb));

// MassTransit + RabbitMQ (publish-only, no consumers in ReplayTool)
builder.Services.AddMassTransit(x =>
{
    x.UsingRabbitMq((ctx, cfg) =>
    {
        var rabbitPort = builder.Configuration.GetValue<ushort>("RabbitMQ:Port", 5672);
        cfg.Host(rabbitHost, rabbitPort, "/", h =>
        {
            h.Username(builder.Configuration["RabbitMQ:Username"] ?? "guest");
            h.Password(builder.Configuration["RabbitMQ:Password"] ?? "guest");
        });
        cfg.Message<OrdersRoutingEventV2>(m => m.SetEntityName("routingResponses/v2"));
        cfg.Publish<OrdersRoutingEventV2>(p => p.ExchangeType = "fanout");
        cfg.Message<AssignmentSolutionV2Event>(m => m.SetEntityName("anytask/solution/v2"));
        cfg.Publish<AssignmentSolutionV2Event>(p => p.ExchangeType = "fanout");
    });
});

builder.Services.AddSingleton<RunQueue>();
builder.Services.AddScoped<IMessagePublisher, RabbitMqPublisher>();
builder.Services.AddScoped(sp => new TriggerRunUseCase(
    sp.GetRequiredService<IFileStorage>(), storageRoot,
    sp.GetRequiredService<RunQueue>()));
builder.Services.AddScoped(sp => new RunExecutionService(
    sp.GetRequiredService<IFileStorage>(), storageRoot,
    sp.GetRequiredService<IJobServiceRepository>(),
    jobServiceDb, allowRemoteDb,
    sp.GetRequiredService<IMessagePublisher>()));
builder.Services.AddScoped(sp => new GetRunUseCase(sp.GetRequiredService<IFileStorage>(), storageRoot));
builder.Services.AddScoped(sp => new ListRunsUseCase(sp.GetRequiredService<IFileStorage>(), storageRoot));
builder.Services.AddScoped(sp => new RetryRunUseCase(
    sp.GetRequiredService<IFileStorage>(), storageRoot,
    sp.GetRequiredService<RunQueue>()));
builder.Services.AddScoped(sp => new RunRecoveryService(sp.GetRequiredService<IFileStorage>(), storageRoot));
builder.Services.AddHostedService<RunWorker>();

var app = builder.Build();

Directory.CreateDirectory(storageRoot);

// Health
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

// Cases
app.MapPost("/cases", async (CreateCaseRequest req, CreateCaseUseCase useCase) =>
{
    if (string.IsNullOrWhiteSpace(req.Name))
        return Results.BadRequest(new { error = "name is required" });

    try
    {
        var @case = await useCase.ExecuteAsync(req.Name, req.Description);
        return Results.Created($"/cases/{@case.Id}", @case);
    }
    catch
    {
        return Results.Problem(statusCode: 500);
    }
});

app.MapGet("/cases", async (ListCasesUseCase useCase) =>
    Results.Ok(await useCase.ExecuteAsync()));

app.MapGet("/cases/{id:guid}", async (Guid id, GetCaseUseCase useCase) =>
{
    var @case = await useCase.ExecuteAsync(id);
    return @case is null ? Results.NotFound() : Results.Ok(@case);
});

// Type files
app.MapPut("/cases/{id:guid}/files/{type}", async (Guid id, string type, HttpRequest req, UploadTypeFileUseCase useCase) =>
{
    using var reader = new StreamReader(req.Body);
    var content = await reader.ReadToEndAsync();

    if (string.IsNullOrWhiteSpace(content))
        return Results.BadRequest(new { error = "Request body is required." });

    try
    {
        var result = await useCase.ExecuteAsync(id, type, content);
        return result is null ? Results.NotFound() : Results.Ok(result);
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
    catch (InvalidOperationException ex)
    {
        return Results.Conflict(new { error = ex.Message });
    }
});

app.MapGet("/cases/{id:guid}/files", async (Guid id, ListTypeFilesUseCase useCase) =>
{
    var types = await useCase.ExecuteAsync(id);
    return types is null ? Results.NotFound() : Results.Ok(new { uploadedTypes = types });
});

app.MapDelete("/cases/{id:guid}/files/{type}", async (Guid id, string type, DeleteTypeFileUseCase useCase) =>
{
    try
    {
        var found = await useCase.ExecuteAsync(id, type);
        if (found is null) return Results.NotFound(new { error = "Case not found." });
        if (!found.Value) return Results.NotFound(new { error = "File not found." });
        return Results.NoContent();
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
    catch (InvalidOperationException ex)
    {
        return Results.Conflict(new { error = ex.Message });
    }
});

app.MapPost("/cases/{id:guid}/seed", async (Guid id, InsertCustomerOrdersUseCase useCase) =>
{
    try
    {
        var steps = await useCase.ExecuteAsync(id);
        return steps is null ? Results.NotFound() : Results.Ok(new { steps });
    }
    catch (InvalidOperationException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

app.MapGet("/cases/{id:guid}/parse", async (Guid id, ParseTypeFilesUseCase useCase) =>
{
    var result = await useCase.ExecuteAsync(id);
    return result is null ? Results.NotFound() : Results.Ok(result);
});

app.MapGet("/cases/{id:guid}/timeline", async (Guid id, ParseTypeFilesUseCase parseUseCase) =>
{
    var parsed = await parseUseCase.ExecuteAsync(id);
    if (parsed is null) return Results.NotFound();

    var timeline = new BuildTimelineUseCase().Execute(
        parsed.RoutingResponses?.Events,
        parsed.AssignmentSolutions?.Events);

    return Results.Ok(new { entries = timeline });
});

// Runs
app.MapPost("/cases/{id:guid}/runs", async (Guid id, TriggerRunRequest? req, TriggerRunUseCase useCase) =>
{
    req ??= new TriggerRunRequest();
    var run = await useCase.ExecuteAsync(id, req);
    if (run is null) return Results.NotFound();
    return run.DryRun
        ? Results.Ok(run)
        : Results.Accepted($"/cases/{id}/runs/{run.Id}", run);
});

app.MapGet("/cases/{id:guid}/runs", async (Guid id, ListRunsUseCase useCase) =>
{
    var runs = await useCase.ExecuteAsync(id);
    return runs is null ? Results.NotFound() : Results.Ok(new { runs });
});

app.MapGet("/cases/{id:guid}/runs/{runId:guid}", async (Guid id, Guid runId, GetRunUseCase useCase) =>
{
    var run = await useCase.ExecuteAsync(id, runId);
    return run is null ? Results.NotFound() : Results.Ok(run);
});

app.MapPost("/cases/{id:guid}/runs/{runId:guid}/retry", async (Guid id, Guid runId, RetryRunUseCase useCase) =>
{
    try
    {
        var run = await useCase.ExecuteAsync(id, runId);
        return run is null ? Results.NotFound() : Results.Accepted($"/cases/{id}/runs/{run.Id}", run);
    }
    catch (InvalidOperationException ex)
    {
        return Results.Conflict(new { error = ex.Message });
    }
});

app.Run();

record CreateCaseRequest(string? Name, string? Description);

public partial class Program { }
