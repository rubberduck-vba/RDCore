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
public record class ParameterDeclarationNode(SyntaxNodeId Identity, SourceLocation Location, string Name, ParameterKind ParameterKind = ParameterKind.ImplicitByRef, bool IsOptional = false, bool IsParamArray = false, ImmutableArray<SyntaxNode> Children = default, bool IsArray = false)
    : SyntaxNode(Identity, Location, Children);
