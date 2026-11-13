using RDCore.Runtime.Execution;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Platform;
using RDCore.SDK.Semantics;
using RDCore.SDK.Semantics.Facts;
using System.Collections.Immutable;

namespace RDCore.Tests.Runtime.Execution;

/// <summary>
/// The model of a module is stored as soon as the static pass has made it, and the facts of evaluating its code are added when they are ready: what is ready is
/// not made to wait for what is not.
/// </summary>
[TestClass]
[TestCategory("RD-VBAL §5.0.3 Semantic Analysis")]
public sealed class SemanticModelStoreTests
{
    private static readonly Uri Module = new("file:///c:/ws/Mod1.bas#Mod1");
    private static readonly SemanticId Procedure = new(new Uri("file:///c:/ws/Mod1.bas#Mod1.Run"));

    private static ModuleSemanticModel Model(params SemanticId[] procedures)
        => new(Module, [], [.. procedures.Select(procedure => new ProcedureSemanticModel(procedure, []))]);

    private static ImmutableDictionary<SemanticId, RuntimeFacts> Facts(bool isFullyAnalyzed = true)
        => ImmutableDictionary<SemanticId, RuntimeFacts>.Empty.Add(Procedure, new RuntimeFacts([], [], [], [], isFullyAnalyzed));

    private static RuntimeFacts? RuntimeOf(SemanticModelStore store, int procedure = 0)
        => store.TryGet(Module, out var model) ? model.Procedures[procedure].Runtime : throw new AssertFailedException("no model");

    [TestMethod]
    public async Task TheModel_IsThereBeforeItsFacts_AndTheFactsAreAddedWhenTheyAreReady()
    {
        var store = new SemanticModelStore();
        using var release = new ManualResetEventSlim();

        store.Store(Model(Procedure), _ =>
        {
            release.Wait();
            return Facts();
        });

        Assert.IsTrue(store.TryGet(Module, out _), "the static model is not held back");
        Assert.IsNull(RuntimeOfModel(store), "and it does not say the code was evaluated: it was not, yet");

        release.Set();
        await store.WhenEvaluatedAsync(Module);

        Assert.IsNotNull(RuntimeOfModel(store));

        static RuntimeFacts? RuntimeOfModel(SemanticModelStore store) => RuntimeOf(store);
    }

    [TestMethod]
    public async Task AModuleStoredWithNothingToEvaluate_HasNothingToWaitFor()
    {
        var store = new SemanticModelStore();
        store.Store(Model(Procedure));

        await store.WhenEvaluatedAsync(Module).WaitAsync(HangGuard);
        await store.WhenAllEvaluatedAsync().WaitAsync(HangGuard);

        Assert.IsNull(RuntimeOf(store));
    }

    [TestMethod]
    public async Task AModuleStoredAgain_StopsTheEvaluationOfTheModelItReplaced()
    {
        var store = new SemanticModelStore();
        using var releaseFirst = new ManualResetEventSlim();
        using var firstStarted = new ManualResetEventSlim();
        CancellationToken firstToken = default;

        store.Store(Model(Procedure), token =>
        {
            firstToken = token;
            firstStarted.Set();
            releaseFirst.Wait();
            return Facts(isFullyAnalyzed: false);
        });
        Assert.IsTrue(firstStarted.Wait(HangGuard));

        var second = Facts(isFullyAnalyzed: true);
        store.Store(Model(Procedure), _ => second);
        await store.WhenEvaluatedAsync(Module);

        Assert.IsTrue(firstToken.IsCancellationRequested, "the evaluation of the code that is gone is told to stop");
        Assert.IsTrue(RuntimeOf(store)!.IsFullyAnalyzed, "the model has the facts of the evaluation that replaced it");
        releaseFirst.Set();
    }

    [TestMethod]
    public async Task AWaitForAModule_IsAWaitForTheModelAsItIsWhenTheWaitIsOver()
    {
        var store = new SemanticModelStore();
        using var releaseFirst = new ManualResetEventSlim();
        store.Store(Model(Procedure), _ =>
        {
            releaseFirst.Wait();
            return Facts(isFullyAnalyzed: false);
        });

        var waiting = store.WhenEvaluatedAsync(Module);
        store.Store(Model(Procedure), _ => Facts(isFullyAnalyzed: true));
        releaseFirst.Set();
        await waiting.WaitAsync(HangGuard);

        Assert.IsTrue(RuntimeOf(store)!.IsFullyAnalyzed);
    }

    [TestMethod]
    public void ARemovedModule_StopsItsEvaluation()
    {
        var store = new SemanticModelStore();
        using var release = new ManualResetEventSlim();
        using var started = new ManualResetEventSlim();
        CancellationToken token = default;
        store.Store(Model(Procedure), t =>
        {
            token = t;
            started.Set();
            release.Wait();
            return Facts();
        });
        Assert.IsTrue(started.Wait(HangGuard));

        Assert.IsTrue(store.Remove(Module));
        Assert.IsTrue(token.IsCancellationRequested, "the evaluation of the module that is gone is told to stop");
        release.Set();
    }

