# 3.0 Abstract Syntax Tree (AST)

This section describes the nodes of a RD-VBA _abstract syntax tree_, which is the output of the _parser_.

## 3.0.1 Token Semantics

The _token semantics_ of RD-VBA are as specified by MS-VBAL, with the exception(s) described in this sub-section.

> 👉 **RDCore** uses the same grammar as the _legacy Rubberduck project_; this grammar was designed around the MS-VBAL specifications and is deemed compliant enough to be able to generate a _concrete syntax tree_ (CST) that can be traversed to produce an _abstract syntax tree_ that is appropriately structured and detailed for **RD-VBA**.


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

> ℹ️ This **explicit disambiguation** is necessary, because an _edge case_ allows an annotation comment on the last line of the _declarations section_ to perhaps unexpectedly bind to the _first module procedure member_ instead of the module itself if there is no vertical empty space between them.

Exactly what annotations are supported or semantically meaningful is _implementation-dependent_.

#### 3.0.1.1 Annotation List

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

> ℹ️ Whether annotation arguments can be another type of _expression_ than _literal expressions_ is host-dependent; _annotation comments are not intended to be executable_.

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

All AST nodes inherit [BoundNode](../_site/api/RDCore.SDK.Model.AST.Abstract.BoundNode.html), an _abstract_ node that  associates a _semantic ID_ (`Uri`) with a specific _location_ in a _workspace source file_.

The node types _directly_ derived from `BoundNode` are as follows:
- [BoundDirective](../_site/api/RDCore.SDK.Model.AST.Abstract.BoundDirective.html)
- [BoundExpression](../_site/api/RDCore.SDK.Model.AST.Abstract.BoundExpression.html)
- [BoundStatement](../_site/api/RDCore.SDK.Model.AST.Abstract.BoundStatement.html)

#### Directives

_Directives_ are **non-executable statements** that influence the semantics of the module they're located in. 

These include `Option` statements:

|Directive|Description
|---|---|
|`Option Explicit`|Implicit declarations become compile-time errors|
|`Option Base`|Determines the base (0 or 1) of implicitly-sized arrays|
|`Option Private Module`|Determines the _accessibility_ of a module|
|`Option Strict`|RD-VBA _language core_ extension making _implicit late-binding_ compile-time errors|

Directives also include `Def<Type>` _implicit definition_ statements:

|Directive|Description
|---|---|
|`DefBool`|Configures implicit definitions for [VBBooleanType](../_site/api/RDCore.SDK.Model.Types.VBBooleanType.html)|
|`DefByte`|Configures implicit definitions for [VBByteType](../_site/api/RDCore.SDK.Model.Types.VBByteType.html)|
|`DefInt`|Configures implicit definitions for [VBIntegerType](../_site/api/RDCore.SDK.Model.Types.VBIntegerType.html)|
|`DefLng`|Configures implicit definitions for [VBLongType](../_site/api/RDCore.SDK.Model.Types.VBLongType.html)|
|`DefLngLng`|Configures implicit definitions for [VBLongLongType](../_site/api/RDCore.SDK.Model.Types.VBLongLongType.html) in 64-bit environments|
|`DefLngPtr`|Configures implicit definitions for [VBLongPtrType_x86](../_site/api/RDCore.SDK.Model.Types.VBLongPtrType_x86.html) (32-bit) or [VBLongPtrType_x86](../_site/api/RDCore.SDK.Model.Types.VBLongPtrType_x64.html) (64-bit)|
|`DefCur`|Configures implicit definitions for [VBCurrencyType](../_site/api/RDCore.SDK.Model.Types.VBCurrencyType.html)|
|`DefSng`|Configures implicit definitions for [VBSingleType](../_site/api/RDCore.SDK.Model.Types.VBSingleType.html)|
|`DefDbl`|Configures implicit definitions for [VBDoubleType](../_site/api/RDCore.SDK.Model.Types.VBDoubleType.html)|
|`DefDate`|Configures implicit definitions for [VBDateType](../_site/api/RDCore.SDK.Model.Types.VBDateType.html)|
|`DefStr`|Configures implicit definitions for [VBStringType](../_site/api/RDCore.SDK.Model.Types.VBStringType.html)|
|`DefObj`|Configures implicit definitions for [VBObjectType](../_site/api/RDCore.SDK.Model.Types.VBObjectType.html)|
|`DefVar`|Configures implicit definitions for [VBVariantType](../_site/api/RDCore.SDK.Model.Types.VBVariantType.html)|

Other directives include `Implements` and `Attribute` statements:

|Directive|Description
|---|---|
|`Implements`|Specifies that the (class) module _implements_ an _interface class_.|
|`Attribute`|Specifies flags and modifiers that alter the semantics of a module or member.|


## In this section

- [**RD-VBAL 3.1** Attributes and Directives](./rd-vbal.3.1.attributes-directives.md)
- [**RD-VBAL 3.2** Literals](./rd-vbal.3.2.literals.md)
- [**RD-VBAL 3.3** Operators](./rd-vbal.3.3.0.operators.md)
- [**RD-VBAL 3.4** Statements](./rd-vbal.3.4.0.statements.md)
- [**RD-VBAL 3.5** Instructions](./rd-vbal.3.5.0.instructions.md)


---
 V I V A T 🩷 C U C U M I S ™  

---

<p align="center">
<img alt="Logo™ 9562-7303 Québec inc." src="../images/vector-ducky.svg" style="width:200px; margin-top:72px;" /><br/>
<small>© Copyright <strong>9562-7303 Québec inc.</strong> (2026)<br/></small>
</p>

