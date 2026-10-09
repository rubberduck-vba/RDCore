using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.Source;
using System.Collections.Immutable;

namespace RDCore.SDK.Model.AST.Declarations;

/// <summary>
/// An AST node representing a parameter (child of a member node).
/// </summary>
/// <param name="Identity">A unique identifier for this specific syntax node.</param>
/// <param name="Location">The source location of this module; the <c>SourceRange</c> is invalid.</param>
/// <param name="Name">The declared identifier name of the member.</param>
/// <param name="ParameterKind">The kind (ByRef/ByVal) of parameter.</param>
/// <param name="IsOptional">An indicator that is <c>true</c> if the parameter is optional.</param>
/// <param name="IsParamArray">An indicator that is <c>true</c> if the parameter is a parameter array.</param>
/// <param name="Children">The nodes the declaration is made of, among them its <c>As</c> clause.</param>
/// <param name="IsArray">
/// An indicator that is <c>true</c> if the parameter is an array, which the parentheses after its name say:
/// <c>Items() As Long</c> (<strong>MS-VBAL §5.3.1.5</strong>). What the parameter is an array <em>of</em> is its <c>As</c> clause's.
/// </param>
/// <param name="TypeHint">The <em>type hint</em> the name is written with, if any (<c>Name$</c>), in place of an <c>As</c> clause.</param>
/// <param name="IsByRefIgnored">
/// <c>true</c> when <c>ByRef</c> is written on the value parameter of a <c>Property Let</c> or <c>Property Set</c> (<strong>MS-VBAL §5.3.1.7</strong>), where it has no effect:
/// the parameter is passed by value whatever is written, so its <see cref="ParameterKind"/> is <see cref="ParameterKind.ImplicitByVal"/>, and this is what
/// remembers that the keyword is there.
/// </param>
/// <param name="NameRange">Where the name is written, with its type-declaration character; <see langword="null"/> when it is not known.</param>
public record class ParameterDeclarationNode(SyntaxNodeId Identity, SourceLocation Location, string Name, ParameterKind ParameterKind = ParameterKind.ImplicitByRef, bool IsOptional = false, bool IsParamArray = false, ImmutableArray<SyntaxNode> Children = default, bool IsArray = false, string? TypeHint = null, bool IsByRefIgnored = false, SourceRange? NameRange = null)
    : SyntaxNode(Identity, Location, Children);
