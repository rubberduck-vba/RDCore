using Microsoft.Extensions.Options;
using RDCore.LanguageServer.Parsing;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.AST.Statements;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Server.Configuration;

namespace RDCore.LanguageServer.Debugging;

/// <summary>
/// What a client can do with the program that runs under a debugger, whichever protocol it speaks: the language server's side of <c>rdcore/session/debug/*</c> and of
/// the debug adapter alike.
/// </summary>
/// <remarks>
/// The component that owns the runtime session is the only one that knows where the program is; this is the language server's way to it, and the one place the language
/// server decides what a request means when there is no such component, or when the text of an expression is not one.
/// </remarks>
internal interface IProgramDebugService
{
    /// <summary>Goes on with the program that waits, to the next place it waits or to its end.</summary>
    /// <param name="step">How far it goes, or <see langword="null"/> for as far as it goes.</param>
    /// <param name="token">A token that cancels the request.</param>
    Task<ExecuteSessionResult> ResumeAsync(StepKind? step, CancellationToken token);

    /// <summary>Stops the program that is running where it is.</summary>
    /// <param name="token">A token that cancels the request.</param>
    Task<HostDebugAck> PauseAsync(CancellationToken token);

    /// <summary>Moves the point the program that waits goes on from.</summary>
    /// <param name="line">The zero-based line, when the point is a line.</param>
    /// <param name="label">The label, when the point is a label.</param>
    /// <param name="token">A token that cancels the request.</param>
    Task<HostDebugGotoResult> GotoAsync(int line, string? label, CancellationToken token);

    /// <summary>Ends the program that runs or waits.</summary>
    /// <param name="wipe">Whether the session is wiped as well.</param>
    /// <param name="token">A token that cancels the request.</param>
    Task<HostDebugAck> TerminateAsync(bool wipe, CancellationToken token);

    /// <summary>Sets the lines of a module the program waits at.</summary>
    /// <param name="moduleName">The programmatic name of the module.</param>
    /// <param name="lines">The zero-based lines, replacing the ones set before.</param>
    /// <param name="token">A token that cancels the request.</param>
    Task<HostDebugBreakpointsResult> SetBreakpointsAsync(string moduleName, IReadOnlyList<int> lines, CancellationToken token);

    /// <summary>The activations of the program that waits, innermost first.</summary>
    /// <param name="token">A token that cancels the request.</param>
    Task<HostDebugStackResult> StackAsync(CancellationToken token);

    /// <summary>The variables of an activation of the program that waits, or the parts of one of them.</summary>
    /// <param name="frameId">The activation, by its place on the stack.</param>
    /// <param name="scope">Which of its variables.</param>
    /// <param name="reference">The variable whose parts are wanted, or <c>0</c> for the variables of the scope.</param>
    /// <param name="token">A token that cancels the request.</param>
    Task<HostDebugVariablesResult> VariablesAsync(int frameId, HostVariableScope scope, int reference, CancellationToken token);

    /// <summary>The value of an expression in an activation of the program that waits.</summary>
    /// <param name="frameId">The activation, by its place on the stack.</param>
    /// <param name="expression">The expression, as text.</param>
    /// <param name="token">A token that cancels the request.</param>
    Task<HostDebugEvaluateResult> EvaluateAsync(int frameId, string expression, CancellationToken token);

    /// <summary>
    /// Runs the statement a person typed in an activation of the program that waits, as the immediate window of a procedure that is stopped does.
    /// </summary>
    /// <param name="frameId">The activation, by its place on the stack.</param>
    /// <param name="text">What was typed: one statement. <c>?</c> at the start of it is <c>Debug.Print</c>, as it is in the immediate window, and that is the only way a
    /// value is shown - <c>x = 5</c> is an assignment, <c>?x = 5</c> is a comparison that is printed, and <c>SomeFunc(5)</c> is a call that prints nothing.</param>
    /// <param name="token">A token that cancels the request.</param>
    /// <returns>What the statement printed, or why it was not run.</returns>
    Task<HostDebugEvaluateResult> ExecuteAsync(int frameId, string text, CancellationToken token);

