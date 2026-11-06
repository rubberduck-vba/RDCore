# 3.0.1 Token Semantics

The _token semantics_ of **RD-VBA** are as specified by
[**MS-VBAL §3.3** Lexical Tokens](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/848bd0b2-e453-4f50-a42a-452eb0923c6d),
with the exceptions described in this section.

**RDCore** uses the same grammar as the _legacy Rubberduck project_. That grammar was designed around the
MS-VBAL specifications, and is deemed compliant enough with MS-VBAL to generate a _concrete syntax tree_ (CST).
The CST can be traversed to produce an _abstract syntax tree_ (AST) that is appropriately structured and detailed
for **RD-VBA**.

## Token Semantics Provider

Token semantics are provided to the _parser_ by an `ITokenSemanticsProvider`. The `ITokenSemanticsProvider` is
implemented by the _environment host_. The semantics it provides may themselves be provided by platform-level
extensions ([**RD-VBAL §1.1.1** Platform Extensions](rd-vbal.1.1.1.platform-extensions.md)).

> [!NOTE]
> **Not implemented.** The specifics of the token semantics provider are not designed. Only its requirements are
> stated here.

The requirements of the token semantics provider are as follows:

- The provider accepts a base type from `Antlr4.Runtime`. The specific base type is not specified.

## 3.0.1.1 Comment Annotations Syntax

**RD-VBA** comments can contain semantically meaningful metadata in the form of **annotations**. Both the
_language core_ and _platform extensions_ can consume annotations as they see fit. The comment annotations syntax
from the _legacy Rubberduck VBIDE add-in_ is a language core extension
([**RD-VBAL §1.1.2** Language Core Extensions](rd-vbal.1.1.2.language-core-extensions.md)).

Annotations can bind to:

- modules;
- members (declarations, procedures);
- statements in a _logical line of code_.

The rules of annotation binding differ depending on the annotation's intended _target_:

|Annotation|Binding rule|
|---|---|
|Module annotation|May only be used to bind at _module_ level. Must be specified in the _declarations section_.|
|Member annotation|Must appear **immediately above** the member declaration.|
|Any other annotation type|How it binds to its target is _implementation-dependent_.|

> [!NOTE]
> An annotation comment on the last line of the _declarations section_ can bind to the first procedure member of
> the module, instead of the module itself, when there is no vertical empty space (blank line) between them.
> This edge case is why the module and member binding rules are explicitly disambiguated.

Which annotations are supported or semantically meaningful is _implementation-dependent_.
Surfacing `Attribute` statements does not necessarily make `@Description` annotations obsolete
([**RD-VBAL §3.1.1.7** VB_Description](rd-vbal.3.1.1.attributes.md#3117-vb_description)).

### 3.0.1.1.1 Annotation List

Annotations may appear as a comma-separated _annotations list_, defined as follows:

```antlr
annotationList : SINGLEQUOTE (AT annotation)+ (COLON commentBody)?;
```

An annotations list is a comment marker, one or more `@`-prefixed annotations, then an optional `:` followed by a
comment body. The tokens of the annotation grammar are:

|Token|Definition|
|---|---|
|`SINGLEQUOTE`|A _comment marker_ token.|
|`AT`|A literal `@` token.|
|`COLON`|A literal `:` token.|
|`LPAREN`|A literal `(` token.|
|`RPAREN`|A literal `)` token.|
|`COMMA`|A literal `,` token.|
|`WS`|A literal ` ` (space) whitespace token.|
|`LINE_CONTINUATION`|A whitespace (space) followed by a literal `_` underscore token.|

👉 In an annotation comment, anything that follows a `:` colon is a regular comment.

### 3.0.1.1.2 Annotation

An annotation consists of its _name_ and an optional _argument list_:

```antlr
annotation : annotationName annotationArgList? whiteSpace?;
annotationName : unrestrictedIdentifier;
whiteSpace : (WS | LINE_CONTINUATION)+;
```

An annotation is an annotation name, an optional argument list and optional trailing whitespace. Whitespace is one
or more `WS` or `LINE_CONTINUATION` tokens. `unrestrictedIdentifier` may be any valid _identifier name_.

Example 1, a marker annotation. The text after the colon is a regular comment:

```vb
'@ExampleAnnotation : this is a regular comment that may explain why there's an annotation here.
```

Example 2, an annotations list:

```vb
'@ExampleAnnotation1, @ExampleAnnotation2
```

Annotations may be parameterized. Whether the arguments of a parameterized annotation are enclosed in parentheses
depends on its context:

|Context|Parentheses around the arguments|
|---|---|
|The annotation is part of an _annotations list_|Required.|
|The annotation is not part of an _annotations list_|Optional.|

Example 3, parameterized annotations:

```vb
'@ExampleAnnotation "Argument1", 42
'@ExampleAnnotation("Argument1", 42)
'@ExampleAnnotation("Argument1", 42), @ExampleAnnotation2
```

The first line writes the arguments without parentheses; the second encloses them in parentheses. The third line is part of an annotations
list, where the parentheses are required.

### 3.0.1.1.3 Annotation Arguments

Whether annotation arguments can be _expressions_ other than _literal expressions_ is host-dependent.
Annotation comments are not intended to be executable.

```antlr
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

`annotationArgList` has five alternatives, in this order:

|Alternative|Form|
|---|---|
|1|One argument in parentheses.|
|2|Empty parentheses, `()`.|
|3|Two or more comma-separated arguments in parentheses.|
|4|One argument after whitespace, without parentheses.|
|5|Two or more comma-separated arguments after whitespace, without parentheses.|

Whitespace around the parentheses and the commas is optional. The `LPAREN`, `RPAREN`, `COMMA`, `WS` and
`LINE_CONTINUATION` tokens are defined in [§3.0.1.1.1](#30111-annotation-list).

---
> ⏮️ [**RD-VBAL §3.0** Abstract Syntax Tree](rd-vbal.3.0.syntax-tree.md) | ⏭️ [**RD-VBAL §3.0.2** Node Types](rd-vbal.3.0.2.node-types.md)
