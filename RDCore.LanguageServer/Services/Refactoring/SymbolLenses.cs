using RDCore.Runtime.LanguageCore.Refactoring;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Semantics.Refactoring;

namespace RDCore.LanguageServer.Services.Refactoring;

public sealed record class SymbolLens(string Name, SourceRange NameRange);

// a function's own result, assigned or read in its body, is no reference of it; a recursive call is.
// MS-VBAL §5.2.3, §5.3
public static class SymbolLenses
{
    public static IReadOnlyList<SymbolLens> In(RenameModule module)
        => [.. module.Syntax.Descendants()
            .OfType<MemberDeclarationNode>()
            .Where(member => member.MemberKind is not (MemberKind.ModuleField or MemberKind.UserDefinedTypeField) && member.NameRange is not null)
            .Select(member => new SymbolLens(member.Name, member.NameRange!.Value))];

    public static IReadOnlyList<SymbolSite> ReferencesOf(RenameContext context, Uri module, SourcePosition namePosition)
        => SymbolNavigation.ReferencesAt(context, module, namePosition, includeDeclaration: false)?.Sites ?? [];
}
