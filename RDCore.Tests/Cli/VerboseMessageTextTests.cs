using Microsoft.Extensions.Options;
using RDCore.CLI.App.VerboseMessages;
using RDCore.Runtime.Semantics;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Model.Values.Meta;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Shared;
using RDCore.SDK.Semantics;
using System.Collections.Immutable;

namespace RDCore.Tests.Cli;

/// <summary>
/// The verbose text of a run-time error, character for character: the message, the expression it was raised at, and the frames of the operation, as each
/// setting of <c>CLI.VerboseMessages</c> has it. The runtime records the details, and the console words them.
/// </summary>
[TestClass]
public sealed class VerboseMessageTextTests
{
    private const string Message = "the message";

    private static readonly SyntaxNodeId ExpressionId = new(TestUri.TestModuleUri().AbsolutePath, [3]);
    private static readonly SyntaxNodeId FrameId = new(TestUri.TestModuleUri().AbsolutePath, [42]);
    private static readonly SyntaxNodeId CallerFrameId = new(TestUri.TestModuleUri().AbsolutePath, [7]);

    // an operation's frame: what an operator is given.
    private sealed record class OperandsFrame(SyntaxNodeId NodeId, ImmutableArray<VBTypedValue> Inputs) : IStackFrame;

    private static VBBinaryOperatorExpressionNode Expression(SourceRange range) => new("+", ExpressionId, new SourceLocation(TestUri.TestBinaryOpUri(), range),
    [
        new LiteralExpressionNode(default, TestLocations.TestLocationLHS, new VBLongValue(40000)),
        new LiteralExpressionNode(default, TestLocations.TestLocationRHS, new VBLongValue(1)),
    ]);

    private static readonly VBBinaryOperatorExpressionNode AtOnePosition = Expression(new SourceRange(1, 2, 1, 2));

    private static LetCoercionStackFrame Coercion()
        => new(FrameId, InputIndex.CoercionSourceValue, new VBLongValue(40000), new VBTypeDescValue(VBIntegerType.TypeInfo));

    private static OperandsFrame Operands(SyntaxNodeId id) => new(id, [new VBLongValue(40000), new VBLongValue(1)]);

    private static string Formatted(VerboseMessageOptions options, string message, ExpressionNode expression, params IStackFrame[] frames)
        => new VerboseMessageBuilder(Options.Create(options), new ExpressionInfoBuilder(options), new DefaultStackTraceFormatter(options))
            .Format(new ErrorDetailsRecorder().Record(message, expression, frames));

    private static string Id(SyntaxNodeId id) => id.ToString();

    [TestMethod]
    public void ByDefault_TheMessage_TheExpression_AndTheFrame_AreEachMarkedWithTheirIcon()
    {
        var frame = Coercion();
        var destination = frame.DestinationTypeDesc.TypeInfo.Name;

        Assert.AreEqual($"💠 {Message}[VBBinaryOperatorExpressionNode] 🔑 {Id(ExpressionId)}📌 L1C2👉 Frame details:    >💥 {Id(FrameId)}(🚩 Long, {destination})",
            Formatted(new VerboseMessageOptions(), Message, AtOnePosition, frame));
    }

    [TestMethod]
    public void AnExpressionThatSpansCharacters_IsLocatedFromItsStartToItsEnd()
        => Assert.AreEqual($"💠 {Message}[VBBinaryOperatorExpressionNode] 🔑 {Id(ExpressionId)}📌 L1C2 ..L3C9👉 Frame details:",
            Formatted(new VerboseMessageOptions(), Message, Expression(new SourceRange(1, 2, 3, 9))));

    [TestMethod]
    public void NoFrame_LeavesTheFrameDetailsHeadingAlone()
        => Assert.AreEqual($"💠 {Message}[VBBinaryOperatorExpressionNode] 🔑 {Id(ExpressionId)}📌 L1C2👉 Frame details:",
            Formatted(new VerboseMessageOptions(), Message, AtOnePosition));

    [TestMethod]
    public void TheFramesBelowTheTopOne_AreEachMarkedAndHeaded()
        => Assert.AreEqual(
            $"💠 {Message}[VBBinaryOperatorExpressionNode] 🔑 {Id(ExpressionId)}📌 L1C2👉 Frame details:"
            + $"    >💥 {Id(FrameId)}(🚩 Long, Long)"
            + $"    >Frame details: {Id(CallerFrameId)}(🚩 Long, Long)",
            Formatted(new VerboseMessageOptions(), Message, AtOnePosition, Operands(FrameId), Operands(CallerFrameId)));

