using RDCore.Runtime.LanguageCore.Refactoring;
using RDCore.SDK.Model;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;

namespace RDCore.LanguageServer.Services.Refactoring;

/// <summary>
/// Whether a name a refactoring writes is a name VBA accepts.
/// </summary>
internal enum NewNameValidity
{
    /// <summary>
    /// The name can be declared.
    /// </summary>
    Valid,

    /// <summary>
    /// The name is empty.
    /// </summary>
    Empty,

    /// <summary>
    /// The name is not an identifier (<see cref="VBIdentifierValidity.NotAnIdentifier"/>).
    /// </summary>
    NotAnIdentifier,

    /// <summary>
    /// The name is a keyword (<see cref="VBIdentifierValidity.Reserved"/>).
    /// </summary>
    Reserved,

    /// <summary>
    /// The name is longer than <see cref="NewNames.MaximumLength"/> characters.
    /// </summary>
    TooLong,
}

/// <summary>
/// What a name a refactoring writes conflicts with, and where that is.
/// </summary>
/// <param name="Other">What else the name is: the symbol already declared as it, or the symbol it means where it is written.</param>
/// <param name="Module">The module the conflict points to, when it points to one.</param>
/// <param name="Position">Where in <paramref name="Module"/>.</param>
internal sealed record class NameConflict(string Other, Uri? Module, SourcePosition Position);

/// <summary>
/// The rules every refactoring that declares a name holds it to: a VBA identifier of at most 255 characters that is not reserved (MS-VBAL §3.3.5), and one that
/// nothing written in a module would mean instead of what it means now.
/// </summary>
internal static class NewNames
{
    /// <summary>
    /// The longest a name can be.
    /// </summary>
    public const int MaximumLength = 255;

    /// <summary>
    /// Whether VBA accepts <paramref name="name"/> as a declared name.
    /// </summary>
    /// <param name="name">The name.</param>
    public static NewNameValidity ValidityOf(string name) => VBIdentifier.Validate(name) switch
    {
        VBIdentifierValidity.Empty => NewNameValidity.Empty,
        VBIdentifierValidity.NotAnIdentifier => NewNameValidity.NotAnIdentifier,
        VBIdentifierValidity.Reserved => NewNameValidity.Reserved,
        _ when name.Length > MaximumLength => NewNameValidity.TooLong,
        _ => NewNameValidity.Valid,
    };

    /// <summary>
    /// Where a module writes <paramref name="name"/> and means something a new declaration of it would hide: something not known, or something farther than
    /// the new declaration.
    /// </summary>
    /// <param name="context">The workspace, whose modules are bound.</param>
    /// <param name="syntax">The module to search.</param>
    /// <param name="declaringScope">
    /// The scope the name would be declared in, when <paramref name="syntax"/> is its module: what is outside it is farther. <see langword="null"/> for
    /// another module, where only a library's declaration is farther.
    /// </param>
    /// <param name="name">The name.</param>
    /// <returns>The first such place; <see langword="null"/> when there is none.</returns>
    public static NameConflict? RebindingOf(RenameContext context, RenameModule syntax, LexicalScope? declaringScope, string name)
    {
        var occurrences = NameOccurrences.Find(context, name, [syntax]);
        foreach (var occurrence in occurrences.Occurrences.Concat(occurrences.Unknown).Where(occurrence => occurrence.Kind is OccurrenceKind.Name))
        {
            var meant = occurrence.Binding is { } binding ? NameOccurrences.SymbolOf(context, binding) : null;
            var nearest = declaringScope is null
                ? occurrence.Scope.SelfAndAncestors().TakeWhile(scope => scope.Kind is not LexicalScopeKind.Project).Count() - 1
                : occurrence.Scope.SelfAndAncestors().TakeWhile(scope => scope.Uri.AbsoluteUri != declaringScope.Uri.AbsoluteUri).Count();
            if (meant is null || RenameBinding.ChainIndex(occurrence.Scope, meant) > nearest)
            {
                return new(meant is null ? name : NameOccurrences.Describe(meant), occurrence.Module, occurrence.Range.Start);
            }
        }

        return null;
    }

    /// <summary>
    /// A symbol a name conflicts with, described, and where it is declared when it is the workspace's.
    /// </summary>
    /// <param name="other">The symbol.</param>
    public static NameConflict ConflictWith(Symbol other)
        => new(NameOccurrences.Describe(other), other is WorkspaceSymbol ? NameOccurrences.ModuleOf(other.Uri) : null,
            (other as WorkspaceSymbol)?.PrimarySelectionRange.Start ?? default);

    /// <summary>
    /// Whether a symbol is declared by a library: a library's symbol says which library it is of, and the workspace's say none.
    /// </summary>
    /// <param name="symbol">The symbol.</param>
    public static bool IsOfALibrary(Symbol symbol) => symbol.GetProperty(SymbolProperties.Library) is not null;
}
