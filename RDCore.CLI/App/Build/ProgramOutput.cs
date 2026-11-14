using RDCore.Runtime.Support.Build;
using RDCore.SDK.Runtime.Libraries;
using System.IO.Abstractions;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace RDCore.CLI.App.Build;

// a built program's folder; the license terms it carries are those of the platform rdc runs from, whose root holds its runtime folder (ProgramNotices).
internal sealed class ProgramOutput(IFileSystem fileSystem, string runtimeFolder, string? platformRoot = null)
{
    private const string RdcName = "rdc";

    public string RuntimeFolder => runtimeFolder;

    public bool IsTaken(string program)
        => fileSystem.File.Exists(fileSystem.Path.Combine(runtimeFolder, $"{program}.dll")) || string.Equals(program, RdcName, StringComparison.OrdinalIgnoreCase);

    // the program's executable, or null when no host was given; and the license documents the folder lacks, having none to copy. A program whose project has
    // no Visual Basic 6.0 form (hostsForms) runs on nothing of RDForms: its folder holds none of it, nor what an earlier build of the folder copied of it.
    public (string? Executable, IReadOnlyList<string> Lacking) Write(string folder, string program, byte[] assembly, byte[] pdb, string? host, string runtime,
        IReadOnlyList<LibraryDescription> libraries, bool hostsForms)
    {
        _ = fileSystem.Directory.CreateDirectory(folder);
        var dll = $"{program}.dll";
        fileSystem.File.WriteAllBytes(fileSystem.Path.Combine(folder, dll), assembly);
        fileSystem.File.WriteAllBytes(fileSystem.Path.Combine(folder, $"{program}.pdb"), pdb);
        fileSystem.File.WriteAllText(fileSystem.Path.Combine(folder, $"{program}.runtimeconfig.json"), RuntimeConfiguration());
        string[] roots = [SupportName, .. ReferencesOf(assembly)];
        var closure = RuntimeClosure(roots, hostsForms ? [] : [BuiltProgram.FormsAssembly]);
        var natives = new RuntimeNatives(fileSystem, runtimeFolder).Of(closure, runtime);
        if (!hostsForms)
        {
            var forms = RuntimeClosure(roots).Except(closure, StringComparer.OrdinalIgnoreCase).ToArray();
            RemoveRuntime(folder, forms, [.. new RuntimeNatives(fileSystem, runtimeFolder).Of(forms, runtime)
                .ExceptBy(natives.Select(native => native.Name), native => native.Name, StringComparer.OrdinalIgnoreCase)]);
        }

        CopyRuntime(folder, closure, natives);
        WriteLibraries(fileSystem.Path.Combine(folder, BuiltProgram.LibrariesFolder), libraries);

        string? executable = null;
        if (host is not null)
        {
            executable = fileSystem.Path.Combine(folder, AppHost.ExecutableName(program, runtime));
            fileSystem.File.WriteAllBytes(executable, AppHost.Patch(fileSystem.File.ReadAllBytes(host), dll));
            if (!OperatingSystem.IsWindows() && fileSystem is FileSystem)
            {
                File.SetUnixFileMode(executable, File.GetUnixFileMode(executable) | UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute);
            }
        }

        var lacking = new ProgramNotices(fileSystem, PlatformRoot).Write(folder, program, closure, [.. natives.Select(native => native.Name)], host,
            executable is null ? null : fileSystem.Path.GetFileName(executable), runtime);
        return (executable, lacking);
    }

    // the libraries the program's project references, and those they depend on, described as the platform describes them, one file each by its name: what the program's session
    // reads in place of the platform's. A library an earlier build of the folder wrote, and that the project no longer references, is not left there.
    private void WriteLibraries(string symbols, IReadOnlyList<LibraryDescription> libraries)
    {
        if (fileSystem.Directory.Exists(symbols))
        {
            foreach (var stale in fileSystem.Directory.EnumerateFiles(symbols, "*" + DirectoryLibrarySource.Extension).ToArray())
            {
                fileSystem.File.Delete(stale);
            }
        }

        if (libraries.Count == 0)
        {
            return;
        }

        _ = fileSystem.Directory.CreateDirectory(symbols);
        foreach (var library in libraries)
        {
            fileSystem.File.WriteAllText(fileSystem.Path.Combine(symbols, library.Name + DirectoryLibrarySource.Extension), LibraryJson.Write(library));
        }
    }

    // the folder of the platform's rdcore.json, as rdc install has it: the one that holds the runtime folder.
    private string PlatformRoot => platformRoot ?? fileSystem.Path.GetDirectoryName(fileSystem.Path.TrimEndingDirectorySeparator(runtimeFolder)) ?? runtimeFolder;

