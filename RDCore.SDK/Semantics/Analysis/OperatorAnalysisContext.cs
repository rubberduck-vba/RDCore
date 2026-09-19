using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Runtime.Shared;

namespace RDCore.SDK.Semantics.Analysis;

/// <summary>
/// Encapsulates the context of an <c>Analyze</c> operation as the nullable results of successive operations.
/// </summary>
/// <param name="NodeId">The <c>Identity</c> of the associated expression node.</param>
/// <param name="EffectiveTypeResult">The outcome of <em>determining the effective type</em> of the operation, the <strong>first step</strong> of the evaluation process.</param>
/// <param name="ValidationResults">The outcome of <em>validating the operands</em> of the operator expression, which involves let-coercion semantics and possible implicit type conversions. This is the <strong>second step</strong> of the evaluation process.</param>
/// <param name="EvaluationResult">The outcome of <em>evaluating the result</em> in the current execition context, which is the <strong>third and last step</strong> of the evaluation process.</param>
/// <param name="SemanticFlags">The semantic flags associated with the operation.</param>
/// <param name="CompareMode">The mode the operation compares <c>String</c> values in where it is analyzed (<strong>MS-VBAL 5.2.1.1</strong>): never <c>Database</c>.</param>
/// <typeparam name="TFlags">The specific type of semantic flags associated with the operation.</typeparam>
public readonly record struct OperatorAnalysisContext<TFlags>(
    SyntaxNodeId NodeId,
    DetermineOperatorEffectiveTypeResult EffectiveTypeResult,
    LetCoercionAnalysisContext ValidationResults,
    RuntimeSemanticsEvaluationResult EvaluationResult,
    TFlags SemanticFlags,
    OptionCompare CompareMode = OptionCompare.Binary)
where TFlags : struct, Enum { }
