# 2.3.2 Mode / State

At any point in time, a _VBA host environment_ is in exactly one of the following modes / states:

|Mode|Description|
|---|---|
|`Design`|A _static context_ exists and is actively being synchronized with the _workspace source code_ being edited. In design mode, the host environment maintains a static execution context ([**RD-VBAL §4.0** Program Structure and Organization](rd-vbal.4.0.program-structure.md)).|
|`Run`|The _host environment_ is actively executing instructions uninterrupted.|
|`Break`|Execution is halted at the _current instruction_. Instructions can be manually stepped over / into, or rewound ([**RD-VBAL §2.3.1.1** Execution Session](rd-vbal.2.3.1.1.execution-session.md)).|

For an **RD-VBA** project without any modules, the host cannot exit design mode to run or debug
([**RD-VBAL §4.0** Program Structure and Organization](rd-vbal.4.0.program-structure.md)).

## Entering break mode

`Break` mode is entered through any of the following:

|Cause|Example|
|---|---|
|A _manual break_|Suspending running execution ([Manual break](#manual-break), below).|
|A _semantic break_|A failed `Assert` call ([**RD-VBAL §5.4.2.23** Assert Statement](rd-vbal.5.4.2.23.assert-statement.md)), or an encountered `Stop` keyword ([**RD-VBAL §5.4.2.11** Stop Statement](rd-vbal.5.4.2.11.stop-statement.md)).|
|An _unhandled run-time error_|See [Behaviour on error](#behaviour-on-error), below, and [**RD-VBAL §5.4.4** Error Handling Statements](rd-vbal.5.4.4.error-handling-statements.md).|

### Manual break

Provided that the _host application_ is able to respond to keyboard inputs, execution in _running mode_ may be
suspended at any point to enter _break mode_.

In the _Microsoft Visual Basic Editor_, a manual break is entered with the
<kbd>Ctrl</kbd>+<kbd>Pause|Break</kbd> keyboard shortcut. The platform considers this keyboard shortcut an
implementation detail of the _environment host_.

An environment host may offer the same functionality through different, _implementation-defined_ means that may or
may not be equivalent.

## Behaviour on error

> 👉 The exact behaviour of the _host environment_ on error is _implementation-defined_.

Depending on the _workspace application_ configuration, a failing workspace application may either:

- terminate the host process with an _error code_; or
- enter _break mode_ and offer to _debug_ at the failing location.

---
> ⏮️ [**RD-VBAL §2.3.1.3** Name Resolution](rd-vbal.2.3.1.3.name-resolution.md) | ⏭️ [**RD-VBAL §2.3.3** Configuration](rd-vbal.2.3.3.configuration.md)
