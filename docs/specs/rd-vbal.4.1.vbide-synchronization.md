# 4.1 VBIDE Synchronization

> [!NOTE]
> This specification is incomplete at this time.

This section specifies a _lightweight VBIDE add-in_ for the **RDCore** platform tooling.

The VBIDE add-in is responsible for:

|Responsibility|Description|
|---|---|
|Export|Exporting an **MS-VBA** _source project_ to an **RD-VBA** _workspace folder_ ([**RD-VBAL §2.2** RDPROJ Structure](rd-vbal.2.2.rdproj-structure.md)).|
|Import|Importing an **RD-VBA** _workspace folder_ into an **MS-VBA** _source project_.|
|Launch|Launching an **RD-VBA** _environment host_ attached to a specified _host process ID_.|

Attaching an environment host to a host process is described in
[**RD-VBAL §4.0** Program Structure and Organization](rd-vbal.4.0.program-structure.md).
