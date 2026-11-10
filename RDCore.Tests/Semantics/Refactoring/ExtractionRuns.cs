using RDCore.LanguageServer.Services.Refactoring.ExtractMethod;
using RDCore.SDK.Formatting;
using RDCore.SDK.Platform.Protocol;
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
    // driver is the body of the program's Main, which calls Run. The workspace has the class modules in classes beside the module.
    public static async Task<(Run Before, Run After, string Module, ExtractionPlan Plan)> BeforeAndAfterAsync(
        string body, int first, int last, string driver = "Run 3", string? returnValue = null, string header = "Public Sub Run(ByVal n As Long)",
        string end = "End Sub", string declarations = "", IReadOnlyList<(string Name, string Source)>? classes = null)
    {
        var extracted = ExtractionFixture.Extracted(body, first, last, returnValue: returnValue, header: header, end: end, declarations: declarations,
            formatting: CodeFormattingOptions.Default, classes: classes);
        Assert.IsNull(extracted.Edit.Refusal, $"refused: {extracted.Edit.Refusal}");
        Assert.AreNotEqual(extracted.Original, extracted.Module);

        var before = await RunAsync(extracted.Original, driver, classes ?? [], "before");
        var after = await RunAsync(extracted.Module, driver, classes ?? [], "after");
        return (before, after, extracted.Module, extracted.Plan);
    }

    // the module, named Program and given the Main that runs it, run as the platform runs a program; a module that does not load fails the test with its text,
    // which says which run it is.
    private static async Task<Run> RunAsync(string module, string driver, IReadOnlyList<(string Name, string Source)> classes, string which)
    {
        var program = module.Replace("Attribute VB_Name = \"Main\"", "Attribute VB_Name = \"Program\"", StringComparison.Ordinal)
            + $"\r\nPublic Sub Main()\r\n{driver.ReplaceLineEndings("\r\n")}\r\nEnd Sub\r\n";
        Run? run = null;
        try
        {
            await ModuleWorkspace.InspectAsync(classes, program, (_, result, _) =>
            {
                run = new Run(result.Outcome, string.Join(" | ", result.Output.Select(line => line.Trim())), result.ErrorNumber);
                return Task.CompletedTask;
            });
        }
        catch (AssertFailedException failed)
        {
            throw new AssertFailedException($"the module {which} the extraction: {failed.Message}\r\n{program}", failed);
        }

        return run!;
    }
}
