using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Semantics.Flags;

namespace RDCore.SDK.Semantics.Facts;

/// <summary>
/// What the runtime semantics of a statement (<strong>MS-VBAL 5.4</strong>) establish about it, as the language core states it.
/// </summary>
/// <remarks>
/// 👉 Only the statements whose semantics establish something of their own state a fact: what most statements do is to convert and to operate, and those
/// are facts of their own (<see cref="ConversionFact"/>, <see cref="OperatorFact"/>). One kind of statement is one derived record, so that a new kind does not
/// grow the facts of every statement by a property. The run-time error a statement raises is a fact of the procedure's (<see cref="RuntimeFacts.Errors"/>).
/// </remarks>
/// <param name="Node">The <c>Identity</c> of the statement.</param>
/// <param name="Location">Where in the document the statement is.</param>
/// <param name="Statement">The keyword of the statement: one of the <c>Tokens</c>.</param>
public abstract record class StatementFact(
    SyntaxNodeId Node,
    SourceLocation Location,
    string Statement);

/// <summary>An <c>LSet</c> or an <c>RSet</c> statement (<strong>MS-VBAL 5.4.3.6-7</strong>).</summary>
/// <param name="Flags">What is known of the statement: which form it takes, and what a record it copies holds.</param>
/// <inheritdoc cref="StatementFact"/>
public sealed record class FixedAssignmentStatementFact(
    SyntaxNodeId Node,
    SourceLocation Location,
    string Statement,
    FixedAssignmentSemanticFlags Flags)
    : StatementFact(Node, Location, Statement);

/// <summary>
/// A range clause of a <c>Case</c> (<strong>MS-VBAL 5.4.2.10</strong>), stated when its value is compared with the selector in the selector's declared type,
/// where the comparison of the two as an expression has another effective type (<see cref="CaseClauseSemanticFlags.ComparedInSelectorType"/>).
/// </summary>
/// <param name="Flags">What is known of the clause: which form it takes, and how its value is compared with the selector.</param>
/// <param name="IsValueKnown">Whether the selector and the value of the clause are known, as opposed to values their types merely allow.</param>
/// <inheritdoc cref="StatementFact"/>
public sealed record class CaseClauseStatementFact(
    SyntaxNodeId Node,
    SourceLocation Location,
    string Statement,
    CaseClauseSemanticFlags Flags,
    bool IsValueKnown)
    : StatementFact(Node, Location, Statement);

/// <summary>A <c>With</c> statement (<strong>MS-VBAL 5.4.2.21</strong>).</summary>
/// <param name="Flags">What is known of the statement: whether its target is an object, which is Set-assigned to the block variable, or a record, which is Let-assigned.</param>
/// <param name="IsValueKnown">Whether the value of the target is known, as opposed to a value its type merely allows.</param>
/// <inheritdoc cref="StatementFact"/>
public sealed record class WithStatementFact(
    SyntaxNodeId Node,
    SourceLocation Location,
    string Statement,
    WithStatementSemanticFlags Flags,
    bool IsValueKnown)
    : StatementFact(Node, Location, Statement);
