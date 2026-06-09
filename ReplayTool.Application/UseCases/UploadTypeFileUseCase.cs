using System.Text.Json;
using ReplayTool.Application.Interfaces;
using ReplayTool.Domain.Entities;

namespace ReplayTool.Application.UseCases;

public class UploadTypeFileUseCase
{
    private readonly IFileStorage _storage;
    private readonly string _storageRoot;

    public UploadTypeFileUseCase(IFileStorage storage, string storageRoot)
    {
        _storage = storage;
        _storageRoot = storageRoot;
    }

    public async Task<CaseResponse?> ExecuteAsync(Guid caseId, string type, string content)
    {
        if (!CaseFileType.IsAllowed(type))
            throw new ArgumentException(
                $"Unknown type '{type}'. Allowed: {string.Join(", ", CaseFileType.All)}");

        var result = await CaseStore.ReadAsync(_storage, _storageRoot, caseId);
        if (result is null) return null;

        var (@case, folder) = result.Value;

        if (@case.Status == CaseStatus.Running)
            throw new InvalidOperationException("Cannot upload files while a run is in progress.");

        try { JsonDocument.Parse(content); }
        catch (JsonException ex) { throw new ArgumentException("Content is not valid JSON.", ex); }

        await _storage.WriteFileAsync(Path.Combine(folder, CaseFileType.Filename(type)), content);

        if (@case.Status == CaseStatus.Draft)
        {
            @case = @case with { Status = CaseStatus.Ready };
            await CaseStore.WriteAsync(_storage, folder, @case);
        }

        var types = await CaseStore.GetUploadedTypesAsync(_storage, folder);
        return CaseStore.ToResponse(@case, types);
    }
}
