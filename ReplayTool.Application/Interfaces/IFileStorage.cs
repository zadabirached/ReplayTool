namespace ReplayTool.Application.Interfaces;

public interface IFileStorage
{
    Task CreateDirectoryAsync(string path);
    Task WriteFileAsync(string path, string content);
    Task<string> ReadFileAsync(string path);
    Task DeleteFileAsync(string path);
    Task DeleteDirectoryAsync(string path);
    Task<bool> DirectoryExistsAsync(string path);
    Task<bool> FileExistsAsync(string path);
    Task<IEnumerable<string>> ListDirectoriesAsync(string path);
    Task<IEnumerable<string>> ListFilesAsync(string path);
}
