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
    /// A module was loaded: when a definition is held, what loaded it is kept, to lower it again once the held definitions are applied.
    /// </summary>
    /// <param name="session">The session it was loaded in.</param>
    /// <param name="module">The module's name.</param>
    /// <param name="lower">What lowers it again, from the parse it was loaded with: the errors its code has, none when it loads.</param>
    public static void Loaded(IRuntimeSession session, string module, Func<ImmutableArray<string>> lower)
    {
        if (session.Symbols.HeldDefinitions.Count > 0)
        {
            LoadsBySession.GetOrCreateValue(session).Add(module, lower);
        }
    }

    /// <summary>
    /// The program is over: nothing is held from now on, what was held is applied, and the code lowered against it is lowered again.
    /// </summary>
    /// <param name="session">The session the program was in.</param>
    /// <returns>The modules lowered again, each with the errors its code has now; none when nothing was held.</returns>
    public static IReadOnlyDictionary<string, ImmutableArray<string>> Release(IRuntimeSession session)
    {
        session.Symbols.HoldsStoredValues = false;
        var loads = LoadsBySession.GetOrCreateValue(session).Take();
        if (session.Symbols.ApplyHeldDefinitions().Count == 0)
        {
            return ImmutableDictionary<string, ImmutableArray<string>>.Empty;
        }

        return loads.ToImmutableDictionary(load => load.Key, load => load.Value(), StringComparer.OrdinalIgnoreCase);
    }
}
