# 2.3 Application Host
> ℹ️ This specification is incomplete at this time.

🎯 The **RDCore** platform shall provide with the `rdc.exe` CLI client, the ability to _compose, host, analyze, run, and debug_ any **RD-VBA** application.

## Composition Root

The _environment host_ is responsible for _composing_ a **RD-VBA** from its _instructions_ and _symbols_, configuring the _host VBA environment_ implicit storage and loading any _application settings_ and additional _workspace resources_ into the runtime environment.

It then proceeds to resolve an _entry point_, construct and push a [ICallStackFrame](../_site/api/RDCore.SDK.Runtime.Abstract.Execution.ICallStackFrame.html) to the _evaluation engine_ that then proceeds to sequentially evaluate each instruction in the frame.

## Mode / State

Provided that the _host application_ is able to respond to keyboard inputs, execution in _running mode_ may be suspended at any point to enter _break mode_ through what has traditionally been a <kbd>Ctrl</kbd>+<kbd>Pause|Break</kbd> keyboard shortcut in the _Microsoft Visual Basic Editor_, however the platform considers this an implementation detail of the _environment host_, that may offer the same functionality through different, _implementation-dependent_ means that may or may not be equivalent.

At any point in time, a _VBA host environment_ may be in either one of the following modes / states:
- `Design`: a _static context_ exists and is actively being synchronized with the _workspace source code_ being edited;
- `Run`: the _host environment_ is actively executing instructions uninterrupted;
- `Break`: execution is halted at the _current instruction_ either through a _manual break_, a _semantic break_ (e.g. a failed `Assert` call, or a `Stop` keyword was encountered), or an _unhandled run-time error_ has occurred and instructions can be manually stepped over/into, or rewinded;




---
 V I V A T 🩷 C U C U M I S ™  

---

<p align="center">
<img alt="Logo™ 9562-7303 Québec inc." src="../images/vector-ducky.svg" style="width:200px; margin-top:72px;" /><br/>
<small>© Copyright <strong>9562-7303 Québec inc.</strong> (2026)<br/></small>
</p>
