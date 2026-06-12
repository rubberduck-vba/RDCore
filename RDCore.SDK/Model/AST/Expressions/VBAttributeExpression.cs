using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.SDK.Model.AST.Abstract;

namespace RDCore.SDK.Model.AST.Expressions;

/// <summary>
/// A <c>VB_Attribute</c> expression node.
/// </summary>
/// <remarks>
/// <strong>MS-VBAL 4.2 Modules</strong> defines the static semantics of this expression.
/// </remarks>
/// <param name="SemanticId">A semantic <c>Uri</c> uniquely identifying this specific node.</param>
/// <param name="Location">The document location (<c>Uri</c>+<c>Range</c>) of the bound expression.</param>
public record class VBAttributeExpression(Uri SemanticId, Location Location, BoundExpression NameExpression, BoundExpression ValueExpression) 
    : BoundExpression(SemanticId, Location) { }

/// <summary>
/// A <c>BoundExpression</c> with static semantics that resolve the <c>VBType</c> of a <c>VBTypedDeclarationExpression</c>.
/// </summary>
/// <param name="SemanticId">A semantic <c>Uri</c> uniquely identifying this specific node.</param>
/// <param name="Location">The document location (<c>Uri</c>+<c>Range</c>) of the bound expression.</param>
/// <param name="TypeName">The <em>name value</em> of the <c>As &lt;Type&gt;</c> clause.</param>
/// <param name="QualifierName">The qualifying module or library name, if present.</param>
/// <param name="AsAutoObject"><c>true</c> if the expression includes a <c>New</c> token, declaring an <em>auto-object</em>.</param>
public record class VBAsTypeExpression(Uri SemanticId, Location Location, string TypeName, string? QualifierName = default, bool AsAutoObject = false)
    : BoundExpression(SemanticId, Location) { }

/// <summary>
/// A <c>BoundExpression</c> representing any <em>declaration expression</em> that evaluates to a <c>TypedSymbol</c>.
/// </summary>
/// <param name="SemanticId">A semantic <c>Uri</c> uniquely identifying this specific node.</param>
/// <param name="Location">The document location (<c>Uri</c>+<c>Range</c>) of the bound expression.</param>
/// <param name="IdentifierName">The <em>identifier</em> name of the declared symbol.</param>
/// <param name="AsTypeExpression">The <c>As &lt;Type&gt;</c> clause of the declaration, if present.</param>
public record class VBTypedDeclarationExpression(Uri SemanticId, Location Location, string IdentifierName, VBAsTypeExpression? AsTypeExpression = default) 
    : BoundExpression(SemanticId, Location) { }


/// <summary>
/// A <c>BoundStatement</c> representing a <em>declaration list</em> containing one or more <em>declaration expressions</em>.
/// </summary>
/// <param name="SemanticId">A semantic <c>Uri</c> uniquely identifying this specific node.</param>
/// <param name="Location">The document location (<c>Uri</c>+<c>Range</c>) of the bound expression.</param>
/// <param name="Declarations">The declarations this statement evaluates.</param>
/// <param name="Modifier">The access modifier token specified, if any.</param>
/// <param name="IsWithEvents"><c>true</c> if the declaration list includes the <c>WithEvents</c> keyword.</param>
/// <param name="IsStatic"><c>true</c> if the declaration list includes the <c>Static</c> keyword.</param>
public record class VBDeclarationStatement(Uri SemanticId, Location Location, VBTypedDeclarationExpression[] Declarations, AccessModifier? Modifier = AccessModifier.Implicit, bool IsWithEvents = false, bool IsStatic = false) 
    : BoundStatement(SemanticId, Location) { }
