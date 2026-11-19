# 5.6.2 Expression Evaluation

> [!NOTE]
> This section describes the implementation of [**MS-VBAL §5.6.2** Expression Evaluation](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/1d6362a2-0d3c-41a7-9c9f-c3afa0a11335).


## 5.6.2.1 Evaluation to a data value

This section corresponds to [**MS-VBAL §5.6.2.1** Evaluation to a data value](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/ff101375-b301-4cba-80b9-e1ab59f7a8be).

> [!NOTE]
> Reserved. This section has no content yet.


## 5.6.2.2 Evaluation to a simple data value

> [!NOTE]
> This section describes the implementation of [**MS-VBAL §5.6.2.2** Evaluation to a simple data value](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/f1cc9a8d-e681-4e20-9c5e-e3385545440e).

The _default member_ of a class type can be implicitly invoked through _let-coercion_, yielding the _data value_ of
the object. See [**RD-VBAL §2.4.2.2.2** Default Member](rd-vbal.2.4.2.non-intrinsic-types.md#24222-default-member) and
[**RD-VBAL §5.5.1.2** Runtime semantics](rd-vbal.5.5.1.2.runtime-semantics.md).


## 5.6.2.3 Default Member Recursion Limits

> [!NOTE]
> This section describes the implementation of [**MS-VBAL §5.6.2.3** Default Member Recursion Limits](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/b57f9c36-de21-4fe3-afa6-53668192ae18).

When the default member also yields an object data type, the implicit invocation of a default member through
let-coercion recurses as needed.

A Let to `os`, where the default member of `os` is a `Property Get` with no `Property Let` that returns an object (`os = 5`),
assigns the default member of the object it returns, as **MS-VBAL §5.4.3.8** specifies. MS-VBA refuses it at compile time
([**RD-VBAL §5.4.3.8** Targets of a class](rd-vbal.5.4.3.8.let-statement.md#targets-of-a-class)).

See [**RD-VBAL §2.4.2** Non-intrinsic Types](rd-vbal.2.4.2.non-intrinsic-types.md).