    // the value itself, not the name of the type that holds it at run time, which is what the text said before the runtime recorded the details.
    [TestMethod]
    public void ShowValues_PutsEachInputsValueBeforeItsType()
        => Assert.AreEqual($"💠 {Message}[VBBinaryOperatorExpressionNode] 🔑 {Id(ExpressionId)}📌 L1C2👉 Frame details:    >💥 {Id(FrameId)}(🚩 [40000]:Long, [1]:Long)",
            Formatted(new VerboseMessageOptions(ShowValues: true), Message, AtOnePosition, Operands(FrameId)));

    [TestMethod]
    public void WithoutIcons_EachPartIsWrittenBare()
        => Assert.AreEqual($"{Message}[VBBinaryOperatorExpressionNode] {Id(ExpressionId)}L1C2Frame details:    >💥 {Id(FrameId)}(Long, Long)",
            Formatted(new VerboseMessageOptions(ShowMessageIcon: false, ShowSemanticIdIcon: false, ShowDocumentLocationIcon: false, ShowStackTraceIcon: false,
                ShowValuesIcon: false), Message, AtOnePosition, Operands(FrameId)));

    [TestMethod]
    public void TheIconsAndMarkers_AreTheOnesSet()
        => Assert.AreEqual($"* {Message}[VBBinaryOperatorExpressionNode] # {Id(ExpressionId)}@ L1C2= Frame details:    !! {Id(FrameId)}(? Long, Long)"
            + $"    -Frame details: {Id(CallerFrameId)}(? Long, Long)",
            Formatted(new VerboseMessageOptions(MessageIcon: "*", SemanticIdIcon: "#", DocumentLocationIcon: "@", StackTraceIcon: "=", StackFrameMarker: "-",
                TopStackFrameMarker: "!!", ValuesIcon: "?"), Message, AtOnePosition, Operands(FrameId), Operands(CallerFrameId)));

    [TestMethod]
    public void EachPart_IsLeftOutWhenItsSettingIsOff()
        => Assert.AreEqual($"💠 {Message}",
            Formatted(new VerboseMessageOptions(ExpressionType: false, ExpressionSemanticId: false, DocumentLocation: false, StackTrace: false), Message,
                AtOnePosition, Operands(FrameId)));

    [TestMethod]
    public void Disabled_IsNoTextAtAll()
        => Assert.AreEqual(string.Empty, Formatted(new VerboseMessageOptions(IsEnabled: false), Message, AtOnePosition, Operands(FrameId)));

    [TestMethod]
    public void ShowValues_LeavesTheTypeACoercionIsGivenWithoutAValue()
    {
        var frame = Coercion();
        var destination = frame.DestinationTypeDesc.TypeInfo.Name;

        Assert.AreEqual($"💠 {Message}[VBBinaryOperatorExpressionNode] 🔑 {Id(ExpressionId)}📌 L1C2👉 Frame details:    >💥 {Id(FrameId)}(🚩 [40000]:Long, {destination})",
            Formatted(new VerboseMessageOptions(ShowValues: true), Message, AtOnePosition, frame));
    }

    // a coercion frame is not enclosed in parentheses, as it never was.
    [TestMethod]
    public void TheLetCoercionTrace_SaysTheCoercionsSourceAndDestination_AndTheOtherFramesAsAnyOther()
    {
        var options = new VerboseMessageOptions();
        var text = new VerboseMessageBuilder(Options.Create(options), new ExpressionInfoBuilder(options), new LetCoercionTraceFormatter(options))
            .Format(new ErrorDetailsRecorder().Record(Message, AtOnePosition, [Coercion(), Operands(CallerFrameId)]));

        Assert.AreEqual($"💠 {Message}[VBBinaryOperatorExpressionNode] 🔑 {Id(ExpressionId)}📌 L1C2👉 Frame details:    >💥 {Id(FrameId)}Long -> Integer"
            + $"    >Frame details: {Id(CallerFrameId)}(🚩 Long, Long)", text);
    }
}
