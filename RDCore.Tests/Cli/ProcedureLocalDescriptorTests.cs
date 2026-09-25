using RDCore.LanguageServer.Symbols;
using RDCore.Parsing;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Platform.Protocol;

namespace RDCore.Tests.Cli;

/// <summary>
/// A procedure's <c>Dim</c>/<c>Static</c> variables have to reach the environment host, because the
/// procedure symbol is what an activation allocates its frame storage from
/// (<strong>MS-VBAL §5.4.3</strong> step 4). A procedure that arrives without them has a body that
/// cannot assign to any of its own locals — so this covers every stage between the AST and the
/// reconstructed symbol, not just the ends.
/// </summary>
[TestClass]
public sealed class ProcedureLocalDescriptorTests
{
    private static readonly Uri WorkspaceRoot = new("file:///c:/ws/");
    private static readonly Uri ModuleUri = new UriBuilder(WorkspaceRoot) { Fragment = "Program" }.Uri;

    private const string Source = "Attribute VB_Name = \"Program\"\r\n"
        + "Public Sub Main()\r\n"
        + "10 Dim Counter As Long\r\n"
        + "20 Counter = 1\r\n"
        + "End Sub\r\n";

    private static IReadOnlyList<Symbol> Symbols()
    {
        var parse = new ModuleParser().Parse(new Uri("file:///c:/ws/Program.bas"), Source);
        Assert.IsTrue(parse.IsSuccess, string.Join("; ", parse.SyntaxErrors.Select(error => error.Verbose)));

        return [.. new SyntaxTreeSymbolProvider(
            WorkspaceRoot, ModuleUri, ModuleType.StdModule, parse, new IntrinsicSymbolResolver()).ProvideSymbols()];
    }

    [TestMethod]
    public void TheSymbolProvider_YieldsTheLocal_ParentedToItsProcedure()
    {
        var symbols = Symbols();

        var procedure = symbols.OfType<VBProcedureMemberSymbol>().Single(symbol => symbol.Name == "Main");
        var local = symbols.OfType<VBLocalVariableSymbol>().SingleOrDefault(symbol => symbol.Name == "Counter");

        Assert.IsNotNull(local, "the Dim never became a symbol at all");
        Assert.AreEqual(procedure.Uri.ToString(), local.ParentUri.ToString());
    }

    [TestMethod]
    public void TheProjector_CarriesTheLocalOnItsProcedureDescriptor()
    {
        var descriptors = SymbolDescriptorProjector.Project(Symbols(), ModuleUri);

        var main = descriptors.Single(descriptor => descriptor.Name == "Main");
        var local = main.Locals.SingleOrDefault(descriptor => descriptor.Name == "Counter");

        Assert.IsNotNull(local, "the local was dropped on the way to the descriptor");
        Assert.AreEqual("Long", local.DeclaredTypeName);
        Assert.IsFalse(local.IsStatic);
    }

    [TestMethod]
    public void TheReader_ReconstructsTheLocalOntoTheProcedureSymbol()
    {
        var descriptors = SymbolDescriptorProjector.Project(Symbols(), ModuleUri);

        var reconstructed = SymbolDescriptorReader.Read(new DefineSymbolsParams
        {
            WorkspaceRoot = WorkspaceRoot,
            ModuleUri = ModuleUri,
            ModuleName = "Program",
            Symbols = descriptors,
        }, name => IntrinsicVBTypes.TryResolve(name, out var type) ? type : null).ToArray();

        var main = reconstructed.OfType<VBProcedureMemberSymbol>().Single(symbol => symbol.Name == "Main");
        var local = main.Locals.OfType<VBLocalVariableSymbol>().SingleOrDefault(symbol => symbol.Name == "Counter");

        Assert.IsNotNull(local, "the local never made it back onto the procedure symbol");
        Assert.AreEqual(VBLongType.TypeInfo, local.ResolvedType);
        Assert.AreEqual(ScopeKind.Local, local.ScopeKind);
    }
}
