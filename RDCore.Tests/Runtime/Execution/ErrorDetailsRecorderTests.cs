using RDCore.Runtime.Semantics;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Runtime.Abstract.Execution;
using System.Collections.Immutable;

namespace RDCore.Tests.Runtime.Execution;

/// <summary>
/// The details of an error hold at most <see cref="VBErrorDetails.MaxFrames"/> frames, the innermost ones, and say when the operation had more: an error raised
/// deep in a recursion carries no more than that.
/// </summary>
[TestClass]
public sealed class ErrorDetailsRecorderTests
{
    private sealed record class OperandsFrame(SyntaxNodeId NodeId, ImmutableArray<VBTypedValue> Inputs) : IStackFrame;

    private static readonly LiteralExpressionNode Expression
        = new(new SyntaxNodeId(TestUri.TestModuleUri().AbsolutePath, [1]), new SourceLocation(TestUri.TestModuleUri(), new SourceRange(1, 0, 1, 5)), new VBLongValue(1));

    // the frames of an operation, innermost first: frame 0 is the innermost.
    private static IEnumerable<IStackFrame> Frames(int count)
        => Enumerable.Range(0, count).Select(index => new OperandsFrame(new SyntaxNodeId(TestUri.TestModuleUri().AbsolutePath, [index]), [new VBLongValue(index)]));

    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(VBErrorDetails.MaxFrames)]
    public void FramesUpToTheBound_AreEachRecorded(int count)
    {
        var details = new ErrorDetailsRecorder().Record("the message", Expression, Frames(count));

        Assert.HasCount(count, details.Frames);
        Assert.IsFalse(details.FramesTruncated);
    }

    [TestMethod]
    public void FramesPastTheBound_AreLeftOut_TheInnermostKept()
    {
        var details = new ErrorDetailsRecorder().Record("the message", Expression, Frames(VBErrorDetails.MaxFrames * 10));

        Assert.HasCount(VBErrorDetails.MaxFrames, details.Frames);
        Assert.IsTrue(details.FramesTruncated);
        Assert.AreEqual("0", details.Frames[0].Inputs[0].Value, "the innermost frame is the first");
        Assert.AreEqual($"{VBErrorDetails.MaxFrames - 1}", details.Frames[^1].Inputs[0].Value);
    }
}
