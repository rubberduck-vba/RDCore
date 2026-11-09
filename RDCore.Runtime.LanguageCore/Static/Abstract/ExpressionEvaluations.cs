using RDCore.SDK.Model.AST.Abstract;

namespace RDCore.Runtime.LanguageCore.Static.Abstract;

// an expression is evaluated once however deep it is nested: evaluating it again at each level makes a nested call's work a power of its depth.
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
