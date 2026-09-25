using RDCore.SDK.Client;

using RDCore.SDK.Platform.Protocol;

namespace RDCore.SDK.Server.Services;

/// <summary>
/// Remembers what the connected client asked this server for in the
/// <c>rdcore/platform/initialize</c> handshake, so handlers can act on it.
/// </summary>
/// <remarks>
/// The handshake has always been two-way on the wire — the caller sends
/// <see cref="PlatformInitializeParams.Expected"/>, the callee answers with what it provides — but
/// only the answer was ever used. A server that serves optional, client-driven request families
/// (an interactive shell's, say) needs the question too: a request for something the client never
/// advertised is a client bug, and refusing it is how the client finds out. Every RDCore server gets
/// one of these; whether it consults it is the server's own business.
/// </remarks>
public interface IPlatformClientCapabilitiesService
{
    /// <summary>
    /// Whether the platform handshake has completed. Every other member reports the neutral "nothing
    /// expected" answer until it has.
    /// </summary>
    bool IsHandshakeCompleted { get; }

    /// <summary>
    /// The component the connected client believes it is talking to, or <c>null</c> before the handshake.
    /// </summary>
    CoreServerComponent? ExpectedComponent { get; }

    /// <summary>
    /// The capabilities the connected client expects this server to provide.
    /// </summary>
    CorePlatformClientCapabilities Expected { get; }

    /// <summary>
    /// Whether the connected client expects a specific language-server capability.
    /// </summary>
    /// <param name="capability">Selects the capability from the language-server group, e.g. <c>group =&gt; group.SessionStatus</c>.</param>
    /// <returns>
    /// <c>false</c> before the handshake, when the client sent no language-server expectations at
    /// all, or when it sent that capability as unsupported.
    /// </returns>
    bool Expects(Func<LanguageServerCapabilities, CorePlatformClientCapability> capability);

    /// <summary>
    /// Records the handshake request. Called by <c>PlatformInitializeHandler</c>; a later handshake
    /// on the same connection replaces the earlier one.
    /// </summary>
    /// <param name="request">The handshake request as received.</param>
    void Record(PlatformInitializeParams request);
}

/// <inheritdoc cref="IPlatformClientCapabilitiesService"/>
public sealed class PlatformClientCapabilitiesService : IPlatformClientCapabilitiesService
{
    private PlatformInitializeParams? _handshake;

    /// <inheritdoc/>
    public bool IsHandshakeCompleted => _handshake is not null;

    /// <inheritdoc/>
    public CoreServerComponent? ExpectedComponent => _handshake?.ExpectedComponent;

    /// <inheritdoc/>
    public CorePlatformClientCapabilities Expected => _handshake?.Expected ?? new();

    /// <inheritdoc/>
    public bool Expects(Func<LanguageServerCapabilities, CorePlatformClientCapability> capability)
        => Expected.LanguageServer is { } group && capability(group).IsSupported;

    /// <inheritdoc/>
    public void Record(PlatformInitializeParams request) => _handshake = request;
}
