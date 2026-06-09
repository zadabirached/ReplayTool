using System.Text.Json;
using ReplayTool.Application.Interfaces;
using ReplayTool.Domain.Entities;

namespace ReplayTool.Application.UseCases;

public class ListCasesUseCase
{
    private readonly IFileStorage _storage;
    private readonly string _storageRoot;

    public ListCasesUseCase(IFileStorage storage, string storageRoot)
    {
        _storage = storage;
        _storageRoot = storageRoot;
    }

    public async Task<IReadOnlyList<CaseResponse>> ExecuteAsync()
    {
        if (!await _storage.DirectoryExistsAsync(_storageRoot))
            return [];

        var directories = await _storage.ListDirectoriesAsync(_storageRoot);
        var results = new List<CaseResponse>();

        foreach (var dir in directories)
        {
            var caseJsonPath = Path.Combine(dir, "case.json");
            if (!await _storage.FileExistsAsync(caseJsonPath))
                continue;

            try
            {
                var json = await _storage.ReadFileAsync(caseJsonPath);
                var @case = JsonSerializer.Deserialize<Case>(json, JsonConfig.Options);
                if (@case is null) continue;

                var types = await CaseStore.GetUploadedTypesAsync(_storage, dir);
                results.Add(CaseStore.ToResponse(@case, types));
            }
            catch
            {
                // skip malformed case folders
            }
        }

        return results;
    }
}
