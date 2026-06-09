using System.Text.Json.Serialization;
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
builder.Services.AddScoped(_ => new CreateCaseUseCase(
    new LocalFileStorage(),
    storageRoot));

var app = builder.Build();

Directory.CreateDirectory(storageRoot);

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.MapPost("/cases", async (CreateCaseRequest request, CreateCaseUseCase useCase) =>
{
    if (string.IsNullOrWhiteSpace(request.Name))
        return Results.BadRequest(new { error = "name is required" });

    try
    {
        var @case = await useCase.ExecuteAsync(request.Name, request.Description);
        return Results.Created($"/cases/{@case.Id}", @case);
    }
    catch
    {
        return Results.Problem(statusCode: 500);
    }
});

app.Run();

record CreateCaseRequest(string? Name, string? Description);
