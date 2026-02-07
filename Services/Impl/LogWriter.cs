using System;
using System.IO;
using System.Text.Json;
using StartUp.Services.Abstractions;

namespace StartUp.Services.Impl;

internal sealed class LogWriter : ILog
{
    private readonly string _logPath;
    private readonly object _lock = new();

    public LogWriter(string logPath)
    {
        _logPath = logPath;
        Directory.CreateDirectory(Path.GetDirectoryName(_logPath) ?? ".");
    }

    public void Info(string message, object? context = null) => Write("INFO", message, context, null);
    public void Warn(string message, object? context = null) => Write("WARN", message, context, null);
    public void Error(Exception ex, string message, object? context = null) => Write("ERROR", message, context, ex);

    private void Write(string level, string message, object? context, Exception? ex)
    {
        var line = new
        {
            ts = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
            level,
            message,
            context,
            exception = ex?.ToString()
        };

        var text = JsonSerializer.Serialize(line);
        lock (_lock)
        {
            File.AppendAllText(_logPath, text + Environment.NewLine);
        }
    }
}
