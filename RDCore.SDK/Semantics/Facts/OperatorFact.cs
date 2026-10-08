using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.Errors.Abstract;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Runtime;
using RDCore.SDK.Semantics.Flags;
using RDCore.SDK.Semantics.Runtime.Operators;

namespace RDCore.SDK.Semantics.Facts;

/// <summary>
/// An operation of an <em>operator</em> (<strong>MS-VBAL 5.6.9</strong>), as the language core states it.
/// </summary>
/// <remarks>
/// 👉 One family of operators is one derived record, so that a new family does not grow the facts of every operation by a property.
/// The conversions of the operands of the operation are facts of their own (<see cref="ConversionFact"/>), at
/// <see cref="ConversionSite.OperatorOperand"/>. A fact is only stated when it is true of the code: see <see cref="ConversionFact"/>.
/// </remarks>
/// <param name="Node">The <c>Identity</c> of the syntax node the operation is evaluated for.</param>
/// <param name="Location">Where in the document the operation is.</param>
/// <param name="Operator">The operator: one of the <c>Tokens</c> operators.</param>
/// <param name="EffectiveType">The <em>effective type</em> of the operation, if it has one.</param>
/// <param name="IsValueKnown">Whether every operand of the operation is known, as opposed to a value its type merely allows.</param>
/// <param name="Error">The run-time error the operation raises. Only stated when the operands are known, or when their types alone guarantee it.</param>
public abstract record class OperatorFact(
    SyntaxNodeId Node,
    SourceLocation Location,
    string Operator,
    VBType? EffectiveType,
    bool IsValueKnown,
    VBErrorInfo? Error);

/// <summary>An operation of an arithmetic operator (<strong>MS-VBAL 5.6.9.3</strong>).</summary>
/// <param name="Flags">What is known of the operation.</param>
/// <inheritdoc cref="OperatorFact"/>
public sealed record class ArithmeticOperatorFact(
    SyntaxNodeId Node,
    SourceLocation Location,
    string Operator,
    VBType? EffectiveType,
    bool IsValueKnown,
    VBErrorInfo? Error,
    ArithmeticOperatorSemanticFlags Flags)
    : OperatorFact(Node, Location, Operator, EffectiveType, IsValueKnown, Error);

/// <summary>An operation of a relational operator (<strong>MS-VBAL 5.6.9.5</strong>).</summary>
/// <param name="Flags">What is known of the operation.</param>
/// <param name="Comparison">How the operation compares <c>String</c> values where it is evaluated.</param>
/// <inheritdoc cref="OperatorFact"/>
public sealed record class ComparisonOperatorFact(
    SyntaxNodeId Node,
    SourceLocation Location,
    string Operator,
    VBType? EffectiveType,
    bool IsValueKnown,
    VBErrorInfo? Error,
    ComparisonOperatorSemanticFlags Flags,
    StringComparisonRules Comparison)
    : OperatorFact(Node, Location, Operator, EffectiveType, IsValueKnown, Error);

/// <summary>An operation of a logical operator (<strong>MS-VBAL 5.6.9.8</strong>).</summary>
/// <param name="Flags">What is known of the operation.</param>
/// <inheritdoc cref="OperatorFact"/>
public sealed record class LogicalOperatorFact(
    SyntaxNodeId Node,
    SourceLocation Location,
    string Operator,
    VBType? EffectiveType,
    bool IsValueKnown,
    VBErrorInfo? Error,
    LogicalOperatorSemanticFlags Flags)
    : OperatorFact(Node, Location, Operator, EffectiveType, IsValueKnown, Error);

/// <summary>An operation of the concatenation operator (<strong>MS-VBAL 5.6.9.4</strong>).</summary>
/// <param name="Flags">What is known of the operation.</param>
/// <inheritdoc cref="OperatorFact"/>
public sealed record class ConcatOperatorFact(
    SyntaxNodeId Node,
    SourceLocation Location,
    string Operator,
    VBType? EffectiveType,
    bool IsValueKnown,
    VBErrorInfo? Error,
    ConcatOperationSemanticFlags Flags)
    : OperatorFact(Node, Location, Operator, EffectiveType, IsValueKnown, Error);
