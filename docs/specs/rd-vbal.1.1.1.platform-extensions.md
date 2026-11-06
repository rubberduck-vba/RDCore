# 1.1.1 Platform Extensions

> 🧩 RDCore operates on a **capability-driven host model**: extended features may or may not be available, depending on the execution environment. Extensions must be resilient to partial capability availability.

The *platform* is intended to be extended extensively through first-party and third-party extensions. The capabilities of platform extensions are negotiated with the *RD-VBA environment host*; see [**RD-VBAL §2.0.2** Client/Server Capabilities](rd-vbal.2.0.2.client-server-capabilities.md).

> [!NOTE]
> In the RDCore ecosystem, the default RD-VBA environment host is `rdc.exe` (see [**RD-VBAL §2.0** RD-VBA Computational Environment](rd-vbal.2.0.computational-environment.md)). An LSP client other than `rdc.exe` acting as an RD-VBA environment host would be packaged separately from the RDCore language platform.

The examples of invalid *language core* extensions in [**RD-VBAL §1.1.2** Language Core Extensions](rd-vbal.1.1.2.language-core-extensions.md) are not invalid extensions in the RDCore ecosystem: each of them could be a platform-level extension.

🎯 VB6 ActiveX designer features are out of scope for the RDCore language core. See [**RD-VBAL §3.1.1** Attributes](rd-vbal.3.1.1.attributes.md).

How the environment host discovers, validates and grants capabilities to an extension is described in:

- [**RD-VBAL §1.1.5** Extension Manifest](rd-vbal.1.1.5.extension-manifest.md);
- [**RD-VBAL §1.1.6** Capabilities Provider](rd-vbal.1.1.6.capabilities-provider.md).
