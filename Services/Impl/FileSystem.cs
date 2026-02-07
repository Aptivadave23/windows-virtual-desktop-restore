using System.IO;
using StartUp.Services.Abstractions;

namespace StartUp.Services.Impl;

internal sealed class FileSystem : IFileSystem
{
    public bool FileExists(string path) => File.Exists(path);
    public bool DirectoryExists(string path) => Directory.Exists(path);
    public string ReadAllText(string path) => File.ReadAllText(path);
    public void WriteAllText(string path, string content, bool append = false)
    {
        if (append) File.AppendAllText(path, content);
        else File.WriteAllText(path, content);
    }
    public void Copy(string source, string dest, bool overwrite = false) => File.Copy(source, dest, overwrite);
    public void CreateDirectory(string path) => Directory.CreateDirectory(path);
}
