using System.Collections.Immutable;
using RDCore.Runtime.LanguageCore.Static;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Platform;
using RDCore.SDK.Semantics;
using RDCore.SDK.Semantics.Facts;

namespace RDCore.Runtime.Execution;

/// <summary>
/// What the semantic analysis pass found out about the modules of a session: the model of each module as of the last time its code was loaded.
/// </summary>
/// <remarks>
/// <para>
/// The model of a module is kept whether or not its code could be loaded: what is wrong with it is what a diagnostics extension is asking after. A module that is
/// loaded again takes the place of its model. Reads see a consistent snapshot and never block.
/// </para>
/// <para>
/// A model is stored as soon as the static pass has made it, and the facts that come of evaluating the code (<see cref="ProcedureSemanticModel.Runtime"/>) are
/// added to it when they are ready: they take longer, and what is ready is not made to wait for what is not. The store owns that evaluation. A module that is
/// loaded again, or removed, stops the one that was running for it, and an evaluation that finishes after its model was replaced is dropped: its facts are of
/// code that is no longer there.
/// </para>
/// </remarks>
public sealed class SemanticModelStore
{
    private readonly object _storing = new();
    private volatile ImmutableDictionary<string, Stored> _models = ImmutableDictionary<string, Stored>.Empty;
    private readonly Dictionary<string, Evaluation> _evaluations = [];

    // a module's model, the facts of the expressions of its declarations that the model was made with, and the names its excluded #If branches write: they are
    // read together, from one snapshot.
    private sealed record Stored(ModuleSemanticModel Model, ModuleDeclarationFacts? Declarations, IReadOnlySet<string>? ExcludedBranchNames);

    private sealed record Evaluation(CancellationTokenSource Cancellation, Task Completion);

    private readonly Func<Action, CancellationToken, Task> _start;

    /// <summary>
    /// A store whose evaluations run on <see cref="LargeStackThread"/>.
    /// </summary>
    public SemanticModelStore()
        : this(LargeStackThread.Start)
    {
    }

    // what starts an evaluation: LargeStackThread, or the same on threads a test counts itself.
    internal SemanticModelStore(Func<Action, CancellationToken, Task> start) => _start = start;

    /// <summary>
    /// Keeps a module's model, replacing the one it had.
    /// </summary>
    /// <param name="model">The model of a module.</param>
    public void Store(ModuleSemanticModel model) => Store(model, evaluate: null);

    /// <summary>
    /// Keeps a module's model, replacing the one it had, and has the facts of its code evaluated in the background.
    /// </summary>
    /// <param name="model">The model of a module, as the static pass made it.</param>
    /// <param name="evaluate">
    /// Finds the facts of the procedures of the module, by their <see cref="Symbol.SemanticId"/>; <see langword="null"/> when there is nothing to evaluate. It runs
    /// on another thread, at most once - an evaluation still waiting for a thread when the module is stored again or removed never runs - and is given the token
    /// that stops it: it reads nothing that the thread that stored the model goes on changing.
    /// </param>
    /// <param name="declarations">
    /// The facts of the expressions in the module's declarations (the value of a constant, the bounds of an array) that the model was made with; <see langword="null"/>
    /// when the module has none to give. <see cref="AcrossProject"/> counts the references they make.
    /// </param>
    /// <param name="excludedBranchNames">
    /// The names that the module's excluded <c>#If</c> branches write (<see cref="ExcludedBranchNames"/>); <see langword="null"/> when it has none.
    /// <see cref="AcrossProject"/> states no count for a declaration of one of these names.
    /// </param>
    public void Store(
        ModuleSemanticModel model, Func<CancellationToken, ImmutableDictionary<SemanticId, RuntimeFacts>>? evaluate, ModuleDeclarationFacts? declarations = null,
        IReadOnlySet<string>? excludedBranchNames = null)
    {
        lock (_storing)
        {
            var key = model.Module.AbsoluteUri;
            _models = _models.SetItem(key, new Stored(model, declarations, excludedBranchNames));
            Stop(key);

            if (evaluate is not null)
            {
                var cancellation = new CancellationTokenSource();
                // the code is evaluated as a program runs it, on a thread whose stack has room for its deepest expression. An evaluation stopped before one is
                // free never runs; either way what is waited for is that it is over, and not how.
                var evaluation = _start(() => Complete(key, model, evaluate, cancellation.Token), cancellation.Token);
                _evaluations[key] = new Evaluation(cancellation,
                    evaluation.ContinueWith(static _ => { }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default));
            }
        }
    }

