using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.Source;

namespace RDCore.SDK.Semantics.Refactoring;

/// <summary>
/// A member of a module that a client shows the references of above its declaration (<strong>LSP 3.17</strong> §Code Lens Request).
/// </summary>
/// <param name="Name">The name the member is declared with.</param>
/// <param name="NameRange">Where the name is written in the module: what the references of the member are asked for at.</param>
public sealed record class SymbolLens(string Name, SourceRange NameRange);

/// <summary>
/// The members of a module whose references a client shows (<strong>LSP 3.17</strong> §Code Lens Request), and those references: what
/// <see cref="SymbolNavigation.ReferencesAt"/> finds of each, its declarations not included.
/// </summary>
/// <remarks>
/// A member is a procedure (<c>Sub</c>, <c>Function</c>, and each procedure of a property), a <c>Declare</c> statement, an <c>Event</c>, a <c>Type</c> or an
/// <c>Enum</c> (<strong>MS-VBAL §5.2.3</strong>, §5.3). A variable or a constant of the module is not one, nor is a field of a <c>Type</c> or a member of an
/// <c>Enum</c>. The procedures of a property are one symbol, and each says the references of all of them.
/// </remarks>
public static class SymbolLenses
{
    /// <summary>
    /// The members <paramref name="module"/> declares, in the order they are written.
    /// </summary>
    /// <param name="module">The module.</param>
    /// <returns>Each member whose name is written in the module: a member the parser could not place the name of has none.</returns>
    public static IReadOnlyList<SymbolLens> In(RenameModule module)
        => [.. module.Syntax.Descendants()
            .OfType<MemberDeclarationNode>()
            .Where(member => member.MemberKind is not (MemberKind.ModuleField or MemberKind.UserDefinedTypeField) && member.NameRange is not null)
            .Select(member => new SymbolLens(member.Name, member.NameRange!.Value))];

    /// <summary>
    /// The names that refer to the member a lens is of, in every module of the workspace.
    /// </summary>
    /// <param name="context">The workspace.</param>
    /// <param name="module">The module the lens is of.</param>
    /// <param name="namePosition">Where the name of the member is written: the start of <see cref="SymbolLens.NameRange"/>.</param>
    /// <returns>Each name, as a site whose range is the name, by module and then in the order written; none where what the name refers to is not known.</returns>
    public static IReadOnlyList<SymbolSite> ReferencesOf(RenameContext context, Uri module, SourcePosition namePosition)
        => SymbolNavigation.ReferencesAt(context, module, namePosition, includeDeclaration: false)?.Sites ?? [];
}
