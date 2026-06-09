using System.Text.Json;
using ReplayTool.Application.Interfaces;
using ReplayTool.Domain.Entities;

namespace ReplayTool.Application;

internal static class CaseStore
{
    internal static async Task<(Case @case, string folder)?> ReadAsync(
        IFileStorage storage, string storageRoot, Guid id)
    {
        var folder = Path.Combine(storageRoot, id.ToString());
        if (!await storage.DirectoryExistsAsync(folder))
            return null;

        var json = await storage.ReadFileAsync(Path.Combine(folder, "case.json"));
        var @case = JsonSerializer.Deserialize<Case>(json, JsonConfig.Options)!;
        return (@case, folder);
    }

    internal static async Task WriteAsync(IFileStorage storage, string folder, Case @case)
    {
        var json = JsonSerializer.Serialize(@case, JsonConfig.Options);
        await storage.WriteFileAsync(Path.Combine(folder, "case.json"), json);
    }

    internal static async Task<IReadOnlyList<string>> GetUploadedTypesAsync(
        IFileStorage storage, string folder)
    {
        var present = new List<string>();
        foreach (var type in CaseFileType.All)
        {
            if (await storage.FileExistsAsync(Path.Combine(folder, CaseFileType.Filename(type))))
                present.Add(type);
        }
        return present;
    }

    internal static CaseResponse ToResponse(Case @case, IReadOnlyList<string> uploadedTypes) =>
        new(@case.Id, @case.Name, @case.Description, @case.Status, @case.CreatedAt, uploadedTypes);
}