    [TestMethod]
    public async Task AnEvaluationThatFails_LeavesTheModelAsItWas_NotEvaluated()
    {
        var store = new SemanticModelStore();
        store.Store(Model(Procedure), _ => throw new InvalidOperationException("a defect of the evaluation"));

        await store.WhenEvaluatedAsync(Module).WaitAsync(HangGuard);

        Assert.IsNull(RuntimeOf(store), "no facts are made up for it");
    }

    [TestMethod]
    public async Task ADeclarationThatRepeatsAnother_DoesNotGetTheFactsOfTheFirst()
    {
        var store = new SemanticModelStore();
        store.Store(Model(Procedure, Procedure), _ => Facts());

        await store.WhenEvaluatedAsync(Module);

        Assert.IsNotNull(RuntimeOf(store, 0));
        Assert.IsNull(RuntimeOf(store, 1), "its code is not what the session has");
    }

    [TestMethod]
    public async Task AWaitThatIsCancelled_StopsWaiting_NotTheEvaluation()
    {
        var store = new SemanticModelStore();
        using var release = new ManualResetEventSlim();
        store.Store(Model(Procedure), _ =>
        {
            release.Wait();
            return Facts();
        });

        using var cancellation = new CancellationTokenSource(50);
        await Assert.ThrowsAsync<OperationCanceledException>(() => store.WhenEvaluatedAsync(Module, cancellation.Token));

        release.Set();
        await store.WhenEvaluatedAsync(Module).WaitAsync(HangGuard);
        Assert.IsNotNull(RuntimeOf(store));
    }

    // a cap on a hang, not a budget: each wait is on the event itself, which a loaded machine brings as late as it does.
    private static readonly TimeSpan HangGuard = TimeSpan.FromMinutes(5);

    // work started on a LargeStackThread once one of the threads the test counts is free: a wait the token cancels never runs it.
    private static async Task StartCountedAsync(SemaphoreSlim threads, Action work, CancellationToken token)
    {
        await threads.WaitAsync(token);
        try
        {
            await LargeStackThread.Start(work, token);
        }
        finally
        {
            threads.Release();
        }
    }

    // the evaluations run on one thread the test counts itself, and holds: the module's evaluation waits for it, as it waits when every LargeStackThread is busy.
    private static (SemanticModelStore Store, ManualResetEventSlim Release, Task Busy) StoreWithItsOneThreadBusy(SemaphoreSlim threads)
    {
        var release = new ManualResetEventSlim();
        using var running = new ManualResetEventSlim();
        var busy = StartCountedAsync(threads, () =>
        {
            running.Set();
            release.Wait();
        }, CancellationToken.None);
        Assert.IsTrue(running.Wait(HangGuard));

        return (new SemanticModelStore((work, token) => StartCountedAsync(threads, work, token)), release, busy);
    }

    [TestMethod]
    public async Task AnEvaluationRemovedWhileItWaitsForAThread_NeverRuns_AndTheWaitForItIsOver()
    {
        using var threads = new SemaphoreSlim(1, 1);
        var (store, release, busy) = StoreWithItsOneThreadBusy(threads);
        using var released = release;
        var ran = false;
        store.Store(Model(Procedure), _ =>
        {
            ran = true;
            return Facts();
        });
        var waiting = store.WhenEvaluatedAsync(Module);
        Assert.IsFalse(waiting.IsCompleted, "the evaluation waits for the thread");

        Assert.IsTrue(store.Remove(Module));

        await waiting.WaitAsync(HangGuard);
        release.Set();
        await busy.WaitAsync(HangGuard);
        Assert.IsFalse(ran, "it was stopped before a thread was free");
    }

    [TestMethod]
    public async Task AnEvaluationReplacedWhileItWaitsForAThread_NeverRuns_AndTheWaitIsForTheOneThatReplacedIt()
    {
        using var threads = new SemaphoreSlim(1, 1);
        var (store, release, busy) = StoreWithItsOneThreadBusy(threads);
        using var released = release;
        var (first, second) = (0, 0);
        store.Store(Model(Procedure), _ =>
        {
            first++;
            return Facts();
        });
        var waiting = store.WhenEvaluatedAsync(Module);

        store.Store(Model(Procedure), _ =>
        {
            second++;
            return Facts(isFullyAnalyzed: false);
        });
        Assert.IsFalse(waiting.IsCompleted, "the evaluation that replaced it waits for the thread");

        release.Set();
        await busy.WaitAsync(HangGuard);
        await waiting.WaitAsync(HangGuard);
        Assert.AreEqual((0, 1), (first, second), "each evaluation runs at most once, and the replaced one never");
        Assert.IsFalse(RuntimeOf(store)!.IsFullyAnalyzed);
    }
}
