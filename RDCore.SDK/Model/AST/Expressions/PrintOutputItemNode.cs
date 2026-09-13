using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.Source;
using System.Collections.Immutable;

namespace RDCore.SDK.Model.AST.Expressions;

/// <summary>
/// <strong>MS-VBAL 5.4.5.8.1</strong> one item of a <c>Print</c>/<c>Write</c> output list — a value
/// (or none, for a bare separator) followed by an optional <c>;</c>/<c>,</c> column-position
/// separator. A pure AST node; the column-spacing semantics <c>;</c>/<c>,</c> and <c>Spc</c>/<c>Tab</c>
/// imply are a runtime-layer concern, not this node's.
/// </summary>
/// <param name="Identity">A unique identifier for this specific syntax node.</param>
/// <param name="Location">The <c>Location</c> (holds the document <c>Uri</c> and a <c>Range</c>) of the bound expression.</param>
/// <param name="Value">
/// The item's value — a plain expression, or a <see cref="PrintSpcClauseNode"/>/<see cref="PrintTabClauseNode"/>
/// — or <c>null</c> for a bare separator with nothing printed before it.
/// </param>
/// <param name="Separator">The trailing <c>;</c> or <c>,</c>, or <c>null</c> for the last item with none.</param>
public sealed record class PrintOutputItemNode(SyntaxNodeId Identity, SourceLocation Location, ExpressionNode? Value, string? Separator)
    : ExpressionNode(Identity, Location, Value is null ? [] : [Value]);

/// <summary>
/// <strong>MS-VBAL 5.4.5.8.1</strong> a <c>Spc(n)</c> clause in a <c>Print</c>/<c>Write</c> output list.
/// </summary>
/// <param name="Identity">A unique identifier for this specific syntax node.</param>
/// <param name="Location">The <c>Location</c> (holds the document <c>Uri</c> and a <c>Range</c>) of the bound expression.</param>
/// <param name="Count">The number of spaces to emit.</param>
public sealed record class PrintSpcClauseNode(SyntaxNodeId Identity, SourceLocation Location, ExpressionNode Count)
    : ExpressionNode(Identity, Location, [Count]);

/// <summary>
/// <strong>MS-VBAL 5.4.5.8.1</strong> a <c>Tab</c> or <c>Tab(n)</c> clause in a <c>Print</c>/<c>Write</c>
/// output list.
/// </summary>
/// <param name="Identity">A unique identifier for this specific syntax node.</param>
/// <param name="Location">The <c>Location</c> (holds the document <c>Uri</c> and a <c>Range</c>) of the bound expression.</param>
/// <param name="Column">The target column, or <c>null</c> for bare <c>Tab</c> (next print zone).</param>
public sealed record class PrintTabClauseNode(SyntaxNodeId Identity, SourceLocation Location, ExpressionNode? Column)
    : ExpressionNode(Identity, Location, Column is null ? [] : [Column]);

/// <summary>
/// <strong>MS-VBAL 5.6</strong> <c>object-print-expression</c> (<c>owner.Print outputList</c>, e.g.
/// <c>Debug.Print "x"</c>) — part of the <c>l-expression</c> grammar family, reachable as a statement
/// through <c>CallStatementNode</c>.
/// </summary>
/// <param name="Identity">A unique identifier for this specific syntax node.</param>
/// <param name="Location">The <c>Location</c> (holds the document <c>Uri</c> and a <c>Range</c>) of the bound expression.</param>
/// <param name="Owner">The object whose <c>Print</c> member is being invoked.</param>
/// <param name="Items">The output list, in source order.</param>
public sealed record class ObjectPrintExpressionNode(SyntaxNodeId Identity, SourceLocation Location, ExpressionNode Owner, ImmutableArray<PrintOutputItemNode> Items)
    : ExpressionNode(Identity, Location, [Owner, .. Items]);
