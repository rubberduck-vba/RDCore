using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK;

namespace RDCore.Runtime.LanguageCore.Static.Abstract;

public readonly record struct StaticSemanticsEvaluationResult(
    VBType? Result, 
    VBCompileErrorInfo? ErrorInfo)
{
    public bool IsSuccess => /*IsApplicable &&*/ ErrorInfo is null;
    public bool IsError => ErrorInfo is not null;

    public static StaticSemanticsEvaluationResult Success(VBType result) => new(result, null);
    public static StaticSemanticsEvaluationResult Error(VBCompileErrorInfo error) => new(null, error);
}


public interface IStaticSemantics
{
    StaticSemanticsEvaluationResult DetermineDeclaredType(StaticEvaluationContext context, ExpressionNode expression, params VBType[] operandDeclaredTypes);
}

public abstract record class StaticSemantics() : IStaticSemantics
{
    public abstract StaticSemanticsEvaluationResult DetermineDeclaredType(StaticEvaluationContext context, ExpressionNode expression, params VBType[] operandDeclaredTypes);

    protected static VBCompileErrorInfo GetStaticTypeMismatchErrorInfo(ExpressionNode expression, VBType[] operandDeclaredTypes)
        => VBCompileErrorInfo.For(VBCompileErrorId.TypeMismatch, expression.Location, 
            Exceptions.VBCompileError_TypeMismatch_Verbose.Replace("{$INPUTS}", string.Join(", ", operandDeclaredTypes.Select(type => type.Name))));

    protected static VBCompileErrorInfo GetStaticCoercionTypeMismatchErrorInfo(ExpressionNode expression, VBType[] operandDeclaredTypes)
        => VBCompileErrorInfo.For(VBCompileErrorId.TypeMismatch, expression.Location, 
            $"{Exceptions.VBCompileError_LetCoercionTypeMismatch_Verbose} ({string.Join(" → ", operandDeclaredTypes.Select(type => type.Name))})");
}
