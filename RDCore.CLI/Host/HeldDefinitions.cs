using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using RDCore.SDK.Runtime.Abstract.Execution;

namespace RDCore.CLI.Host;

/// <summary>
/// The definitions a program in progress holds (<see cref="ISessionSymbols.HoldsStoredValues"/>), and the code lowered against them: applied, and lowered
/// again, when the program is over.
/// </summary>
/// <remarks>
/// A variable redeclared as another type while a program runs or waits stays as it was, with its value, so that the edit can be taken back without the
/// value being gone. Code the analysis pass loaded meanwhile was lowered against the variable as it was; once the held definitions are applied, it is
/// lowered again from the parse it was loaded with, against the variable as it is declared.
/// </remarks>
internal static class HeldDefinitions
{
    // the modules loaded while a definition was held, by name, each with what lowers it again.
    private static readonly ConditionalWeakTable<IRuntimeSession, Loads> LoadsBySession = new();

    private sealed class Loads
    {
        private readonly Lock _sync = new();
        private ImmutableDictionary<string, Func<ImmutableArray<string>>> _modules = ImmutableDictionary.Create<string, Func<ImmutableArray<string>>>(StringComparer.OrdinalIgnoreCase);

        public void Add(string module, Func<ImmutableArray<string>> lower)
        {
            lock (_sync)
            {
                _modules = _modules.SetItem(module, lower);
            }
        }

        public void Remove(string module)
        {
            lock (_sync)
            {
                _modules = _modules.Remove(module);
            }
        }

        public bool Contains(string module)
        {
            lock (_sync)
            {
                return _modules.ContainsKey(module);
            }
        }

        public ImmutableDictionary<string, Func<ImmutableArray<string>>> Take()
        {
            lock (_sync)
            {
                var taken = _modules;
                _modules = _modules.Clear();
                return taken;
            }
        }
    }

    /// <summary>A program starts: from now until it is over, a definition that would let go of a value is held.</summary>
    public static void Hold(IRuntimeSession session) => session.Symbols.HoldsStoredValues = true;

    /// <summary>
    /// A module was loaded: when a definition is held, what loaded it is kept, to lower it again once the held definitions are applied. Loaded when nothing
    /// is held, it is lowered against the variables as they are declared, and needs no lowering again.
    /// </summary>
    /// <param name="session">The session it was loaded in.</param>
    /// <param name="module">The module's name.</param>
    /// <param name="lower">What lowers it again, from the parse it was loaded with, and says what is wrong with it the way a load does.</param>
    public static void Loaded(IRuntimeSession session, string module, Func<ImmutableArray<string>> lower)
    {
        var loads = LoadsBySession.GetOrCreateValue(session);
        if (session.Symbols.HeldDefinitions.Count > 0)
        {
            loads.Add(module, lower);
        }
        else
        {
            loads.Remove(module);
        }
    }

    /// <summary>
    /// Whether <paramref name="module"/> was last loaded while a definition is held: its code was checked against the variable as it was, not as edited.
    /// </summary>
    public static bool LoweredUnderAHold(IRuntimeSession session, string module)
        => session.Symbols.HeldDefinitions.Count > 0 && LoadsBySession.TryGetValue(session, out var loads) && loads.Contains(module);

    /// <summary>
    /// The program is over: nothing is held from now on, what was held is applied, and the code lowered against it is lowered again - each module by what
    /// loaded it, which says what is wrong with it as its load would have.
    /// </summary>
    /// <param name="session">The session the program was in.</param>
    /// <returns>The names of the modules lowered again; none when nothing was held.</returns>
    public static IReadOnlyCollection<string> Release(IRuntimeSession session)
    {
        // stopping to hold and taking what is held are one step (ISessionSymbols.ApplyHeldDefinitions): a definition that arrives meanwhile is held before
        // it, or applied as it arrives.
        var applied = session.Symbols.ApplyHeldDefinitions();
        var loads = LoadsBySession.GetOrCreateValue(session).Take();
        if (applied.Count == 0)
        {
            return [];
        }

        foreach (var (_, lower) in loads)
        {
            _ = lower();
        }

        return [.. loads.Keys];
    }
}
