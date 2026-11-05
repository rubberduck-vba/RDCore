using RDCore.SDK.Model;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types.Complex;
using RDCore.SDK.Workspace;

namespace RDCore.Tests.Workspace;

/// <summary>
/// The kind of module an entry point is in, judged of the symbols themselves. A runtime session gives a class's procedures its object as their first parameter
/// (<c>Me</c>), so no class, form or document of one has a <c>Sub Main()</c> that takes nothing: <c>HostEntryPointsHandlerTests</c> cannot tell the module's
/// kind from that, and this does.
/// </summary>
[TestClass]
public sealed class ProgramEntryPointTests
{
    private static readonly Uri WorkspaceRoot = new("file:///c:/ws/");

    private static VBProcedureMemberSymbol SubMain(Uri module)
        => new(WorkspaceRoot, module, "Main", ScopeKind.Module, SymbolKindExt.Procedure, VBVoidType.TypeInfo, new SourceRange(1, 0, 2, 7), new SourceRange(1, 11, 1, 15),
            AccessModifier.Public);

    [TestMethod]
    public void ASubMainOfAStandardModule_IsWhereTheProgramStarts()
    {
        var module = new VBStandardModuleSymbol(WorkspaceRoot, WorkspaceRoot, "Program");

        Assert.IsTrue(ProgramEntryPoint.IsDeclaredBy(module, [SubMain(module.Uri)]));
    }

    [TestMethod]
    public void ASubMainOfAClass_IsNot_EvenOneThatTakesNoArgument()
    {
        var module = new VBClassModuleSymbol(WorkspaceRoot, WorkspaceRoot, "Helper");

        Assert.IsFalse(ProgramEntryPoint.IsDeclaredBy(module, [SubMain(module.Uri)]));
    }
}
