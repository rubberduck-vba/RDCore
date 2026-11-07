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

            // a library is known by its name, and the name is what finds it: a dependency is the latest library of its name, as it is when it is asked for, and
            // the version that the library which depends on it happens to be linked with is only what is described when the machine has none of the name.
            var loaded = TypeLibraryLocator.Load(reference, message => Write(MessageKind.Information, message))
                ?? (referenced is null ? null : TypeLibraryLocator.Of(referenced));
            if (loaded is null)
            {
                Write(MessageKind.Error, string.Format(Resources.ExportLibrary_NotFound, reference));
                return 1;
            }

            if (!done.Add(loaded.Name))
            {
                continue;
            }

            var export = new TypeLibraryExporter(!options.WithoutDescriptions).Export(loaded, exportedBy);
            var path = fileSystem.Path.Combine(output, loaded.Name + DirectoryLibrarySource.Extension);
            fileSystem.File.WriteAllText(path, LibraryJson.Write(export.Description), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            Write(MessageKind.Success, string.Format(Resources.ExportLibrary_Written, loaded.Name, path), Counts(export.Description));
            if (export.Skipped.Count > 0)
            {
                Write(MessageKind.Warning, string.Format(Resources.ExportLibrary_Skipped, Abbreviated(export.Skipped)));
            }

            if (options.Dependencies)
            {
                foreach (var (name, library) in export.Dependencies.Where(dependency => !done.Contains(dependency.Key)))
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
