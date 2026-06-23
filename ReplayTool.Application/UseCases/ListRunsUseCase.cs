using ReplayTool.Application.Interfaces;
using ReplayTool.Domain.Entities;

namespace ReplayTool.Application.UseCases;

public class ListRunsUseCase
{
    private readonly IFileStorage _storage;
    private readonly string _storageRoot;

    public ListRunsUseCase(IFileStorage storage, string storageRoot)
    {
        _storage = storage;
        _storageRoot = storageRoot;
    }

    // Returns null when the case is not found.
    public async Task<IReadOnlyList<Run>?> ExecuteAsync(Guid caseId)
    {
        var result = await CaseStore.ReadAsync(_storage, _storageRoot, caseId);
        if (result is null) return null;

        return await RunStore.ListAsync(_storage, result.Value.folder);
    }
}
