using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.AST.Statements;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Runtime.Abstract.Execution;
using System.Collections.Immutable;

namespace RDCore.Runtime.Semantics.Precompiler;

/// <summary>
/// What evaluating the conditional compilation of a module found out.
/// </summary>
/// <param name="DeadRanges">The source ranges of every branch that is not live, in no particular order.</param>
/// <param name="Errors">The compile errors of the directives: a <c>#Const</c> declared twice, a condition that is not a number or a <c>Boolean</c>.</param>
public readonly record struct PrecompilerEvaluation(ImmutableArray<SourceRange> DeadRanges, ImmutableArray<VBCompileErrorInfo> Errors);

/// <summary>
/// Evaluates every <c>#If</c>/<c>#ElseIf</c>/<c>#Else</c> block a module's
/// <c>ModuleParseResult.PrecompilerTrivia</c> carries, yielding the source ranges of every branch that
/// is <em>not</em> live (<strong>MS-VBAL §3.4.2</strong>). A body statement whose own
/// <see cref="StatementNode.SourceLocation"/> falls inside one of these ranges did not actually compile
/// — <c>InstructionListLowering</c> skips it, the same way the real MS-VBA preprocessor "logically
/// removes" an excluded block before the rest of the language ever sees it.
/// </summary>
/// <remarks>
/// <strong>Why ranges, not nodes.</strong> The parser blanks only the <c>#</c>-prefixed directive lines
/// before its main pass (<c>ModuleParser</c>) — both branches' statements survive into the ordinary body
/// AST as plain siblings, with no link back to which <c>#If</c>/branch they came from.
/// <c>ModuleParseResult.PrecompilerTrivia</c> is a completely separate array, built by a second,
/// independent parse of the same source text. The only thing the two passes still agree on is source
/// position, so that is what correlates a branch to the statements inside it.
/// <para>
/// A <c>#If</c>/<c>#ElseIf</c> condition is evaluated through the same <see cref="RuntimeExpressionEvaluator"/>
/// every other expression is — a conditional-compilation constant is an ordinary
/// <see cref="PrecompilerNameExpressionNode"/> that evaluator resolves against the module's own <c>#Const</c> directives and then the project's, and
/// every comparison/logical/arithmetic operator a condition can use already has real runtime semantics
/// there. A condition that cannot be evaluated is a compile error (<see cref="PrecompilerEvaluation.Errors"/>), and this reports no dead range for that whole
/// <c>#If</c>/<c>#ElseIf</c>/<c>#Else</c> chain: an earlier unresolved header could have been the one that mattered, so nothing after it can be trusted either.
/// </para>
/// <para>
/// <strong>MS-VBAL §3.4.1</strong>: every <c>#Const</c> of the module is processed - those in an excluded block too - and binds a constant that every
/// <c>cc-expression</c> of the module can name, wherever in the module it is written, and that shadows a project-level constant of the same name.
/// </para>
/// </remarks>
public static class PrecompilerLiveBranchEvaluator
{
    /// <summary>
    /// Evaluates every <c>#If</c> block found (directly, or nested inside a live branch) in
    /// <paramref name="precompilerTrivia"/>.
    /// </summary>
    /// <param name="session">The runtime session <paramref name="evaluator"/> resolves conditional-compilation constants against.</param>
    /// <param name="evaluator">Evaluates a condition's expression tree.</param>
    /// <param name="context">The scope a condition's constants resolve from.</param>
    /// <param name="precompilerTrivia">A module's <c>ModuleParseResult.PrecompilerTrivia</c>.</param>
    /// <returns>The source ranges of every branch that is not live, in no particular order.</returns>
    public static ImmutableArray<SourceRange> GetDeadRanges(IRuntimeSession session, RuntimeExpressionEvaluator evaluator, RuntimeEvaluationContext context, ImmutableArray<SyntaxNode> precompilerTrivia)
        => Evaluate(session, evaluator, context, precompilerTrivia).DeadRanges;

