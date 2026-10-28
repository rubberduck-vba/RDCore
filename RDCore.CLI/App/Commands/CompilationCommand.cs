using CommandLine;
using RDCore.CLI.App.Bench;
using RDCore.Runtime.Compiled;
using RDCore.SDK.ConsoleIO;
using RDCore.SDK.ConsoleIO.Model;
using RDCore.SDK.Workspace;
using System.Globalization;
using System.IO.Abstractions;

namespace RDCore.CLI.App.Commands;

internal sealed class CompilationCommandOptions
{
    [Value(0, MetaName = "path", Required = false, HelpText = "A module file, or a workspace folder (with or without a .rdproj). The built-in corpus when omitted.")]
    public string? Path { get; set; }
}

/// <summary>
/// <c>rdc compilation [path]</c>: says, for each procedure of a workspace, whether it runs compiled, and what keeps it in the interpreter when it does not
/// (IR lowering §9.2, eligibility).
/// </summary>
/// <remarks>
/// The workspace is loaded as <c>rdc bench</c> loads it, and each procedure compiled as its first call would compile it; nothing is run.
/// </remarks>
/// <param name="writer">Writes the platform's messages: what went wrong.</param>
/// <param name="fileSystem">The file system the workspace is on.</param>
/// <param name="projects">Reads a <c>.rdproj</c>.</param>
/// <param name="runner">Loads the workspace, and compiles its procedures.</param>
/// <param name="output">Where the report is written; the console unless said otherwise.</param>
internal sealed class CompilationCommand(IConsoleMessageWriter writer, IFileSystem fileSystem, IProjectFileLoader projects, BenchRunner runner, TextWriter? output = null)
    : ICliCommand
{
    private TextWriter Output => output ?? System.Console.Out;

    public string Name => CommandNames.CompilationCommand.Name;
    public IReadOnlyList<string> Aliases => [CommandNames.CompilationCommand.Alias];
    public string Summary => Resources.Compilation_Summary;

    public async Task<int> ExecuteAsync(IReadOnlyList<string> args, CancellationToken token)
    {
        var parsed = Parser.Default.ParseArguments<CompilationCommandOptions>(args);
        if (parsed.Errors.Any())
        {
            // CommandLine already wrote the usage/errors to stderr.
            return 2;
        }

        var corpus = parsed.Value.Path is null ? BenchCommand.ExtractCorpus(fileSystem) : null;
        try
        {
            BenchWorkspace workspace;
            try
            {
                workspace = await BenchWorkspace.LoadAsync(fileSystem, projects, corpus ?? parsed.Value.Path!, token);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                writer.WriteMessage(new ConsoleMessageBuilder().WithKind(MessageKind.Error).WithMessageBody(exception.Message));
                return 1;
            }

            var (procedures, problems) = await runner.CompileAsync(workspace, token);
            foreach (var problem in problems)
            {
                writer.WriteMessage(new ConsoleMessageBuilder().WithKind(MessageKind.Error).WithMessageBody(problem));
            }

            Write(procedures);
            return problems.Count > 0 ? 1 : 0;
        }
        finally
        {
            if (corpus is not null && fileSystem.Directory.Exists(corpus))
            {
                fileSystem.Directory.Delete(corpus, recursive: true);
            }
        }
    }

    private void Write(IReadOnlyList<(string Module, ProcedureCompilation Compilation)> procedures)
    {
        var width = procedures.Count == 0 ? 0 : procedures.Max(entry => entry.Module.Length + entry.Compilation.Procedure.Name.Length + 1) + 2;
        var label = Math.Max(Resources.Compilation_Compiled.Length, Resources.Compilation_Interpreted.Length) + 2;
        foreach (var (module, compilation) in procedures)
        {
            var name = $"{module}.{compilation.Procedure.Name}".PadRight(width);
            if (compilation.IsCompiled)
            {
                Output.WriteLine($"{Resources.Compilation_Compiled.PadRight(label)}{name}");
                continue;
            }

            Output.WriteLine($"{Resources.Compilation_Interpreted.PadRight(label)}{name}{string.Join("; ", compilation.Reasons.Select(Describe))}");
        }

        Output.WriteLine();
        Output.WriteLine(string.Format(CultureInfo.CurrentCulture, Resources.Compilation_Count, procedures.Count(entry => entry.Compilation.IsCompiled), procedures.Count));
    }

    /// <summary>What keeps a procedure in the interpreter, worded in the user's language, and the line it is on.</summary>
    internal static string Describe(Ineligibility reason)
    {
        var wording = Resources.ResourceManager.GetString($"Compilation_Reason_{reason.Kind}", Resources.Culture) ?? reason.Kind.ToString();
        var described = string.Format(CultureInfo.CurrentCulture, wording, reason.Detail);
        return reason.Location is { } location
            ? $"{described} ({string.Format(CultureInfo.CurrentCulture, Resources.Compilation_Line, location.Range.Start.Line + 1)})"
            : described;
    }
}
