using RDCore.Parsing;
using RDCore.SDK.Formatting;
using System.Diagnostics;

namespace RDCore.CLI.App.Bench;

/// <summary>
/// How long formatting a module took: parsing it, making the edits that indent it from that parse, and applying them to its text.
/// </summary>
/// <param name="Module">The module.</param>
/// <param name="Lines">How many lines it has.</param>
/// <param name="Edits">How many edits indenting it made.</param>
/// <param name="Parse">How long each parse took.</param>
/// <param name="Indent">How long making the edits took each time, from the parse.</param>
/// <param name="Apply">How long applying them took each time.</param>
internal sealed record class FormattingCase(BenchModule Module, int Lines, int Edits, IReadOnlyList<TimeSpan> Parse, IReadOnlyList<TimeSpan> Indent, IReadOnlyList<TimeSpan> Apply)
{
    /// <summary>The fastest parse.</summary>
    public TimeSpan ParseMin => Parse.Min();

    /// <summary>The fastest making of the edits.</summary>
    public TimeSpan IndentMin => Indent.Min();

    /// <summary>The fastest applying of them.</summary>
    public TimeSpan ApplyMin => Apply.Min();

    /// <summary>The run in the middle, of making the edits and applying them.</summary>
    public TimeSpan Median => Indent.Zip(Apply, (indent, apply) => indent + apply).Order().ElementAt(Indent.Count / 2);
}

/// <summary>
/// <c>rdc bench --format</c>: formats every module of a workspace from a parse of it, as the language server does with the parse it has - and times the parse
/// apart from the indentation, which is all a formatting request costs once the module is parsed.
/// </summary>
internal static class FormattingBench
{
    /// <summary>Formats each module of the workspace as many times as there are runs.</summary>
    public static IReadOnlyList<FormattingCase> Run(BenchWorkspace workspace, CodeFormattingOptions options, int runs, CancellationToken token)
    {
        var cases = new List<FormattingCase>();
        foreach (var module in workspace.Modules)
        {
            var uri = new Uri(Path.Combine(workspace.Root, module.RelativePath));
            var (parses, indents, applies, edits) = (new List<TimeSpan>(), new List<TimeSpan>(), new List<TimeSpan>(), 0);
            for (var run = 0; run < runs; run++)
            {
                token.ThrowIfCancellationRequested();
                var stopwatch = Stopwatch.StartNew();
                var parse = new ModuleParser().Parse(uri, module.Source);
                parses.Add(stopwatch.Elapsed);

                stopwatch.Restart();
                var made = ModuleIndenter.Indent(parse, module.Source, options);
                indents.Add(stopwatch.Elapsed);

                stopwatch.Restart();
                _ = ModuleIndenter.Apply(module.Source, made);
                applies.Add(stopwatch.Elapsed);
                edits = made.Count;
            }

            cases.Add(new FormattingCase(module, module.Source.Split('\n').Length, edits, parses, indents, applies));
        }

        return cases;
    }
}