    // the facts are added to the model they were evaluated for, and to no other: a model that has been replaced is of code that is not there any more.
    private void Complete(string key, ModuleSemanticModel model, Func<CancellationToken, ImmutableDictionary<SemanticId, RuntimeFacts>> evaluate, CancellationToken token)
    {
        ImmutableDictionary<SemanticId, RuntimeFacts> facts;
        try
        {
            facts = evaluate(token);
        }
        catch (Exception)
        {
            // stopped, or a defect of the evaluation: either way there are no facts to add, and the model says what it always said, that the code has not been
            // evaluated (ProcedureSemanticModel.Runtime is null). Nothing is made up for it.
            return;
        }

        lock (_storing)
        {
            if (!_models.TryGetValue(key, out var current) || !ReferenceEquals(current.Model, model) || token.IsCancellationRequested)
            {
                return;
            }

            // the first of the declarations that repeat one another is the one whose code the session has.
            var attached = new HashSet<SemanticId>();
            _models = _models.SetItem(key, current with
            {
                Model = model with
                {
                    Procedures = [.. model.Procedures.Select(procedure =>
                        facts.TryGetValue(procedure.Procedure, out var found) && attached.Add(procedure.Procedure) ? procedure with { Runtime = found } : procedure)],
                },
            });
            _evaluations.Remove(key);
        }
    }

    // called with the lock held.
    private void Stop(string key)
    {
        if (_evaluations.Remove(key, out var running))
        {
            running.Cancellation.Cancel();
        }
    }

    /// <summary>
    /// Forgets a module's model, and stops the evaluation of its code.
    /// </summary>
    /// <param name="module">The <see cref="RDCore.SDK.Model.Symbols.Abstract.Symbol.Uri"/> of the module.</param>
    /// <returns><see langword="false"/> if the module had none.</returns>
    public bool Remove(Uri module)
    {
        lock (_storing)
        {
            Stop(module.AbsoluteUri);
            var found = _models.ContainsKey(module.AbsoluteUri);
            _models = _models.Remove(module.AbsoluteUri);
            return found;
        }
    }

    /// <summary>
    /// The model of a module, if its code has been analyzed.
    /// </summary>
    /// <param name="module">The <see cref="RDCore.SDK.Model.Symbols.Abstract.Symbol.Uri"/> of the module.</param>
    /// <param name="model">The model.</param>
    public bool TryGet(Uri module, out ModuleSemanticModel model)
    {
        var found = _models.TryGetValue(module.AbsoluteUri, out var stored);
        model = stored?.Model!;
        return found;
    }

    /// <summary>
    /// The model of every module whose code has been analyzed.
    /// </summary>
    public IReadOnlyCollection<ModuleSemanticModel> All => _models.Values.Select(stored => stored.Model).ToList();

    /// <summary>
    /// The model of every module whose code has been analyzed, with the references to a declaration that only the modules of a project can refer to counted over
    /// all of them.
    /// </summary>
    /// <param name="project">The modules of the project.</param>
    /// <remarks>
    /// The models and the facts of the expressions in their declarations are read from one snapshot. See <see cref="DeclarationUsage.AcrossProject"/>.
    /// </remarks>
    public IReadOnlyCollection<ModuleSemanticModel> AcrossProject(IEnumerable<VBModuleSymbol> project)
    {
        var snapshot = _models.Values.ToList();
        var declarations = snapshot.Where(stored => stored.Declarations is not null)
            .ToImmutableDictionary(stored => stored.Model.Module.AbsoluteUri, stored => stored.Declarations!, StringComparer.Ordinal);
        var excluded = snapshot.Where(stored => stored.ExcludedBranchNames is { Count: > 0 })
            .ToImmutableDictionary(stored => stored.Model.Module.AbsoluteUri, stored => stored.ExcludedBranchNames!, StringComparer.Ordinal);
        return DeclarationUsage.AcrossProject([.. snapshot.Select(stored => stored.Model)], declarations, project, excluded);
    }

    /// <summary>
    /// Waits until the facts of the code of a module have been evaluated, or there is nothing left to wait for.
    /// </summary>
    /// <param name="module">The <see cref="RDCore.SDK.Model.Symbols.Abstract.Symbol.Uri"/> of the module.</param>
    /// <param name="token">Stops the wait, and not the evaluation.</param>
    /// <remarks>
    /// A module that is loaded again while this waits is waited for again: what the caller asked after is the module as it is when the wait is over. It is over
    /// as well when the evaluation was stopped or failed, and then the model has no facts of the code (<see cref="ProcedureSemanticModel.Runtime"/>).
    /// </remarks>
    public async Task WhenEvaluatedAsync(Uri module, CancellationToken token = default)
    {
        var key = module.AbsoluteUri;
        while (PendingEvaluationAsync(key) is { } pending)
        {
            await pending.WaitAsync(token).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Waits until the facts of the code of every module have been evaluated.
    /// </summary>
    /// <param name="token">Stops the wait, and not the evaluations.</param>
    public async Task WhenAllEvaluatedAsync(CancellationToken token = default)
    {
        while (true)
        {
            Task[] pending;
            lock (_storing)
            {
                pending = [.. _evaluations.Values.Where(evaluation => !evaluation.Completion.IsCompleted).Select(evaluation => evaluation.Completion)];
            }

            if (pending.Length == 0)
            {
                return;
            }

            await Task.WhenAll(pending).WaitAsync(token).ConfigureAwait(false);
        }
    }

    // the evaluation of a module that is still running; one that has finished has taken itself out.
    private Task? PendingEvaluationAsync(string key)
    {
        lock (_storing)
        {
            return _evaluations.TryGetValue(key, out var evaluation) && !evaluation.Completion.IsCompleted ? evaluation.Completion : null;
        }
    }
}
