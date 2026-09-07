using System.IO.Abstractions;
using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace RDCore.CLI.Themes;

/// <summary>Loads <see cref="ThemeDocument"/>s — the built-in default plus any on disk.</summary>
public interface IAppThemeLoaderService
{
    /// <summary>The theme compiled into the assembly (<c>rdc-default</c>). Always available.</summary>
    ThemeDocument LoadBuiltInDefault();

    /// <summary>Every <c>*.theme</c> under <paramref name="directory"/> that parses; empty if the folder is absent.</summary>
    Task<IReadOnlyList<ThemeDocument>> DiscoverAsync(string directory, CancellationToken token);
}

/// <inheritdoc/>
public sealed class AppThemeLoaderService(IFileSystem fileSystem) : IAppThemeLoaderService
{
    private static readonly JsonSerializerOptions _json = new() { PropertyNameCaseInsensitive = true };

    /// <inheritdoc/>
    public ThemeDocument LoadBuiltInDefault()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resource = assembly.GetManifestResourceNames().Single(name => name.EndsWith("rdc-default.theme", StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(resource)!;
        return JsonSerializer.Deserialize<ThemeDocument>(stream, _json) ?? new ThemeDocument { Name = "rdc-default" };
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<ThemeDocument>> DiscoverAsync(string directory, CancellationToken token)
    {
        if (!fileSystem.Directory.Exists(directory))
        {
            return [];
        }

        var documents = new List<ThemeDocument>();
        foreach (var file in fileSystem.Directory.EnumerateFiles(directory, "*.theme"))
        {
            try
            {
                var content = await fileSystem.File.ReadAllTextAsync(file, token);
                if (JsonSerializer.Deserialize<ThemeDocument>(content, _json) is ThemeDocument document)
                {
                    documents.Add(document);
                }
            }
            catch (Exception)
            {
                // a malformed theme file is skipped, not fatal.
            }
        }
        return documents;
    }
}

/// <summary>Holds the loaded themes and the current selection.</summary>
public interface IAppThemeService
{
    /// <summary>The active theme (the neutral built-in when theming is disabled or nothing loaded).</summary>
    AppTheme Theme { get; }

    /// <summary>The names of every loaded theme.</summary>
    IReadOnlyCollection<string> ThemeNames { get; }

    /// <summary>Selects a loaded theme by name. Returns <c>false</c> (and keeps the current theme) if the name is unknown.</summary>
    bool SetTheme(string name);

    /// <summary>Loads the built-in default and any themes under the configured discovery path.</summary>
    Task InitializeAsync(CancellationToken token);
}

/// <inheritdoc/>
public sealed class AppThemeService(IOptions<AppOptions> options, IAppThemeLoaderService loader) : IAppThemeService
{
    private readonly Dictionary<string, AppTheme> _themes = new(StringComparer.OrdinalIgnoreCase);
    private string _selection = "rdc-default";

    /// <inheritdoc/>
    public IReadOnlyCollection<string> ThemeNames => _themes.Keys;

    /// <inheritdoc/>
    public AppTheme Theme
        => options.Value.ThemesEnabled && _themes.TryGetValue(_selection, out var theme) ? theme : AppTheme.Neutral;

    /// <inheritdoc/>
    public bool SetTheme(string name)
    {
        if (!_themes.ContainsKey(name))
        {
            return false;
        }
        _selection = name;
        return true;
    }

    /// <inheritdoc/>
    public async Task InitializeAsync(CancellationToken token)
    {
        if (!options.Value.ThemesEnabled)
        {
            return;
        }

        Add(loader.LoadBuiltInDefault());
        foreach (var document in await loader.DiscoverAsync(options.Value.ThemesDiscoveryPath, token))
        {
            Add(document);
        }

        _selection = _themes.ContainsKey(options.Value.Theme) ? options.Value.Theme : "rdc-default";
    }

    private void Add(ThemeDocument document) => _themes[document.Name] = new AppTheme(document);
}
