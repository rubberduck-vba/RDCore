using NSubstitute;
using RDCore.Runtime.Execution;
using RDCore.Runtime.Execution.Frames;
using RDCore.Runtime.Semantics;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.Tests.Cli;

namespace RDCore.Tests.Semantics.Runtime;

/// <summary>
/// A sweep (<see cref="ModuleSweep"/>) makes none of the elements of a fixed-size array a procedure declares: an array of the largest length
/// (<see cref="VBArrayValue.MaximumLength"/>) costs nothing until its elements are used, and a sweep that is told to stop stops between instructions,
/// with no array to finish making first.
/// </summary>
/// <remarks>
/// The sweep's token tells whether an element is made: each element made asks it whether to stop (<see cref="DeclaredVariableDefaults"/>). The token is
/// canceled before the sweep begins, so an element made throws <see cref="OperationCanceledException"/>: the sweep throws nothing, and the first element
/// the test then uses throws.
/// </remarks>
[TestClass]
[TestCategory("RD-VBAL §5.0.3 Semantic Analysis")]
public sealed class StoppedSweepTests
{
    private static readonly int MostElements = (int)VBArrayValue.MaximumLength;

    private static string Program(string type) => "Attribute VB_Name = \"Program\"\r\nOption Explicit\r\n"
        + "Private Type TPoint\r\n    X As Long\r\n    Y As Long\r\nEnd Type\r\n"
        + $"Public Sub Main()\r\n    Dim b(1 To {MostElements}) As {type}\r\nEnd Sub\r\n";

    // the sweep of Main, told to stop before it begins, and the array its local b starts as in that sweep's session: what the sweep throws, if it throws.
    private static async Task<(Exception? Swept, VBArrayValue Array)> SweepStoppedAsync(string type)
    {
        Exception? swept = null;
        VBArrayValue? array = null;
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
                swept = exception;
            }

            var b = ((VBProcedureMemberSymbol)main!).Locals.OfType<VBLocalVariableSymbol>().Single(local => local.Name == "b");
            array = Assert.IsInstanceOfType<VBArrayValue>(analysis.Symbols.Defaults!.DefaultValueOf(b));
            return Task.CompletedTask;
        });

        return (swept, array!);
    }

    [TestMethod]
    [DataRow("Byte", DisplayName = "of a Byte, in a cell of its own")]
    [DataRow("Variant", DisplayName = "of a Variant, in a Variant's cell")]
    [DataRow("TPoint", DisplayName = "of a record")]
    public async Task ASweepThatIsStopped_MakesNoElementOfTheArrayAProcedureDeclares(string type)
    {
        if (type is not "TPoint" && !UnboxedEvaluation.IsEnabled)
        {
            Assert.Inconclusive("values are boxed (RDCORE_UNBOXED=0): an element is in the cell the array makes of its default value, made with nothing asked");
        }

        var (swept, array) = await SweepStoppedAsync(type);

        Assert.IsNull(swept, $"{swept}");
        Assert.AreEqual(MostElements, array.Length);
        // the token is asked: the first element made asks whether to stop, and is not made.
        Assert.ThrowsExactly<OperationCanceledException>(() => array.CellAt(0));
    }
}
