using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using RDCore.Runtime.Execution;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Runtime.Abstract.Execution;

namespace RDCore.Runtime.Support.Host;

/// <summary>
/// The definitions a program in progress holds (<see cref="ISessionSymbols.HoldsStoredValues"/>), and what was defined and lowered against them: applied, and
/// defined and lowered again, when the program is over.
/// </summary>
/// <remarks>
/// A variable redeclared as another type, or a Type given other fields, while a program runs or waits stays as it was - the variable with its value, the
/// Type with the fields the program's values of it are laid out by - so that the edit can be taken back with nothing lost. What the analysis pass sent
/// meanwhile was defined and lowered against them as they were; once the held definitions are applied, each of those requests is made again, in the order
/// it came in, against them as they are declared: a variable of the Type is given the Type as edited, and the code is checked and lowered against it.
/// <para>
/// A Type given other fields is applied with a reset of the project, as MS-VBA resets it for this edit (at the edit; here, once the program is over): every
/// value and object of the session goes, as an <c>End</c> lets go of them (<see cref="SessionWipe.End"/>), rather than a value of the Type anywhere in
/// storage - a Static, an object's field, an array, another Type - being laid out again.
/// </para>
/// <para>
/// A module whose code no longer compiles once it is made again keeps the code it had, as a module that fails to load does; the next run is refused with
/// what is wrong with it (<see cref="FailedToLoad"/>), rather than running that code.
/// </para>
/// </remarks>
internal static class HeldDefinitions
{
    // the requests received while a definition was held, the latest of each module and phase, in the order they came in, each with what makes it again.
    private static readonly ConditionalWeakTable<IRuntimeSession, Requests> ReceivedBySession = new();

    // LoadsCode: the request loads the module's code, and what it says is wrong with it is what the module fails to load with.
    // Held: a definition was held when it was made, and it was made against the declarations as they were.
    private sealed record class Request(string Key, string Module, bool LoadsCode, bool Held, Func<ImmutableArray<string>> Again);

    private sealed class Requests
    {
        private readonly Lock _sync = new();
        private ImmutableList<Request> _requests = [];
        private ImmutableDictionary<string, ImmutableArray<string>> _failed = ImmutableDictionary.Create<string, ImmutableArray<string>>(StringComparer.OrdinalIgnoreCase);

        // the modules the project gained while the program is in progress, each with its module symbol once the project says what it is (null until then).
        private ImmutableDictionary<string, VBModuleSymbol?> _added = ImmutableDictionary.Create<string, VBModuleSymbol?>(StringComparer.OrdinalIgnoreCase);

        // taken by a request for as long as it defines and loads, and by a release for as long as it applies and makes the requests again: neither is made in
        // the middle of the other.
        public Lock Defining { get; } = new();

        public void Add(Request request)
        {
            lock (_sync)
            {
                _requests = _requests.RemoveAll(older => older.Key == request.Key).Add(request);
            }
        }

        public void Remove(string key)
        {
            lock (_sync)
            {
                _requests = _requests.RemoveAll(request => request.Key == key);
            }
        }

        public bool ContainsHeld(string module)
        {
            lock (_sync)
            {
                return _requests.Any(request => request.Held && string.Equals(request.Module, module, StringComparison.OrdinalIgnoreCase));
            }
        }

        public (ImmutableList<Request> Requests, ImmutableDictionary<string, VBModuleSymbol?> Added) Take()
        {
            lock (_sync)
            {
                var taken = (_requests, _added);
                _requests = [];
                _added = _added.Clear();
                return taken;
            }
        }

        public ImmutableDictionary<string, VBModuleSymbol?> Added
        {
            get
            {
                lock (_sync)
                {
                    return _added;
                }
            }
        }

        public void Add(string module, VBModuleSymbol? symbol)
        {
            lock (_sync)
            {
                _added = _added.SetItem(module, symbol ?? _added.GetValueOrDefault(module));
            }
        }

        public void Remove(string module, bool requests)
        {
            lock (_sync)
            {
                _added = _added.Remove(module);
                if (requests)
                {
                    _requests = _requests.RemoveAll(request => string.Equals(request.Module, module, StringComparison.OrdinalIgnoreCase));
                }
            }
        }

        public ImmutableDictionary<string, ImmutableArray<string>> Failed
        {
            get
            {
                lock (_sync)
                {
                    return _failed;
                }
            }
        }

