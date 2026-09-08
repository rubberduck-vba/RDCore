using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.Source;
using System.Collections.Immutable;

namespace RDCore.SDK.Model.AST.Declarations;

/// <summary>
/// An AST node representing a declared constant (child of either a member or a module node).
/// </summary>
/// <param name="Identity">A unique identifier for this specific syntax node.</param>
/// <param name="Location">The source location of this module; the <c>SourceRange</c> is invalid.</param>
/// <param name="Name">The declared identifier name of the member.</param>
/// <param name="ConstKind">The scope kind of constant declaration.</param>
/// <param name="AccessModifier">An access modifier, if one was supplied.</param>
/// <param name="TypeHint">The <em>type-declaration character</em> (e.g. <c>$</c> in <c>Const Foo$</c>), if one was supplied.</param>
public record class ConstantDeclarationNode(SyntaxNodeId Identity, SourceLocation Location, string Name, ConstKind ConstKind, ImmutableArray<SyntaxNode> Children, AccessModifier AccessModifier = AccessModifier.Implicit, string? TypeHint = default)
    : SyntaxNode(Identity, Location, Children);

/// <summary>
/// An AST node representing a precompiler constant declaration.
/// </summary>
/// <param name="Identity">A unique identifier for this specific syntax node.</param>
/// <param name="Location">The source location of this module; the <c>SourceRange</c> is invalid.</param>
/// <param name="ConstKind">The scope kind of constant declaration.</param>
/// <param name="AccessModifier">An access modifier, if one was supplied.</param>
public record class PrecompilerConstantDeclarationNode(SyntaxNodeId Identity, SourceLocation Location, ConstKind ConstKind, ImmutableArray<SyntaxNode> Children)
    : SyntaxNode(Identity, Location, Children);

/// <summary>
/// An AST node representing a reference to a precompiler constant declaration. It is an
/// <see cref="ExpressionNode"/> so it can be an operand of a precompiler operator expression.
/// </summary>
/// <param name="Identity">A unique identifier for this specific syntax node.</param>
/// <param name="Location">The source location of this module; the <c>SourceRange</c> is invalid.</param>
/// <param name="Name">The name of the referenced precompiler constant.</param>
public record class PrecompilerNameExpressionNode(SyntaxNodeId Identity, SourceLocation Location, string Name)
    : ExpressionNode(Identity, Location, []);

