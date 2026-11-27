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

    // the lenses of each member: its references, and (MS-VBAL §5.2.4.2) for a member of a class other classes implement, how many procedures implement
    // it; for a procedure that implements members of interfaces, one lens for each member it implements.
    public static IReadOnlyList<SymbolLens> In(RenameContext context, RenameModule module)
    {
        var lenses = new List<SymbolLens>();
        foreach (var lens in In(module))
        {
            lenses.Add(lens);
            if (SymbolNavigation.IsInterfaceMemberAt(context, module.Module, lens.NameRange.Start, module.Document))
            {
                lenses.Add(lens with { Kind = SymbolLensKind.Implementations });
            }

            lenses.AddRange(SymbolNavigation.ImplementedAt(context, module.Module, lens.NameRange.Start, module.Document)
                .Select(implemented => lens with { Kind = SymbolLensKind.Implements, Implemented = implemented }));
        }

        return lenses;
    }

    // the document names the part of a module of parts (RD-VBAL §5.1) the name is in; null for the first part at the address.
    public static IReadOnlyList<SymbolSite> ReferencesOf(RenameContext context, Uri module, SourcePosition namePosition, Uri? document = null)
        => SymbolNavigation.UsersAt(context, module, namePosition, document)?.Sites ?? [];

    // the procedures that implement the member, as Go to Implementation finds them.
    public static IReadOnlyList<SymbolSite> ImplementationsOf(RenameContext context, Uri module, SourcePosition namePosition, Uri? document = null)
        => SymbolNavigation.ImplementationsAt(context, module, namePosition, document)?.Sites ?? [];
}
