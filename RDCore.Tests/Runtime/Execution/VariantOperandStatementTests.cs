using RDCore.Runtime.Execution;
using RDCore.SDK.Model;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using System.IO.Abstractions.TestingHelpers;

namespace RDCore.Tests.Runtime.Execution;

/// <summary>
/// A <c>Variant</c> operand of a statement is the value it holds: a statement that coerces its own operands is no different for a variable of
/// the type than for the value in it.
/// </summary>
/// <remarks>
/// These statements coerce their operands themselves, with the strategy for the type, and not through the provider that unwraps a <c>Variant</c>
/// before it dispatches: the strategies are what has to know. It was an internal error, once, and a <c>NullReferenceException</c> where a
/// run-time error was raised without anything to say.
/// </remarks>
[TestClass]
public sealed class VariantOperandStatementTests
{
    private const string Root = "/ws";

    private static VBModuleFieldVariableMemberSymbol Variable(string name, VBType type)
        => new(TestUri.WorkspaceRoot(), RuntimeSourceHarness.ModuleUri, name, ScopeKind.Module, type,
            SourceRange.Empty, SourceRange.Empty, AccessModifier.Implicit);

    private static (MockFileSystem Files, RuntimeExecutionOutcome Outcome, Dictionary<string, object?> Values) Run(
        (string Name, VBType Type)[] variables, params string[] body)
    {
        var files = new MockFileSystem(new Dictionary<string, MockFileData> { [$"{Root}/in.txt"] = new("line one\r\n") });
        var symbols = variables.Select(variable => Variable(variable.Name, variable.Type)).ToArray();
        var (session, outcome) = RuntimeSourceHarness.Run(files, symbols, body);

        return (files, outcome, symbols.ToDictionary(symbol => symbol.Name, symbol => session.Symbols.Resolver.GetValue(symbol).Value.BoxedValue));
    }

    private static readonly (string, VBType) P = ("P", VBVariantType.TypeInfo);
    private static readonly (string, VBType) N = ("N", VBVariantType.TypeInfo);

    private static void AssertCompleted(RuntimeExecutionOutcome outcome)
        => Assert.AreEqual(RuntimeExecutionOutcomeKind.ExitProcedure, outcome.Kind, outcome.ErrorInfo?.Verbose);

    [TestMethod]
    public void Open_WithAVariantPath_OpensThatFile()
    {
        var (files, outcome, _) = Run([P], $"P = \"{Root}/out.txt\"", "Open P For Output As #1", "Print #1, \"x\"", "Close #1");

        AssertCompleted(outcome);
        Assert.IsTrue(files.File.Exists($"{Root}/out.txt"));
    }

    [TestMethod]
    public void AFileNumber_ThatIsAVariant_IsTheNumberItHolds()
    {
        var (files, outcome, _) = Run([N], "N = 3", $"Open \"{Root}/out.txt\" For Output As #N", "Print #N, \"hello\"", "Close #N");

        AssertCompleted(outcome);
        StringAssert.Contains(files.File.ReadAllText($"{Root}/out.txt"), "hello");
    }

    [TestMethod]
    public void Seek_WithAVariantPosition_IsThePositionItHolds()
    {
        var (_, outcome, _) = Run([N], "N = 3", $"Open \"{Root}/in.txt\" For Input As #1", "Seek #1, N", "Close #1");

        AssertCompleted(outcome);
    }

    [TestMethod]
    public void Width_WithAVariantLineWidth_IsTheWidthItHolds()
    {
        var (_, outcome, _) = Run([N], "N = 20", $"Open \"{Root}/out.txt\" For Output As #1", "Width #1, N", "Close #1");

        AssertCompleted(outcome);
    }

    [TestMethod]
    public void ReDim_WithAVariantBound_IsTheBoundItHolds()
    {
        var (_, outcome, _) = Run([N, ("A", new VBResizableArrayType(VBLongType.TypeInfo))], "N = 2", "ReDim A(N)");

        AssertCompleted(outcome);
    }

    [TestMethod]
    public void Mid_WithAVariantPositionAndLength_AreTheNumbersTheyHold()
    {
        var (_, outcome, values) = Run([N, ("L", VBVariantType.TypeInfo), ("S", VBStringType.TypeInfo)],
            "S = \"abcdef\"", "N = 2", "L = 3", "Mid(S, N, L) = \"XYZ\"");

        AssertCompleted(outcome);
        Assert.AreEqual("aXYZef", values["S"]);
    }

    [TestMethod]
    public void Error_WithAVariantNumber_RaisesThatError()
    {
        var (_, outcome, _) = Run([N], "N = 11", "Error N");

        Assert.AreEqual(RuntimeExecutionOutcomeKind.Error, outcome.Kind);
        Assert.AreEqual(11, outcome.ErrorInfo!.ErrorId);
    }

    [TestMethod]
    public void AVariantThatHoldsNull_IsInvalidUseOfNull_NotACrash()
    {
        var (_, outcome, _) = Run([P], "P = Null", "Open P For Output As #1");

        Assert.AreEqual(RuntimeExecutionOutcomeKind.Error, outcome.Kind);
        Assert.AreEqual(94, outcome.ErrorInfo!.ErrorId);
    }
}
