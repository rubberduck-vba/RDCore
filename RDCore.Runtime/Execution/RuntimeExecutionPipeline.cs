using RDCore.Runtime.Semantics;
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
        IProcedureInvoker invoker)
    {
        Expressions = expressions;
        LetCoercion = letCoercion;
        Statements = statements;
        Executor = executor;
        Invoker = invoker;
    }

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
    public static RuntimeExecutionPipeline Create(
        IRuntimeSession session,
        IReadOnlyDictionary<SemanticId, InstructionList> bodies,
        IVerboseMessageBuilder messages)
    {
        var handle = new ProviderHandle();
        var booleanCoercion = new VBBooleanLetCoercionRuntimeSemantics(handle, messages);
        var numericCoercion = new VBNumericLetCoercionTypeRuntimeSemantics(messages, handle);
        var stringCoercion = new VBStringLetCoercionRuntimeSemantics(messages);

        var letCoercion = new LetCoercionRuntimeSemanticsProvider(
            [
                numericCoercion,
                booleanCoercion,
                stringCoercion,
                new VBDateLetCoercionRuntimeSemantics(handle, messages),
                new VBFixedStringLetCoercionRuntimeSemantics(handle, messages),
                new VBVariantTypeLetCoercionRuntimeSemantics(handle, messages),
                new VBEmptyTypeLetCoercionRuntimeSemantics(messages),
                new VBNullTypeLetCoercionRuntimeSemantics(messages),
                new VBErrorTypeLetCoercionRuntimeSemantics(handle, messages),
                new VBObjectLetCoercionRuntimeSemantics(handle, messages),
                new VBUserDefinedTypeLetCoercionRuntimeSemantics(handle, messages),
                new VBResizableByteArrayLetCoercionRuntimeSemantics(handle, messages),
                new VBResizableArrayLetCoercionRuntimeSemantics(handle, messages),
            ],
            messages);
        handle.Inner = letCoercion;

        var setCoercion = new SetCoercionRuntimeSemantics(messages);
        var expressions = new RuntimeExpressionEvaluator(new OperatorRuntimeSemanticsProvider(letCoercion, messages));
        var print = new PrintOutputEvaluator(expressions, stringCoercion, numericCoercion);
        var statements = new StatementRuntimeSemanticsProvider(expressions, letCoercion, setCoercion, print, messages);

        var executor = new ProcedureExecutor(
            statements,
            new ConditionEvaluator(expressions, booleanCoercion),
            new WithTargetEvaluator(expressions, new WithStatementRuntimeSemantics(setCoercion, letCoercion)),
            new CaseMatchEvaluator(expressions, letCoercion, messages),
            new ForLoopEvaluator(expressions, letCoercion, messages),
            new ForEachEvaluator(expressions, letCoercion, setCoercion, messages),
            new JumpTableEvaluator(expressions, numericCoercion),
            new ErrorHandlingEvaluator(expressions, numericCoercion));

        // the evaluator needs the invoker, which needs the executor, which needs the evaluator: the
        // last edge of the cycle is closed by assignment rather than by construction.
        var invoker = new RuntimeProcedureInvoker(session, bodies, executor);
        expressions.ProcedureInvoker = invoker;
        expressions.LetCoercionProvider = letCoercion;

        return new RuntimeExecutionPipeline(expressions, letCoercion, statements, executor, invoker);
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
