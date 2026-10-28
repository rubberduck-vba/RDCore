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
/// <param name="IsStatic">
/// <c>true</c> when the procedure is declared with the <c>Static</c> keyword (<strong>MS-VBAL §5.3.1.2</strong>): every local variable of it then has module
/// extent, as if each were declared <c>Static</c>. Only a <c>Sub</c>, a <c>Function</c> or a property can be.
/// </param>
/// <param name="TypeHint">
/// The <em>type hint</em> the name is written with, if any (<c>Function Name$()</c>): the declared type of what a function or a property getter returns, written
/// in place of an <c>As</c> clause. Only a member that returns a value can have one.
/// </param>
/// <param name="NameRange">Where the name is written, with its type-declaration character; <see langword="null"/> when it is not known.</param>
public record class MemberDeclarationNode(SyntaxNodeId Identity, SourceLocation SourceLocation, ImmutableArray<SyntaxNode> Children, string Name, MemberKind MemberKind, AccessModifier AccessModifier = AccessModifier.Implicit, bool IsStatic = false, string? TypeHint = null, SourceRange? NameRange = null)
    : SyntaxNode(Identity, SourceLocation, Children)
{
    /// <summary>
    /// The other headers of a procedure whose header is written once in each branch of a <c>#If</c> block, around one body (<strong>MS-VBAL §3.4</strong>):
    /// <c>#If VBA7 Then</c> / <c>Private Function F(p As LongPtr) As LongPtr</c> / <c>#Else</c> / <c>Private Function F(p As Long) As Long</c> /
    /// <c>#End If</c>, then the body and one <c>End Function</c>. Empty for a procedure that has one header.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The parse is of every branch, whatever the constants evaluate to: this node is the procedure as its header in one branch declares it - the one whose
    /// kind its closing statement is of - and each alternative is a header of another branch, with its own access, kind, name, parameters and type, at its
    /// own location. Which one is compiled is decided where the constants are known.
    /// </para>
    /// <para>
    /// An alternative has no body: the statements written in its branch after its header are in <see cref="SyntaxNode.Children"/>, in source order, with the
    /// rest of the body, and are compiled when its branch is (<see cref="WithEachHeader"/>).
    /// </para>
    /// </remarks>
    public ImmutableArray<MemberDeclarationNode> HeaderAlternatives { get; init; } = [];

    /// <summary>
    /// What the header declares, of <see cref="SyntaxNode.Children"/>: the parameters and the <c>As</c> clause, which come before the body.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public IEnumerable<SyntaxNode> HeaderChildren => Children.TakeWhile(IsOfTheHeader);

    /// <summary>
    /// The procedure as each of its headers declares it: this node, then each <see cref="HeaderAlternatives">alternative</see> with this node's body.
    /// </summary>
    public IEnumerable<MemberDeclarationNode> WithEachHeader()
    {
        yield return this;
        if (HeaderAlternatives.IsDefaultOrEmpty)
        {
            yield break;
        }

        var body = Children.SkipWhile(IsOfTheHeader).ToImmutableArray();
        foreach (var alternative in HeaderAlternatives)
        {
            yield return alternative with { Children = [.. alternative.HeaderChildren, .. body], HeaderAlternatives = [] };
        }
    }

    private static bool IsOfTheHeader(SyntaxNode child) => child is ParameterDeclarationNode or Expressions.AsTypeExpressionNode;
}
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
/// <param name="TypeHint">The <em>type hint</em> the name is written with, if any (<c>Declare Function Name$ Lib "lib"</c>).</param>
/// <param name="NameRange">Where the name is written, with its type-declaration character; <see langword="null"/> when it is not known.</param>
public record class ExternalMemberDeclarationNode(SyntaxNodeId Identity, SourceLocation SourceLocation, ImmutableArray<SyntaxNode> Children, string Name = "", string Library = "", bool IsPtrSafe = false, MemberKind MemberKind = MemberKind.ExternalProcedure, string? Alias = default, AccessModifier AccessModifier = AccessModifier.Implicit, string? TypeHint = null, SourceRange? NameRange = null)
    : MemberDeclarationNode(Identity, SourceLocation, Children, Name, MemberKind, AccessModifier, TypeHint: TypeHint, NameRange: NameRange);
