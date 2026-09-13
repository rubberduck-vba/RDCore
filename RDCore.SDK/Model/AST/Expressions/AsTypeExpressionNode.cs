using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.AST.Abstract;

namespace RDCore.SDK.Model.AST.Expressions;

/// <summary>
/// An <c>ExpressionNode</c> with static semantics that resolve the <c>VBType</c> of a <c>VBTypedDeclarationExpression</c>.
/// </summary>
/// <param name="Identity">A unique identifier for this specific syntax node.</param>
/// <param name="Location">The document location (<c>Uri</c>+<c>Range</c>) of the bound expression.</param>
/// <param name="TypeName">The <em>name value</em> of the <c>As &lt;Type&gt;</c> clause.</param>
/// <param name="QualifierName">The qualifying module or library name, if present.</param>
/// <param name="AsAutoObject"><c>true</c> if the expression includes a <c>New</c> token, declaring an <em>auto-object</em>.</param>
/// <param name="IsArrayDef"><c>true</c> if the expression is an array definition.</param>
public record class AsTypeExpressionNode(SyntaxNodeId Identity, SourceLocation Location, string TypeName, string? QualifierName = default, bool AsAutoObject = false, bool IsArrayDef = false)
    : ExpressionNode(Identity, Location, []);

/// <summary>
/// An <c>ExpressionNode</c> representing any <em>declaration expression</em> that evaluates to a <c>TypedSymbol</c>.
/// </summary>
/// <param name="Identity">A unique identifier for this specific syntax node.</param>
/// <param name="Location">The document location (<c>Uri</c>+<c>Range</c>) of the bound expression.</param>
/// <param name="IdentifierName">The <em>identifier</em> name of the declared symbol.</param>
/// <param name="AsTypeExpression">The <c>As &lt;Type&gt;</c> clause of the declaration, if present.</param>
public record class VBTypedDeclarationExpressionNode(SyntaxNodeId Identity, SourceLocation Location, string IdentifierName, AsTypeExpressionNode? AsTypeExpression = default)
    : ExpressionNode(Identity, Location, AsTypeExpression is null ? [] : [AsTypeExpression]);


/// <summary>
/// A <c>StatementNode</c> representing a <em>declaration list</em> containing one or more <em>declaration expressions</em>.
/// </summary>
/// <param name="Identity">A unique identifier for this specific syntax node.</param>
/// <param name="Location">The document location (<c>Uri</c>+<c>Range</c>) of the bound expression.</param>
/// <param name="Declarations">The declarations this statement evaluates.</param>
/// <param name="Modifier">The access modifier token specified, if any.</param>
/// <param name="IsWithEvents"><c>true</c> if the declaration list includes the <c>WithEvents</c> keyword.</param>
/// <param name="IsStatic"><c>true</c> if the declaration list includes the <c>Static</c> keyword.</param>
public record class VBDeclarationStatementNode(SyntaxNodeId Identity, SourceLocation Location, VBTypedDeclarationExpressionNode[] Declarations, AccessModifier? Modifier = AccessModifier.Implicit, bool IsWithEvents = false, bool IsStatic = false)
    : StatementNode(Identity, Location, [.. Declarations.Cast<ExpressionNode>()]);