    /// <summary>
    /// Evaluates the conditional compilation of a module: its <c>#Const</c> directives, then its <c>#If</c> blocks.
    /// </summary>
    /// <param name="session">The runtime session <paramref name="evaluator"/> resolves the project's conditional-compilation constants against.</param>
    /// <param name="evaluator">Evaluates a condition's expression tree.</param>
    /// <param name="context">The scope a condition's constants resolve from.</param>
    /// <param name="precompilerTrivia">A module's <c>ModuleParseResult.PrecompilerTrivia</c>.</param>
    public static PrecompilerEvaluation Evaluate(IRuntimeSession session, RuntimeExpressionEvaluator evaluator, RuntimeEvaluationContext context, ImmutableArray<SyntaxNode> precompilerTrivia)
    {
        var state = new Evaluation(session, evaluator, context);
        if (precompilerTrivia.IsDefaultOrEmpty)
        {
            return new([], []);
        }

        foreach (var node in precompilerTrivia)
        {
            state.Declare(node);
        }

        foreach (var node in precompilerTrivia)
        {
            Visit(node, state);
        }

        return new(state.DeadRanges.ToImmutable(), state.Errors.ToImmutable());
    }

    private static void Visit(SyntaxNode node, Evaluation state)
    {
        switch (node)
        {
            case PrecompilerIfBlockStatementNode ifBlock:
                EvaluateIfBlock(ifBlock, state);
                break;
            case PrecompilerTriviaNode trivia:
                // a live branch's own body may itself contain a nested #Const/#If.
                foreach (var child in trivia.Children)
                {
                    Visit(child, state);
                }
                break;
        }
    }

    private static void EvaluateIfBlock(PrecompilerIfBlockStatementNode ifBlock, Evaluation state)
    {
        var branches = CollectBranches(ifBlock);
        if (branches.Count == 0)
        {
            return;
        }

        var liveIndex = -1;
        for (var i = 0; i < branches.Count; i++)
        {
            var condition = branches[i].Condition;
            if (condition is null)
            {
                // #Else - reached only once every prior branch is determined false; always live then.
                liveIndex = i;
                break;
            }
            if (!state.TryEvaluateCondition(condition, out var isTrue))
            {
                return;
            }
            if (isTrue)
            {
                liveIndex = i;
                break;
            }
        }

        for (var i = 0; i < branches.Count; i++)
        {
            if (i == liveIndex)
            {
                continue;
            }
            if (branches[i].Body is { } deadBody)
            {
                state.DeadRanges.Add(deadBody.SourceLocation.Range);
            }
        }

        if (liveIndex >= 0 && branches[liveIndex].Body is { } liveBody)
        {
            foreach (var child in liveBody.Children)
            {
                Visit(child, state);
            }
        }
    }

    // One entry per #If/#ElseIf/#Else branch, in source order; Condition is null for #Else.
    private static List<(ExpressionNode? Condition, PrecompilerTriviaNode? Body)> CollectBranches(PrecompilerIfBlockStatementNode ifBlock)
    {
        var branches = new List<(ExpressionNode?, PrecompilerTriviaNode?)>();

        if (ifBlock.Children is [PrecompilerInlineIfStatementNode header, PrecompilerTriviaNode body, ..])
        {
            branches.Add((GetCondition(header.Children), body));
        }

        foreach (var elseIf in ifBlock.Children.OfType<PrecompilerElseIfBlockStatementNode>())
        {
            branches.Add((GetCondition(elseIf.Children), elseIf.Children.OfType<PrecompilerTriviaNode>().FirstOrDefault()));
        }

        if (ifBlock.Children.OfType<PrecompilerElseBlockStatementNode>().FirstOrDefault() is { } elseBlock)
        {
            branches.Add((null, elseBlock.Children.OfType<PrecompilerTriviaNode>().FirstOrDefault()));
        }

        return branches;
    }

    // The condition is whatever the ccExpression rule produced, directly at position 0.
    private static ExpressionNode? GetCondition(ImmutableArray<SyntaxNode> children)
        => children is [ExpressionNode condition, ..] ? condition : null;

    private sealed class Evaluation
    {
        private readonly IRuntimeSession _session;
        private readonly RuntimeExpressionEvaluator _evaluator;
        private readonly RuntimeEvaluationContext _context;

        // every #Const of the module, wherever it is written: a constant binds in the whole module (§3.4.1), so one is evaluated when something names it.
        private readonly Dictionary<string, ExpressionNode> _declared = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, VBTypedValue> _values = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _evaluating = new(StringComparer.OrdinalIgnoreCase);

        public Evaluation(IRuntimeSession session, RuntimeExpressionEvaluator evaluator, RuntimeEvaluationContext context)
        {
            _session = session;
            _evaluator = evaluator;
            _context = context with { ConditionalConstant = ConstantOf };
        }

