using RDCore.Runtime.LanguageCore.Refactoring;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Semantics.Refactoring;

namespace RDCore.LanguageServer.Services.Refactoring;

// what a lens above a member's name counts or says: the names that refer to the member, the procedures that implement it, or the member of an interface the
// procedure implements.
public enum SymbolLensKind
{
    References,
    Implementations,
    Implements,
}

// the member of an interface an Implements lens names: its class, its name, and where it is declared.
public sealed record class ImplementedMember(string Interface, string Member, SymbolSite Declaration);

public sealed record class SymbolLens(string Name, SourceRange NameRange)
{
    public SymbolLensKind Kind { get; init; } = SymbolLensKind.References;

    public ImplementedMember? Implemented { get; init; }
}

// a function's own result, assigned or read in its body, is no reference of it; a recursive call is.
// MS-VBAL §5.2.3, §5.3
public static class SymbolLenses
{
    public static IReadOnlyList<SymbolLens> In(RenameModule module)
        => [.. module.Syntax.Descendants()
            .OfType<MemberDeclarationNode>()
            .Where(member => member.MemberKind is not (MemberKind.ModuleField or MemberKind.UserDefinedTypeField) && member.NameRange is not null)
            .Select(member => new SymbolLens(member.Name, member.NameRange!.Value))];

    // the lenses of each member, and besides its references (MS-VBAL §5.2.4.2): of a member of a class other classes implement, how many procedures
    // implement it; of a procedure that implements a member of an interface, which member, one lens for each.
    public static IReadOnlyList<SymbolLens> In(RenameContext context, RenameModule module)
    {
        var lenses = new List<SymbolLens>();
        foreach (var lens in In(module))
        {
            lenses.Add(lens);
            if (SymbolNavigation.IsInterfaceMemberAt(context, module.Module, lens.NameRange.Start))
            {
                lenses.Add(lens with { Kind = SymbolLensKind.Implementations });
            }

            lenses.AddRange(SymbolNavigation.ImplementedAt(context, module.Module, lens.NameRange.Start)
                .Select(implemented => lens with { Kind = SymbolLensKind.Implements, Implemented = implemented }));
        }

        return lenses;
    }

    public static IReadOnlyList<SymbolSite> ReferencesOf(RenameContext context, Uri module, SourcePosition namePosition)
        => SymbolNavigation.ReferencesAt(context, module, namePosition, includeDeclaration: false)?.Sites ?? [];

    // the procedures that implement the member, as Go to Implementation finds them.
    public static IReadOnlyList<SymbolSite> ImplementationsOf(RenameContext context, Uri module, SourcePosition namePosition)
        => SymbolNavigation.ImplementationsAt(context, module, namePosition)?.Sites ?? [];
}
