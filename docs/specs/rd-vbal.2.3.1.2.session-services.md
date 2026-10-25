# 2.3.1.2 Session Services

An _execution session_'s services are rooted at an
[IRuntimeSession](../api/RDCore.SDK.Runtime.Abstract.Execution.IRuntimeSession.html).

## IRuntimeSession

`IRuntimeSession` exposes:

|Exposes|Description|
|---|---|
|`Environment.Is64Bit`|The environment bitness, on the session's [IRuntimeEnvironmentProfile](../api/RDCore.SDK.Runtime.Abstract.Execution.IRuntimeEnvironmentProfile.html). It also sets the default value of the `#If Win64` pre-compiler constant. `#If VBA7` does not depend on it: the environment host defaults `VBA7` to true in either bitness. A `.rdproj` `#Const` or a `--define` argument overrides these defaults.|
|`Environment.Culture`|The regional settings of the environment, which its `Lcid` selects; `0` is the invariant culture. A number or a date let-coerced to `String` is written in them ([**RD-VBAL §5.5.1.2.4** Let-coercion to and from String](rd-vbal.5.5.1.2.runtime-semantics.md#55124-let-coercion-to-and-from-string)).|
|`Environment.AnsiEncoding`|The `Encoding` of the environment's `AnsiCodePage`: what a character is as one or two bytes outside a `String`'s own data, which the session's files are written and read in. Let-coercion between `String` and `Byte()` takes no code page ([**RD-VBAL §5.5.1.2.6** Let-coercion to and from resizable Byte()](rd-vbal.5.5.1.2.runtime-semantics.md#55126-let-coercion-to-and-from-resizable-byte)).|
|`References`|The workspace's project and library references ([References](#references), below).|
|The three session services|`ISessionMemoryAllocator`, `ISessionSymbols` and `ISessionObjects` ([Services](#services), below).|

## Services

|Service|Responsibility|Members|
|---|---|---|
|[ISessionMemoryAllocator](../api/RDCore.SDK.Runtime.Abstract.Execution.ISessionMemoryAllocator.html)|Allocates and frees blocks in the session's memory space, and reports allocation / fragmentation statistics.|`TryAllocate`, `TryDeallocate`, `Info`|
|[ISessionSymbols](../api/RDCore.SDK.Runtime.Abstract.Execution.ISessionSymbols.html)|The session's symbol table.|`TryDefine` defines a [Symbol](../api/RDCore.SDK.Model.Symbols.Abstract.Symbol.html) in a scope. `TryResolveValue` resolves a name visible from a scope; `TryResolveType` is its type binding context counterpart. `Resolver` is the session's symbol resolver.|
|[ISessionObjects](../api/RDCore.SDK.Runtime.Abstract.Execution.ISessionObjects.html)|Object lifetime.|`CreateObject`; `AddRef` / `RemoveRef` (reference counting); `TryRemoveObject` removes an instance whose reference count has reached zero.|

`ISessionMemoryAllocator` is an _accounting_ layer: it tracks sizes and addresses, in the manner of MSVBVM. It does
not hold the values themselves.

`ISessionSymbols` mirrors the `ISymbolResolver.ResolveValue` / `ResolveType` pair as `TryResolveValue` and
`TryResolveType`. `ISessionSymbols.Resolver` is a `ScopeTreeSymbolResolver` over the session's own symbols, rebuilt
as symbols are defined. See [**RD-VBAL §2.3.1.3** Name Resolution](rd-vbal.2.3.1.3.name-resolution.md).

For object values and their lifetime, see
[**RD-VBAL §2.5.2.1.4** Object Values](rd-vbal.2.5.2.1.4.object-values.md).

## References

`IRuntimeSession.References` is the workspace's project and library references, as an ordered
`IReadOnlyList<ReferencePriorityInfo>`. It is the runtime-facing view of the `.rdproj` _RDCoreReference_ list
([**RD-VBAL §2.2.3.2** RDCoreReference](rd-vbal.2.2.3.projectfile.md)).

Each [ReferencePriorityInfo](../api/RDCore.SDK.Model.Symbols.ReferencePriorityInfo.html) entry carries only:

|Member|Description|
|---|---|
|`Name`|The reference's source-visible name.|
|`Priority`|The reference's rank in the `IRuntimeSession.References` list.|

The list is the _reference priority_ order defined for global-scope name resolution
([**RD-VBAL §2.3.1.3** Name Resolution](rd-vbal.2.3.1.3.name-resolution.md)). It keeps the order in which the
language server provides the references.

A referenced library's own members are not taken from this list: they are contributed by an
[ISymbolProvider](../api/RDCore.SDK.Runtime.Abstract.Execution.ISymbolProvider.html) (see [below](#isymbolprovider))
and resolved through `ISymbolResolver`.

Name resolution across referenced projects and libraries shall consult the ordering to disambiguate a global-scope
name. The name-resolution algorithm itself is a separate concern from the list
([**RD-VBAL §2.3.1.3** Name Resolution](rd-vbal.2.3.1.3.name-resolution.md)).

Name resolution takes that order from the libraries the project loads, not from this list: each symbol of a
referenced library carries the rank of its library (`SymbolProperties.LibraryPriority`, from
`ReferencedLibraries.Priorities`, which follows the order of the references), and `ScopeTreeSymbolResolver` ranks the
candidates of one tier of the lookup order by it, so a name that two libraries declare means the symbol of the one
referenced later ([Reference priority](rd-vbal.2.3.1.3.name-resolution.md#reference-priority)). The symbols of two
libraries never share a rank. Only symbols of one tier that do, such as the members of two procedural modules of the
workspace that both declare the name, are reported as **VBC09301** _Ambiguous name_ (see
[Duplicate and ambiguous names](rd-vbal.2.3.1.3.name-resolution.md#duplicate-and-ambiguous-names)).

## ISymbolProvider

`ISymbolProvider` exposes a single `ProvideSymbols` method, which yields the `Symbol`s its source defines. The
composition root ([**RD-VBAL §2.3.1** Composition Root](rd-vbal.2.3.1.composition-root.md)) then defines each provided `Symbol`:

|Context|Each provided `Symbol` is defined into|
|---|---|
|Static|The semantic layer.|
|Runtime|The session symbol table, through `ISessionSymbols.TryDefine`.|

`ISymbolProvider` is the abstraction behind the several _symbol providers_ a session is composed from:

|Symbol provider source|
|---|
|Configuration flags.|
|AST declarations.|
|Reflected referenced libraries.|
|The environment host's own runtime and standard library ([**RD-VBAL §6.0** Standard Library](rd-vbal.6.0.standard-library.md)).|

## Heaps

The session's `ISessionSymbols` and `ISessionObjects` implementations should maintain the following internal
structures:

|Internal structure|Holds|
|---|---|
|_Global heap_|An [IBindingHandle](../api/RDCore.SDK.Model.Values.Bindings.IBindingHandle.html) for any globally-scoped `Symbol`.|
|_Workspace heap_|An `IBindingHandle` for any workspace-scoped `Symbol`.|
|_Static locals heap_|An `IBindingHandle` for any module-scoped `Symbol`.|
|_Object heap_|The `Symbol` references and their associated bindings for any [VBObjectValue](../api/RDCore.SDK.Model.Values.Intrinsic.VBObjectValue.html).|
|_Symbol table_|A map of a `Uri` to its associated `Symbol`.|
|_Name table_|The current representation (casing) of all loaded symbols.|

A `Symbol`'s scope kind determines how, and whether, the symbol is allocated in memory: a
`ScopeKind.Global` symbol lives in the globals heap, a `ScopeKind.Module` symbol in the workspace statics heap, and
a `ScopeKind.Instance` symbol in the object heap. A `Static` local's storage lives in the same heap tier a module
field uses; one of a member of a class lives in the object heap, with the fields of the object the member is called on
([**RD-VBAL §5.4.3.1** Local Variable Declarations](rd-vbal.5.4.3.1.local-variable-declarations.md)).

See [**RD-VBAL §2.5.1** Runtime Entities](rd-vbal.2.5.1.runtime-entities.md) for `ScopeKind` and the allocation scopes.

The session's address space begins above the first 64 KiB, so that no address is `0`. A variable is at an address whatever it holds, one that holds
nothing being given a pointer's size. The elements of an array are not at an address each: the first time the address of one of them is asked for
(`VarPtr`), the array is given a block of the session's memory as wide as all of its elements side by side, and every element is at its place in it, as
**MS-VBA** lays them out ([**RD-VBAL §6.1.2.13**](rd-vbal.6.1.2.13.hidden-module.md#varptr)). The block belongs to the array, and does not keep it
alive: an array that is gone - `ReDim`'d, assigned another, a local of an activation that returned - is let go of with its block, which the session frees
when it next makes one. A `PEEK` reads the block as the elements' bytes, and a `POKE` writes the element the byte is part of; a record's address reads,
and writes, its fields at their offsets.

A variable-length `String` is a pointer, as in **MS-VBA**: the address of its characters, in a `BSTR` of the session's memory - a `Long` of its length in
bytes, the characters, a null character ([**RD-VBAL §6.1.2.13**](rd-vbal.6.1.2.13.hidden-module.md#strptr)). The `BSTR` is made for what holds the `String`
(a variable, an element, a field, a `Variant`) the first time the address is asked for, by `StrPtr` or by a `PEEK` of the pointer, and is that holder's for
as long as it holds that `String`: a `String` assigned to it is in another `BSTR`, and the one it was is freed, as is the `BSTR` of a holder that is freed. A
`POKE` into the characters changes the `String` in place, at the same address, and one into the length makes it as long as the length says. A `POKE` into a
`String`'s pointer makes it the `String` of the `BSTR` the pointer points at - which the two holders then share, each change to the characters being the
other's - or `vbNullString` for `0`; the `BSTR` it pointed at is let go of, not freed, as **MS-VBA** leaves it.

An object is a pointer too: the address of an interface of the object, a block the session makes for each interface it is asked for through
([**RD-VBAL §6.1.2.13**](rd-vbal.6.1.2.13.hidden-module.md#objptr)) and frees when the object is destroyed. A `Variant` is a `VARIANT`: its type, then its
data, a `String`'s or an object's being the pointer it is.

The order in which names are looked up in these heaps is specified in
[**RD-VBAL §2.3.1.3** Name Resolution](rd-vbal.2.3.1.3.name-resolution.md).

## Memory addressing

`ISessionMemoryAllocator` maintains an internal _address pointer_ that tracks the current _memory offset_.

The current memory address pointer should be incremented by a _host-defined_ `IntPtrSize`, which represents the
size of a pointer in the current environment (32 or 64 bits).

The _memory map_ (`MemoryAddress` → `IBindingHandle`) is held by the session storage (`SessionStorage`, below):
it binds the `IBindingHandle` of a value to the
[MemoryAddress](../api/RDCore.SDK.Runtime.Shared.MemoryAddress.html) reserved for it.

> [!NOTE]
> **Not implemented.** The _raw address map_ (`MemoryAddress` → `Uri`) is not implemented.

## Session storage

> [!NOTE]
> **Not implemented.** A bound value is not stored as a byte block at its address: an `IBindingHandle` is itself
> the value bound to a `Symbol`.

A symbol's storage allocation is performed by `SymbolAddressTable`, which
[ICallStackFrame](../api/RDCore.SDK.Runtime.Abstract.Execution.ICallStackFrame.html) and the module/global resolver
share ([**RD-VBAL §2.5.2.1.2** Array Values](rd-vbal.2.5.2.1.2.array-values.md)).
`SymbolAddressTable.FreshBinding` re-boxes a `Variant`'s wrapped value into a fresh
[VBRuntimeVariantValue](../api/RDCore.SDK.Model.Values.Runtime.VBRuntimeVariantValue.html) on every store
([**RD-VBAL §2.5.2.1.5** Variant Values](rd-vbal.2.5.2.1.5.variant-values.md)).

### Zero-size storage

`ISessionMemoryAllocator` refuses a zero-size allocation, as an enforced invariant: a 0-byte
bump-pointer/free-list allocation would hand the same address to the next caller.

For a non-positive size, `SessionStorage.TryAllocate` mints its own address and never passes the request to the
session memory allocator. The following values have a non-positive storage size, and all of them take this path:

|Value with a non-positive storage size|
|---|
|`Nothing`|
|`Null`|
|`Empty`|
|An uninitialized array|
|An empty `ParamArray` array|

Such a value still gets a binding that resolves by name, but never gets memory from the session memory allocator.

The addresses `SessionStorage.TryAllocate` mints for non-positive sizes are negative, so that they can never collide
with, or be mistaken for, an allocator address.

Zero-size storage is supported because a `ParamArray` call with nothing left over is the most common
`ParamArray` call shape: `Call Callee(100)` against a `ParamArray rest()` parameter with no arguments left over
collects an empty array whose `Size` is 0
([**RD-VBAL §5.3.1.11** Procedure Invocation Argument Processing](rd-vbal.5.3.1.11.procedure-invocation-argument-processing.md)).

## Call stack and file channels

The session's call stack, `RuntimeCallStack`, enforces a call-depth limit, in `OnBeforeTryPush`
([**RD-VBAL §5.3.1.11** Procedure Invocation Argument Processing](rd-vbal.5.3.1.11.procedure-invocation-argument-processing.md)).
Of the GoSub Resumption List, the SDK interface `ICallStackFrame` exposes only `GoSubDepth`, the number of its
entries; it does not expose the list's push/pop mutators ([**RD-VBAL §3.5.4** Execution](rd-vbal.3.5.4.execution.md)).

All file statements run through one session-level shim, the
[IFileChannels](../api/RDCore.SDK.Runtime.Abstract.Execution.IFileChannels.html) /
[IFileChannel](../api/RDCore.SDK.Runtime.Abstract.Execution.IFileChannel.html) interfaces
([**RD-VBAL §5.4.5** File Statements](rd-vbal.5.4.5.file-statements.md)).

## Thread safety

The **RDCore** implementations (⚖️ GPLv3) of the session services are intended to be _thread-safe_.

RD-VBA normally executes on a single thread, but the RD-VBA runtime implementation is not _inherently_
single-threaded. Whether an RD-VBA _environment host_ supports the concurrent execution of RD-VBA execution
threads is _host-dependent_.

---
> ⏮️ [**RD-VBAL §2.3.1.1** Execution Session](rd-vbal.2.3.1.1.execution-session.md) | ⏭️ [**RD-VBAL §2.3.1.3** Name Resolution](rd-vbal.2.3.1.3.name-resolution.md)