        public ImmutableArray<SourceRange>.Builder DeadRanges { get; } = ImmutableArray.CreateBuilder<SourceRange>();

        public ImmutableArray<VBCompileErrorInfo>.Builder Errors { get; } = ImmutableArray.CreateBuilder<VBCompileErrorInfo>();

        // §3.4.1: "All <cc-const> directives are processed including those contained in excluded blocks", and the name of each is different.
        public void Declare(SyntaxNode node)
        {
            if (node is PrecompilerConstantDeclarationNode { Children: [PrecompilerNameExpressionNode name, ExpressionNode value, ..] } declaration)
            {
                if (!_declared.TryAdd(name.Name, value))
                {
                    Errors.Add(VBCompileErrorInfo.For(VBCompileErrorId.DuplicateDeclaration, declaration.SourceLocation, $"'{name.Name}' is declared more than once in this module."));
                }

                return;
            }

            // a #Const is a directive of a block; blocks hold blocks.
            if (node is PrecompilerIfBlockStatementNode or PrecompilerTriviaNode or PrecompilerElseIfBlockStatementNode or PrecompilerElseBlockStatementNode)
            {
                foreach (var child in node.Children)
                {
                    Declare(child);
                }
            }
        }

        // null when the module declares no such constant, and the name is the project's to bind (or is nothing, which is 0).
        private VBTypedValue? ConstantOf(string name)
        {
            if (_values.TryGetValue(name, out var known))
            {
                return known;
            }

            // a constant that names itself, directly or through others, has nothing to be but what the name means without it.
            if (!_declared.TryGetValue(name, out var expression) || !_evaluating.Add(name))
            {
                return null;
            }

            try
            {
                return _values[name] = TryEvaluate(expression, out var value) ? value : new VBIntegerValue(0);
            }
            finally
            {
                _evaluating.Remove(name);
            }
        }

        public bool TryEvaluateCondition(ExpressionNode condition, out bool isTrue)
        {
            isTrue = false;
            if (!TryEvaluate(condition, out var value))
            {
                return false;
            }

            if (TryCoerceBoolean(value, out isTrue))
            {
                return true;
            }

            Errors.Add(VBCompileErrorInfo.For(VBCompileErrorId.TypeMismatch, condition.Location, "A conditional-compilation condition is a number or a Boolean."));
            return false;
        }

        // The value of a cc-expression; or why it has none, as a compile error. An expression the interpreter cannot answer is not an error of the
        // program's, and is not reported as one.
        private bool TryEvaluate(ExpressionNode expression, out VBTypedValue value)
        {
            value = new VBIntegerValue(0);
            var result = _evaluator.Evaluate(_session, expression, _context);
            if (!result.IsSuccess)
            {
                if (result.ErrorInfo is { } error)
                {
                    Errors.Add(VBCompileErrorInfo.For(VBCompileErrorId.TypeMismatch, expression.Location, error.Description));
                }

                return false;
            }

            if (result.Result is VBObjectValue)
            {
                // Nothing is an object, and a conditional-compilation expression has none to be.
                Errors.Add(VBCompileErrorInfo.For(VBCompileErrorId.InvalidUseOfObject, expression.Location, "A conditional-compilation expression is not an object."));
                return false;
            }

            value = result.Result!;
            return true;
        }
    }

    private static bool TryCoerceBoolean(VBTypedValue value, out bool result)
    {
        if (value is VBBooleanValue boolean)
        {
            result = boolean.Value.StoredValue != 0;
            return true;
        }

        if (TryGetDouble(value, out var number))
        {
            result = number != 0;
            return true;
        }

        result = false;
        return false;
    }

    private static bool TryGetDouble(VBTypedValue value, out double result)
    {
        switch (value)
        {
            case VBBooleanValue boolean: result = boolean.Value.StoredValue != 0 ? -1 : 0; return true;
            case VBIntegerValue integer: result = integer.Value; return true;
            case VBLongValue longValue: result = longValue.Value; return true;
            case VBLongLongValue longLong: result = longLong.Value; return true;
            case VBSingleValue single: result = single.Value; return true;
            case VBDoubleValue @double: result = @double.Value; return true;
            default: result = 0; return false;
        }
    }
}
