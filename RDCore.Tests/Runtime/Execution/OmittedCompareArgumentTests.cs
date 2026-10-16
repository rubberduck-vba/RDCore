using RDCore.Runtime.Execution;
using RDCore.SDK.Model;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Complex;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Runtime.Abstract.Execution;

namespace RDCore.Tests.Runtime.Execution;

/// <summary>
/// A comparing member's omitted <c>Compare</c> takes the <c>Option Compare</c> of the calling module (<strong>MS-VBAL §6.1.2.11</strong>:
/// "if omitted, it uses the &lt;option-compare-directive&gt; of the calling module"), where its declaration says <c>= vbBinaryCompare</c>.
/// </summary>
/// <remarks>
/// The parameter is a hand-built one carrying <see cref="SymbolProperties.DefaultsToOptionCompare"/>, the flag the standard library's reader
/// stamps, on a procedure of the workspace's: what fills the omitted argument is the call site's, whoever is called.
/// </remarks>
[TestClass]
[TestCategory("MS-VBAL 5.3.1.11 Procedure Invocation Argument Processing")]
public sealed class OmittedCompareArgumentTests
{
    private static readonly Uri Workspace = TestUri.WorkspaceRoot();
    private static readonly SourceRange R = SourceRange.Empty;

    // Function Probe(Optional ByVal Compare As Long = 0) As Long, whose Compare takes the caller's Option Compare when omitted.
    private static (VBTypeMemberSymbol Symbol, string[] Body) Probe()
    {
        var stub = new VBFunctionMemberSymbol(Workspace, RuntimeSourceHarness.ModuleUri, "Probe", ScopeKind.Module,
            SymbolKindExt.Function, VBLongType.TypeInfo, R, R, AccessModifier.Public);
        var compare = (VBParameterSymbol)new VBParameterSymbol(Workspace, stub.Uri, "Compare", R, R, ParameterKind.ExplicitByVal, VBLongType.TypeInfo,
            IsOptional: true, DefaultValue: TestExpressions.Literal(new VBLongValue(0))).With(SymbolProperties.DefaultsToOptionCompare, true);
        return (stub with { Parameters = [compare] }, ["Probe = Compare"]);
    }

    // Function Outer(ByVal Value As Long) As Long, which returns what it is given.
    private static (VBTypeMemberSymbol Symbol, string[] Body) Outer()
    {
        var stub = new VBFunctionMemberSymbol(Workspace, RuntimeSourceHarness.ModuleUri, "Outer", ScopeKind.Module,
            SymbolKindExt.Function, VBLongType.TypeInfo, R, R, AccessModifier.Public);
        return (stub with { Parameters = [new VBParameterSymbol(Workspace, stub.Uri, "Value", R, R, ParameterKind.ExplicitByVal, VBLongType.TypeInfo)] },
            ["Outer = Value"]);
    }

    private static IReadOnlyList<string> Run(OptionCompare? mode, IEnumerable<Symbol> symbols, IReadOnlyList<(VBTypeMemberSymbol, string[])> procedures, params string[] body)
    {
        var output = new RuntimeOutputBuffer();
        var (_, outcome) = RuntimeSourceHarness.Run(
            fileSystem: null, symbols, output, standardLibrary: false, arrange: null,
            new RuntimeSourceHarness.Surroundings
            {
                Directives = mode is { } compare ? new ModuleDirectives(Compare: compare) : ModuleDirectives.None,
                Procedures = [Probe(), Outer(), .. procedures],
            },
            body);

        Assert.AreEqual(RuntimeExecutionOutcomeKind.ExitProcedure, outcome.Kind, outcome.ErrorInfo?.Verbose);
        return [.. output.Lines.Select(line => line.Trim())];
    }

    [TestMethod]
    [DataRow(null, "0", DisplayName = "no Option Compare")]
    [DataRow(OptionCompare.Binary, "0", DisplayName = "Option Compare Binary")]
    [DataRow(OptionCompare.Text, "1", DisplayName = "Option Compare Text")]
    // the environment's DatabaseCompare, which is Text unless it says otherwise, as the comparison operators read it.
    [DataRow(OptionCompare.Database, "1", DisplayName = "Option Compare Database")]
    public void AnOmittedCompare_IsTheCallingModulesOptionCompare(OptionCompare? mode, string expected)
        => CollectionAssert.AreEqual(new[] { expected }, Run(mode, [], [], "Debug.Print Probe()").ToArray());

    [TestMethod]
    public void AnExplicitCompare_IsNeverReplaced()
        => CollectionAssert.AreEqual(new[] { "0" }, Run(OptionCompare.Text, [], [], "Debug.Print Probe(0)").ToArray());

    [TestMethod]
    public void AnOmittedCompare_InAnotherCallsArguments_IsStillTheCallers()
        // the inner call's arguments are bound while the outer call's are, before either callee's frame is pushed.
        => CollectionAssert.AreEqual(new[] { "1" }, Run(OptionCompare.Text, [], [], "Debug.Print Outer(Probe())").ToArray());

    [TestMethod]
    public void AnOmittedCompare_InAClassModulesCode_IsThatClassModulesOptionCompare()
    {
        // Widget states Option Compare Text; the module the body and Probe are declared in states none. Probe called from Widget's code
        // takes Widget's, and called from the body takes the body's: the caller's, never the declaring module's of what is called.
        var widget = new VBClassModuleSymbol(Workspace, Workspace, "Widget") { Directives = new ModuleDirectives(Compare: OptionCompare.Text) };
        var go = new VBProcedureMemberSymbol(Workspace, widget.Uri, "Go", ScopeKind.Instance, SymbolKindExt.Procedure, VBVoidType.TypeInfo, R, R, AccessModifier.Public);
        go = go with { Parameters = [new VBParameterSymbol(Workspace, go.Uri, "Me", R, R, ParameterKind.ImplicitByRef, VBObjectType.TypeInfo)] };
        widget = widget with { Members = [go], DefaultInterfaceMembers = [go] };
        var w = new VBModuleFieldVariableMemberSymbol(Workspace, RuntimeSourceHarness.ModuleUri, "W", ScopeKind.Module,
            VBClassType.FromClassModule(widget), R, R, AccessModifier.Private);

        var output = Run(null, [widget, go, w], [(go, ["Debug.Print Probe()"])], "Set W = New Widget", "W.Go", "Debug.Print Probe()");

        CollectionAssert.AreEqual(new[] { "1", "0" }, output.ToArray());
    }
}
