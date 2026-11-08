using RDCore.SDK.Model.AST.Abstract;

namespace RDCore.Runtime.LanguageCore.Static.Abstract;

/// <summary>
/// What the expressions of one tree evaluated to, while the tree is evaluated: a check that looks at an operand again (its declared type, whether it is a
/// value) is given what the operand evaluated to, and does not evaluate it again. An expression is evaluated once however deep it is nested, where evaluating
/// it again at each level would make the work of a nested call a power of its depth.
/// </summary>
/// <remarks>
/// An evaluation is reused where it answers the one asked for: with the same resolver, in the same scope, With block and environment, and recording at least
/// what is asked to be recorded. One that records nothing (<c>Facts</c> and <c>Bindings</c> both <see langword="null"/>) is answered by any, and one that
/// records is answered only by an evaluation that recorded into the same sinks, which already hold what it would record.
/// </remarks>
internal sealed class ExpressionEvaluations
{
    private readonly Dictionary<ExpressionNode, (StaticEvaluationContext Context, StaticSemanticsEvaluationResult Result)> _evaluated = new(ReferenceEqualityComparer.Instance);

    public bool TryGet(in StaticEvaluationContext context, ExpressionNode expression, out StaticSemanticsEvaluationResult result)
    {
        if (_evaluated.TryGetValue(expression, out var evaluated) && Answers(evaluated.Context, context))
        {
            result = evaluated.Result;
            return true;
        }

        result = default;
        return false;
    }

    public void Add(in StaticEvaluationContext context, ExpressionNode expression, StaticSemanticsEvaluationResult result)
        => _evaluated[expression] = (context, result);

    private static bool Answers(in StaticEvaluationContext evaluated, in StaticEvaluationContext asked)
        => ReferenceEquals(evaluated.Resolver, asked.Resolver) && ReferenceEquals(evaluated.Scope, asked.Scope)
            && Equals(evaluated.EnclosingWithTargetType, asked.EnclosingWithTargetType)
            && Equals(evaluated.Environment, asked.Environment)
            && (asked.Facts is null || ReferenceEquals(asked.Facts, evaluated.Facts))
            && (asked.Bindings is null || ReferenceEquals(asked.Bindings, evaluated.Bindings));
}
