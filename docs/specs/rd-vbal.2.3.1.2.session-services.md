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

What is at an address in these heaps, and what its bytes are, is the [memory model](#memory-model) below.

The order in which names are looked up in these heaps is specified in
[**RD-VBAL §2.3.1.3** Name Resolution](rd-vbal.2.3.1.3.name-resolution.md).

## Memory addressing

`ISessionMemoryAllocator` reserves address space, never values: blocks carved from segments of 4 KiB (2 KiB in a 32-bit
environment) that begin at `0x10000`, a block that is freed being reused for a later one that fits. The width of a pointer
is the environment's (`IRuntimeEnvironmentProfile.Is64Bit`): 8 bytes, or 4.

The _memory map_ (`MemoryAddress` → `IBindingHandle`) is held by the session storage (`SessionStorage`, below):
it binds the `IBindingHandle` of a value to the
[MemoryAddress](../api/RDCore.SDK.Runtime.Shared.MemoryAddress.html) reserved for it.

> [!NOTE]
> **Not implemented.** The _raw address map_ (`MemoryAddress` → `Uri`) is not implemented: what is at an address is
> known by what is bound there, not by the name of a symbol.

## Session storage

A value is not stored as bytes: what is bound at an address is its cell - an `IBindingHandle` - or a block the session
lays out for it (a `BSTR`, an interface, a `SAFEARRAY`), and the bytes at the address are what the value is in
memory, laid out as **MS-VBA** lays it out when they are read (`PEEK`) and read back into the value when one of them is
written (`POKE`). See the [memory model](#memory-model).

A symbol's storage allocation is performed by `SymbolAddressTable`, which
[ICallStackFrame](../api/RDCore.SDK.Runtime.Abstract.Execution.ICallStackFrame.html) and the module/global resolver
share ([**RD-VBAL §2.5.2.1.2** Array Values](rd-vbal.2.5.2.1.2.array-values.md)).
`SymbolAddressTable.FreshBinding` re-boxes a `Variant`'s wrapped value into a fresh
[VBRuntimeVariantValue](../api/RDCore.SDK.Model.Values.Runtime.VBRuntimeVariantValue.html) on every store
([**RD-VBAL §2.5.2.1.5** Variant Values](rd-vbal.2.5.2.1.5.variant-values.md)).

### Zero-size storage

`ISessionMemoryAllocator` refuses a zero-size allocation, as an enforced invariant: a 0-byte
bump-pointer/free-list allocation would hand the same address to the next caller.

For a non-positive size, `SessionStorage.TryAllocate` asks for a pointer's size instead - a `VARIANT`'s, for a `Variant` - : a variable has an address
whatever it holds, which a program can take (`VarPtr`), and what holds nothing is a pointer in **MS-VBA** - a `String` or
a dynamic array whose pointer is `0`. The following values have a non-positive storage size, and all of them take this
path:

|Value with a non-positive storage size|
|---|
|`vbNullString`|
|`Nothing`|
|`Null`|
|`Empty`|
|An uninitialized array|
|An empty `ParamArray` array|

Zero-size storage is supported because a `ParamArray` call with nothing left over is the most common
`ParamArray` call shape: `Call Callee(100)` against a `ParamArray rest()` parameter with no arguments left over
collects an empty array whose `Size` is 0
([**RD-VBAL §5.3.1.11** Procedure Invocation Argument Processing](rd-vbal.5.3.1.11.procedure-invocation-argument-processing.md)).

## Memory model

The session's memory is one address space, the one `VarPtr`, `StrPtr`, `ObjPtr` and `AddressOf` give addresses in, and
`PEEK` and `POKE` read and write. What is in it is laid out as **MS-VBA** lays it out (recorded in Excel), so that what a
program learns of it - an address, what is at an address - is what it would learn in **MS-VBA**, whatever the engine that
runs it.

### What has an address

|What|Its address|
|---|---|
|A variable: a module's, a `Static` local, a field of an object in its own code|Its own, given when it is defined, the same for as long as it exists.|
|A local, a parameter `ByVal`|Its own, given the first time it is asked for: for the whole activation, whatever is assigned to it.|
|A parameter `ByRef`|The address of the variable it was given.|
|An element of an array|In the block the array's elements are laid out in, side by side, made the first time the address of one of them is asked for ([`VarPtr`](rd-vbal.6.1.2.13.hidden-module.md#varptr)).|
|A field of a record|The record's address, plus the field's offset; a fixed-size array field's elements are in the record.|
|The result variable of a `Function` or a `Property Get`, in its own code|Its own, for the activation.|
|What is no variable (a literal, an expression, a fixed-length `String` given to `VarPtr`)|A temporary's, until the statement is over.|
|The characters of a `String`|In a `BSTR` ([`StrPtr`](rd-vbal.6.1.2.13.hidden-module.md#strptr)).|
|An object|An interface of it, one for each it is asked for through ([`ObjPtr`](rd-vbal.6.1.2.13.hidden-module.md#objptr)).|
|A dynamic array|Its `SAFEARRAY` ([**RD-VBAL §6.1.2.13**](rd-vbal.6.1.2.13.hidden-module.md#safearray)).|
|A procedure of a standard module|Its own ([`AddressOf`](rd-vbal.5.6.16.constrained-expressions.md#56168-addressof-expressions)).|

### What the bytes are

|A value of|Is, at its address|
|---|---|
|A number, `Boolean`, `Date`, `Currency`|Its bytes, little-endian: a `Boolean` two, `-1` for `True`; a `Date` a `Double`; a `Currency` a 64-bit integer of ten-thousandths.|
|A fixed-length `String`|Its characters, two bytes each.|
|A variable-length `String`|The address of its characters (`0` for `vbNullString`), which are in a `BSTR`: a `Long` of their length in bytes, the characters, a null character.|
|An object|The address of the interface its declared type says (`0` for `Nothing`); the first pointer there is the class's.|
|A `Variant`|A `VARIANT` - 24 bytes, 16 in a 32-bit environment: its `VarType` in the first two, its data eight bytes in, a pointer for a `String`, an object, an array.|
|A dynamic array|The address of its `SAFEARRAY` (`0` for one of no dimensions): its dimensions, features, element width, locks, the address of its elements, and each dimension's count and lower bound, the last first.|
|A record|Its fields, each at its offset - aligned as **MS-VBA** aligns it for the pointer width - each as its type is, and zeros between them.|

### When what the session lays out is made and freed

|Block|Made|Freed|
|---|---|---|
|An array's elements|The first time the address of an element is asked for.|When the array is gone - `ReDim`'d, assigned another, a local of an activation that returned - the next time a block is made.|
|A `BSTR`|The first time the address of the characters is asked for, or the bytes of the `String`.|When a `String` is assigned to what holds it, or what holds it is freed; not when a pointer `POKE`d over it lets go of it.|
|An interface|The first time its address is asked for.|When the object is destroyed.|
|A `SAFEARRAY`|The first time its address is asked for.|A dynamic array's when it holds no array (`Erase`), a `Variant`'s when it holds another; not when a pointer `POKE`d over it lets go of it.|
|A procedure's address|The first time it is asked for.|Never: it lasts as long as the session.|
|A temporary|When it is passed.|When the statement is over.|

A block never takes the address of the one it replaces: a new `BSTR`, a new array block, is made before the old one is
freed, so that the two are never at one address (an array `ReDim`'d is elsewhere, as in **MS-VBA**). A block that is let
go of is freed the next time it is asked for, or when what holds it is freed - which is what a program can tell.

### What a POKE writes

|A POKE into|Makes|
|---|---|
|A number's, a `Boolean`'s, a `Date`'s, a fixed-length `String`'s bytes|The value its bytes say.|
|A `String`'s characters, a `BSTR`'s length|The `String` they say, at the same address, for everything that holds it.|
|A `String`'s, an object's, a dynamic array's pointer|What it points at - another's `BSTR`, interface, `SAFEARRAY` - which the two then hold, without a reference of their own; `0` makes it `vbNullString`, `Nothing`, an array of no dimensions. A pointer is written a byte at a time, and one that points at nothing the session has leaves what it was.|
|A `VARIANT`'s type or data|The value they say; its data, when it holds a `String`, an object or an array, is the pointer it is.|
|A `SAFEARRAY`'s lower bound, or count|The array under those bounds, its elements where they are; never more elements than it has.|
|An interface, a procedure's address, the rest of a `SAFEARRAY`|Nothing: the `POKE` is refused.|

### Implementation-defined

What **MS-VBA** leaves to the memory it runs in, and RD-VBA decides:

- **The addresses are the session's.** The address space begins at `0x10000`, so that no address is `0` or a small number; where in it a block is depends on
  what was made and freed before it, as it does in **MS-VBA**, and a program that compares two addresses for anything but equality learns nothing it can rely on.
- **Native code does not share it.** An address given to a `Declare` is a number, not a pointer into memory the native code can read or write
  (🚧 *Ptr plan phase B: a mirror of the session's memory around a native call, and callbacks for `AddressOf`).
- **What MS-VBA leaves as it was is zeros.** The bytes past what a block holds, an interface's table, a procedure's address, and the upper bytes of a
  `Single` passed `ByVal` to `VarPtr` (where **MS-VBA** leaves whatever the register held) read as zeros.
- **What would be memory that is not the session's is refused.** A `POKE` whose result would point past a block - a `BSTR`'s length, a `SAFEARRAY`'s
  count - or into what only the session writes is refused, rather than leaving something that points nowhere.
- **A pointer `POKE`d over what it pointed at leaks it**, as it does in **MS-VBA**: the `BSTR` or `SAFEARRAY` stays where it was, and pointing at it again
  is the value again. A weak reference made so - an interface's address `POKE`d into an object variable - counts no reference, as in **MS-VBA**.

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
