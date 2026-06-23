# 2.3 Application Host
> [!NOTE]
> This specification may be incomplete at this time.

🎯 The **RDCore** platform shall provide with the `rdc.exe` CLI client, the ability to _compose, host, analyze, run, and debug_ any **RD-VBA** application.

## 2.3.1 Composition Root

The _environment host_ is responsible for _composing_ a **RD-VBA** from its _references_, _instructions_, and _symbols_; configuring the _host VBA environment_ implicit storage, and loading any _application settings_ and additional _workspace resources_ into the runtime environment.

It then proceeds to resolve an _entry point_, construct and push a [ICallStackFrame](../api/RDCore.SDK.Runtime.Abstract.Execution.ICallStackFrame.html) to the _evaluation engine_ that then proceeds to sequentially evaluate each instruction in the frame.

## 2.3.2 Mode / State

Provided that the _host application_ is able to respond to keyboard inputs, execution in _running mode_ may be suspended at any point to enter _break mode_ through what has traditionally been a <kbd>Ctrl</kbd>+<kbd>Pause|Break</kbd> keyboard shortcut in the _Microsoft Visual Basic Editor_, however the platform considers this an implementation detail of the _environment host_, that may offer the same functionality through different, _implementation-defined_ means that may or may not be equivalent.

At any point in time, a _VBA host environment_ may be in either one of the following modes / states:  
- `Design`: a _static context_ exists and is actively being synchronized with the _workspace source code_ being edited;
- `Run`: the _host environment_ is actively executing instructions uninterrupted;
- `Break`: execution is halted at the _current instruction_ either through a _manual break_, a _semantic break_ (e.g. a failed `Assert` call, or a `Stop` keyword was encountered), or an _unhandled run-time error_ has occurred and instructions can be manually stepped over/into, or rewinded;

> 👉 The exact behavior of the _host environment_ on error is _implementation-defined_: depending on the _workspace application_ configuration, a failing workspace application may terminate the host process with an _error code_, or enter _break mode_ and offer to _debug_ at that location.


> ⏮️ [**RD-VBAL §2.2** RDPROJ Structure](./rd-vbal.2.2.rdproj-structu.html) | ⏭️ [**RD-VBAL §2.4** Static Types](./rd-vbal.2.4.static-types.html)

