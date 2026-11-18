using NSubstitute;
using RDCore.Runtime.Execution;
using RDCore.Runtime.Execution.Frames;
using RDCore.Runtime.Semantics;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.Tests.Cli;

namespace RDCore.Tests.Semantics.Runtime;

/// <summary>
/// A sweep that is told to stop (<see cref="ModuleSweep"/>) makes no element of a fixed-size array a procedure declares: an element is made the first time
/// it is used, and an array of millions of elements is seconds and gigabytes, which a sweep nobody waits for any more does not spend.
/// </summary>
[TestClass]
[TestCategory("RD-VBAL §5.0.3 Semantic Analysis")]
public sealed class StoppedSweepTests
{
    private static string Program(string type) => "Attribute VB_Name = \"Program\"\r\nOption Explicit\r\n"
        + "Private Type TPoint\r\n    X As Long\r\n    Y As Long\r\nEnd Type\r\n"
        + $"Public Sub Main()\r\n    Dim b(1 To 1000) As {type}\r\nEnd Sub\r\n";

    // the sweep of Main, told to stop before it begins: whether it was complete, and what it throws, if it throws.
    private static async Task<(bool Complete, Exception? Thrown)> SweepStoppedAsync(string type)
    {
        var complete = true;
        Exception? thrown = null;
        await ModuleWorkspace.WithHostAsync([], Program(type), provider =>
        {
            Assert.IsTrue(provider.Session.Symbols.TryResolveValue("Program", GlobalSymbols.UnresolvedSymbol, out var module));
            Assert.IsTrue(provider.Session.Symbols.TryResolveValue("Main", module!, out var main));

            var stopped = new CancellationToken(canceled: true);
            var analysis = RuntimeSessionComposer.ComposeForAnalysis(provider.Session);
            var pipeline = RuntimeExecutionPipeline.CreateForSweep(
                analysis, provider.Image, Substitute.For<IErrorDetailsRecorder>(), new RecordingAnalysisObserver(), stopped);

            try
            {
                complete = pipeline.Sweeper!.Sweep((VBTypeMemberSymbol)main!);
            }
            catch (Exception exception)
            {
                thrown = exception;
            }

            return Task.CompletedTask;
        });

        return (complete, thrown);
    }

    // an element made once the sweep is told to stop throws (DeclaredVariableDefaults): a sweep that made every element of the array, or any, would throw.
    // It makes none, and stops before Main's first instruction.
    [TestMethod]
    [DataRow("Byte", DisplayName = "of a Byte, in a cell of its own")]
    [DataRow("Variant", DisplayName = "of a Variant, in a Variant's cell")]
    [DataRow("TPoint", DisplayName = "of a record")]
    [DataRow("String", DisplayName = "of a String")]
    public async Task ASweepThatIsStopped_MakesNoElementOfAnArray_AndStops(string type)
    {
        var (complete, thrown) = await SweepStoppedAsync(type);

        Assert.IsNull(thrown, "no element is made");
        Assert.IsFalse(complete, "the sweep stopped before it was complete");
    }
}
