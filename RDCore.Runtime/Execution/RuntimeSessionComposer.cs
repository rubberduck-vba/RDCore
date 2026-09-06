using RDCore.Runtime.Execution.Memory;
using RDCore.SDK.Runtime.Abstract.Execution;

namespace RDCore.Runtime.Execution;

/// <summary>
/// Builds an <see cref="IRuntimeSession"/> from a host environment profile and a set of symbol
/// providers, running each provider and defining its symbols into the session's symbol table.
/// </summary>
public static class RuntimeSessionComposer
{
    /// <inheritdoc cref="Compose(IRuntimeEnvironmentProfile, IEnumerable{ISymbolProvider})"/>
    public static IRuntimeSession Compose(IRuntimeEnvironmentProfile environment, params ISymbolProvider[] providers)
        => Compose(environment, (IEnumerable<ISymbolProvider>)providers);

    /// <summary>
    /// Composes a session. Providers are applied in order — an earlier provider's symbol wins a name
    /// collision (<see cref="ISessionSymbols.TryDefine"/> keeps the first).
    /// </summary>
    public static IRuntimeSession Compose(IRuntimeEnvironmentProfile environment, IEnumerable<ISymbolProvider> providers)
    {
        var memory = new SessionMemory(new FreeListManager(), environment.Is64Bit ? PointerSize.x64 : PointerSize.x86);
        var symbols = new SessionSymbols();
        var objects = new SessionObjects();

        foreach (var provider in providers)
        {
            foreach (var symbol in provider.ProvideSymbols())
            {
                symbols.TryDefine(symbol, symbol.ScopeKind);
            }
        }

        return new RuntimeSession(environment, memory, symbols, objects);
    }
}
