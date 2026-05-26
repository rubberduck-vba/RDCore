using Microsoft.Extensions.Options;
using RDCore.CLI.Themes.Model.Serialization;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.IO.Abstractions;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RDCore.CLI.Themes.Model;

internal interface IAppThemeService
{
    /// <summary>
    /// Gets all available themes.
    /// </summary>
    ImmutableArray<AppThemeModel> Themes { get; }

    /// <summary>
    /// Gets the current theme.
    /// </summary>
    AppThemeModel Theme { get; }
}

internal class AppThemeService(IOptions<AppOptions> options, IAppThemeLoaderService loader) : IAppThemeService
{
    private Dictionary<string, AppThemeModel> _themes = [];
    public ImmutableArray<AppThemeModel> Themes => [.. _themes.Values];
    private string _selection = "rdc-default";

    public AppThemeModel Theme => _themes[_selection];


    public async Task InitializeAsync(CancellationToken token)
    {
        if (options.Value.ThemesEnabled)
        {
            _themes = (await loader.DiscoverThemesAsync(token)).ToDictionary(theme => theme.Name, theme => new AppThemeModel(theme));
        }
        else
        {
            // TODO log/output
        }
    }
}

internal class AppOptions
{
    [JsonPropertyName("themes-enabled")]
    public required bool ThemesEnabled { get; init; }
    [JsonPropertyName("themes-path")]
    public required string ThemesDiscoveryPath { get; init; }    
    [JsonPropertyName("theme")]
    public required string Theme { get; init; }
}

internal interface IAppThemeLoaderService
{
    /// <summary>
    /// Discovers and loads all available themes.
    /// </summary>
    /// <param name="token">A <c>CancellationToken</c>.</param>
    Task<ImmutableArray<AppTheme>> DiscoverThemesAsync(CancellationToken token);
}

internal class AppThemeLoaderService(IOptions<AppOptions> options, IFileSystem FileSystem) : IAppThemeLoaderService
{
    private readonly AppOptions _options = options.Value;
    private readonly IFileSystem _fileSystem = FileSystem;

    private readonly Dictionary<string, AppTheme> _themes = [];
    public ImmutableArray<AppTheme> Themes => [.. _themes.Values];

    public async Task<ImmutableArray<AppTheme>> DiscoverThemesAsync(CancellationToken token)
    {
        var themes = new Dictionary<string, AppTheme>();
        try
        {
            foreach (var file in _fileSystem.Directory.EnumerateFiles(_options.ThemesDiscoveryPath, "*.theme"))
            {
                if (await LoadJsonAsync(file, token) is AppTheme theme && !_themes.TryAdd(theme.Name, theme))
                {
                    // duplicate themes in folder
                }
            }
        }
        catch
        {
            if (themes.Count == 0)
            {
                var fallback = AppTheme.Default;
                themes.Add(fallback.Name, fallback);
            }
        }
        return [.. themes.Values];
    }

    private async Task<AppTheme?> LoadJsonAsync(string path, CancellationToken token)
    {
        var content = await _fileSystem.File.ReadAllTextAsync(path, token);
        return JsonSerializer.Deserialize<AppTheme>(content);
    }
}