using RDCore.Runtime.Semantics;
using RDCore.Runtime.Execution.External;
using RDCore.Runtime.Execution.External.Automation;
using RDCore.Runtime.StdLib;
using RDCore.Runtime.Semantics.LetCoercion;
using RDCore.Runtime.Semantics.Operators;
using RDCore.Runtime.Semantics.SetCoercion;
using RDCore.Runtime.Semantics.Statements;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Runtime.Abstract;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Shared;
using RDCore.SDK.Semantics.Analysis;
using RDCore.SDK.Semantics.Builders;
using RDCore.SDK.Semantics.Facts;
using RDCore.SDK.Semantics.Instructions;
using RDCore.SDK.Services.VerboseMessages;

namespace RDCore.Runtime.Execution;

/// <summary>
/// The whole runtime execution pipeline for one session, composed: every Let-coercion strategy, the
/// operator provider, the expression and statement evaluators, the block evaluators the executor
/// dispatches to, the procedure invoker, and the executor itself.
/// </summary>
/// <remarks>
/// The graph has a cycle in it by nature — a coercion strategy coerces its own operands, so it needs
/// the provider that owns it, and the expression evaluator invokes procedures, so it needs an invoker
/// built from an executor built from a statement provider built from that same evaluator. Composing
/// it is therefore not a matter of constructor order alone, and every caller that needs a running
/// runtime would otherwise have to know exactly how to break those cycles. This is the one place that
/// does.
/// </remarks>
public sealed class RuntimeExecutionPipeline
{
    private RuntimeExecutionPipeline(
        RuntimeExpressionEvaluator expressions,
        ILetCoercionRuntimeSemanticsProvider letCoercion,
        IStatementRuntimeSemanticsProvider statements,
        ProcedureExecutor executor,
        IProcedureInvoker invoker,
        RuntimeProcedureInvoker? sweeper)
    {
        Expressions = expressions;
        LetCoercion = letCoercion;
        Statements = statements;
        Executor = executor;
        Invoker = invoker;
        Sweeper = sweeper;
    }

    /// <summary>
    /// Enters a procedure to evaluate every instruction of it (<see cref="RuntimeProcedureInvoker.Sweep"/>); <see langword="null"/> unless the
    /// pipeline was composed to sweep (<see cref="CreateForSweep"/>).
    /// </summary>
    public RuntimeProcedureInvoker? Sweeper { get; }

    /// <summary>The Let-coercion strategies, as one provider.</summary>
    public ILetCoercionRuntimeSemanticsProvider LetCoercion { get; }

    /// <summary>Evaluates an expression against the session.</summary>
    public RuntimeExpressionEvaluator Expressions { get; }

    /// <summary>Executes one statement against the session.</summary>
    public IStatementRuntimeSemanticsProvider Statements { get; }

    /// <summary>Drives one activation's program counter through its instruction list.</summary>
    public ProcedureExecutor Executor { get; }

    /// <summary>Invokes a procedure of the session, from a call site or from a host.</summary>
    public IProcedureInvoker Invoker { get; }

    /// <summary>
    /// Composes the pipeline for <paramref name="session"/>.
    /// </summary>
    /// <param name="session">The session every part of the pipeline runs against.</param>
    /// <param name="bodies">Every procedure's lowered body, keyed by its <see cref="Symbol.SemanticId"/>.</param>
    /// <param name="messages">Builds the verbose half of a run-time error message.</param>
    /// <param name="cancellation">
    /// Stops the interpreter between instructions. A pipeline is composed per run, so this is that
    /// run's own cancellation — it is what makes a program that will never finish on its own
    /// interruptible.
    /// </param>
    /// <param name="observer">
    /// Told of every conversion and operation the pipeline evaluates, as the facts the language core states about the code
    /// being evaluated. <see langword="null"/> (the default) when the pipeline runs code rather than analyzes it: then nothing
    /// is observed, and nothing about how the code runs is different.
    /// </param>
    /// <param name="automation">
    /// What reaches the automation servers that the objects of a referenced library are held by; those of the machine the process runs on
    /// (<see cref="AutomationServers.Machine"/>) unless said otherwise.
    /// </param>
    public static RuntimeExecutionPipeline Create(
        IRuntimeSession session,
        IReadOnlyDictionary<SemanticId, InstructionList> bodies,
        IVerboseMessageBuilder messages,
        CancellationToken cancellation = default,
        IAnalysisObserver? observer = null,
        IAutomationServer? automation = null)
        => Build(session, bodies, messages, cancellation, observer, PipelineMode.Run, automation);

