using NSubstitute;
using RDCore.Runtime.Execution;
using RDCore.Runtime.Semantics;
using RDCore.Runtime.Support.Host;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Platform.Protocol;

namespace RDCore.Tests.Cli;

/// <summary>
/// The objects of the standard library's classes (a <c>Collection</c>, a <c>RegExp</c>) as a session holds them across the pipelines composed on it: one
/// for each run, and one for each statement run while a program waits. What such an object holds is the session's, read by every pipeline, and let go of
/// once when the object is destroyed, whichever pipeline destroys it.
/// </summary>
[TestClass]
[TestCategory("RDCore.CLI.ProgramExecution")]
public sealed class LibraryObjectsAcrossPipelinesTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(30);

    private static readonly (string Name, string Source)[] Classes =
    [
        ("Widget", ModuleWorkspace.ClassModule("Widget",
            "Private Sub Class_Terminate()",
            "    Debug.Print \"terminated\"",
            "End Sub")),
    ];

    private static readonly string Waiting = string.Join("\r\n",
        "Attribute VB_Name = \"Program\"",
        "Public Col As Collection",
        "Public Re As Object",
        "Public E As Object",
        "Public Sub Main()",
        "    Set E = Err",
        "    Set Col = New Collection",
        "    Col.Add New Widget",
        "    Col.Add \"two\"",
        "    Set Re = CreateObject(\"VBScript.RegExp\")",
        "    Re.Pattern = \"b+\"",
        "    Stop",
        "End Sub",
        "Public Sub Count()",
        "    Debug.Print Col.Count",
        "End Sub",
        "Public Sub Second()",
        "    Debug.Print Col(2)",
        "End Sub",
        "Public Sub Drop()",
        "    Set Col = Nothing",
        "    Debug.Print \"dropped\"",
        "End Sub",
        "Public Sub Pattern()",
        "    Debug.Print Re.Pattern",
        "End Sub",
        "Public Sub SameErr()",
        "    Debug.Print E Is Err",
        "End Sub") + "\r\n";

    private static Task DebugAsync(Func<EnvironmentSessionProvider, Task> test)
        => ModuleWorkspace.InspectAsync(Classes, Waiting, (provider, _, _) => test(provider), debug: true);

    // a statement run while the program waits, as the host runs one: in a pipeline of its own, composed for it.
    private static Task<ExecuteResult> Alongside(EnvironmentSessionProvider provider, string procedure)
    {
        var session = provider.Session;
        Assert.IsTrue(session.Symbols.TryResolveValue("Program", GlobalSymbols.UnresolvedSymbol, out var module));
        Assert.IsTrue(session.Symbols.TryResolveValue(procedure, module!, out var entry));
        var pipeline = RuntimeExecutionPipeline.Create(session, provider.Image, Substitute.For<IErrorDetailsRecorder>());
        return provider.Execution.RunAsync(pipeline, (VBTypeMemberSymbol)entry!, debug: false, CancellationToken.None, immediate: true).WaitAsync(Patience);
    }

    private static string[] Lines(ExecuteResult result) => [.. result.Output.Select(line => line.Trim())];

    [TestMethod]
    public async Task ACollectionTheProgramMade_IsReadWithItsMembers_ByAStatementRunWhileTheProgramWaits()
        => await DebugAsync(async provider =>
        {
            var counted = await Alongside(provider, "Count");
            var second = await Alongside(provider, "Second");

            Assert.AreEqual(ExecutionOutcome.Completed, counted.Outcome, counted.ErrorMessage);
            CollectionAssert.AreEqual(new[] { "2" }, Lines(counted));
            Assert.AreEqual(ExecutionOutcome.Completed, second.Outcome, second.ErrorMessage);
            CollectionAssert.AreEqual(new[] { "two" }, Lines(second));
        });

    [TestMethod]
    public async Task ACollectionTheProgramMade_DestroyedByAStatementRunWhileTheProgramWaits_LetsGoOfItsMembers()
        => await DebugAsync(async provider =>
        {
            var dropped = await Alongside(provider, "Drop");

            Assert.AreEqual(ExecutionOutcome.Completed, dropped.Outcome, dropped.ErrorMessage);
            CollectionAssert.AreEqual(new[] { "terminated", "dropped" }, Lines(dropped));
        });

    [TestMethod]
    public async Task ARegExpTheProgramMade_KeepsItsPattern_ForAStatementRunWhileTheProgramWaits()
        => await DebugAsync(async provider =>
        {
            var read = await Alongside(provider, "Pattern");

            Assert.AreEqual(ExecutionOutcome.Completed, read.Outcome, read.ErrorMessage);
            CollectionAssert.AreEqual(new[] { "b+" }, Lines(read));
        });

    [TestMethod]
    public async Task PipelinesComposedOnASession_GiveItNoHandlerOfWhatADestroyedObjectHolds()
        => await DebugAsync(async provider =>
        {
            var objects = (SessionObjects)provider.Session.Objects;
            var before = objects.DestroyingHandlerCount;

            for (var run = 0; run < 5; run++)
            {
                Assert.AreEqual(ExecutionOutcome.Completed, (await Alongside(provider, "Count")).Outcome);
            }

            Assert.AreEqual(before, objects.DestroyingHandlerCount, "the session lets go of what a destroyed object holds with the handlers it was composed with");
        });

    [TestMethod]
    public async Task PipelinesComposedOnASession_MakeNoObjectOfTheirOwn()
        => await DebugAsync(async provider =>
        {
            var objects = (SessionObjects)provider.Session.Objects;
            var before = objects.ObjectCount;

            for (var run = 0; run < 5; run++)
            {
                Assert.AreEqual(ExecutionOutcome.Completed, (await Alongside(provider, "Count")).Outcome);
            }

            Assert.AreEqual(before, objects.ObjectCount, "a pipeline's standard library makes no object until the program asks for one");
        });

    [TestMethod]
    public async Task TheErrorObjectTheProgramTook_IsTheErrorObject_OfAStatementRunWhileTheProgramWaits()
        => await DebugAsync(async provider =>
        {
            var compared = await Alongside(provider, "SameErr");

            Assert.AreEqual(ExecutionOutcome.Completed, compared.Outcome, compared.ErrorMessage);
            CollectionAssert.AreEqual(new[] { "True" }, Lines(compared));
        });
}
