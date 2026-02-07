using System;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Text.Json;
using StartUp;
using StartUp.Services.Abstractions;

namespace StartUp.Services.Impl;

internal sealed class ConfigLoader : IConfigLoader
{
    private readonly IFileSystem _fs;

    public ConfigLoader(IFileSystem fs) => _fs = fs;

    public WorkspaceConfig Load(string path)
    {
        if (!_fs.FileExists(path))
            throw new FileNotFoundException($"Config file not found: {path}");

        var json = _fs.ReadAllText(path);
        var options = JsonSerializer.Deserialize<WorkspaceConfig>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            AllowTrailingCommas = true
        }) ?? throw new InvalidOperationException("Config file could not be parsed.");

        Validate(options);
        return options;
    }

    private static void Validate(WorkspaceConfig cfg)
    {
        var ctx = new ValidationContext(cfg);
        Validator.ValidateObject(cfg, ctx, validateAllProperties: true);

        if (cfg.Apps == null || cfg.Apps.Count == 0)
            throw new ValidationException("Config must contain at least one app.");
    }
}
