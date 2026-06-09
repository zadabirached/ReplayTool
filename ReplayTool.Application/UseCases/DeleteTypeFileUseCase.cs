using ReplayTool.Application.Interfaces;
using ReplayTool.Domain.Entities;

namespace ReplayTool.Application.UseCases;

public class DeleteTypeFileUseCase
{
    private readonly IFileStorage _storage;
    private readonly string _storageRoot;

    public DeleteTypeFileUseCase(IFileStorage storage, string storageRoot)
    {
        _storage = storage;
        _storageRoot = storageRoot;
    }

    // null = case not found, false = file not found, true = deleted
    public async Task<bool?> ExecuteAsync(Guid caseId, string type)
    {
        if (!CaseFileType.IsAllowed(type))
            throw new ArgumentException(
                $"Unknown type '{type}'. Allowed: {string.Join(", ", CaseFileType.All)}");

        var result = await CaseStore.ReadAsync(_storage, _storageRoot, caseId);
        if (result is null) return null;

        var (@case, folder) = result.Value;

        if (@case.Status == CaseStatus.Running)
            throw new InvalidOperationException("Cannot delete files while a run is in progress.");

        var filePath = Path.Combine(folder, CaseFileType.Filename(type));
        if (!await _storage.FileExistsAsync(filePath))
            return false;

        await _storage.DeleteFileAsync(filePath);
        return true;
    }
}
