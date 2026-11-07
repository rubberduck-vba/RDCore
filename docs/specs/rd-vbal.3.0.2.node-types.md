# 3.0.2 Node Types

All AST nodes inherit [SyntaxNode](../api/RDCore.SDK.Model.AST.Abstract.SyntaxNode.html). `SyntaxNode` is an
_abstract_ node type. It associates a _semantic ID_
([SyntaxNodeId](../api/RDCore.SDK.Model.AST.Abstract.SyntaxNodeId.html)) with a specific _location_ in a
_workspace source file_.

The node types derived directly from `SyntaxNode` are as follows:

|Node type|Described in|
|---|---|
|[DirectiveNode](../api/RDCore.SDK.Model.AST.Abstract.DirectiveNode.html)|[**RD-VBAL §3.1** Attributes and Directives](rd-vbal.3.1.attributes-directives.md)|
|[ExpressionNode](../api/RDCore.SDK.Model.AST.Abstract.ExpressionNode.html)|[**RD-VBAL §3.2.0** Literal Expressions](rd-vbal.3.2.0.literals.md), [**RD-VBAL §3.3.0** Operator Expressions](rd-vbal.3.3.0.operators.md), [**RD-VBAL §5.6** Expressions](rd-vbal.5.6.expressions.md)|
|[StatementNode](../api/RDCore.SDK.Model.AST.Abstract.StatementNode.html)|[**RD-VBAL §3.4.0** Statements](rd-vbal.3.4.0.statements.md)|

Every statement node derives from `StatementNode`, which implements
[IExecutableNode](../api/RDCore.SDK.Model.AST.Abstract.IExecutableNode.html). A statement node's `Inputs` are the
expressions evaluated immediately before the statement executes.

## Expression Nodes

The following expression node types have their own semantics pages:

|Expression|Node type|Described in|
|---|---|---|
|A bare name|[SimpleNameExpressionNode](../api/RDCore.SDK.Model.AST.Expressions.SimpleNameExpressionNode.html)|[**RD-VBAL §5.6.10** Simple Name Expressions](rd-vbal.5.6.10.simple-name-expressions.md)|
|Member access|[MemberAccessExpressionNode](../api/RDCore.SDK.Model.AST.Expressions.MemberAccessExpressionNode.html)|[**RD-VBAL §5.6.12** Member Access Expressions](rd-vbal.5.6.12.member-access-expressions.md)|
|Index|[IndexExpressionNode](../api/RDCore.SDK.Model.AST.Expressions.IndexExpressionNode.html)|[**RD-VBAL §5.6.13** Index Expressions](rd-vbal.5.6.13.index-expressions.md)|
|Dictionary access|[DictionaryAccessExpressionNode](../api/RDCore.SDK.Model.AST.Expressions.DictionaryAccessExpressionNode.html)|[**RD-VBAL §5.6.14** Dictionary Access Expressions](rd-vbal.5.6.14.dictionary-access-expressions.md)|
|`New <class>`|[NewExpressionNode](../api/RDCore.SDK.Model.AST.Expressions.NewExpressionNode.html)|[**RD-VBAL §5.6.8** New Expressions](rd-vbal.5.6.8.new-expressions.md)|
|`TypeOf <expr> Is <type>`|[TypeOfIsExpressionNode](../api/RDCore.SDK.Model.AST.Expressions.TypeOfIsExpressionNode.html)|[**RD-VBAL §5.6.7** TypeOf...Is Expressions](rd-vbal.5.6.7.typeof-is-expressions.md)|

The target of an assignment statement draws from the first four: member access, index, dictionary access, or a bare
name ([**RD-VBAL §3.4.2** Simple Statements](rd-vbal.3.4.2.simple-statements.md)).

## Unbuilt Trivia Nodes

A grammar alternative that the parser recognizes but has no dedicated node type for is never silently dropped from
the AST. It is never folded into an unrelated node either. It is preserved as an _Unbuilt trivia node_:

|Position|Node type|
|---|---|
|Expression|[UnbuiltExpressionTriviaNode](../api/RDCore.SDK.Model.AST.Abstract.UnbuiltExpressionTriviaNode.html)|
|Statement|[UnbuiltStatementTriviaNode](../api/RDCore.SDK.Model.AST.Abstract.UnbuiltStatementTriviaNode.html), the statement-position counterpart of `UnbuiltExpressionTriviaNode`|

An Unbuilt trivia node carries:

- the exact original source text of the construct;
- whatever sub-expression the parser's own walk already built underneath it.

The Unbuilt trivia fallback keeps the AST a faithful, lossless representation of the source, even where a proper
semantic node does not exist. The fallback applies to any construct without a dedicated node type.

It also applies to code with a syntax error:

- A binary operator with no right operand at the end of its statement (`a = 1 -`, `Foo 1 Mod`) is a syntax error, reported as a zero-width range at the
  operator: before a `-`, `*`, `=`, `Like`, `Is`, `Mod`, `And`, `Or`, `Xor`, `Eqv` or `Imp`, after a `+`, `/`, `\`, `^`, `&`, `<>`, `<`,
  `>`, `<=` or `>=`. The operation is kept as an Unbuilt expression of its text (`1 -`), with its left operand under it, so the
  statement keeps its operator. The statement ends at its own line end or colon, and the next statement is read on its own.
- A statement that no grammar rule reads (`a = 1-`, with no space before the operator, or `If x = 1 - Then`) is kept as
  an Unbuilt statement of its text, at its place in the procedure.
- So is a statement that begins with a word no statement begins with (`Mod = 1`). An operator keyword is a reserved identifier and names no
  variable (**MS-VBAL §3.3.5.2**). The syntax error is at that word, and the next statement is read on its own.
- A declaration with a word that is no name where its name is written (`Dim For As Long`, `Dim 1 As Long`, `Dim Mod As Long`) is a syntax error
  at that word. The statement is kept at its place, and the next statement is read on its own.
- A statement has one syntax error, the first one the parser finds in it. Two statements on one line (`a = 1 -: b = = 2`) have one error each.
