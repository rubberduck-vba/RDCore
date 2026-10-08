using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.Source;
using System.Collections.Immutable;

namespace RDCore.SDK.Model.AST.Declarations;

/// <summary>
/// An AST node representing a variable declaration — a procedure-local <c>Dim</c>/<c>Static</c>
/// (child of a member node) or a module-level field (child of the module node). An
/// <see cref="ArrayBoundsNode"/> child, when present, carries the <c>(&#8230;)</c> array-dimension clause.
/// </summary>
/// <param name="Identity">A unique identifier for this specific syntax node.</param>
/// <param name="SourceLocation">
/// The declaration as it is written. A statement that declares several variables (<c>Dim a As Long, b As String</c>) has the keywords in front of the first of them,
/// so the first one's range begins with <c>Dim</c> (or <c>Static</c>, or the visibility), and the others' begin with their names.
/// </param>
/// <param name="Name">The declared identifier name of the member.</param>
/// <param name="AccessModifier">The access modifier, if one was supplied.</param>
/// <param name="TypeHint">The <em>type hint</em> token, if one was supplied.</param>
/// <param name="IsWithEvents"><c>true</c> if the variable is a <c>WithEvents</c> field.</param>
/// <param name="IsStatic"><c>true</c> if the declaration has an explicit <c>Static</c> token (MS-VBAL &#167;5.4.3.1).</param>
/// <param name="NameRange">Where the name is written, with its type-declaration character; <see langword="null"/> when it is not known.</param>
public record class VariableDeclarationNode(SyntaxNodeId Identity, SourceLocation SourceLocation, string Name, ImmutableArray<SyntaxNode> Children, AccessModifier AccessModifier = AccessModifier.Implicit, string? TypeHint = default, bool IsWithEvents = false, bool IsStatic = false, SourceRange? NameRange = null)
    : SyntaxNode(Identity, SourceLocation, Children);
