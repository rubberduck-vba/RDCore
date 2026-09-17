using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.AST.Directives;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.AST.Statements;
using RDCore.SDK.Model.Source;
using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace RDCore.SDK.Model.AST.Abstract;

/// <summary>
/// A node in the <em>abstract syntax tree</em> (AST).
/// </summary>
/// <remarks>
/// This is the base abstract node type every AST node is derived from.
/// </remarks>
/// <param name="Identity">A unique identifier for this specific syntax node.</param>
/// <param name="SourceLocation">The document location (<c>Uri</c>+<c>Range</c>) of this node.</param>
[JsonPolymorphic]
[JsonDerivedType(typeof(AnnotationTriviaNode), "AnnotationTrivia")]
[JsonDerivedType(typeof(CommentTriviaNode), "CommentTrivia")]
[JsonDerivedType(typeof(PrecompilerTriviaNode), "PrecompilerTrivia")]
[JsonDerivedType(typeof(UnbuiltExpressionTriviaNode), "UnbuiltExpressionTrivia")]
[JsonDerivedType(typeof(UnbuiltStatementTriviaNode), "UnbuiltStatementTrivia")]

[JsonDerivedType(typeof(AddressOfExpressionNode), "AddressOfExpression")]
[JsonDerivedType(typeof(ArrayBoundsNode), "ArrayBounds")]
[JsonDerivedType(typeof(AssignmentStatementNode), "AssignmentStatement")]
[JsonDerivedType(typeof(AttributeDirectiveNode), "AttributeDirective")]
[JsonDerivedType(typeof(CallStatementNode), "CallStatement")]
[JsonDerivedType(typeof(CaseComparisonRangeClauseNode), "CaseComparisonRangeClause")]
[JsonDerivedType(typeof(CaseElseClauseStatementNode), "CaseElseClauseStatement")]
[JsonDerivedType(typeof(CaseExpressionStatementNode), "CaseExpression")]
[JsonDerivedType(typeof(CaseToRangeClauseNode), "CaseToRangeClause")]
[JsonDerivedType(typeof(CaseValueRangeClauseNode), "CaseValueRangeClause")]
[JsonDerivedType(typeof(ConstantDeclarationNode), "Constant")]
[JsonDerivedType(typeof(DictionaryAccessExpressionNode), "DictionaryAccessExpression")]
[JsonDerivedType(typeof(DoLoopStatementNode), "DoLoopStatement")]
[JsonDerivedType(typeof(DoLoopUntilStatementNode), "DoLoopUntilStatement")]
[JsonDerivedType(typeof(DoLoopWhileStatementNode), "DoLoopWhileStatement")]
[JsonDerivedType(typeof(DoUntilLoopStatementNode), "DoUntilLoopStatement")]
[JsonDerivedType(typeof(DoWhileLoopStatementNode), "DoWhileLoopStatement")]
[JsonDerivedType(typeof(ElseIfBlockStatementNode), "ElseIfBlockStatement")]
[JsonDerivedType(typeof(ElseBlockStatementNode), "ElseBlockStatement")]
[JsonDerivedType(typeof(ErrorStatementNode), "ErrorStatement")]
[JsonDerivedType(typeof(ExternalMemberDeclarationNode), "DeclareStatement")]
[JsonDerivedType(typeof(ForEachStatementNode), "ForEachStatement")]
[JsonDerivedType(typeof(ForStatementNode), "ForNextStatement")]
[JsonDerivedType(typeof(GoSubStatementNode), "GoSubStatement")]
[JsonDerivedType(typeof(GoToStatementNode), "GoToStatement")]
[JsonDerivedType(typeof(IfBlockStatementNode), "IfBlockStatement")]
[JsonDerivedType(typeof(ImplementsDirectiveNode), "ImplementsDirective")]
[JsonDerivedType(typeof(IndexExpressionNode), "IndexExpression")]
[JsonDerivedType(typeof(InlineIfStatementNode), "InlineIfStatement")]
[JsonDerivedType(typeof(InstanceExpressionNode), "InstanceExpression")]
[JsonDerivedType(typeof(KeywordStatementNode), "KeywordStatement")]
[JsonDerivedType(typeof(LineLabelNode), "LineLabel")]
[JsonDerivedType(typeof(LineNumberNode), "LineNumber")]
[JsonDerivedType(typeof(MemberDeclarationNode), "Member")]
[JsonDerivedType(typeof(MissingArgumentNode), "MissingArgument")]
[JsonDerivedType(typeof(ModuleNode), "Module")]
[JsonDerivedType(typeof(ModuleOptionDirectiveNode), "OptionDirective")]
[JsonDerivedType(typeof(NamedArgumentNode), "NamedArgument")]
[JsonDerivedType(typeof(ObjectPrintExpressionNode), "ObjectPrintExpression")]
[JsonDerivedType(typeof(OnErrorGoToStatementNode), "OnErrorGoToStatement")]
[JsonDerivedType(typeof(OnErrorResumeStatementNode), "OnErrorResumeStatement")]
[JsonDerivedType(typeof(OpenStatementNode), "OpenStatement")]
[JsonDerivedType(typeof(ParameterDeclarationNode), "Parameter")]
[JsonDerivedType(typeof(PrintOutputItemNode), "PrintOutputItem")]
[JsonDerivedType(typeof(PrintSpcClauseNode), "PrintSpcClause")]
[JsonDerivedType(typeof(PrintStatementNode), "PrintStatement")]
[JsonDerivedType(typeof(PrintTabClauseNode), "PrintTabClause")]
[JsonDerivedType(typeof(RedimDeclarationNode), "Redim")]
[JsonDerivedType(typeof(ResumeNextStatementNode), "ResumeNextStatement")]
[JsonDerivedType(typeof(ResumeStatementNode), "ResumeStatement")]
[JsonDerivedType(typeof(ReturnStatementNode), "ReturnStatement")]
[JsonDerivedType(typeof(SelectCaseStatementNode), "SelectCaseStatement")]
[JsonDerivedType(typeof(TypeDefDirectiveNode), "TypeDefDirective")]
[JsonDerivedType(typeof(VariableDeclarationNode), "Variable")]
[JsonDerivedType(typeof(AsTypeExpressionNode), "AsTypeExpression")]
[JsonDerivedType(typeof(VBTypedDeclarationExpressionNode), "TypedDeclarationExpression")]
[JsonDerivedType(typeof(VBBinaryOperatorExpressionNode), "BinaryOpExpression")]
[JsonDerivedType(typeof(VBDeclarationStatementNode), "DeclarationStatement")]
[JsonDerivedType(typeof(LiteralExpressionNode), "LiteralExpression")]
[JsonDerivedType(typeof(MemberAccessExpressionNode), "MemberAccessExpression")]
[JsonDerivedType(typeof(NewExpressionNode), "NewExpression")]
[JsonDerivedType(typeof(SimpleNameExpressionNode), "SimpleNameExpression")]
[JsonDerivedType(typeof(TypeOfIsExpressionNode), "TypeOfIsExpression")]
[JsonDerivedType(typeof(VBUnaryOperatorExpressionNode), "UnaryOpExpression")]
[JsonDerivedType(typeof(WhileWendStatementNode), "WhileWendStatement")]
[JsonDerivedType(typeof(WithStatementNode), "WithStatement")]

