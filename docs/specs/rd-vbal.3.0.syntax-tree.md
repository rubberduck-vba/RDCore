# 3.0 Abstract Syntax Tree (AST)

This section describes the nodes of a RD-VBA _abstract syntax tree_, which is the output of the _parser_.


---
## 3.0.1 Token Semantics

The _token semantics_ of RD-VBA are as specified by MS-VBAL, with the exception(s) described in this sub-section.

> [!NOTE]
> **RDCore** uses the same grammar as the _legacy Rubberduck project_; this grammar was designed around the MS-VBAL specifications and is deemed compliant enough to be able to generate a _concrete syntax tree_ (CST) that can be traversed to produce an _abstract syntax tree_ that is appropriately structured and detailed for **RD-VBA**.

Token semantics are be _provided_ to the _parser_ by a `ITokenSemanticsProvider` that is implemented by the _environment host_ and may provide semantics that may themselves be provided by platform-level extensions.

The token semantics provider specifics are not yet designed, but its requirements are as follows:
- The provider accepts a base `Antlr4.Runtime.` 


### 3.0.1.1 Comment Annotations Syntax
RD-VBA comments can contain semantically meaningful metadata in the form of **annotations**, that both the _language core_ and _platform extensions_ can consume as they see fit.

Annotations can bind to:
- Modules
- Members (declarations, procedures)
- Statements in a _logical line of code_.

There are different rules of annotation bindings, depending on their intended _target_:
- **Module** annotations may only be used to bind at _module_ level and must be specified in the _declarations section_;
- **Member** annotations must appear **immediately above** the member declaration;
- It is _implementation-dependent_ how any other annotation types bind to their target.

> [!NOTE]
> This **explicit disambiguation** is necessary, because an _edge case_ allows an annotation comment on the last line of the _declarations section_ to perhaps unexpectedly bind to the _first module procedure member_ instead of the module itself if there is no vertical empty space between them.

Exactly what annotations are supported or semantically meaningful is _implementation-dependent_.


#### 3.0.1.1 Annotation List
Annotations may appear as a comma-separated _annotations list_, defined as follows:

```antlr
annotationList: SINGLEQUOTE (AT annotation)+ (COLON commentBody)?;
```

Where:
- `SINGLEQUOTE` is a _comment marker_ token;
- `AT` is a literal `@` token;
- `COLON` is a literal `:` token;
- `LPAREN` is a literal `(` token;
- `RPAREN` is a literal `)` token;
- `COMMA` is a literal `,` token;
- `WS` is a literal ` ` whitespace token;
- `LINE_CONTINUATION` consists of a whitespace followed by a literal `_` underscore token.

> 👉 Note that anything that follows a `:` colon is considered a regular comment.


#### 3.0.1.2 Annotation
The annotation itself consists of its _name_ and an optional _argument list_:

```antlr
annotation: annotationName annotationArgList? whiteSapce?;
whiteSpace : (WS | LINE_CONTINUATION)+;
```

Where:
- `WS` is a literal ` ` empty space token;
- `LINE_CONTINUATION` consists of an empty space followed by a literal `_` underscore token;
- `unrestrictedIdentifier` may be any valid _identifier name_.

Examples:

1. Marker annotation

```vb
'@ExampleAnnotation : this is a regular comment that may explain why there's an annotation here.
```

2. Annotation list

```vb
'@ExampleAnnotation1, @ExampleAnnotation2
```

3. Parameterized

Annotations may be parameterized. If the annotation is part of an _annotations list_, the arguments must be enclosed in parentheses, otherwise they are optional:

```vb
'@ExampleAnnotation "Argument1", 42
'@ExampleAnnotation("Argument1", 42)
'@ExampleAnnotation("Argument1", 42), @ExampleAnnotation2
```

#### 3.0.1.3 Annotation Arguments
> [!TIP]
> Whether annotation arguments can be another type of _expression_ than _literal expressions_ is host-dependent; _annotation comments are not intended to be executable_.

```antlr
annotationArgList:
annotationArgList : 
    whiteSpace? LPAREN whiteSpace? annotationArg whiteSpace? RPAREN
    | whiteSpace? LPAREN whiteSpace? RPAREN
    | whiteSpace? LPAREN annotationArg (whiteSpace? COMMA whiteSpace? annotationArg)+ whiteSpace? RPAREN
    | whiteSpace annotationArg
    | whiteSpace annotationArg (whiteSpace? COMMA whiteSpace? annotationArg)+
;
annotationArg : expression;
whiteSpace : (WS | LINE_CONTINUATION)+;
```

Where:
- `LPAREN` is a literal `(` token;
- `RPAREN` is a literal `)` token;
- `COMMA` is a literal `,` token;
- `WS` is a literal ` ` empty space token;
- `LINE_CONTINUATION` consists of an empty space followed by a literal `_` underscore token.


### 3.0.2 Node Types
All AST nodes inherit [BoundNode](../api/RDCore.SDK.Model.AST.Abstract.BoundNode.html), an _abstract_ node that  associates a _semantic ID_ (`Uri`) with a specific _location_ in a _workspace source file_.

The node types _directly_ derived from `BoundNode` are as follows:
- [BoundDirective](../api/RDCore.SDK.Model.AST.Abstract.BoundDirective.html)
- [BoundExpression](../api/RDCore.SDK.Model.AST.Abstract.BoundExpression.html)
- [BoundStatement](../api/RDCore.SDK.Model.AST.Abstract.BoundStatement.html)


---
## 3.0.3 Binding Contexts
**MS-VBAL§5.6.4** breaks down _expression binding contexts_ (for resolving _name lookups_) as follows:
- _Default binding context_ used by most expressions;
- _Type binding context_ used by expressions that expect to reference a _type_ or _class name_;
- _Procedure pointer binding context_ used by expressions that expect to return a _pointer to a procedure_;
- _Conditional compilation binding context_ used by expressions within _conditional compilation_ statements.

The **RDCore** interpretation is reflected in its modelization as follows:
- 🎯 _name lookups_ become an _explicit evaluation step_ involving specific AST nodes such as `VBSimpleNameExpression`;
- 🎯 Evaluation returns an [_evaluation result record_](../api/RDCore.SDK.Runtime.Shared.RuntimeSemanticsEvaluationResult.html) describing and encapsulating the result, or runtime error metadata.

Because the type system includes and leverages meta-types such as `VBTypeDescValue`, the binding context is easily inferred from the managed type of a provided value.

> [!WARNING]
> Because a `VBTypeDescValue` is a _data value_ that represents a _data type_, the implementation of both static and runtime semantics must be mindful of the possbility of accidentally pattern-matching such a _type descriptor_.  


---
## In this section
- [**RD-VBAL §3.1** Attributes and Directives](rd-vbal.3.1.attributes-directives.md)
- [**RD-VBAL §3.2** Literals](rd-vbal.3.2.literals.md)
- [**RD-VBAL §3.3** Operators](rd-vbal.3.3.0.operators.md)
<!-- TODO
- [**RD-VBAL §3.4** Statements](rd-vbal.3.4.0.statements.md)
- [**RD-VBAL §3.5** Instructions](rd-vbal.3.5.0.instructions.md) 
-->

---
> ⏮️ [**RD-VBAL §2.0** Computational Environment](rd-vbal.2.0.computational-environment.html) | ⏭️ [**RD-VBAL §4.0** Program Structure](rd-vbal.4.0.program-structure.html)
