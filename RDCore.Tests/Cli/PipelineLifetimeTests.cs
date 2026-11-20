using NSubstitute;
using RDCore.Runtime.Execution;
using RDCore.Runtime.Semantics;
using RDCore.Runtime.Support.Host;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Platform.Protocol;
using System.Runtime.CompilerServices;

namespace RDCore.Tests.Cli;

/// <summary>
/// A pipeline composed on a session for one run, as the host composes one for each request: once the run is over, nothing of the session holds it, so
/// a session that runs its program many times keeps none of the pipelines it ran in.
/// </summary>
[TestClass]
[TestCategory("RDCore.CLI.ProgramExecution")]
public sealed class PipelineLifetimeTests
{
    // Named is compiled: the invoker keeps the activation of its compiled body for the next call it makes of it.
    private static readonly string Program = string.Join("\r\n",
        "Attribute VB_Name = \"Program\"",
        "Public Sub Main()",
        "End Sub",
        "Public Sub Named()",
        "    Dim I As Long",
        "    Dim N As Long",
        "    For I = 1 To 10",
        "        N = N + I",
        "    Next",
        "End Sub") + "\r\n";

    [TestMethod]
    [DataRow(VBExecutionEngine.Compiled)]
    [DataRow(VBExecutionEngine.Interpreter)]
    public async Task APipelineARunWasComposedFor_IsHeldByNothing_OnceTheRunIsOver(VBExecutionEngine engine)
        => await ModuleWorkspace.InspectAsync([], Program, (provider, first, _) =>
        {
            Assert.AreEqual(ExecutionOutcome.Completed, first.Outcome, first.ErrorMessage);
            var ran = RunInAPipelineOfItsOwn(provider);
            RunInAPipelineOfItsOwn(provider);

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            Assert.IsFalse(ran.IsAlive, "the executor of a run that is over is held by nothing");
            return Task.CompletedTask;
        }, engine: engine);

    // runs Named on this thread, in a pipeline composed for the run, whose executor only the reference it gives holds, weakly: the pipeline is a local of
    // this method only, which is over once it returns. A program's own thread, and the continuation of its task, would hold it until they unwind.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference RunInAPipelineOfItsOwn(EnvironmentSessionProvider provider)
    {
        var session = provider.Session;
        Assert.IsTrue(session.Symbols.TryResolveValue("Program", GlobalSymbols.UnresolvedSymbol, out var module));
        Assert.IsTrue(session.Symbols.TryResolveValue("Named", module!, out var entry));
        var pipeline = RuntimeExecutionPipeline.Create(session, provider.Image, Substitute.For<IErrorDetailsRecorder>());
        var outcome = pipeline.Invoker.Invoke((VBTypeMemberSymbol)entry!, session.Symbols.Resolver, []);
        Assert.IsTrue(outcome.IsSuccess, outcome.ErrorInfo?.Description);
        return new WeakReference(pipeline.Executor);
    }
}
