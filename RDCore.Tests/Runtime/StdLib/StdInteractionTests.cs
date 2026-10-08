using RDCore.Runtime.Execution;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Complex;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Abstract.StdLib;
using RDCore.SDK.Runtime.StdLib;

namespace RDCore.Tests.Runtime.StdLib;

/// <summary>
/// The <c>VBA.Interaction</c> module (<strong>MS-VBAL §6.1.2.8</strong>). <c>DoEvents</c> and <c>Beep</c> have nothing to reach on the platform - no event queue to
/// yield to, no speaker to sound - and so they run; the rest are declared, and resolve, and say they cannot be answered yet.
/// </summary>
[TestClass]
public sealed class StdInteractionTests
{
    private static string[] Run(params string[] body)
    {
        var output = new RuntimeOutputBuffer();
        var (_, outcome) = RuntimeSourceHarness.Run(null, [], output, standardLibrary: true, arrange: null, ModuleDirectives.None, body);
        Assert.AreEqual(RuntimeExecutionOutcomeKind.ExitProcedure, outcome.Kind, outcome.ErrorInfo?.Description);
        return [.. output.Lines.Select(line => line.Trim())];
    }

    private static string Fail(params string[] body)
    {
        var (_, outcome) = RuntimeSourceHarness.Run(null, [], new RuntimeOutputBuffer(), standardLibrary: true, arrange: null, ModuleDirectives.None, body);
        Assert.AreEqual(RuntimeExecutionOutcomeKind.Error, outcome.Kind, "an error that says what it is, and not an internal one");
        return outcome.ErrorInfo!.Verbose;
    }

    // ---- DoEvents ----

    [TestMethod]
    public void DoEvents_IsAFunction_AnswersZero_WhenNothingIsOpen()
        => CollectionAssert.AreEqual(new[] { "0" }, Run("Debug.Print DoEvents()"));

    [TestMethod]
    public void DoEvents_IsAStatementToo_AsAFunctionIsCalledForItsEffect()
        => CollectionAssert.AreEqual(new[] { "done" }, Run("DoEvents", "Debug.Print \"done\""));

    [TestMethod]
    public void DoEvents_IsFoundByModuleAndByLibrary_WhateverTheCase()
        => CollectionAssert.AreEqual(new[] { "0", "0" }, Run("Debug.Print Interaction.DoEvents()", "Debug.Print VBA.doevents()"));

    // ---- Beep ----

    [TestMethod]
    public void Beep_IsASub_ThatHasNothingToSound_AndIsNoError()
        => CollectionAssert.AreEqual(new[] { "done" }, Run("Beep", "VBA.Beep", "Debug.Print \"done\""));

    [TestMethod]
    public void Beep_IsNotAVariable_ThatTheNameDeclaresByBeingUsed()
    {
        // an unbound name in a module with no Option Explicit is a declaration (MS-VBAL 5.6.10): which is what Beep used to be.
        CollectionAssert.AreEqual(new[] { "done" }, Run("Beep", "Debug.Print \"done\""));
    }

    [TestMethod]
    public async Task DoEventsAndBeep_AreStatementsOfTheShellToo_NotVariablesItDeclares()
    {
        var result = await RDCore.Tests.Cli.ShellHost.Compose().RunAsync([(10, "DOEVENTS"), (20, "BEEP"), (30, "PRINT DOEVENTS")]);

        Assert.AreEqual(RDCore.SDK.Platform.Protocol.ExecutionOutcome.Completed, result.Outcome, result.ErrorMessage);
        CollectionAssert.AreEqual(new[] { "0" }, result.Output.Select(line => line.Trim()).ToArray());
    }

    // ---- the rest ----

    [TestMethod]
    [DataRow("MsgBox \"x\"", "MsgBox")]
    [DataRow("Debug.Print InputBox(\"x\")", "InputBox")]
    [DataRow("Debug.Print Environ(\"PATH\")", "Environ")]
    [DataRow("SendKeys \"a\"", "SendKeys")]
    [DataRow("SaveSetting \"a\", \"b\", \"c\", \"d\"", "SaveSetting")]
    [DataRow("AppActivate \"a\"", "AppActivate")]
    [DataRow("Debug.Print Shell(\"a\")", "Shell")]
    public void AMemberThatNothingImplementsYet_SaysSo_ByNameInAnErrorOfItsOwn(string statement, string member)
        => StringAssert.Contains(Fail(statement), member);

    // ---- what the declaration says ----

    private static readonly Uri Root = new("file://rdcore-test");

    private static IReadOnlyList<Symbol> Read() => new StdLibSymbolReader(Root).Read(typeof(IStdInteractionModule).Assembly);

    private static Symbol Member(string name)
    {
        var module = Read().OfType<VBStandardModuleSymbol>().Single(candidate => candidate.Name == "Interaction");
        return Read().Where(symbol => symbol.ParentUri.AbsoluteUri == module.Uri.AbsoluteUri).Single(member => member.Name == name);
    }

    [TestMethod]
    [DataRow("CallByName")]
    [DataRow("Choose")]
    [DataRow("Command")]
    [DataRow("Command$")]
    [DataRow("CreateObject")]
    [DataRow("DoEvents")]
    [DataRow("Environ")]
    [DataRow("Environ$")]
    [DataRow("GetAllSettings")]
    [DataRow("GetAttr")]
    [DataRow("GetObject")]
    [DataRow("GetSetting")]
    [DataRow("IIf")]
    [DataRow("InputBox")]
    [DataRow("MsgBox")]
    [DataRow("Partition")]
    [DataRow("Shell")]
    [DataRow("Switch")]
    [DataRow("AppActivate")]
    [DataRow("Beep")]
    [DataRow("DeleteSetting")]
    [DataRow("SaveSetting")]
    [DataRow("SendKeys")]
    public void EveryMemberTheSpecificationNames_IsNamedAsItIsInSource(string name) => Assert.IsNotNull(Member(name));

    [TestMethod]
    public void TheSubroutines_AreSubs_AndTheFunctionsAreNot()
    {
        Assert.AreEqual(VBVoidType.TypeInfo, ((VBProcedureMemberSymbol)Member("Beep")).ResolvedType);
        Assert.AreEqual(VBIntegerType.TypeInfo, ((VBFunctionMemberSymbol)Member("DoEvents")).ResolvedType);
    }

    [TestMethod]
    public void MsgBox_AnswersAnEnum_AndGetAttrToo()
    {
        Assert.AreEqual("VbMsgBoxResult", ((VBFunctionMemberSymbol)Member("MsgBox")).ResolvedType.Name);
        Assert.AreEqual("VbFileAttribute", ((VBFunctionMemberSymbol)Member("GetAttr")).ResolvedType.Name);
    }
}
