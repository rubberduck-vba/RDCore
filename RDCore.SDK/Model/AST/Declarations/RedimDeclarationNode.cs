using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Source;
using System.Collections.Immutable;

namespace RDCore.SDK.Model.AST.Declarations;

/// <summary>
/// A single target of a body-level <c>ReDim</c> statement (MS-VBAL &#167;5.4.3.3) — one node per
/// comma-separated declaration, child of the enclosing member node.
/// </summary>
/// <remarks>
/// A <c>ReDim</c> always re-dimensions a <em>dynamic array</em>. It re-dimensions an existing array
/// (a local, a parameter, or a module field) when the target name already resolves; an unqualified
/// name that resolves to nothing <em>implicitly declares</em> a local, which stays legal under
/// <c>Option Explicit</c> (a later analysis pass flags it, and turns it into an error under
/// <c>Option Strict</c>).
/// <para>
/// The <see cref="ArrayBoundsNode"/> child carries the new dimensions; unlike a <c>Dim</c> array
/// clause these bounds are ordinary run-time expressions, not constant expressions. An
/// <see cref="AsTypeExpressionNode"/> child, when present, carries the optional <c>As</c> clause.
/// </para>
/// </remarks>
/// <param name="Identity">A unique identifier for this specific syntax node.</param>
/// <param name="Location">The source location of the <c>ReDim</c> target.</param>
/// <param name="Name">The target identifier name.</param>
/// <param name="QualifierName">The qualifier when the target is a member access (<c>obj.Buffer</c>, <c>Me.Buffer</c>, <c>.Buffer</c>); <c>null</c> for a simple name.</param>
/// <param name="Children">The <see cref="ArrayBoundsNode"/>, and the <see cref="AsTypeExpressionNode"/> when an <c>As</c> clause is present.</param>
/// <param name="IsPreserve"><c>true</c> when the <c>ReDim</c> statement has the <c>Preserve</c> keyword.</param>
/// <param name="TypeHint">The <em>type-declaration character</em> on the target name (e.g. <c>%</c> in <c>ReDim n%(2)</c>), if one was supplied.</param>
public record class RedimDeclarationNode(SyntaxNodeId Identity, SourceLocation Location, string Name, string? QualifierName, ImmutableArray<SyntaxNode> Children, bool IsPreserve = false, string? TypeHint = default)
    : SyntaxNode(Identity, Location, Children);
