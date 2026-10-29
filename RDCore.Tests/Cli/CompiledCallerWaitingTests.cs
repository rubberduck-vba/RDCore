using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using RDCore.CLI.Host;
using RDCore.CLI.Host.Handlers;
using RDCore.Parsing;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Services.VerboseMessages;

namespace RDCore.Tests.Cli;

/// <summary>
/// A compiled caller of a callee the program waits in goes on as an interpreted caller does, whatever was changed while it waited: the module defined
/// again with the caller's body as it was, its function result, or its variables.
/// </summary>
[TestClass]
public sealed class CompiledCallerWaitingTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(20);

    private static string Program(params string[] lines) => string.Join("\r\n", lines) + "\r\n";

    private static Task<ExecuteSessionResult> Resume(EnvironmentSessionProvider provider)
        => new HostDebugResumeHandler(provider, NullLogger<HostDebugResumeHandler>.Instance)
            .Handle(new HostDebugResumeParams(), CancellationToken.None).WaitAsync(Patience);

    private static Task<HostDebugAck> Terminate(EnvironmentSessionProvider provider)
        => new HostDebugTerminateHandler(provider).Handle(new HostDebugTerminateParams(), CancellationToken.None).WaitAsync(Patience);

    private static SyntaxNode Statement(string text)
    {
        var parse = new ModuleParser().Parse(new Uri("file:///c:/ws/Execute.bas"), $"Public Sub E()\r\n{text}\r\nEnd Sub\r\n");
        Assert.IsTrue(parse.IsSuccess, string.Join("; ", parse.SyntaxErrors.Select(error => error.Verbose)));
        return parse.SyntaxTree!.Children.OfType<MemberDeclarationNode>().Single().Children.OfType<StatementNode>().Single();
    }

    // a statement a debugger runs in the frame of Outer.
    private static async Task InOuter(EnvironmentSessionProvider provider, string statement)
    {
        var outer = provider.Execution.Stack().Frames.Single(frame => frame.Procedure == "Outer");
        var made = await new HostDebugExecuteHandler(provider, Substitute.For<IVerboseMessageBuilder>())
            .Handle(new HostDebugExecuteParams { FrameId = outer.Id, Json = PlatformJson.Serialize(Statement(statement)) }, CancellationToken.None).WaitAsync(Patience);
        Assert.IsTrue(made.Success, made.Error);
    }

    private static string Observed(ExecuteSessionResult result)
        => $"{result.Outcome} | {string.Join(" ; ", result.Output.Select(line => line.Trim()))} | {result.ErrorNumber} {result.ErrorMessage} @{result.ErrorLine}";

    // Main waits at its Stop; resumed, it calls Outer, which waits in its call of Inner; then the module is defined again as edited, or a debugger runs
    // statements in Outer's frame, and the program goes on to its end. What it is to the person who runs it.
    private static async Task<string> WaitedInAsync(string program, string? edited, string[] statements, VBExecutionEngine engine)
    {
        var observed = "";
        await ModuleWorkspace.InspectAsync([], program, async (provider, first, runAgain) =>
        {
            Assert.AreEqual(ExecutionOutcome.Suspended, first.Outcome, first.ErrorMessage);
            try
            {
                var stopped = await Resume(provider);
                Assert.AreEqual(ExecutionOutcome.Suspended, stopped.Outcome, stopped.ErrorMessage);
                Assert.AreEqual(("Inner", "Outer"), (provider.Execution.Stack().Frames[0].Procedure, provider.Execution.Stack().Frames[1].Procedure));
                if (engine is VBExecutionEngine.Compiled)
                {
                    Assert.IsTrue(provider.Image.Compiler.Results.Any(result => result.Procedure.Name == "Outer" && result.Result.Body is not null), "Outer runs compiled");
                }

                if (edited is not null)
                {
                    Assert.IsEmpty(await ModuleWorkspace.ReloadOnlyAsync(provider, ["Program"], ("Program", ModuleType.StdModule, edited)));
                }

                foreach (var statement in statements)
                {
                    await InOuter(provider, statement);
                }

                observed = Observed(await Resume(provider));
            }
            finally
            {
                if (provider.Execution.State is not ProgramState.Idle)
                {
                    _ = await Terminate(provider);
                }
            }
        }, debug: true, engine: engine);
        return observed;
    }

    private static async Task<string> AssertAlikeAsync(string program, string? edited, params string[] statements)
    {
        CompiledExecutionTests.RequirePayloads();
        var interpreted = await WaitedInAsync(program, edited, statements, VBExecutionEngine.Interpreter);
        var compiled = await WaitedInAsync(program, edited, statements, VBExecutionEngine.Compiled);
        Assert.AreEqual(interpreted, compiled);
        return compiled;
    }

    // Outer calls Inner, which waits at its first call, then Helper; only Helper, after Outer, and the module's declarations are edited.
    private static string Calling(string declarations, string helper, string helperCall = "Helper(i)") => Program(
        declarations,
        "Public Sub Main()",
        "    Stop",
        "    Debug.Print Outer(3)",
        "End Sub",
        "Private Function Outer(ByVal n As Long) As Long",
        "    Dim i As Long, t As Long, u As Long",
        "    For i = 1 To n",
        "        u = Inner(i)",
        "        t = t + u",
        $"        u = {helperCall}",
        "        t = t + u + K",
        "    Next",
        "    Outer = t",
        "End Function",
        "Private Function Inner(ByVal x As Long) As Long",
        "    If x = 1 Then Stop",
        "    Inner = x",
        "End Function",
        helper);

    private const string Constant = "Private Const K As Long = 5";

    // the header of a callee edited while the caller waits: the caller's instructions are as they were, and what they call is bound as it now is.
    [TestMethod]
    [DataRow("ByVal y As Integer", "ByVal y As Double", "As Long", "Helper = y * 10", "Helper(i + 0.5)", DisplayName = "a parameter of another type")]
    [DataRow("ByVal y As Long", "ByRef y As Long", "As Long", "Helper = y: y = y + 100", "Helper(t)", DisplayName = "a parameter by reference")]
    [DataRow("ByVal y As Long", "ByVal y As Long, Optional ByVal z As Long = 7", "As Long", "Helper = y", "Helper(i)", DisplayName = "an optional parameter added")]
    [DataRow("ByVal y As Long", "ByVal y As Long", "As Double", "Helper = y * 1.5", "Helper(i)", DisplayName = "a result of another type")]
    public async Task ACallee_EditedWhileACompiledCallerWaits_IsCalledAsItNowIs(string parameters, string edited, string returns, string body, string call)
    {
        var helper = $"Private Function Helper({parameters}) As Long\r\n    {body}\r\nEnd Function";
        var editedHelper = $"Private Function Helper({edited}) {returns}\r\n    {body}\r\nEnd Function";
        if (edited.Contains("Optional", StringComparison.Ordinal))
        {
            editedHelper = editedHelper.Replace("Helper = y", "Helper = y + z", StringComparison.Ordinal);
        }

        _ = await AssertAlikeAsync(Calling(Constant, helper, call), Calling(Constant, editedHelper, call));
    }

    // a constant the program already read, given another value, is not applied to a program that waits: refused as for an interpreted caller.
    [TestMethod]
    public async Task AConstant_EditedWhileACompiledCallerWaits_IsRefusedAsForAnInterpretedCaller()
    {
        const string helper = "Private Function Helper(ByVal y As Long) As Long\r\n    Helper = y\r\nEnd Function";
        var observed = await AssertAlikeAsync(Calling(Constant, helper), Calling("Private Const K As Long = 6", helper));
        StringAssert.StartsWith(observed, "Refused");
        StringAssert.Contains(observed, "Program.K");
    }

    private static readonly string Returning = Program(
        "Public Sub Main()",
        "    Stop",
        "    Debug.Print Outer(3)",
        "End Sub",
        "Private Function Outer(ByVal n As Long) As Long",
        "    Dim u As Long",
        "    u = Inner(1)",
        "    n = n + u",
        "End Function",
        "Private Function Inner(ByVal x As Long) As Long",
        "    If x = 1 Then Stop",
        "    Inner = x",
        "End Function");

    // what a debugger sets in the compiled caller's frame while the program waits in the callee is what the caller goes on with.
    [TestMethod]
    public async Task TheResultOfACompiledCaller_SetWhileItWaits_IsWhatItReturns()
        => StringAssert.Contains(await AssertAlikeAsync(Returning, null, "Outer = 1000"), "| 1000 |");

    [TestMethod]
    public async Task AVariableOfACompiledCaller_SetWhileItWaits_IsWhatItGoesOnWith()
        => StringAssert.Contains(await AssertAlikeAsync(Returning.Replace("    n = n + u", "    Outer = n + u", StringComparison.Ordinal), null, "n = 1000"), "| 1001 |");
}
