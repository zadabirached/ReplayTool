using ReplayTool.Application.Interfaces;

namespace ReplayTool.Infrastructure.Storage;

public class LocalFileStorage : IFileStorage
{
    public Task CreateDirectoryAsync(string path)
    {
        Directory.CreateDirectory(path);
        return Task.CompletedTask;
    }

    public Task WriteFileAsync(string path, string content)
        => File.WriteAllTextAsync(path, content);

    public Task<string> ReadFileAsync(string path)
        => File.ReadAllTextAsync(path);

    public Task DeleteFileAsync(string path)
    {
        File.Delete(path);
        return Task.CompletedTask;
    }

    public Task DeleteDirectoryAsync(string path)
    {
        Directory.Delete(path, recursive: true);
        return Task.CompletedTask;
    }

    public Task<bool> DirectoryExistsAsync(string path)
        => Task.FromResult(Directory.Exists(path));

    public Task<bool> FileExistsAsync(string path)
        => Task.FromResult(File.Exists(path));

    public Task<IEnumerable<string>> ListDirectoriesAsync(string path)
        => Task.FromResult<IEnumerable<string>>(Directory.GetDirectories(path));

    public Task<IEnumerable<string>> ListFilesAsync(string path)
        => Task.FromResult<IEnumerable<string>>(Directory.GetFiles(path));
}
