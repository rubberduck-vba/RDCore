using NSubstitute;
using RDCore.Runtime.Execution;
using RDCore.Runtime.Execution.Frames;
using RDCore.Runtime.Semantics;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.Tests.Cli;

namespace RDCore.Tests.Semantics.Runtime;

/// <summary>
/// A sweep that is told to stop (<see cref="ModuleSweep"/>) stops while it makes the elements of a fixed-size array a procedure declares, and not only
/// between instructions: an array of millions of elements is seconds and gigabytes, which a sweep nobody waits for any more does not spend.
/// </summary>
[TestClass]
[TestCategory("RD-VBAL §5.0.3 Semantic Analysis")]
public sealed class StoppedSweepTests
{
    private static string Program(string type) => "Attribute VB_Name = \"Program\"\r\nOption Explicit\r\n"
        + "Private Type TPoint\r\n    X As Long\r\n    Y As Long\r\nEnd Type\r\n"
        + $"Public Sub Main()\r\n    Dim b(1 To 1000) As {type}\r\nEnd Sub\r\n";

    // the sweep of Main, told to stop before it begins: what it throws, if it throws.
    private static async Task<Exception?> SweepStoppedAsync(string type)
    {
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
                _ = pipeline.Sweeper!.Sweep((VBTypeMemberSymbol)main!);
            }
            catch (Exception exception)
            {
                thrown = exception;
            }

            return Task.CompletedTask;
        });

        return thrown;
    }

    // the first element made asks whether to stop, and is not made: the sweep ends there, before Main's first instruction.
    [TestMethod]
    [DataRow("Byte", DisplayName = "of a Byte, in a cell of its own")]
    [DataRow("Variant", DisplayName = "of a Variant, in a Variant's cell")]
    [DataRow("TPoint", DisplayName = "of a record")]
    public async Task ASweepThatIsStopped_StopsWhileItMakesTheElementsOfAnArray(string type)
    {
        if (type is not "TPoint" && !UnboxedEvaluation.IsEnabled)
        {
            Assert.Inconclusive("values are boxed (RDCORE_UNBOXED=0): an element is in the cell the array makes of its default value, made with nothing asked");
        }

        Assert.IsInstanceOfType<OperationCanceledException>(await SweepStoppedAsync(type));
    }

    [TestMethod]
    [Ignore("an element of a String is in the cell the SDK's array makes of its default value, with nothing asked: the sweep stops only once the array is made")]
    public async Task ASweepThatIsStopped_StopsWhileItMakesTheElementsOfAnArrayOfStrings()
        => Assert.IsInstanceOfType<OperationCanceledException>(await SweepStoppedAsync("String"));
}
