using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.Source;
using System.Collections.Immutable;

namespace RDCore.SDK.Model.AST.Declarations;

/// <summary>
/// An AST node representing a member of a module.
/// </summary>
/// <param name="Identity">A unique identifier for this specific syntax node.</param>
/// <param name="SourceLocation">The source location of this module; the <c>SourceRange</c> is invalid.</param>
/// <param name="Children">An immutable array containing all AST nodes of this module.</param>
/// <param name="Name">The declared identifier name of the member.</param>
/// <param name="MemberKind">Specifies the kind of member.</param>
/// <param name="AccessModifier">An access modifier, if one was supplied.</param>
public record class MemberDeclarationNode(SyntaxNodeId Identity, SourceLocation SourceLocation, ImmutableArray<SyntaxNode> Children, string Name, MemberKind MemberKind, AccessModifier AccessModifier = AccessModifier.Implicit)
    : SyntaxNode(Identity, SourceLocation, Children);
/// <summary>
/// 
/// </summary>
/// <param name="Identity">A unique identifier for this specific syntax node.</param>
/// <param name="SourceLocation">The source location of this module; the <c>SourceRange</c> is invalid.</param>
/// <param name="Children">An immutable array containing all AST nodes of this module.</param>
/// <param name="Name">The declared identifier name of the member.</param>
/// <param name="Library">The external library.</param>
/// <param name="IsPtrSafe">An indicator that is <c>true</c> when the declaration includes a <c>PtrSafe</c> token.</param>
/// <param name="MemberKind">Specifies the kind of member.</param>
/// <param name="Alias">The declared alias of the external member, if provided.</param>
/// <param name="AccessModifier">An access modifier, if one was supplied.</param>
public record class ExternalMemberDeclarationNode(SyntaxNodeId Identity, SourceLocation SourceLocation, ImmutableArray<SyntaxNode> Children, string Name = "", string Library = "", bool IsPtrSafe = false, MemberKind MemberKind = MemberKind.ExternalProcedure, string? Alias = default, AccessModifier AccessModifier = AccessModifier.Implicit)
    : MemberDeclarationNode(Identity, SourceLocation, Children, Name, MemberKind, AccessModifier);