        public void Loaded(string module, ImmutableArray<string> errors)
        {
            lock (_sync)
            {
                _failed = errors.IsDefaultOrEmpty ? _failed.Remove(module) : _failed.SetItem(module, errors);
            }
        }
    }

    /// <summary>A program starts: from now until it is over, a definition that would let go of a value, or change a Type, is held.</summary>
    public static void Hold(IRuntimeSession session) => session.Symbols.HoldsStoredValues = true;

    /// <summary>
    /// Defines a module's symbols, or loads its code, for a request - never while what was held is applied and made again (<see cref="Release"/>), which the
    /// request comes after - and, when a definition is held, keeps what makes the request, to make it again once the held definitions are applied. Made when
    /// nothing is held, it was made against the declarations as they are, and needs making again no more than the older request of the same module and phase it
    /// replaces.
    /// </summary>
    /// <typeparam name="TResult">What the request is answered with.</typeparam>
    /// <param name="session">The session it is made in.</param>
    /// <param name="key">The module and the phase of the definition it makes: a later request with the same key replaces it.</param>
    /// <param name="module">The module's name.</param>
    /// <param name="codeErrors">
    /// What is wrong with the module's code, as the request that loads it says; <see langword="null"/> for a request that loads no code. A module whose code
    /// failed to load refuses every run until a request loads it (<see cref="FailedToLoad"/>).
    /// </param>
    /// <param name="define">What makes it.</param>
    /// <param name="again">What makes it again, and says what is wrong with the module's code the way the request did.</param>
    public static TResult Define<TResult>(IRuntimeSession session, string key, string module, Func<TResult, ImmutableArray<string>>? codeErrors, Func<TResult> define,
        Func<ImmutableArray<string>> again)
    {
        var received = ReceivedBySession.GetOrCreateValue(session);
        lock (received.Defining)
        {
            var result = define();
            var held = session.Symbols.HeldDefinitions.Count > 0;
            var inProgress = held || session.Symbols.HoldsStoredValues;

            // a module the session does not have, defined while the program is in progress: the project gained it, and the program runs without it.
            if (inProgress && module.Length > 0 && !session.Symbols.TryGetModule(module, out _))
            {
                received.Add(module, null);
            }

            if (inProgress)
            {
                // made again once the program is over, when a definition was held or the project gained a module (Release).
                received.Add(new Request(key, module, codeErrors is not null, held, again));
            }
            else
            {
                received.Remove(key);
            }

            if (!held && codeErrors is not null && !received.Added.ContainsKey(module))
            {
                // what is wrong with the module's code is the request's to say now: nothing of the project runs while it does not load.
                received.Loaded(module, codeErrors(result));
            }

            return result;
        }
    }

    /// <summary>
    /// The project gained a module while the program is in progress: the program runs without it, its code is not loaded, and it is not resumed until the
    /// module is taken out again (<see cref="AddedWhileTheProgramIsInProgress"/>). Once the program is over, the module is defined, and its requests are
    /// made again (<see cref="Release"/>).
    /// </summary>
    /// <param name="session">The session the program is in.</param>
    /// <param name="module">The module the project gained, what the project makes of it: a standard module or a class.</param>
    public static void Added(IRuntimeSession session, VBModuleSymbol module)
        => ReceivedBySession.GetOrCreateValue(session).Add(module.Name, module);

    /// <summary>
    /// The modules the project gained while the program is in progress, which the program runs without, in the order of their names (<see cref="Added"/>).
    /// </summary>
    public static IReadOnlyList<string> AddedWhileTheProgramIsInProgress(IRuntimeSession session)
        => ReceivedBySession.TryGetValue(session, out var received)
            ? [.. received.Added.Keys.Order(StringComparer.OrdinalIgnoreCase)]
            : [];

    /// <summary>
    /// Whether <paramref name="module"/> was defined or loaded while a definition is held: it was checked against the declarations as they were, not as
    /// edited.
    /// </summary>
    public static bool LoweredUnderAHold(IRuntimeSession session, string module)
        => session.Symbols.HeldDefinitions.Count > 0 && ReceivedBySession.TryGetValue(session, out var received) && received.ContainsHeld(module);

