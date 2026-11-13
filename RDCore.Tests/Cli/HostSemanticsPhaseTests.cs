using RDCore.CLI.Host.Handlers;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Semantics;
using RDCore.SDK.Semantics.Facts;
using System.Collections.Immutable;
using RDCore.SDK.Platform.Components;
using RDCore.SDK.Extensibility.Diagnostics;

namespace RDCore.Tests.Cli;

/// <summary>
/// <c>rdcore/host/semantics</c>: what is ready when a module is loaded is answered at once, and what takes longer is waited for only by a request that asks for it.
/// </summary>
[TestClass]
[TestCategory("RD-VBAL §5.0.3 Semantic Analysis")]
public sealed class HostSemanticsPhaseTests
{
    // only a hang guard: each wait is for an answer the test has made due. The evaluation holds a pool thread until it is released, so the answers' own
    // continuations may wait for the pool to add one.
    private static readonly TimeSpan Patience = TimeSpan.FromMinutes(5);

    [TestMethod]
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
            try
            {
                var staticAnswer = await handler.Handle(new HostSemanticsParams { ModuleName = "Slow", Phase = AnalysisPhase.Static }, CancellationToken.None)
                    .WaitAsync(Patience);
                var everything = handler.Handle(new HostSemanticsParams { ModuleName = "Slow", Phase = AnalysisPhase.All }, CancellationToken.None);
                var everythingEmptyName = handler.Handle(new HostSemanticsParams { Phase = AnalysisPhase.Runtime }, CancellationToken.None);

                var statically = PlatformJson.Deserialize<SemanticsPayload>(staticAnswer.Json).Modules.Single();
                Assert.IsNull(statically.Procedures.Single().Runtime, "the static answer is the model as it is, and does not claim the code was evaluated");

                // the evaluation is held until it is released below, so neither request can have been answered.
                Assert.IsFalse(everything.IsCompleted, "a request for everything waits for the evaluation");
                Assert.IsFalse(everythingEmptyName.IsCompleted, "and so does one that names no module");

                release.Set();
                var complete = PlatformJson.Deserialize<SemanticsPayload>((await everything.WaitAsync(Patience)).Json).Modules.Single();
                Assert.IsNotNull(complete.Procedures.Single().Runtime);
                await everythingEmptyName.WaitAsync(Patience);
            }
            finally
            {
                release.Set();
            }
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

            try
            {
                // cancelled while it waits for the evaluation, which is held until it is released below.
                using var cancellation = new CancellationTokenSource();
                var waiting = new HostSemanticsHandler(provider).Handle(new HostSemanticsParams { ModuleName = "Slow" }, cancellation.Token);
                await cancellation.CancelAsync();
                await Assert.ThrowsAsync<OperationCanceledException>(() => waiting.WaitAsync(Patience));
            }
            finally
            {
                release.Set();
            }
        });
    }
}