    /// <summary>
    /// Composes the pipeline for <paramref name="session"/>, a session composed to be analyzed (<see cref="RuntimeSessionComposer.ComposeForAnalysis"/>).
    /// </summary>
    /// <param name="session">The session every part of the pipeline runs against.</param>
    /// <param name="bodies">Every procedure's lowered body, keyed by its <see cref="Symbol.SemanticId"/>.</param>
    /// <param name="messages">Builds the verbose half of a run-time error message.</param>
    /// <param name="observer">Told of every conversion and operation the analysis evaluates.</param>
    /// <param name="cancellation">Stops the interpreter between instructions.</param>
    /// <remarks>
    /// <para>
    /// The code runs: assignments, objects, and the workspace's own procedures are evaluated for real, against state that belongs to the
    /// session. What differs is where the program meets what is not its own: a variable that outlives an activation starts as a value that is
    /// not known (<see cref="AnalysisDefaults"/>), and a call to the outside world is not made, and yields a value that is not known
    /// (<see cref="OutsideWorldCallProvider"/>).
    /// </para>
    /// </remarks>
    public static RuntimeExecutionPipeline CreateForAnalysis(
        IRuntimeSession session,
        IReadOnlyDictionary<SemanticId, InstructionList> bodies,
        IVerboseMessageBuilder messages,
        IAnalysisObserver observer,
        CancellationToken cancellation = default)
        => Build(session, bodies, messages, cancellation, observer, PipelineMode.Analysis);

    /// <summary>
    /// Composes the pipeline for <paramref name="session"/>, a session composed to be analyzed (<see cref="RuntimeSessionComposer.ComposeForAnalysis"/>),
    /// to sweep the code of a module: every instruction evaluated once, wherever the code paths lead (<see cref="Sweeper"/>).
    /// </summary>
    /// <param name="session">The session every part of the pipeline runs against.</param>
    /// <param name="bodies">Every procedure's lowered body, keyed by its <see cref="Symbol.SemanticId"/>.</param>
    /// <param name="messages">Builds the verbose half of a run-time error message.</param>
    /// <param name="observer">Told of every conversion and operation the sweep evaluates.</param>
    /// <param name="cancellation">Stops the sweep between instructions.</param>
    /// <remarks>
    /// <para>
    /// A sweep follows no path, so it can trust nothing that a path made true: what a variable holds is not known whatever was assigned to it
    /// (<see cref="RuntimeExpressionEvaluator.AssumesVariables"/>), and a call to a procedure of the workspace returns a value that is not known
    /// (<see cref="AssumedProcedureInvoker"/>) - the callee is swept as an entry point of its own. What stays known is what the code says: literals
    /// and constants. Facts that follow from the types alone (a <c>Double</c> narrowed into a <c>Long</c>) are stated for every line, and the
    /// run-time errors that the values the code says guarantee (<c>Dim b As Byte: b = 300</c>) are stated for the lines that raise them.
    /// </para>
    /// </remarks>
    public static RuntimeExecutionPipeline CreateForSweep(
        IRuntimeSession session,
        IReadOnlyDictionary<SemanticId, InstructionList> bodies,
        IVerboseMessageBuilder messages,
        IAnalysisObserver observer,
        CancellationToken cancellation = default)
        => Build(session, bodies, messages, cancellation, observer, PipelineMode.Sweep);

    private enum PipelineMode
    {
        Run,
        Analysis,
        Sweep,
    }

