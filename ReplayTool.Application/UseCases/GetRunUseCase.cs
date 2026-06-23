using ReplayTool.Application.Interfaces;
using ReplayTool.Domain.Entities;

namespace ReplayTool.Application.UseCases;

public class GetRunUseCase
{
    private readonly IFileStorage _storage;
    private readonly string _storageRoot;

    public GetRunUseCase(IFileStorage storage, string storageRoot)
    {
        _storage = storage;
        _storageRoot = storageRoot;
    }

    // Returns null when case or run is not found.
    public async Task<Run?> ExecuteAsync(Guid caseId, Guid runId)
    {
        var result = await CaseStore.ReadAsync(_storage, _storageRoot, caseId);
        if (result is null) return null;

        return await RunStore.ReadAsync(_storage, result.Value.folder, runId);
    }
}