[JsonDerivedType(typeof(ConditionalExpressionNode), "ConditionalExpression")]
[JsonDerivedType(typeof(PrecompilerConstantDeclarationNode), "PrecompilerConstant")]
[JsonDerivedType(typeof(PrecompilerInlineIfStatementNode), "PrecompilerConditional")]
[JsonDerivedType(typeof(PrecompilerIfBlockStatementNode), "PrecompilerConditionalBlock")]
[JsonDerivedType(typeof(PrecompilerElseIfBlockStatementNode), "PrecompilerConditionalElseBlock")]
[JsonDerivedType(typeof(PrecompilerElseBlockStatementNode), "PrecompilerConditionalElse")]
[JsonDerivedType(typeof(PrecompilerNameExpressionNode), "PrecompilerName")]
public abstract record class SyntaxNode(SyntaxNodeId Identity, SourceLocation SourceLocation, ImmutableArray<SyntaxNode> Children)
{
    /// <summary>
    /// A unique identifier encoding the node's position in the syntax tree.
    /// </summary>
    public SyntaxNodeId Identity { get; init; } = Identity;
    /// <summary>
    /// The location of the node in the source document.
    /// </summary>
    public SourceLocation SourceLocation { get; init; } = SourceLocation;
    /// <summary>
    /// The child syntax nodes. A <c>default</c> array is normalized to empty so it always
    /// enumerates (and serializes) safely.
    /// </summary>
    public ImmutableArray<SyntaxNode> Children { get; init; } = Children.IsDefault ? [] : Children;
}