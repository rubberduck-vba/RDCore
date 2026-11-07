using CommandLine;
using RDCore.CLI.Libraries;
using RDCore.SDK.ConsoleIO;
using RDCore.SDK.ConsoleIO.Model;
using RDCore.SDK.Runtime.Libraries;
using System.IO.Abstractions;
using System.Reflection;
using System.Runtime.InteropServices.ComTypes;
using System.Text;

namespace RDCore.CLI.App.Commands;

/// <summary>
/// The parsed <c>export-library</c> verb arguments.
/// </summary>
internal sealed class ExportLibraryOptions
{
    [Value(0, MetaName = "library", Required = true, HelpText = "The library: a file (scrrun.dll, EXCEL.EXE, a .tlb), the identifier the machine gave it, or its name (Scripting). A file that holds more than one library is given with the number of the library's resource, as a .vbp references it (MSVBVM60.DLL\\3).")]
    public string Library { get; set; } = string.Empty;

    [Option('o', "output", HelpText = "The folder the description is written to. The Symbols folder of the current directory when omitted.")]
    public string? Output { get; set; }

    [Option("dependencies", HelpText = "Describe the libraries it depends on too, and theirs.")]
    public bool Dependencies { get; set; }

    [Option("without-descriptions", HelpText = "Leave out what the library says each type and member is: the file is a good deal smaller.")]
    public bool WithoutDescriptions { get; set; }
}

/// <summary>
/// Describes a type library of this machine as a library description file (<see cref="LibraryDescription"/>) that a project can reference by name.
/// </summary>
/// <remarks>
/// A workspace is analyzed on machines that do not have the libraries it references, so what a library declares ships with the platform as a file
/// (<c>Symbols/&lt;Name&gt;.json</c>), made once, here, on a machine that has the library.
/// </remarks>
internal sealed class ExportLibraryCommand(IConsoleMessageWriter writer, IFileSystem fileSystem) : ICliCommand
{
    public string Name => CommandNames.ExportLibraryCommand.Name;
    public IReadOnlyList<string> Aliases => [CommandNames.ExportLibraryCommand.Alias];
    public string Summary => Resources.ExportLibrary_Summary;

    public Task<int> ExecuteAsync(IReadOnlyList<string> args, CancellationToken token)
    {
        var parsed = Parser.Default.ParseArguments<ExportLibraryOptions>(args);
        if (parsed.Errors.Any())
        {
            // CommandLine already wrote the usage/errors to stderr.
            return Task.FromResult(2);
        }

        return Task.FromResult(Execute(parsed.Value));
    }

