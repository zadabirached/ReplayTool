using System.Text.Json.Serialization;
using ReplayTool.Application;
using ReplayTool.Application.Interfaces;
using ReplayTool.Application.UseCases;
using ReplayTool.Infrastructure.Storage;

var builder = WebApplication.CreateBuilder(args);

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
    options.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
});

var storageRoot = builder.Configuration["STORAGE_ROOT"] ?? "./cases";
builder.Services.AddScoped<IFileStorage, LocalFileStorage>();
builder.Services.AddScoped(sp => new CreateCaseUseCase(sp.GetRequiredService<IFileStorage>(), storageRoot));
builder.Services.AddScoped(sp => new GetCaseUseCase(sp.GetRequiredService<IFileStorage>(), storageRoot));
builder.Services.AddScoped(sp => new ListCasesUseCase(sp.GetRequiredService<IFileStorage>(), storageRoot));
builder.Services.AddScoped(sp => new UploadTypeFileUseCase(sp.GetRequiredService<IFileStorage>(), storageRoot));
builder.Services.AddScoped(sp => new ListTypeFilesUseCase(sp.GetRequiredService<IFileStorage>(), storageRoot));
builder.Services.AddScoped(sp => new DeleteTypeFileUseCase(sp.GetRequiredService<IFileStorage>(), storageRoot));
builder.Services.AddScoped(sp => new ParseTypeFilesUseCase(sp.GetRequiredService<IFileStorage>(), storageRoot));

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

app.MapGet("/cases/{id:guid}/parse", async (Guid id, ParseTypeFilesUseCase useCase) =>
{
    var result = await useCase.ExecuteAsync(id);
    return result is null ? Results.NotFound() : Results.Ok(result);
});

app.Run();

record CreateCaseRequest(string? Name, string? Description);
