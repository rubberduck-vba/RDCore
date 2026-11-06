# 2.5.2.1.4 Object Values

An instance of a [VBObjectType](../api/RDCore.SDK.Model.Types.VBObjectType.html) is always a [VBObjectValue](../api/RDCore.SDK.Model.Values.Intrinsic.VBObjectValue.html).

The underlying value of an _object value_ is a unique addressable ID.

## Object Lifetime

Object lifetime is the responsibility of the session's [ISessionObjects](../api/RDCore.SDK.Runtime.Abstract.Execution.ISessionObjects.html) service:

|Member|Role|
|---|---|
|`CreateObject`|Creates an object instance.|
|`AddRef` / `RemoveRef`|Reference counting.|
|`TryRemoveObject`|Removes an instance whose reference count has reached zero.|

The session's `ISessionSymbols` and `ISessionObjects` implementations should maintain an internal _object heap_ holding the `Symbol` references and their associated bindings for any `VBObjectValue`. See [**RD-VBAL §2.3.1.2** Session Services](rd-vbal.2.3.1.2.session-services.md).
