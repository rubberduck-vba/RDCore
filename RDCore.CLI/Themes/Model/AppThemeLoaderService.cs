using Microsoft.Extensions.Options;
using RDCore.CLI.Themes.Model.Serialization;
using System.Collections.Immutable;
using System.IO.Abstractions;
using System.Text.Json;

namespace RDCore.CLI.Themes.Model;

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