using Microsoft.Extensions.Options;
using RDCore.CLI.Themes.Model.Serialization;
using System.Collections.Immutable;
using System.IO.Abstractions;
using System.Text.Json;

namespace RDCore.CLI.Themes.Model;

public interface IAppThemeLoaderService
{
    /// <summary>
    /// Discovers and loads all available themes.
    /// </summary>
    /// <param name="token">A <c>CancellationToken</c>.</param>
    Task<ImmutableArray<AppThemeModel>> DiscoverThemesAsync(CancellationToken token);
}

public class AppThemeLoaderService(IOptions<AppOptions> options, IFileSystem FileSystem) : IAppThemeLoaderService
{
    private readonly AppOptions _options = options.Value;
    private readonly IFileSystem _fileSystem = FileSystem;

    private readonly Dictionary<string, AppThemeModel> _themes = [];
    public ImmutableArray<AppThemeModel> Themes => [.. _themes.Values];

    public async Task<ImmutableArray<AppThemeModel>> DiscoverThemesAsync(CancellationToken token)
    {
        var themes = new Dictionary<string, AppThemeModel>();
        try
        {
            foreach (var file in _fileSystem.Directory.EnumerateFiles(_options.ThemesDiscoveryPath, "*.theme"))
            {
                if (await LoadJsonAsync(file, token) is AppThemeModel theme && !_themes.TryAdd(theme.Name, theme))
                {
                    // duplicate themes in folder
                }
            }
        }
        catch
        {
            if (themes.Count == 0)
            {
                var fallback = AppThemeModel.Default;
                themes.Add(fallback.Name, fallback);
            }
        }
        return [.. themes.Values];
    }

    private async Task<AppThemeModel?> LoadJsonAsync(string path, CancellationToken token)
    {
        var content = await _fileSystem.File.ReadAllTextAsync(path, token);
        return JsonSerializer.Deserialize<AppThemeModel>(content);
    }
}