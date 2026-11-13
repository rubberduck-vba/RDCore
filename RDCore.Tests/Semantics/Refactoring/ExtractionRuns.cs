using RDCore.LanguageServer.Services.Refactoring.ExtractMethod;
using RDCore.SDK.Formatting;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.Tests.Cli;

namespace RDCore.Tests.Semantics.Refactoring;

/// <summary>
/// A program run in RDCore's runtime before an extraction of lines of its procedure <c>Run</c>, as <see cref="ExtractionFixture"/> writes it, and after: what each
/// run printed, and how it ended.
/// </summary>
internal static class ExtractionRuns
{
    /// <summary>
    /// How a run of the program ended.
    /// </summary>
    /// <param name="Outcome">Whether it completed.</param>
    /// <param name="Output">What it printed, its lines trimmed and joined by <c> | </c>.</param>
    /// <param name="ErrorNumber">The number of the error it ended with; 0 when it completed.</param>
    internal sealed record class Run(ExecutionOutcome Outcome, string Output, int ErrorNumber);

    // The program run with the module as it is, then with the lines of Run's body from first to last extracted, and formatted as for a user who set nothing:
    // driver is the body of the program's Main, which calls Run. The workspace has the class modules in classes beside the module. With classModule, the
    // module is the class module Worker, and Main is a standard module's, which makes the objects whose Run it calls. Erl counts lines as erl says. A range,
    // lines zero-based from the body's first, is extracted instead of the lines. The parameters are answered as parameters says.
    public static async Task<(Run Before, Run After, string Module, ExtractionPlan Plan)> BeforeAndAfterAsync(
        string body, int first, int last, string driver = "Run 3", string? returnValue = null, string header = "Public Sub Run(ByVal n As Long)",
        string end = "End Sub", string declarations = "", IReadOnlyList<(string Name, string Source)>? classes = null, bool classModule = false,
        VBErlLineNumbering erl = VBErlLineNumbering.DocumentLine, SourceRange? range = null, IReadOnlyList<ExtractionParameterAnswer>? parameters = null)
    {
        var extracted = ExtractionFixture.Extracted(body, first, last, returnValue: returnValue, header: header, end: end, declarations: declarations,
            formatting: CodeFormattingOptions.Default, classes: classes, classModule: classModule, range: range, parameters: parameters);
        Assert.IsNull(extracted.Edit.Refusal, $"refused: {extracted.Edit.Refusal}");
        Assert.AreNotEqual(extracted.Original, extracted.Module);

        var before = await RunAsync(extracted.Original, driver, classes ?? [], classModule, erl, "before");
        var after = await RunAsync(extracted.Module, driver, classes ?? [], classModule, erl, "after");
        return (before, after, extracted.Module, extracted.Plan);
    }

    // The program run with the module as it is, then with the GoSub routine the label starts converted to a procedure, as ExtractionFixture.Converted writes it.
    public static async Task<(Run Before, Run After, string Module)> ConvertedBeforeAndAfterAsync(
        string body, string label, string driver = "Run 3", string header = "Public Sub Run(ByVal n As Long)", string end = "End Sub", string declarations = "",
        VBErlLineNumbering erl = VBErlLineNumbering.DocumentLine)
    {
        var converted = ExtractionFixture.Converted(body, label, header: header, end: end, declarations: declarations);
        Assert.IsNull(converted.Edit.Refusal, $"refused: {converted.Edit.Refusal}");
        Assert.AreNotEqual(converted.Original, converted.Module);

        var before = await RunAsync(converted.Original, driver, [], classModule: false, erl, "before");
        var after = await RunAsync(converted.Module, driver, [], classModule: false, erl, "after");
        return (before, after, converted.Module);
    }

    // the module, named Program and given the Main that runs it, run as the platform runs a program; a class module is named Worker, beside a Program that has
    // only the Main. A module that does not load fails the test with its text, which says which run it is.
    private static async Task<Run> RunAsync(
        string module, string driver, IReadOnlyList<(string Name, string Source)> classes, bool classModule, VBErlLineNumbering erl, string which)
    {
        var main = $"\r\nPublic Sub Main()\r\n{driver.ReplaceLineEndings("\r\n")}\r\nEnd Sub\r\n";
        var program = classModule
            ? $"Attribute VB_Name = \"Program\"\r\nOption Explicit\r\n{main}"
            : module.Replace("Attribute VB_Name = \"Main\"", "Attribute VB_Name = \"Program\"", StringComparison.Ordinal) + main;
        classes = classModule
            ? [.. classes, ("Worker", module.Replace("Attribute VB_Name = \"Main\"", "Attribute VB_Name = \"Worker\"", StringComparison.Ordinal))]
            : classes;
        Run? run = null;
        try
        {
            await ModuleWorkspace.InspectAsync(classes, program, (_, result, _) =>
            {
                run = new Run(result.Outcome, string.Join(" | ", result.Output.Select(line => line.Trim())), result.ErrorNumber);
                return Task.CompletedTask;
            }, environment: profile => profile with { ErlLineNumbering = erl });
        }
        catch (AssertFailedException failed)
        {
            throw new AssertFailedException($"the module {which} the extraction: {failed.Message}\r\n{program}", failed);
        }

        return run!;
    }
}