    private int Execute(ExportLibraryOptions options)
    {
        if (!OperatingSystem.IsWindows())
        {
            Write(MessageKind.Error, Resources.ExportLibrary_WindowsOnly);
            return 1;
        }

        var output = options.Output ?? "Symbols";
        fileSystem.Directory.CreateDirectory(output);

        var exportedBy = $"rdc export-library {Assembly.GetEntryAssembly()?.GetName().Version?.ToString(2) ?? "0"}";
        var pending = new Queue<(string Reference, ITypeLib? Referenced)>();
        pending.Enqueue((options.Library, null));
        var done = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        while (pending.Count > 0)
        {
            var (reference, referenced) = pending.Dequeue();

            // a dependency is the type library the one that depends on it is linked with: a machine can have several libraries of its name (DAO), and the
            // latest of them is not the one the library was built against.
            var loaded = referenced is null
                ? TypeLibraryLocator.Load(reference, message => Write(MessageKind.Information, message))
                : TypeLibraryLocator.Of(referenced);
            if (loaded is null)
            {
                Write(MessageKind.Error, string.Format(Resources.ExportLibrary_NotFound, reference));
                return 1;
            }

            var identity = TypeLibraryExporter.IdentityOf(loaded.Name, loaded.Library);
            if (!done.Add(identity.ToString()))
            {
                continue;
            }

            var export = new TypeLibraryExporter(!options.WithoutDescriptions).Export(loaded, exportedBy);
            var path = PathOf(output, loaded, identity);
            fileSystem.File.WriteAllText(path, LibraryJson.Write(export.Description), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            Write(MessageKind.Success, string.Format(Resources.ExportLibrary_Written, loaded.Name, path), Counts(export.Description));
            if (export.Skipped.Count > 0)
            {
                Write(MessageKind.Warning, string.Format(Resources.ExportLibrary_Skipped, Abbreviated(export.Skipped)));
            }

            if (options.Dependencies)
            {
                foreach (var (name, library) in export.Dependencies)
                {
                    pending.Enqueue((name, library));
                }
            }
            else if (export.Dependencies.Count > 0)
            {
                Write(MessageKind.Warning, string.Format(Resources.ExportLibrary_DependsOn, string.Join(", ", export.Dependencies.Keys.Order())));
            }
        }

        return 0;
    }

    // <Name>.json, unless the folder has one that describes another type library of the name: then <Name>.<file stem>.json, which the platform reads as
    // a library of the same name (DirectoryLibrarySource), and so on with the version, then a number. The first that is not there, or that describes this
    // type library, is written: a file of another type library is never written over.
    private string PathOf(string output, LoadedTypeLibrary loaded, LibraryIdentity identity)
    {
        var stem = loaded.File is null
            ? null
            : fileSystem.Path.GetFileNameWithoutExtension(TypeLibraryLocator.FileNameOf(loaded.File).Split('\\')[0]).ToLowerInvariant();
        var qualifier = stem ?? $"v{identity.Major}_{identity.Minor}";
        IEnumerable<string> Names()
        {
            yield return loaded.Name;
            yield return $"{loaded.Name}.{qualifier}";
            yield return $"{loaded.Name}.{qualifier}.v{identity.Major}_{identity.Minor}";
            for (var number = 2; ; number++)
            {
                yield return $"{loaded.Name}.{qualifier}.v{identity.Major}_{identity.Minor}.{number}";
            }
        }

        // the file of the folder that already describes it, whatever it is called, is the one written again.
        var own = fileSystem.Directory.EnumerateFiles(output, "*" + DirectoryLibrarySource.Extension)
            .Where(path => string.Equals(DirectoryLibrarySource.LibraryNameOf(path), loaded.Name, StringComparison.OrdinalIgnoreCase))
            .Order(StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(path => IsOf(path, identity, strictly: true));
        return own ?? Names().Select(name => fileSystem.Path.Combine(output, name + DirectoryLibrarySource.Extension))
            .First(path => !fileSystem.File.Exists(path) || IsOf(path, identity));
    }

    // whether the file describes the type library. One that cannot be read, or states none, is written over, as it always was, unless `strictly`.
    private bool IsOf(string path, LibraryIdentity identity, bool strictly = false)
    {
        try
        {
            var existing = LibraryIdentity.Of(LibraryJson.Read(fileSystem.File.ReadAllText(path)));
            return existing.Guid is null
                ? !strictly
                : existing.Guid == identity.Guid && existing.Major == identity.Major && existing.Minor == identity.Minor;
        }
        catch (InvalidDataException)
        {
            return !strictly;
        }
    }

    private static string Counts(LibraryDescription description)
        => string.Format(
            Resources.ExportLibrary_Counts,
            description.Classes.Length, description.Enums.Length, description.Classes.Sum(declared => declared.Members.Length + declared.Events.Length));

    private static string Abbreviated(IReadOnlyList<string> skipped)
        => skipped.Count <= 8 ? string.Join(", ", skipped) : string.Join(", ", skipped.Take(8)) + $" (+{skipped.Count - 8})";

    private void Write(MessageKind kind, string body, string? verbose = null)
    {
        var message = new ConsoleMessageBuilder().WithKind(kind).WithTitle(Resources.ExportLibrary_Title).WithMessageBody(body);
        writer.WriteMessage(verbose is null ? message : message.WithVerbose(verbose));
    }
}
