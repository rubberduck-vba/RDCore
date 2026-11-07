using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Source;
using System.Collections.Immutable;

namespace RDCore.SDK.Model.AST.Statements;

/// <summary>
/// A statement against the <c>Debug</c> object: <c>Debug.Print</c> or <c>Debug.Assert</c>.
/// </summary>
/// <remarks>
/// These parse as ordinary qualified call statements and are recognized as their own node afterwards,
/// because they are the only statements whose presence in a compiled program depends on the build:
/// lowering leaves them out entirely when the <c>DEBUG</c> conditional compilation constant is false,
/// so nothing about them survives into a release build's instructions. A qualified call that is
/// indistinguishable from every other qualified call cannot be left out that way, which is the whole
/// reason this node exists rather than a <see cref="CallStatementNode"/> that the runtime pattern-matches
/// on the spelling of its owner.
/// <para>
/// <c>Debug</c> is not part of MS-VBAL's standard library (§6.1 has no such module or class): it comes
/// from the host's own development environment, and it has exactly these two members. The object's
/// symbol is synthesized to match: see <c>StdLibSymbolProvider</c>.
/// </para>
/// </remarks>
/// <param name="Identity">A unique identifier for this specific syntax node.</param>
/// <param name="SourceLocation">The document location (<c>Uri</c>+<c>Range</c>) of the statement.</param>
/// <param name="Children">The statement's own operands.</param>
public abstract record class DebugStatementNode(SyntaxNodeId Identity, SourceLocation SourceLocation, ImmutableArray<SyntaxNode> Children)
    : StatementNode(Identity, SourceLocation, Children);

/// <summary>
/// <c>Debug.Print [outputList]</c>: writes an output list to the environment's own output.
/// </summary>
/// <remarks>
/// The output list is <strong>MS-VBAL §5.4.5.8.1</strong>'s own grammar construct, the same one a
/// <c>Print #</c> statement takes: <c>Spc</c>, <c>Tab</c> and the <c>;</c>/<c>,</c> separators are not
/// expressions and could not be an argument list. §5.4.5.8's output rules apply unchanged, against no
/// file.
/// </remarks>
/// <param name="Identity">A unique identifier for this specific syntax node.</param>
/// <param name="SourceLocation">The document location (<c>Uri</c>+<c>Range</c>) of the statement.</param>
/// <param name="Items">The output list, in source order; empty for a bare <c>Debug.Print</c>.</param>
public sealed record class DebugPrintStatementNode(SyntaxNodeId Identity, SourceLocation SourceLocation, ImmutableArray<PrintOutputItemNode> Items)
    : DebugStatementNode(Identity, SourceLocation, [.. Items]);

/// <summary>
/// <c>Debug.Assert booleanExpression</c>: suspends execution when its expression is <c>False</c>.
/// </summary>
/// <param name="Identity">A unique identifier for this specific syntax node.</param>
/// <param name="SourceLocation">The document location (<c>Uri</c>+<c>Range</c>) of the statement.</param>
/// <param name="Condition">The expression that must be <c>True</c> for execution to continue.</param>
public sealed record class DebugAssertStatementNode(SyntaxNodeId Identity, SourceLocation SourceLocation, ExpressionNode Condition)
    : DebugStatementNode(Identity, SourceLocation, [Condition]);
