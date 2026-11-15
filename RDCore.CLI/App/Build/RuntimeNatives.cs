using System.IO.Abstractions;
using System.Text.Json;

namespace RDCore.CLI.App.Build;

// the native libraries a built program's assemblies call, as rdc's deps.json lists them: a program has no deps.json of its own, and .NET finds a native library
// of an application beside it, so its folder holds each one, by its file name. A publish of rdc for a runtime identifier lists those of that identifier only
// (its target's "native" assets, beside rdc); a portable rdc lists each identifier's under runtimes/ ("runtimeTargets").
internal sealed class RuntimeNatives(IFileSystem fileSystem, string runtimeFolder)
{
    private const string DepsFile = "rdc.deps.json";

    // the native libraries for the runtime identifier, of the packages and projects that ship any of the assemblies and of those they depend on, transitively:
    // the path of each in the runtime folder, and its file name. None when the runtime folder has no deps.json, or none for that identifier.
    public IReadOnlyList<(string Source, string Name)> Of(IReadOnlyCollection<string> assemblies, string runtime)
    {
        var path = fileSystem.Path.Combine(runtimeFolder, DepsFile);
        if (!fileSystem.File.Exists(path))
        {
            return [];
        }

        using var deps = JsonDocument.Parse(fileSystem.File.ReadAllText(path));
        if (!deps.RootElement.TryGetProperty("runtimeTarget", out var runtimeTarget) || runtimeTarget.GetProperty("name").GetString() is not { } name
            || !deps.RootElement.GetProperty("targets").TryGetProperty(name, out var target))
        {
            return [];
        }

        var publishedFor = name.Split('/') is [_, var identifier] ? identifier : null;
        var held = new HashSet<string>(assemblies, StringComparer.OrdinalIgnoreCase);
        var entries = target.EnumerateObject().ToDictionary(library => library.Name, library => library.Value, StringComparer.OrdinalIgnoreCase);
        var pending = new Queue<string>(entries.Where(library => Assets(library.Value, "runtime").Any(asset => held.Contains(Path.GetFileNameWithoutExtension(asset.Path))))
            .Select(library => library.Key));
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var natives = new List<(string Source, string Name)>();
        while (pending.TryDequeue(out var key))
        {
            if (!seen.Add(key) || !entries.TryGetValue(key, out var library))
            {
                continue;
            }

            if (string.Equals(publishedFor, runtime, StringComparison.OrdinalIgnoreCase))
            {
                natives.AddRange(Assets(library, "native").Select(asset => Native(asset.LocalPath)));
            }

            natives.AddRange(Assets(library, "runtimeTargets")
                .Where(asset => asset.Value.TryGetProperty("assetType", out var kind) && kind.GetString() == "native"
                    && asset.Value.TryGetProperty("rid", out var rid) && string.Equals(rid.GetString(), runtime, StringComparison.OrdinalIgnoreCase))
                .Select(asset => Native(asset.LocalPath)));

            if (library.TryGetProperty("dependencies", out var dependencies))
            {
                foreach (var dependency in dependencies.EnumerateObject())
                {
                    pending.Enqueue($"{dependency.Name}/{dependency.Value.GetString()}");
                }
            }
        }

        return [.. natives.DistinctBy(native => native.Name, StringComparer.OrdinalIgnoreCase)];
    }

    private (string Source, string Name) Native(string relative) => (fileSystem.Path.Combine(runtimeFolder, relative), fileSystem.Path.GetFileName(relative));

    // the assets of a kind a library lists: each one's path in its package, its path in the runtime folder (localPath, or that same path when it gives none),
    // and what is said of it.
    private static IEnumerable<(string Path, string LocalPath, JsonElement Value)> Assets(JsonElement library, string kind)
        => library.TryGetProperty(kind, out var assets)
            ? assets.EnumerateObject().Select(asset => (asset.Name,
                asset.Value.TryGetProperty("localPath", out var local) && local.GetString() is { } localPath ? localPath : asset.Name, asset.Value))
            : [];
}