    // what .NET runs the program on: what it runs rdc on, as rdc's own runtime configuration says, so the runtime's assemblies find what they were built
    // against; the shared framework of the version rdc runs on when rdc has none.
    private string RuntimeConfiguration()
    {
        var rdc = fileSystem.Path.Combine(runtimeFolder, $"{RdcName}.runtimeconfig.json");
        if (fileSystem.File.Exists(rdc))
        {
            return fileSystem.File.ReadAllText(rdc);
        }

        var version = Environment.Version;
        return $$"""
            {
              "runtimeOptions": {
                "tfm": "net{{version.Major}}.{{version.Minor}}",
                "framework": {
                  "name": "Microsoft.NETCore.App",
                  "version": "{{version.Major}}.{{version.Minor}}.0"
                }
              }
            }
            """;
    }

    // the assembly a built program calls into, and the root of what it runs on.
    public static string SupportName { get; } = typeof(BuiltProgram).Assembly.GetName().Name!;

    // the named assemblies of the runtime folder and every one of it they reference, followed transitively without loading them: what the program loads
    // from beside itself. A reference to the shared framework is not in the folder, and is not followed; rdc itself, its parser and its console are not
    // referenced, and are not copied. An excluded assembly is neither in it nor followed.
    public IReadOnlyList<string> RuntimeClosure(IEnumerable<string> roots, IReadOnlyCollection<string>? excluded = null)
    {
        var found = new List<string>();
        var seen = new HashSet<string>(excluded ?? [], StringComparer.OrdinalIgnoreCase);
        var pending = new Queue<string>(roots);
        while (pending.TryDequeue(out var name))
        {
            var path = fileSystem.Path.Combine(runtimeFolder, $"{name}.dll");
            if (!seen.Add(name) || !fileSystem.File.Exists(path))
            {
                continue;
            }

            found.Add(name);
            foreach (var reference in ReferencesOf(fileSystem.File.ReadAllBytes(path)))
            {
                pending.Enqueue(reference);
            }
        }

        return found;
    }

    public static IReadOnlyList<string> ReferencesOf(byte[] assembly)
    {
        using var image = new PEReader(new MemoryStream(assembly, writable: false));
        var metadata = image.GetMetadataReader();
        return [.. metadata.AssemblyReferences.Select(handle => metadata.GetString(metadata.GetAssemblyReference(handle).Name))];
    }

    // the assemblies, their resources in each language that has them, and the native libraries they call, beside them.
    private void CopyRuntime(string folder, IReadOnlyList<string> closure, IReadOnlyList<(string Source, string Name)> natives)
    {
        foreach (var name in closure)
        {
            fileSystem.File.Copy(fileSystem.Path.Combine(runtimeFolder, $"{name}.dll"), fileSystem.Path.Combine(folder, $"{name}.dll"), overwrite: true);
        }

        foreach (var (culture, name) in CultureResources(closure))
        {
            var target = fileSystem.Path.Combine(folder, fileSystem.Path.GetFileName(culture));
            _ = fileSystem.Directory.CreateDirectory(target);
            fileSystem.File.Copy(fileSystem.Path.Combine(culture, $"{name}.resources.dll"), fileSystem.Path.Combine(target, $"{name}.resources.dll"), overwrite: true);
        }

        foreach (var (source, name) in natives.Where(native => fileSystem.File.Exists(native.Source)))
        {
            fileSystem.File.Copy(source, fileSystem.Path.Combine(folder, name), overwrite: true);
        }
    }

    // what an earlier build of the folder copied and this one does not: the assemblies, their resources and the native libraries.
    private void RemoveRuntime(string folder, IReadOnlyList<string> assemblies, IReadOnlyList<(string Source, string Name)> natives)
    {
        var files = assemblies.Select(name => fileSystem.Path.Combine(folder, $"{name}.dll"))
            .Concat(CultureResources(assemblies).Select(resources => fileSystem.Path.Combine(folder, fileSystem.Path.GetFileName(resources.Culture), $"{resources.Name}.resources.dll")))
            .Concat(natives.Select(native => fileSystem.Path.Combine(folder, native.Name)));
        foreach (var file in files.Where(fileSystem.File.Exists))
        {
            fileSystem.File.Delete(file);
        }
    }

    // the culture folders of the runtime folder that hold resources of the assemblies, with the name of each assembly they hold them of.
    private IEnumerable<(string Culture, string Name)> CultureResources(IReadOnlyList<string> assemblies)
        => fileSystem.Directory.GetDirectories(runtimeFolder).SelectMany(culture => assemblies
            .Where(name => fileSystem.File.Exists(fileSystem.Path.Combine(culture, $"{name}.resources.dll")))
            .Select(name => (culture, name)));
}
