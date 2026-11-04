using RDCore.SDK.Client;

namespace RDCore.SDK.Platform.Protocol;

/// <summary>
/// Request for <c>rdcore/platform/initialize</c> (<see cref="RDCorePlatformProtocol.PlatformInitialize"/>), the non-LSP handshake that follows LSP's
/// <c>initialize</c>/<c>initialized</c> exchange and keeps the LSP layer pure LSP: the caller tells the component it connected to what it expects, once LSP's <c>initialize</c> is answered;
/// answered a <see cref="PlatformInitializeResult"/>. A client of the language server sends it as the platform's own components do of one another.
/// </summary>
public record class PlatformInitializeParams
{
    /// <summary>
    /// The component the caller believes it is talking to.
    /// </summary>
    public CoreServerComponent ExpectedComponent { get; init; }

    /// <summary>
    /// The platform capabilities the caller expects the component to provide.
    /// </summary>
    public CorePlatformClientCapabilities Expected { get; init; } = new();
}

/// <summary>
/// Response: what the component actually is and provides.
/// </summary>
public record class PlatformInitializeResult
{
    /// <summary>
    /// The component's own type.
    /// </summary>
    public CoreServerComponent Component { get; init; }

    /// <summary>
    /// Names of the capability types the component provides, as its assembly declares them (e.g. <c>"ParseFullDocument"</c>).
    /// </summary>
    public IReadOnlyList<string> Provided { get; init; } = [];

    /// <summary>
    /// Whether the component provides a capability of the given type.
    /// </summary>
    /// <typeparam name="TCapability">The capability record type to test for.</typeparam>
    /// <returns><see langword="true"/> when <see cref="Provided"/> names it.</returns>
    public bool Provides<TCapability>() where TCapability : CorePlatformClientCapability
        => Provided.Contains(typeof(TCapability).Name);
}
