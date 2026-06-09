using System.Text.Json;
using System.Text.Json.Serialization;
using ReplayTool.Application.Interfaces;
using ReplayTool.Domain.Entities;

namespace ReplayTool.Application.UseCases;

public class CreateCaseUseCase
{
    private readonly IFileStorage _storage;
    private readonly string _storageRoot;

    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public CreateCaseUseCase(IFileStorage storage, string storageRoot)
    {
        _storage = storage;
        _storageRoot = storageRoot;
    }

    public async Task<Case> ExecuteAsync(string name, string? description)
    {
        var id = Guid.NewGuid();
        var caseFolder = Path.Combine(_storageRoot, id.ToString());

        var @case = new Case
        {
            Id = id,
            Name = name,
            Description = description,
            Status = CaseStatus.Draft,
            CreatedAt = DateTime.UtcNow,
        };

        try
        {
            await _storage.CreateDirectoryAsync(caseFolder);
            var json = JsonSerializer.Serialize(@case, JsonOptions);
            await _storage.WriteFileAsync(Path.Combine(caseFolder, "case.json"), json);
        }
        catch
        {
            if (await _storage.DirectoryExistsAsync(caseFolder))
                await _storage.DeleteDirectoryAsync(caseFolder);
            throw;
        }

        return @case;
    }
}
