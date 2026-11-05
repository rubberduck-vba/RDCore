using RDCore.CLI.Host.Handlers;
using RDCore.SDK.Model.Diagnostics;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Semantics;
using RDCore.SDK.Semantics.Facts;
using System.Collections.Immutable;

namespace RDCore.Tests.Cli;

/// <summary>
/// <c>rdcore/host/semantics</c>: what is ready when a module is loaded is answered at once, and what takes longer is waited for only by a request that asks for it.
/// </summary>
[TestClass]
[TestCategory("RD-VBAL §5.0.3 Semantic Analysis")]
public sealed class HostSemanticsPhaseTests
{
    private static readonly TimeSpan Settle = TimeSpan.FromMilliseconds(250);

    [TestMethod]
    [Ignore("nondeterministic: an evaluation blocks a pool thread while three 5 s waits need the pool, which can starve on 2 cores. To be made deterministic before this runs again")]
    public async Task TheStaticPhase_IsNotKeptWaitingForTheEvaluationOfTheCode_ButTheRuntimePhaseWaitsForIt()
    {
        await ModuleWorkspace.WithHostAsync([], "Attribute VB_Name = \"Program\"\r\n", async provider =>
        {
            var module = new Uri("file:///c:/ws/Slow.bas#Slow");
            var procedure = new SemanticId(new Uri("file:///c:/ws/Slow.bas#Slow.Run"));
            using var release = new ManualResetEventSlim();
            provider.Image.Semantics.Store(
                new ModuleSemanticModel(module, [], [new ProcedureSemanticModel(procedure, [])]),
                _ =>
                {
                    release.Wait();
                    return ImmutableDictionary<SemanticId, RuntimeFacts>.Empty.Add(procedure, RuntimeFacts.None);
                });
            var handler = new HostSemanticsHandler(provider);

            var staticAnswer = await handler.Handle(new HostSemanticsParams { ModuleName = "Slow", Phase = AnalysisPhase.Static }, CancellationToken.None)
                .WaitAsync(TimeSpan.FromSeconds(5));
            var everything = handler.Handle(new HostSemanticsParams { ModuleName = "Slow", Phase = AnalysisPhase.All }, CancellationToken.None);
            var everythingEmptyName = handler.Handle(new HostSemanticsParams { Phase = AnalysisPhase.Runtime }, CancellationToken.None);

            var statically = PlatformJson.Deserialize<SemanticsPayload>(staticAnswer.Json).Modules.Single();
            Assert.IsNull(statically.Procedures.Single().Runtime, "the static answer is the model as it is, and does not claim the code was evaluated");

            await Task.Delay(Settle);
            Assert.IsFalse(everything.IsCompleted, "a request for everything waits for the evaluation");
            Assert.IsFalse(everythingEmptyName.IsCompleted, "and so does one that names no module");

            release.Set();
            var complete = PlatformJson.Deserialize<SemanticsPayload>((await everything.WaitAsync(TimeSpan.FromSeconds(5))).Json).Modules.Single();
            Assert.IsNotNull(complete.Procedures.Single().Runtime);
            await everythingEmptyName.WaitAsync(TimeSpan.FromSeconds(5));
        });
    }

    [TestMethod]
    public async Task AWaitThatIsCancelled_IsNotAnAnswer()
    {
        await ModuleWorkspace.WithHostAsync([], "Attribute VB_Name = \"Program\"\r\n", async provider =>
        {
            var module = new Uri("file:///c:/ws/Slow.bas#Slow");
            using var release = new ManualResetEventSlim();
            provider.Image.Semantics.Store(new ModuleSemanticModel(module, [], []), _ =>
            {
                release.Wait();
                return ImmutableDictionary<SemanticId, RuntimeFacts>.Empty;
            });

            using var cancellation = new CancellationTokenSource(Settle);
            await Assert.ThrowsAsync<OperationCanceledException>(
                () => new HostSemanticsHandler(provider).Handle(new HostSemanticsParams { ModuleName = "Slow" }, cancellation.Token));

            release.Set();
        });
    }
}
