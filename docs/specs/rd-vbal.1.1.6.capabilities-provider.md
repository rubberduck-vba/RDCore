# 1.1.6 Capabilities Provider

An *environment host* must implement [IExtensionCapabilityProvider](../api/RDCore.SDK.Extensibility.IExtensionCapabilityProvider.html), and must provide all available extension capabilities through it.

`IExtensionCapabilityProvider` allows a host to determine whether certain extension capabilities should be enabled. The mechanisms by which a capability provider decides are implementation-dependent. They may include, but are not restricted to, the validation of an active subscription to the extended capabilities.

## Denied Capabilities

All platform extensions must *gracefully* handle being denied their extended capabilities by the capability provider. Graceful handling includes:

- TRACE output acknowledging the denied capabilities;
- responding to an LSP `shutdown` request;
- cleanly terminating upon an LSP `exit` notification from the environment host.

An abstract server application in the SDK, [RDCoreServerApp](../api/RDCore.SDK.Server.RDCoreServerApp.html), should handle these lifecycle events correctly, without further configuration.

|Capability registration|Outcome|
|---|---|
|The extension registers at least one capability.|Its process continues to run, and may handle a reduced set of LSP requests and notifications.|
|The extension registers no capability.|The environment host requests the termination of the extension server process.|

## RDCore Platform Cloud Infrastructure

First-party and third-party extensions distributed through the **RDCore Platform Cloud Infrastructure** may use a capability provider. That capability provider may validate the availability of certain advanced capabilities by:

- requiring 2FA authentication;
- validating an active subscription (free or paid);
- validating the signed build against the certified distribution channel build.

Platform capabilities and their exchange between processes are described in [**RD-VBAL §2.0.2** Client/Server Capabilities](rd-vbal.2.0.2.client-server-capabilities.md).