    private static RuntimeExecutionPipeline Build(
        IRuntimeSession session,
        IReadOnlyDictionary<SemanticId, InstructionList> bodies,
        IVerboseMessageBuilder messages,
        CancellationToken cancellation,
        IAnalysisObserver? observer,
        PipelineMode mode,
        IAutomationServer? automation = null)
    {
        var analysis = mode is not PipelineMode.Run;
        // the parts of the pipeline that state facts share one observation, so that describing a fact is not itself observed.
        var observation = observer is null ? null : new AnalysisObservation(observer);
        var handle = new ProviderHandle();
        var objectCoercion = new VBObjectLetCoercionRuntimeSemantics(handle, messages);

        var letCoercion = new LetCoercionRuntimeSemanticsProvider(
            [
                new VBNumericLetCoercionTypeRuntimeSemantics(messages, handle),
                new VBBooleanLetCoercionRuntimeSemantics(handle, messages),
                new VBStringLetCoercionRuntimeSemantics(messages),
                new VBDateLetCoercionRuntimeSemantics(handle, messages),
                new VBFixedStringLetCoercionRuntimeSemantics(handle, messages),
                new VBVariantTypeLetCoercionRuntimeSemantics(handle, messages),
                new VBEmptyTypeLetCoercionRuntimeSemantics(messages),
                new VBNullTypeLetCoercionRuntimeSemantics(messages),
                new VBErrorTypeLetCoercionRuntimeSemantics(handle, messages),
                objectCoercion,
                new VBUserDefinedTypeLetCoercionRuntimeSemantics(handle, messages),
                new VBResizableByteArrayLetCoercionRuntimeSemantics(handle, messages),
                new VBResizableArrayLetCoercionRuntimeSemantics(handle, messages),
            ],
            messages,
            observation);
        handle.Inner = letCoercion;

        var setCoercion = new SetCoercionRuntimeSemantics(messages);
        var operators = new OperatorRuntimeSemanticsProvider(letCoercion, messages, observation);
        var expressions = new RuntimeExpressionEvaluator(operators);
        var print = new PrintOutputEvaluator(expressions, letCoercion);
        var conditions = new ConditionEvaluator(expressions, letCoercion);
        var assignments = new LetAssignmentEvaluator(letCoercion, operators, expressions);
        var files = new FileStatementRuntimeSemantics(
            expressions, print, new WriteOutputEvaluator(expressions), letCoercion,
            assignments, new InputListEvaluator(assignments));
        var statements = new StatementRuntimeSemanticsProvider(
            expressions, assignments, setCoercion, print, conditions, files,
            new FixedAssignmentRuntimeSemantics(expressions, letCoercion, assignments),
            new ArrayStatementRuntimeSemantics(expressions, letCoercion, assignments),
            new MidStatementRuntimeSemantics(expressions, letCoercion, assignments));

        var executor = new ProcedureExecutor(
            statements,
            conditions,
            new WithTargetEvaluator(expressions, new WithStatementRuntimeSemantics(setCoercion, letCoercion)),
            new CaseMatchEvaluator(expressions, operators),
            new ForLoopEvaluator(expressions, operators),
            new ForEachEvaluator(expressions, operators, setCoercion),
            new JumpTableEvaluator(expressions, letCoercion),
            new ErrorHandlingEvaluator(expressions, letCoercion),
            cancellation);

        // the evaluator needs the invoker, which needs the executor, which needs the evaluator: the
        // last edge of the cycle is closed by assignment rather than by construction.
        // the standard library is part of every session (RD-VBAL 6.1), so the dispatcher that reaches it is
        // composed here with the rest of the pipeline rather than being something a caller opts into.
        var running = new RuntimeProcedureInvoker(session, bodies, executor);
        // a sweep calls nothing: it enters each procedure itself, and a call is the value that the procedure is declared to return.
        IProcedureInvoker invoker = mode is PipelineMode.Sweep ? new AssumedProcedureInvoker() : running;
        // every external call goes through the pipeline: the interceptors see it and may refuse it, then
        // whichever provider can reach it runs it.
        // an analysis does not make a call the platform does not run itself, and no policy decides whether it may: the outside world answers
        // with a value that is not known.
        var external = analysis
            ? new ExternalCallPipeline(session, [], [StdLibDispatcher.For(session), new OutsideWorldCallProvider()])
            : ExternalCallPipeline.For(session, [StdLibDispatcher.For(session), new AutomationCallProvider(session, automation ?? AutomationServers.Machine)]);
        var bindings = new RuntimeCallableBindingFactory(invoker, external);
        expressions.ProcedureInvoker = invoker;
        expressions.Bindings = bindings;
        expressions.AssumesVariables = mode is PipelineMode.Sweep;
        // a default member is invoked the same way any member is, and it was reaching neither engine before:
        // nothing assigned these, so every default-member Let-coercion reported an internal error.
        objectCoercion.ProcedureInvoker = invoker;
        objectCoercion.Bindings = bindings;
        objectCoercion.Expressions = expressions;
        objectCoercion.Session = session;
        expressions.LetCoercionProvider = letCoercion;
        expressions.SetCoercion = setCoercion;
        // an object's lifecycle events run its class's handlers, which is code only this pipeline can run.
        session.Lifecycle = new ClassLifecycle(session, bindings);
        // an object of a library's class is made by whatever reaches that library, through the same pipeline as every call into it.
        session.External = external;
        // a fixed-size array is as big as its declaration says, which takes evaluating its bounds: this is what can.
        var defaults = new DeclaredVariableDefaults(session, new ArrayBoundEvaluator(expressions, letCoercion));
        if (analysis)
        {
            // the session shares the declared symbols of another, and holds none of its storage yet: what that starts as is the analysis's to say.
            session.Symbols.Defaults = new AnalysisDefaults(defaults);
            ((SessionSymbols)session.Symbols).AllocateDeclaredStorage();
        }
        else
        {
            session.Symbols.Defaults = defaults;
        }

        return new RuntimeExecutionPipeline(expressions, letCoercion, statements, executor, invoker, mode is PipelineMode.Sweep ? running : null);
    }

    /// <summary>
    /// Stands in for the coercion provider while the strategies that make it up are being built, and
    /// forwards to the real one once there is one.
    /// </summary>
    /// <remarks>
    /// A coercion strategy Let-coerces its own operands — a numeric coercion of a <c>Variant</c>
    /// coerces the wrapped value first — so it needs the provider that owns it, which cannot exist
    /// until every strategy does. This breaks that construction cycle without any strategy having to
    /// know about it.
    /// </remarks>
    private sealed class ProviderHandle : ILetCoercionRuntimeSemanticsProvider
    {
        public ILetCoercionRuntimeSemanticsProvider Inner { get; set; } = default!;

        public LetCoercionResult EvaluateLetCoercionSemantics(ISymbolResolver resolver, ExpressionNode expression, LetCoercionStackFrame frame)
            => Inner.EvaluateLetCoercionSemantics(resolver, expression, frame);

        public LetCoercionAnalysisContext Analyze(ISymbolResolver resolver, ILetCoercionSemanticContextBuilder builder, ExpressionNode expression, LetCoercionStackFrame frame)
            => Inner.Analyze(resolver, builder, expression, frame);
    }
}
