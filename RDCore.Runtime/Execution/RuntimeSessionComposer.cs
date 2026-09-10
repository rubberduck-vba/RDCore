using RDCore.Runtime.Execution.Memory;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Runtime.Abstract.Execution;

namespace RDCore.Runtime.Execution;

/// <summary>
/// Builds an <see cref="IRuntimeSession"/> from a host environment profile, the workspace's ordered
/// references, and a set of symbol providers — running each provider and defining its symbols into
/// the session's symbol table.
/// </summary>
public static class RuntimeSessionComposer
{
    /// <inheritdoc cref="Compose(IRuntimeEnvironmentProfile, IReadOnlyList{ProjectReference}, IEnumerable{ISymbolProvider})"/>
    public static IRuntimeSession Compose(IRuntimeEnvironmentProfile environment, params ISymbolProvider[] providers)
        => Compose(environment, [], providers);

    /// <inheritdoc cref="Compose(IRuntimeEnvironmentProfile, IReadOnlyList{ProjectReference}, IEnumerable{ISymbolProvider})"/>
    public static IRuntimeSession Compose(IRuntimeEnvironmentProfile environment, IEnumerable<ISymbolProvider> providers)
        => Compose(environment, [], providers);

    /// <summary>
    /// Composes a session. Providers are applied in order — an earlier provider's symbol wins a name
    /// collision (<see cref="ISessionSymbols.TryDefine"/> keeps the first). <paramref name="references"/>
    /// is carried on the session in the order given (<strong>RD-VBAL §2.3.1.2</strong> priority
    /// order); the composer does not re-sort it.
    /// </summary>
    public static IRuntimeSession Compose(
        IRuntimeEnvironmentProfile environment,
        IReadOnlyList<ProjectReference> references,
        IEnumerable<ISymbolProvider> providers)
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

        return new RuntimeSession(environment, memory, symbols, objects, references);
    }
}
