# 2.3.1.1 Execution Session

An _execution session_ ([IExecutionSession](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Runtime.Abstract.Execution.IExecutionSession.html))
holds the _state_ of the execution engine and exposes methods that advance execution _steps_. The environment
host initiates an execution session after resolving an _entry point_
([**RD-VBAL §2.3.1** Composition Root](rd-vbal.2.3.1.composition-root.md)).

## Members

|Member|Description|
|---|---|
|`State`|Describes the current _mode_ of the session ([**RD-VBAL §2.3.2** Mode / State](rd-vbal.2.3.2.mode-state.md)).|
|`Frame`|Exposes the current _stack frame_.|
|`GetCurrentStack`|Exposes the current _call stack_.|
|`StepInto`|Advances execution by a single step, into a procedure that the statement calls ([Where a program stops](rd-vbal.3.5.4.execution.md#where-a-program-stops)).|
|`StepOver`|Advances execution into the next statement.|
|`StepOut`|Advances execution to the next statement in the current scope, stepping over any statements in-between.|

In `Break` mode, instructions can be manually stepped over or into, or rewound
([**RD-VBAL §2.3.2** Mode / State](rd-vbal.2.3.2.mode-state.md)).

The services an execution session is rooted at are described in
[**RD-VBAL §2.3.1.2** Session Services](rd-vbal.2.3.1.2.session-services.md).
