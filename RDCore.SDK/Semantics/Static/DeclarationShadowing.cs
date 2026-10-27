using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Runtime.Abstract.Execution;

namespace RDCore.SDK.Semantics.Static;

/// <summary>
/// Tells what a local or a parameter hides (<see cref="DeclarationFact.Hides"/>): what its name means in the rest of the module.
/// </summary>
/// <remarks>
/// A simple name is first matched in its procedure (<strong>MS-VBAL §5.6.10</strong>), so a local or a parameter named like a member of its module, a
/// <c>Public</c> member of another module, a member of a library (<c>Left</c>, <c>Date</c>) or a module, means itself in its procedure, and that other declaration
/// everywhere else. What it hides is what the name resolves to as seen from the module, past the procedure's own names.
/// </remarks>
public static class DeclarationShadowing
{
    /// <summary>
    /// What <paramref name="declared"/> hides.
    /// </summary>
    /// <param name="declared">A symbol the module declares.</param>
    /// <param name="resolver">Resolves names as the session has them.</param>
    /// <param name="module">The module that declares it.</param>
    /// <returns>
    /// What it hides, or <see langword="null"/> when it is not a local or a parameter, or its name means nothing else in the module, or means more than one thing
    /// there (which is an error of its own).
    /// </returns>
    public static HiddenDeclaration? Of(Symbol declared, ISymbolResolver resolver, Uri module)
    {
        // Me is the object a member is called on, which no other declaration can be named (MS-VBAL 5.3.1.5).
        if (declared is not (VBLocalVariableSymbol or VBLocalConstantSymbol) || string.Equals(declared.Name, "Me", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var resolution = resolver.ResolveValue(declared.Name, ScopeKind.Module, module);
        return resolution.Symbol is { } hidden && hidden.SemanticId != declared.SemanticId
            ? new HiddenDeclaration(hidden.Uri, QualifiedNameOf(hidden))
            : null;
    }

    // the address of the symbol: the fragment of its Uri, which names it after the module or library that declares it.
    private static string QualifiedNameOf(Symbol symbol)
        => symbol.Uri.Fragment.TrimStart('#') is { Length: > 0 } address ? address : symbol.Name;
}
