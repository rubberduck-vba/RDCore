using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Runtime.Abstract.Execution;

namespace RDCore.Runtime.Execution;

/// <summary>
/// Takes a module out of a session: what it declared, what it held, and the code and the model that were made of it.
/// </summary>
/// <remarks>
/// <para>
/// Defining a module again replaces what it declares and never removes what it no longer does, so a name a module used to declare stays
/// defined, allocated and visible: a program's variables outlive the program. A client that means the module to go - a shell that clears the program
/// (<c>NEW</c>, <c>LOAD</c>, and <c>RUN</c>, which in BASIC clears the variables) - asks for this.
/// </para>
/// <para>
/// The module's own symbol stays. It is declared by the project, not by the code, and is what the module is defined under again.
/// </para>
/// </remarks>
/// <param name="session">The session the module is defined in.</param>
/// <param name="image">The code of the session.</param>
public sealed class ModuleUnloader(IRuntimeSession session, ProgramImage image)
{
    /// <summary>
    /// Undefines everything the module declares, releases the storage and the objects it held, and forgets its code and its semantic model.
    /// </summary>
    /// <param name="moduleUri">The <see cref="Symbol.Uri"/> of the module symbol.</param>
    /// <returns>The number of symbols that were undefined.</returns>
    public int Unload(Uri moduleUri)
    {
        var declared = session.Symbols.DeclaredIn(moduleUri);

        // an object a variable holds is let go of while everything is still defined: the Terminate that runs when it was the last reference
        // is the code of a class, which may well name a variable of the module that is going.
        foreach (var symbol in declared)
        {
            ReleaseObjectHeldBy(symbol);
        }

        var undefined = 0;
        foreach (var symbol in declared)
        {
            if (session.Symbols.TryUndefine(symbol, symbol.ScopeKind))
            {
                undefined++;
            }
        }

        image.Unload(moduleUri);
        image.Semantics.Remove(moduleUri);
        return undefined;
    }

    private void ReleaseObjectHeldBy(Symbol symbol)
    {
        var resolver = session.Symbols.Resolver;
        if (symbol is not ITypedSymbol typed || !resolver.TryGetAddress(symbol, out _))
        {
            return;
        }

        var holder = resolver.GetValue(symbol);
        if (typed.ResolvedType.CreateValue(holder) is VBObjectValue held)
        {
            ObjectReferences.Release(session, holder, held);
        }
    }
}
