using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.LanguageServer.Symbols;
using RDCore.SDK.Model;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Complex;
using RDCore.SDK.Server.ProtocolExtensions;

namespace RDCore.Tests.LanguageServer;

[TestClass]
public sealed class DocumentSymbolProjectorTests
{
    private static readonly Uri WorkspaceRoot = new("file:///c:/ws/");
    private static readonly Uri ModuleUri = new("file:///c:/ws/#Mod1");

    private static readonly SourceRange ModuleRange = new(0, 0, 10, 0);
    private static readonly SourceRange ModuleSelectionRange = new(0, 17, 0, 21);

    private static DocumentSymbol Project(params Symbol[] symbols)
        => DocumentSymbolProjector.Project(ModuleUri, "Mod1", SymbolKindExt.Module, ModuleRange, ModuleSelectionRange, symbols);

    private static SourceRange Range(int startLine, int startChar, int endLine, int endChar) => new(startLine, startChar, endLine, endChar);

    [TestMethod]
    public void AStandardModuleWithNoMembers_ProjectsAsOneTopLevelSymbolWithNoChildren()
    {
        var module = Project();

        Assert.AreEqual("Mod1", module.Name);
        Assert.AreEqual(SymbolKind.Module, module.Kind);
        Assert.IsNull(module.Children);
    }

    [TestMethod]
    public void TheModuleSymbol_CarriesTheCompleteSourceRangeAndSelectionRange()
    {
        var module = Project();

        Assert.AreEqual(ModuleRange.ToLsp(), module.Range);
        Assert.AreEqual(ModuleSelectionRange.ToLsp(), module.SelectionRange);
    }

    [TestMethod]
    public void AClassModule_ProjectsWithTheClassKind()
    {
        var module = DocumentSymbolProjector.Project(ModuleUri, "Mod1", SymbolKindExt.Class, ModuleRange, ModuleSelectionRange, []);

        Assert.AreEqual(SymbolKind.Class, module.Kind);
    }

    [TestMethod]
    public void AProcedure_BecomesAChildOfTheModuleWithItsOwnRanges()
    {
        var range = Range(1, 0, 3, 7);
        var selectionRange = Range(1, 11, 1, 16);
        var procedure = new VBProcedureMemberSymbol(WorkspaceRoot, ModuleUri, "Alpha", ScopeKind.Module, SymbolKindExt.Procedure,
            VBVoidType.TypeInfo, range, selectionRange, AccessModifier.Public);

        var module = Project(procedure);

        var child = module.Children!.Single();
        Assert.AreEqual("Alpha", child.Name);
        // SymbolKindExt.Procedure (6) is VBA's name for what LSP calls Method (6).
        Assert.AreEqual(SymbolKind.Method, child.Kind);
        Assert.AreEqual(range.ToLsp(), child.Range);
        Assert.AreEqual(selectionRange.ToLsp(), child.SelectionRange);
    }

    [TestMethod]
    public void EveryDeclaredMemberKind_ProjectsWithAValidMappedSymbolKind()
    {
        var symbols = new Symbol[]
        {
            new VBProcedureMemberSymbol(WorkspaceRoot, ModuleUri, "Alpha", ScopeKind.Module, SymbolKindExt.Procedure, VBVoidType.TypeInfo, Range(1, 0, 2, 7), Range(1, 11, 1, 16), AccessModifier.Public),
            new VBFunctionMemberSymbol(WorkspaceRoot, ModuleUri, "Beta", ScopeKind.Module, SymbolKindExt.Function, VBVoidType.TypeInfo, Range(3, 0, 4, 7), Range(3, 10, 3, 14), AccessModifier.Public),
            new VBPropertyGetMemberSymbol(WorkspaceRoot, ModuleUri, ScopeKind.Module, "Value", Range(5, 0, 6, 7), Range(5, 17, 5, 22), AccessModifier.Public),
            new VBEventMemberSymbol(WorkspaceRoot, ModuleUri, "Changed", ScopeKind.Module, Range(7, 0, 7, 25), Range(7, 13, 7, 20), AccessModifier.Public),
            new VBModuleFieldVariableMemberSymbol(WorkspaceRoot, ModuleUri, "mValue", ScopeKind.Module, VBUnknownType.TypeInfo, Range(8, 0, 8, 20), Range(8, 8, 8, 14), AccessModifier.Private),
            new VBConstantMemberSymbol(WorkspaceRoot, ModuleUri, "Limit", ScopeKind.Module, VBUnknownType.TypeInfo, Range(9, 0, 9, 25), Range(9, 13, 9, 18), AccessModifier.Public),
        };

        var module = Project(symbols);

        Assert.HasCount(6, module.Children!);
        foreach (var child in module.Children!)
        {
            Assert.IsTrue(Enum.IsDefined(child.Kind), $"{child.Name} projected an undefined SymbolKind '{child.Kind}'.");
        }
    }