    /// <summary>
    /// What is wrong with the code of each module that failed to load (at a request, or once it was made again by <see cref="Release"/>) and that no request
    /// has loaded since: a run is refused with it, rather than running the code the module had, or none of it.
    /// </summary>
    /// <param name="session">The session.</param>
    /// <param name="except">A module the run loads itself, which says what is wrong with it; <see langword="null"/> for none.</param>
    /// <returns>Each error, after the name of its module, the modules in the order of their names, and each module's errors in the order it said them.</returns>
    public static IReadOnlyList<string> FailedToLoad(IRuntimeSession session, string? except = null)
        => [.. ModulesThatFailedToLoad(session)
            .Where(failed => !string.Equals(failed.Module, except, StringComparison.OrdinalIgnoreCase))
            .SelectMany(failed => failed.Errors.Select(error => $"{failed.Module}: {error}"))];

    /// <summary>
    /// Each module that failed to load and that no request has loaded since (<see cref="FailedToLoad"/>), in the order of their names, with what is wrong with
    /// its code.
    /// </summary>
    public static IReadOnlyList<(string Module, ImmutableArray<string> Errors)> ModulesThatFailedToLoad(IRuntimeSession session)
        => ReceivedBySession.TryGetValue(session, out var received)
            ? [.. received.Failed.OrderBy(failed => failed.Key, StringComparer.OrdinalIgnoreCase).Select(failed => (failed.Key, failed.Value))]
            : [];

    /// <summary>
    /// A module was taken out of the session: it is no module of the project, and nothing it failed to load with refuses a run. One the project gained while
    /// the program is in progress is taken out again, and the program is resumed without it, as it ran.
    /// </summary>
    public static void Discarded(IRuntimeSession session, string module)
    {
        if (ReceivedBySession.TryGetValue(session, out var received) && received.Added.ContainsKey(module))
        {
            received.Remove(module, requests: true);
        }

        Loaded(session, module);
    }

    /// <summary>
    /// A module was loaded by a run: what is wrong with its code, if anything, is the run's to say from now on.
    /// </summary>
    public static void Loaded(IRuntimeSession session, string module)
    {
        if (ReceivedBySession.TryGetValue(session, out var received))
        {
            received.Loaded(module, []);
        }
    }

    /// <summary>
    /// The program is over: nothing is held from now on, what was held is applied, and every request made against it is made again, in the order it came in
    /// - each says what is wrong with its module's code as it did, which the next run is refused with (<see cref="FailedToLoad"/>). A Type given other fields
    /// resets the project then (see the remarks of <see cref="HeldDefinitions"/>). A module the project gained meanwhile is defined (<see cref="Added"/>), and
    /// every request made while the program was in progress is made again.
    /// </summary>
    /// <param name="session">The session the program was in.</param>
    /// <returns>The names of the modules defined and lowered again; none when nothing was held.</returns>
    public static IReadOnlyCollection<string> Release(IRuntimeSession session)
    {
        var received = ReceivedBySession.GetOrCreateValue(session);
        lock (received.Defining)
        {
            // stopping to hold and taking what is held are one step (ISessionSymbols.ApplyHeldDefinitions): a definition that arrives meanwhile is held before
            // it, or waits for the requests made again here, which it is newer than.
            var applied = session.Symbols.ApplyHeldDefinitions();
            var (requests, added) = received.Take();
            if (applied.Count == 0 && added.IsEmpty)
            {
                return [];
            }

            // a module the project gained is the project's now: it is defined, and every request is made again, so that the code that names it - its own,
            // and the code of the modules loaded while it was not there - is checked and lowered with it. One the project said nothing of is still no module.
            foreach (var module in added.Values.OfType<VBModuleSymbol>())
            {
                _ = session.Symbols.TryDefine(module, module.ScopeKind);
            }

            if (added.IsEmpty)
            {
                requests = requests.RemoveAll(request => !request.Held);
            }

            var modules = requests.Select(request => request.Module).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var errors = modules.ToDictionary(module => module, _ => new List<string>(), StringComparer.OrdinalIgnoreCase);
            foreach (var request in requests)
            {
                errors[request.Module].AddRange(request.Again());
            }

            // a module whose code no request loaded is as it was: a request of its symbols alone says nothing of its code.
            foreach (var module in modules.Where(module => requests.Any(request => request.LoadsCode && string.Equals(request.Module, module, StringComparison.OrdinalIgnoreCase))))
            {
                received.Loaded(module, [.. errors[module]]);
            }

            if (applied.Any(symbol => symbol is VBUserDefinedTypeMemberSymbol or VBUserDefinedTypeFieldSymbol))
            {
                SessionWipe.End(session);
            }

            return modules;
        }
    }
}
