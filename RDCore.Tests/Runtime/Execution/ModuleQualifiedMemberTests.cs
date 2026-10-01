using RDCore.SDK.Model;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Runtime.Abstract.Execution;

namespace RDCore.Tests.Runtime.Execution;

/// <summary>
/// A member access whose left-hand side names a project or a procedural module rather than a value
/// (<strong>MS-VBAL §5.6.12</strong>): <c>Strings.LenB("42")</c>, <c>VBA.Strings.LenB("42")</c>, <c>VBA.LenB("42")</c>.
/// </summary>
/// <remarks>
/// Such a name is not an object, so there is nothing to evaluate and nothing to call the member on: it is resolved in
/// the namespace and means what it would unqualified. Only the unqualified forms worked, and every qualified one was an
/// internal error.
/// </remarks>
[TestClass]
[TestCategory("MS-VBAL 5.6.12 Member Access Expressions")]
public sealed class ModuleQualifiedMemberTests
{
    private static string[] Run(params string[] body)
    {
        var output = new RuntimeOutputBuffer();
        RuntimeSourceHarness.Run(fileSystem: null, [], output, standardLibrary: true, body);
        return [.. output.Lines.Select(line => line.Trim())];
    }

    [TestMethod]
    [DataRow("LenB(\"42\")", "4", DisplayName = "unqualified, as it always worked")]
    [DataRow("Strings.LenB(\"42\")", "4", DisplayName = "module-qualified")]
    [DataRow("VBA.Strings.LenB(\"42\")", "4", DisplayName = "project- and module-qualified")]
    [DataRow("VBA.LenB(\"42\")", "4", DisplayName = "project-qualified, with no module named")]
    [DataRow("Strings.Len(\"42\")", "2")]
    [DataRow("Conversion.CInt(\"7\")", "7")]
    [DataRow("VBA.Conversion.CInt(\"7\")", "7")]
    [DataRow("VBA.CInt(\"7\")", "7")]
    public void AQualifiedCall_IsTheCallItNames(string expression, string expected)
        => CollectionAssert.AreEqual(new[] { expected }, Run($"Debug.Print {expression}"));

    [TestMethod]
    [DataRow("Information.Erl")]
    [DataRow("VBA.Information.Erl")]
    [DataRow("VBA.Erl")]
    public void AQualifiedMemberWithNoArguments_IsCalledWithNone(string expression)
        => CollectionAssert.AreEqual(new[] { "0" }, Run($"Debug.Print {expression}"));

    [TestMethod]
    public void AQualifiedCall_IsAnOperandLikeAnyOther()
        => CollectionAssert.AreEqual(new[] { "6" }, Run("Debug.Print Strings.LenB(\"a\") + VBA.LenB(\"bb\")"));

    [TestMethod]
    public void AQualifiedCall_TakesItsArgumentsFromTheCallSite()
        // Len("12345") is 5, which as a String is one character of two bytes.
        => CollectionAssert.AreEqual(new[] { "2" }, Run("Debug.Print VBA.Strings.LenB(Strings.Len(\"12345\") & \"\")"));

    [TestMethod]
    public void ALocalOfTheSameName_IsWhatTheNameMeans()
    {
        // the name is resolved as any other is: what is nearer than the module is what it refers to.
        CollectionAssert.AreEqual(new[] { "5" }, Run("Dim Strings As Long", "Strings = 5", "Debug.Print Strings"));
    }

    [TestMethod]
    public void AWorkspaceModulesOwnVariable_IsReachedThroughItsName()
    {
        // `TestModule1` is the harness's module: a public variable of it, written bare and read qualified.
        var counter = new VBModuleFieldVariableMemberSymbol(
            TestUri.WorkspaceRoot(), RuntimeSourceHarness.ModuleUri, "Counter", ScopeKind.Module, VBLongType.TypeInfo,
            SourceRange.Empty, SourceRange.Empty, AccessModifier.Public);

        var output = new RuntimeOutputBuffer();
        RuntimeSourceHarness.Run(
            fileSystem: null, [counter], output, standardLibrary: true,
            "Counter = 41", "Debug.Print TestModule1.Counter + 1");

        CollectionAssert.AreEqual(new[] { "42" }, output.Lines.Select(line => line.Trim()).ToArray());
    }
}
