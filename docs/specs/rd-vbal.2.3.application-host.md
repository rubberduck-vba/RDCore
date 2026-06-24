# 2.3 Application Host
> [!NOTE]
> This specification may be incomplete at this time.

🎯 The **RDCore** platform shall provide with the `rdc.exe` CLI client, the ability to _compose, host, analyze, run, and debug_ any **RD-VBA** application.

## 2.3.1 Composition Root

The _environment host_ is responsible for _composing_ a **RD-VBA** from its _references_, _instructions_, and _symbols_; configuring the _host VBA environment_ implicit storage, and loading any _application settings_ and additional _workspace resources_ into the runtime environment.

It then proceeds to resolve an _entry point_, initiates an _execution session_, construct and push a [ICallStackFrame](../api/RDCore.SDK.Runtime.Abstract.Execution.ICallStackFrame.html) to the _evaluation engine_ that then proceeds to sequentially evaluate each instruction in the frame.

### 2.3.1.1 Execution Session

An _execution session_ holds the _state_ of the execution engine and exposes methods that advance execution _steps_:

|Member|Description|
|---|---|
|`State`| Describes the current [_mode_](#232-mode--state) of the session|
|`Frame`| Exposes the current _stack frame_|
|`GetCurrentStack`| Exposes the current _call stack_|
|`StepInto`| Advances execution by a single step|
|`StepOver`| Advances execution into the next statement|
|`StepOut` | Advances execution to the next statement in the current scope, stepping over any statements in-between|


### 2.3.1.2 Virtual Heap

The `IVirtualHeap` interface extends `ISymbolProvider` and `ISymbolResolver` and should typically be exposed to the internal API through these specialized get-only interfaces.

- `ISymbolProvider` exposes a single `Define` method that loads a specified `Symbol` and addresses it using its `Uri`.

The `ISymbolResolver` interface exposes the following members:

|Member|Description|
|---|---|
|`Resolve`|Resolves a specified _identifier name_ to a defined `Symbol` by inspecting a specified _allocation scope_|
|`GetValue`|Gets the currently held `VBTypedValue` for a specified `Symbol`|
|`TryRead`|Gets the `VBTypedValue` held at the specified address (offset) if it exists|

The `IVirtualHeap` interface represents a _service that manages the run-time memory structure of an execution context_; it exposes the following additional members to the execution engine:

|Member|Description|
|---|---|
|`CreateObject`|Creates a new `VBObjectValue` of a specified _class type_|
|`SetValue`|Associates a specified `VBTypedValue` to a `Symbol`|
|`Allocate`|Allocates a `VBTypedValue` or a specified number of bytes at the _current memory address_ pointer|
|`Deallocate`|Deallocates the memory held by the symbol at a specified `Uri`|

> [!NOTE]
> The **RDCore** implementation (⚖️GPLv3) of this service is intended to be _thread-safe_. While RD-VBA normally executes on a single thread, its runtime implementation is not _inherently_ single-threaded and it is _host-dependent_ whether a **RD-VBA** _environment host_ supports the concurrent execution of RD-VBA execution threads. This concurrent execution capability is intended to be (optionally) used for eventual _unit testing_ features.

An implementation of `IVirtualHeap` should:
- Maintain an internal _global heap_ to hold a `VBTypedValue` for any given `Symbol` that is globally-scoped;
- Maintain an internal _workspace heap_ to hold a `VBTypedValue` for any given `Symbol` that is workspace-scoped;
- Maintain an internal _static locals heap_ to hold a `VBTypedValue` for any given `Symbol` that is module-scoped;
- Maintain an internal _object heap_ to hold the `Symbol` references and their respective associated `VBTypedValue` for any given `VBObjectValue`;
- Maintain an internal _address pointer_ to track the current _memory offset_;
- Maintain an internal _symbol table_ mapping a `Uri` to its associated `Symbol`;
- Maintain an internal _name table_ holding the current representation (casing) of all loaded symbols;
- Maintain an internal _memory map_ mapping a `VBTypedValue` to a _memory address_ (offset);
- Maintain an internal _raw address map_ mapping a _memory address_ (offset) to a `Uri`.

[ScopeKind](/api/RDCore.SDK.Model.Symbols.Abstract.ScopeKind.html) defines the _allocation scopes_.

The _current memory address_ pointer should be incremented by a _host-defined_ `IntPtrSize` that represents the size of a pointer in the current environment (32 or 64 bits).

The correctly-scoped allocation of all symbols upon their definition should then suffice to make symbol resolution automatically follow the MS-VBAL specification with regards to the order in which an _identifier name_ is resolved, provided that lookups are done in the specified order:
1. If a name refers to a symbol defined on the local _stack frame_, then the resolved symbol is locally scoped;
2. If a name refers to a symbol defined in the _static locals heap_, then the resolved symbol is locally scoped but preserves its value between calls;
3. If a name refers to a symbol defined in the _workspace heap_, then the resolved symbol is workspace-scoped;
4. If a name refers to a symbol defined in the _global heap_, then the resolved symbol is globally-scoped.

- If multiple symbols match a specified name before reaching the _global_ scope, then the name is ambiguous and an appropriate [compile-time error](/api/RDCore.SDK.Model.Errors.VBCompileErrorId.html) should be issued, in this case **VBC009303** _Duplicate declaration_.
- If multiple symbols match a specified name within the _global_ scope, then the name is disambiguated using the _reference priority order_ of the _referenced library_ a matching symbol is defined in. This priotity is determined by the order in which project references appear in the `.rdproj` file of a _workspace folder_.

> [!NOTE]
> The **VBA** standard library always has the _lowest priority_ (i.e. always appears first), meaning any other project reference that defines any identically-named class type or public/global member is always going to _shadow_ the `VBA` library definitions; this _shadowing_ should be detected in the _semantic layer_ and reported through _semantic flags_ so **RDCore.Diagnostics** can issue _shadowed declaration_ diagnostics.

## 2.3.2 Mode / State

Provided that the _host application_ is able to respond to keyboard inputs, execution in _running mode_ may be suspended at any point to enter _break mode_ through what has traditionally been a <kbd>Ctrl</kbd>+<kbd>Pause|Break</kbd> keyboard shortcut in the _Microsoft Visual Basic Editor_, however the platform considers this an implementation detail of the _environment host_, that may offer the same functionality through different, _implementation-defined_ means that may or may not be equivalent.

At any point in time, a _VBA host environment_ may be in either one of the following modes / states:  
- `Design`: a _static context_ exists and is actively being synchronized with the _workspace source code_ being edited;
- `Run`: the _host environment_ is actively executing instructions uninterrupted;
- `Break`: execution is halted at the _current instruction_ either through a _manual break_, a _semantic break_ (e.g. a failed `Assert` call, or a `Stop` keyword was encountered), or an _unhandled run-time error_ has occurred and instructions can be manually stepped over/into, or rewinded;

> 👉 The exact behavior of the _host environment_ on error is _implementation-defined_: depending on the _workspace application_ configuration, a failing workspace application may terminate the host process with an _error code_, or enter _break mode_ and offer to _debug_ at that location.


> ⏮️ [**RD-VBAL §2.2** RDPROJ Structure](./rd-vbal.2.2.rdproj-structu.html) | ⏭️ [**RD-VBAL §2.4** Static Types](./rd-vbal.2.4.static-types.html)

