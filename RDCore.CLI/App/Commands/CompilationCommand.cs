using CommandLine;
using RDCore.CLI.App.Bench;
using RDCore.Runtime.Compiled;
using RDCore.CLI.ConsoleIO;
using RDCore.CLI.ConsoleIO.Model;
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
/// <c>rdc compilation [path]</c>: says, for each procedure of a workspace, whether it runs compiled, and what keeps it in the interpreter when it does not -
/// or, when it does, what of it the interpreter runs.
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
                workspace = await BenchWorkspace.LoadAsync(fileSystem, projects, corpus ?? parsed.Value.Path!, token,
                    ansiCodePage: runner.Environment.AnsiCodePage);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or InvalidDataException)
            {
                writer.WriteMessage(new ConsoleMessageBuilder().WithKind(MessageKind.Error).WithMessageBody(exception.Message));
                return 1;
            }

            var (procedures, problems) = await runner.CompileAsync(workspace, token);
            foreach (var problem in problems)
            {
                writer.WriteMessage(new ConsoleMessageBuilder().WithKind(MessageKind.Error).WithMessageBody(problem));
            }

            Write(procedures, workspace);
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

    private void Write(IReadOnlyList<(string Module, ProcedureCompilation Compilation)> procedures, BenchWorkspace workspace)
    {
        var sources = workspace.Modules.DistinctBy(module => module.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(module => module.Name, module => module.Source.ReplaceLineEndings("\n").Split('\n'), StringComparer.OrdinalIgnoreCase);
        var width = procedures.Count == 0 ? 0 : procedures.Max(entry => entry.Module.Length + entry.Compilation.Procedure.Name.Length + 1) + 2;
        var label = Math.Max(Resources.Compilation_Compiled.Length, Resources.Compilation_Interpreted.Length) + 2;
        foreach (var (module, compilation) in procedures)
        {
            var name = $"{module}.{compilation.Procedure.Name}".PadRight(width);
            var lines = sources.GetValueOrDefault(module);
            // a loop and its Next are at the same line, and a reason of each says the same: the report says it once.
            string Said(IEnumerable<Ineligibility> reasons) => string.Join("; ", reasons.Select(reason => Describe(reason, lines)).Distinct());
            // a compiled procedure has the interpreter run, one at a time, the instructions compiled code does not do, and the statements it does not make
            // itself as support calls.
            if (compilation.IsCompiled)
            {
                string[] handed =
                [
                    .. compilation.Stepped.Count == 0 ? [] : new[]
                    {
                        string.Format(CultureInfo.CurrentCulture, Resources.Compilation_Stepped, Said(compilation.Stepped)),
                    },
                    .. compilation.SupportCalls.Count == 0 ? [] : new[]
                    {
                        string.Format(CultureInfo.CurrentCulture, Resources.Compilation_SupportCalls, Said(compilation.SupportCalls)),
                    },
                ];
                Output.WriteLine($"{Resources.Compilation_Compiled.PadRight(label)}{name}{string.Join(" | ", handed)}");
                continue;
            }

            Output.WriteLine($"{Resources.Compilation_Interpreted.PadRight(label)}{name}{Said(compilation.Reasons)}");
        }

        Output.WriteLine();
        Output.WriteLine(string.Format(CultureInfo.CurrentCulture, Resources.Compilation_Count, procedures.Count(entry => entry.Compilation.IsCompiled), procedures.Count));
    }

    // what keeps a procedure in the interpreter, worded in the user's language, and the line it is on. A statement, an instruction, or an assignment of
    // what is not a variable of the procedure is the code of that line of the module, as it is written; what the analysis calls it only when the line is
    // not known.
    internal static string Describe(Ineligibility reason, IReadOnlyList<string>? lines = null)
    {
        var wording = Resources.ResourceManager.GetString($"Compilation_Reason_{reason.Kind}", Resources.Culture) ?? reason.Kind.ToString();
        var detail = reason is { Kind: IneligibilityKind.Statement or IneligibilityKind.Instruction or IneligibilityKind.AssignmentTarget, Location: { } at }
            && lines is not null && at.Range.Start.Line < lines.Count && lines[at.Range.Start.Line].Trim() is { Length: > 0 } code
                ? code
                : reason.Detail;
        var described = string.Format(CultureInfo.CurrentCulture, wording, detail);
        return reason.Location is { } location
            ? $"{described} ({string.Format(CultureInfo.CurrentCulture, Resources.Compilation_Line, location.Range.Start.Line + 1)})"
            : described;
    }
}