    [TestMethod]
    public void AnEnum_NestsItsConstantsAsChildren()
    {
        var enumSymbol = new VBEnumMemberSymbol(WorkspaceRoot, ModuleUri, "Colour", ScopeKind.Module, SymbolKindExt.Enum,
            VBUnknownType.TypeInfo, Range(1, 0, 3, 7), Range(1, 12, 1, 18), AccessModifier.Public);
        var red = new VBEnumConstMemberSymbol(WorkspaceRoot, enumSymbol.Uri, "Red", ScopeKind.Module, SymbolKindExt.EnumMember,
            Range(2, 4, 2, 11), Range(2, 4, 2, 7));

        var module = Project(enumSymbol, red);

        var enumChild = module.Children!.Single();
        Assert.AreEqual(SymbolKind.Enum, enumChild.Kind);
        var member = enumChild.Children!.Single();
        Assert.AreEqual("Red", member.Name);
        Assert.AreEqual(SymbolKind.EnumMember, member.Kind);
    }

    [TestMethod]
    public void AUserDefinedType_NestsItsFieldsAsChildren()
    {
        var udt = new VBUserDefinedTypeMemberSymbol(WorkspaceRoot, ModuleUri, "TPoint", ScopeKind.Module,
            Range(1, 0, 4, 7), Range(1, 12, 1, 18), AccessModifier.Public);
        var field = new VBUserDefinedTypeFieldSymbol(WorkspaceRoot, udt.Uri, "X", VBUnknownType.TypeInfo,
            Range(2, 4, 2, 13), Range(2, 4, 2, 5), AccessModifier.Implicit);

        var module = Project(udt, field);

        var udtChild = module.Children!.Single();
        // SymbolKindExt.UserDefinedType (23) is VBA's name for what LSP calls Struct (23).
        Assert.AreEqual(SymbolKind.Struct, udtChild.Kind);
        var member = udtChild.Children!.Single();
        Assert.AreEqual("X", member.Name);
        Assert.AreEqual(SymbolKind.Field, member.Kind);
    }

    [TestMethod]
    public void AProcedureLocal_DoesNotAppearAnywhereInTheOutline()
    {
        // locals are parented to their owning procedure, not the module, and are not one of the
        // nestable kinds (Enum constants / UDT fields) — they must not appear anywhere in the outline.
        var procedure = new VBProcedureMemberSymbol(WorkspaceRoot, ModuleUri, "Alpha", ScopeKind.Module, SymbolKindExt.Procedure,
            VBVoidType.TypeInfo, Range(1, 0, 3, 7), Range(1, 11, 1, 16), AccessModifier.Public);
        var local = new VBModuleFieldVariableMemberSymbol(WorkspaceRoot, procedure.Uri, "i", ScopeKind.Local,
            VBUnknownType.TypeInfo, Range(2, 4, 2, 18), Range(2, 8, 2, 9), AccessModifier.Implicit);

        var module = Project(procedure, local);

        var alpha = module.Children!.Single();
        Assert.IsNull(alpha.Children);
    }

    [TestMethod]
    public void AnUnmappedExtensionKind_IsSkippedRatherThanProjectingAnInvalidSymbolKind()
    {
        var ignored = new VBModuleFieldVariableMemberSymbol(WorkspaceRoot, ModuleUri, "Hidden", ScopeKind.Module,
            VBUnknownType.TypeInfo, Range(1, 0, 1, 20), Range(1, 8, 1, 14), AccessModifier.Private) with
        {
            Kind = SymbolKindExt.Attribute,
        };

        var module = Project(ignored);

        Assert.IsNull(module.Children);
    }

    [TestMethod]
    public void ConditionalCompilationDefinitions_ProjectOnceAtThePrimaryRange()
    {
        // A name declared in more than one #If branch collapses into a single BoundSymbol whose
        // Definitions list each branch's site; the outline shows one entry, at the primary (live) site.
        var primaryRange = Range(1, 0, 1, 20);
        var primarySelection = Range(1, 8, 1, 13);
        var secondaryRange = Range(3, 0, 3, 20);
        var secondarySelection = Range(3, 8, 3, 13);
        var field = new VBModuleFieldVariableMemberSymbol(WorkspaceRoot, ModuleUri, "mFlag", ScopeKind.Module,
            VBUnknownType.TypeInfo, primaryRange, primarySelection, AccessModifier.Private)
        {
            Definitions =
            [
                new SymbolDefinition(primaryRange, primarySelection, DefinitionState.Live),
                new SymbolDefinition(secondaryRange, secondarySelection, DefinitionState.Unknown),
            ],
        };

        var module = Project(field);

        Assert.HasCount(1, module.Children!);
        var child = module.Children!.Single();
        Assert.AreEqual(primaryRange.ToLsp(), child.Range);
        Assert.AreEqual(primarySelection.ToLsp(), child.SelectionRange);
    }
}
