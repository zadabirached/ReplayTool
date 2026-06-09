using ReplayTool.Application.Interfaces;

namespace ReplayTool.Application.UseCases;

public class ListTypeFilesUseCase
{
    private readonly IFileStorage _storage;
    private readonly string _storageRoot;

    public ListTypeFilesUseCase(IFileStorage storage, string storageRoot)
    {
        _storage = storage;
        _storageRoot = storageRoot;
    }

    public async Task<IReadOnlyList<string>?> ExecuteAsync(Guid caseId)
    {
        var result = await CaseStore.ReadAsync(_storage, _storageRoot, caseId);
        if (result is null) return null;

        return await CaseStore.GetUploadedTypesAsync(_storage, result.Value.folder);
    }
}