    /// <summary>Says which run-time errors the program waits at, where they are raised.</summary>
    /// <param name="mode">Which.</param>
    /// <param name="token">A token that cancels the request.</param>
    Task SetErrorBreakAsync(ErrorBreakMode mode, CancellationToken token);
}

/// <summary>
/// Relays what a client asks of the program that runs under a debugger to the component that owns the runtime session.
/// </summary>
/// <remarks>
/// The parser parses statements, and an expression is the right-hand side of one: the text of an expression is written as the value of an assignment inside a procedure of
/// its own, and the value is the tree that comes back. Text that is not one expression - a second statement after a <c>:</c>, a line break - is not an expression, and the
/// answer says so without it reaching the host.
/// </remarks>
internal sealed class ProgramDebugService(
    IPlatformOrchestrationService orchestration,
    IParsingClientService parsing,
    IOptions<SdkAppOptions> options) : IProgramDebugService
{
    public async Task<ExecuteSessionResult> ResumeAsync(StepKind? step, CancellationToken token)
    {
        if (orchestration.RuntimeEnvironment is not { } environment)
        {
            return new ExecuteSessionResult { Outcome = ExecutionOutcome.NotFound, ErrorMessage = DebuggerMessages.NoRuntimeEnvironment };
        }

        await environment.WaitForReadyAsync(token);
        return await environment.SendRequestAsync<HostDebugResumeParams, ExecuteSessionResult>(new HostDebugResumeParams { Step = step }, token);
    }

    public async Task<HostDebugAck> PauseAsync(CancellationToken token)
    {
        if (orchestration.RuntimeEnvironment is not { } environment)
        {
            return new HostDebugAck();
        }

        await environment.WaitForReadyAsync(token);
        return await environment.SendRequestAsync<HostDebugPauseParams, HostDebugAck>(new HostDebugPauseParams(), token);
    }

    public async Task<HostDebugGotoResult> GotoAsync(int line, string? label, CancellationToken token)
    {
        if (orchestration.RuntimeEnvironment is not { } environment)
        {
            return new HostDebugGotoResult { Reason = DebuggerMessages.NoRuntimeEnvironment };
        }

        await environment.WaitForReadyAsync(token);
        return await environment.SendRequestAsync<HostDebugGotoParams, HostDebugGotoResult>(new HostDebugGotoParams { Line = line, Label = label }, token);
    }

    public async Task<HostDebugAck> TerminateAsync(bool wipe, CancellationToken token)
    {
        if (orchestration.RuntimeEnvironment is not { } environment)
        {
            return new HostDebugAck();
        }

        await environment.WaitForReadyAsync(token);
        return await environment.SendRequestAsync<HostDebugTerminateParams, HostDebugAck>(new HostDebugTerminateParams { Wipe = wipe }, token);
    }

    public async Task<HostDebugBreakpointsResult> SetBreakpointsAsync(string moduleName, IReadOnlyList<int> lines, CancellationToken token)
    {
        if (orchestration.RuntimeEnvironment is not { } environment)
        {
            return new HostDebugBreakpointsResult { Breakpoints = [.. lines.Select(line => new HostBreakpoint(line, false))] };
        }

        await environment.WaitForReadyAsync(token);
        return await environment.SendRequestAsync<HostDebugBreakpointsParams, HostDebugBreakpointsResult>(
            new HostDebugBreakpointsParams { ModuleName = moduleName, Lines = lines }, token);
    }

    public async Task<HostDebugStackResult> StackAsync(CancellationToken token)
    {
        if (orchestration.RuntimeEnvironment is not { } environment)
        {
            return new HostDebugStackResult();
        }

        await environment.WaitForReadyAsync(token);
        return await environment.SendRequestAsync<HostDebugStackParams, HostDebugStackResult>(new HostDebugStackParams(), token);
    }

    public async Task<HostDebugVariablesResult> VariablesAsync(int frameId, HostVariableScope scope, int reference, CancellationToken token)
    {
        if (orchestration.RuntimeEnvironment is not { } environment)
        {
            return new HostDebugVariablesResult();
        }

        await environment.WaitForReadyAsync(token);
        return await environment.SendRequestAsync<HostDebugVariablesParams, HostDebugVariablesResult>(
            new HostDebugVariablesParams { FrameId = frameId, Scope = scope, Reference = reference }, token);
    }

    public async Task<HostDebugEvaluateResult> EvaluateAsync(int frameId, string expression, CancellationToken token)
    {
        if (orchestration.RuntimeEnvironment is not { } environment)
        {
            return new HostDebugEvaluateResult { Error = DebuggerMessages.NoRuntimeEnvironment };
        }

        if (expression.Contains('\n') || expression.Contains('\r') || string.IsNullOrWhiteSpace(expression))
        {
            return new HostDebugEvaluateResult { Error = DebuggerMessages.ExpressionIsOneLine };
        }

        var document = new UriBuilder(new Uri(options.Value.Workspace.WorkspaceUri)) { Fragment = "Evaluate" }.Uri;
        var parsed = await parsing.ParseFragmentAsync(document, $"Public Sub __Evaluate()\r\n__e = {expression}\r\nEnd Sub\r\n", token);
        if (!parsed.IsSuccess || parsed.SyntaxTree is null)
        {
            return new HostDebugEvaluateResult { Error = parsed.SyntaxErrors.FirstOrDefault()?.Description ?? DebuggerMessages.NotAnExpression };
        }

        var assignments = Descendants(parsed.SyntaxTree).OfType<AssignmentStatementNode>().ToArray();
        if (assignments is not [{ Value: { } value }])
        {
            return new HostDebugEvaluateResult { Error = DebuggerMessages.NotOneExpression };
        }

        await environment.WaitForReadyAsync(token);
        return await environment.SendRequestAsync<HostDebugEvaluateParams, HostDebugEvaluateResult>(
            new HostDebugEvaluateParams { FrameId = frameId, Json = PlatformJson.Serialize<ExpressionNode>(value) }, token);
    }

    public async Task<HostDebugEvaluateResult> ExecuteAsync(int frameId, string text, CancellationToken token)
    {
        if (orchestration.RuntimeEnvironment is not { } environment)
        {
            return new HostDebugEvaluateResult { Error = DebuggerMessages.NoRuntimeEnvironment };
        }

        if (text.Contains('\n') || text.Contains('\r') || string.IsNullOrWhiteSpace(text))
        {
            return new HostDebugEvaluateResult { Error = DebuggerMessages.StatementIsOneLine };
        }

        // the immediate window writes a question mark for Debug.Print.
        var trimmed = text.Trim();
        var source = trimmed.StartsWith('?') ? $"Debug.Print {trimmed[1..].TrimStart()}" : trimmed;

        // a statement is parsed in a procedure of its own, which is where the parser has statements.
        var document = new UriBuilder(new Uri(options.Value.Workspace.WorkspaceUri)) { Fragment = "Execute" }.Uri;
        var parsed = await parsing.ParseFragmentAsync(document, $"Public Sub __Execute()\r\n{source}\r\nEnd Sub\r\n", token);
        if (!parsed.IsSuccess || parsed.SyntaxTree is null)
        {
            return new HostDebugEvaluateResult { Error = parsed.SyntaxErrors.FirstOrDefault()?.Description ?? DebuggerMessages.NotAStatement };
        }

        var statements = parsed.SyntaxTree.Children.OfType<MemberDeclarationNode>().SelectMany(member => member.Children.OfType<StatementNode>()).ToArray();
        if (statements is not [var statement])
        {
            return new HostDebugEvaluateResult { Error = DebuggerMessages.OneStatementAtATime };
        }

        await environment.WaitForReadyAsync(token);
        return await environment.SendRequestAsync<HostDebugExecuteParams, HostDebugEvaluateResult>(
            new HostDebugExecuteParams { FrameId = frameId, Json = PlatformJson.Serialize<SyntaxNode>(statement) }, token);
    }

    public async Task SetErrorBreakAsync(ErrorBreakMode mode, CancellationToken token)
    {
        if (orchestration.RuntimeEnvironment is not { } environment)
        {
            return;
        }

        await environment.WaitForReadyAsync(token);
        _ = await environment.SendRequestAsync<HostDebugErrorBreakParams, HostDebugAck>(new HostDebugErrorBreakParams { Mode = mode }, token);
    }

    private static IEnumerable<SyntaxNode> Descendants(SyntaxNode node) => node.Children.SelectMany(Descendants).Prepend(node);
}
