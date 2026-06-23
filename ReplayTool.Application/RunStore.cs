using System.Text.Json;
using ReplayTool.Application.Interfaces;
using ReplayTool.Domain.Entities;

namespace ReplayTool.Application;

internal static class RunStore
{
    private static string RunsFolder(string caseFolder) => Path.Combine(caseFolder, "runs");
    private static string RunPath(string caseFolder, Guid runId) => Path.Combine(RunsFolder(caseFolder), $"{runId}.json");

    internal static async Task WriteAsync(IFileStorage storage, string caseFolder, Run run)
    {
        var folder = RunsFolder(caseFolder);
        if (!await storage.DirectoryExistsAsync(folder))
            await storage.CreateDirectoryAsync(folder);

        await storage.WriteFileAsync(RunPath(caseFolder, run.Id), JsonSerializer.Serialize(run, JsonConfig.Options));
    }

    internal static async Task<Run?> ReadAsync(IFileStorage storage, string caseFolder, Guid runId)
    {
        var path = RunPath(caseFolder, runId);
        if (!await storage.FileExistsAsync(path)) return null;
        return JsonSerializer.Deserialize<Run>(await storage.ReadFileAsync(path), JsonConfig.Options);
    }

    internal static async Task<IReadOnlyList<Run>> ListAsync(IFileStorage storage, string caseFolder)
    {
        var folder = RunsFolder(caseFolder);
        if (!await storage.DirectoryExistsAsync(folder)) return [];

        var runs = new List<Run>();
        foreach (var file in (await storage.ListFilesAsync(folder)).Where(f => f.EndsWith(".json")))
        {
            try
            {
                var run = JsonSerializer.Deserialize<Run>(await storage.ReadFileAsync(file), JsonConfig.Options);
                if (run is not null) runs.Add(run);
            }
            catch { /* skip corrupt run files */ }
        }

        return runs.OrderByDescending(r => r.CreatedAt).ToList();
    }
}
