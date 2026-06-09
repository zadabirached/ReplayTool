using ReplayTool.Application.Interfaces;

namespace ReplayTool.Application.UseCases;

public class GetCaseUseCase
{
    private readonly IFileStorage _storage;
    private readonly string _storageRoot;

    public GetCaseUseCase(IFileStorage storage, string storageRoot)
    {
        _storage = storage;
        _storageRoot = storageRoot;
    }

    public async Task<CaseResponse?> ExecuteAsync(Guid id)
    {
        var result = await CaseStore.ReadAsync(_storage, _storageRoot, id);
        if (result is null) return null;

        var (c, folder) = result.Value;
        var types = await CaseStore.GetUploadedTypesAsync(_storage, folder);
        return CaseStore.ToResponse(c, types);
    }
}
