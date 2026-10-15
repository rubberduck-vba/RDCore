using RDCore.Runtime.Execution;
using RDCore.SDK.Model;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Runtime.Abstract.Execution;

namespace RDCore.Tests.Runtime.StdLib;

/// <summary>
/// <strong>MS-VBAL §6.1.2.2</strong> the <c>Constants</c> module, read from VBA source: eleven <c>Const</c> members,
/// resolvable unqualified, each folded at its use site to the value the specification's table gives it.
/// </summary>
[TestClass]
[TestCategory("MS-VBAL 6.1.2.2 Constants Module")]
public sealed class ConstantsModuleTests
{
    /// <summary>Runs a body with the standard library available, and reports what it printed.</summary>
    private static IReadOnlyList<string> Run(params string[] body)
    {
        var output = new RuntimeOutputBuffer();

        var (_, outcome) = RuntimeSourceHarness.Run(fileSystem: null, [], output, standardLibrary: true, body);

        Assert.AreEqual(RuntimeExecutionOutcomeKind.ExitProcedure, outcome.Kind,
            $"{outcome.ErrorInfo?.ErrorId} {outcome.ErrorInfo?.Description} | {outcome.ErrorInfo?.Verbose}");
        return output.Lines;
    }

    [TestMethod]
    [DataRow("vbBack", "1")]
    [DataRow("vbCr", "1")]
    [DataRow("vbCrLf", "2")]
    [DataRow("vbFormFeed", "1")]
    [DataRow("vbLf", "1")]
    [DataRow("vbNewLine", "2")]
    [DataRow("vbNullChar", "1")]
    [DataRow("vbTab", "1")]
    [DataRow("vbVerticalTab", "1")]
    public void ACharacterConstant_IsAsLongAsItsCharacters(string constant, string expected)
        => Assert.AreEqual(expected, Run($"Debug.Print Len({constant})").Single().Trim());

    [TestMethod]
    [DataRow("Constants.vbTab", "1")]
    [DataRow("VBA.vbCrLf", "2")]
    [DataRow("VBA.Constants.vbCrLf", "2")]
    public void AQualifiedConstant_FoldsAsAnUnqualifiedOneDoes(string constant, string expected)
    {
        // MS-VBAL §5.6.12: qualified by its module or its project, a constant means what it would unqualified.
        Assert.AreEqual(expected, Run($"Debug.Print Len({constant})").Single().Trim());
    }

    [TestMethod]
    public void vbTab_ConcatenatesAsATab()
        => Assert.AreEqual("a\tb", Run("Debug.Print \"a\" & vbTab & \"b\"").Single().Trim());

    [TestMethod]
    public void AConstant_IsAssignableToAStringVariable()
    {
        var output = Run("Dim X As String", "X = vbCrLf", "Debug.Print Len(X)");

        Assert.AreEqual("2", output.Single().Trim());
    }

    [TestMethod]
    public void vbNullString_EqualsAZeroLengthString_AndHasNoLength()
    {
        // the pointer is what differs, not the characters.
        var output = Run("Debug.Print (vbNullString = \"\")", "Debug.Print Len(vbNullString)");

        CollectionAssert.AreEqual(new[] { "True", "0" }, output.Select(line => line.Trim()).ToArray());
    }

    [TestMethod]
    public void vbNullString_ReachesACallAsANullPointer()
    {
        // the fold hands the declared value itself to the use site, so what Len receives is the String bound to no
        // data, where "" is bound to a zero-length string.
        RuntimeSourceHarness.RecordingDispatcher? spy = null;

        RuntimeSourceHarness.Run(
            fileSystem: null, [], output: null, standardLibrary: true, arrange: null,
            new RuntimeSourceHarness.Surroundings { Dispatcher = inner => spy = new RuntimeSourceHarness.RecordingDispatcher(inner) },
            "Debug.Print Len(vbNullString)", "Debug.Print Len(\"\")");

        Assert.IsNotNull(spy);
        Assert.IsNull(spy.Requests[0].Arguments[0].BoxedValue);
        Assert.AreEqual(string.Empty, spy.Requests[1].Arguments[0].BoxedValue);
    }

    [TestMethod]
    public void vbObjectError_IsItsLongValue()
    {
        // a Long, so adding to it is Long arithmetic: an error number an object raises is vbObjectError + n.
        var output = Run("Debug.Print vbObjectError", "Debug.Print vbObjectError + 513");

        CollectionAssert.AreEqual(new[] { "-2147221504", "-2147220991" }, output.Select(line => line.Trim()).ToArray());
    }

    [TestMethod]
    public void AWorkspaceConstantOfTheSameName_ShadowsTheLibrarys()
    {
        // the workspace's own name outranks a library's at the project tier, so a program that declares a Public Const
        // vbCrLf of its own, in another module, means that one rather than an ambiguous name.
        var workspace = TestUri.WorkspaceRoot();
        var other = new VBStandardModuleSymbol(workspace, workspace, "Other");
        var value = new LiteralExpressionNode(
            new SyntaxNodeId(other.Uri.AbsolutePath, []), new SourceLocation(other.Uri, SourceRange.Empty), new VBLongValue(7));
        var constant = new VBConstantMemberSymbol(workspace, other.Uri, "vbCrLf", ScopeKind.Module, VBLongType.TypeInfo,
            SourceRange.Empty, SourceRange.Empty, AccessModifier.Public, value);
        var output = new RuntimeOutputBuffer();

        var (_, outcome) = RuntimeSourceHarness.Run(fileSystem: null, [other, constant], output, standardLibrary: true, "Debug.Print vbCrLf");

        Assert.AreEqual(RuntimeExecutionOutcomeKind.ExitProcedure, outcome.Kind, outcome.ErrorInfo?.Verbose);
        Assert.AreEqual("7", output.Lines.Single().Trim());
    }
}
