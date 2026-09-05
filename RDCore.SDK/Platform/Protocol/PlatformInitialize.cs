using MediatR;
using OmniSharp.Extensions.JsonRpc;
using RDCore.SDK.Client;
using RDCore.SDK.Client.Connection;

namespace RDCore.SDK.Platform.Protocol;

/// <summary>
/// The non-LSP handshake that follows the LSP <c>initialize</c>/<c>initialized</c> exchange on a
/// platform connection: advertised capabilities become <em>provided</em> ones. Keeps the LSP layer pure LSP.
/// </summary>
public static class RDCorePlatformHandshake
{
    public const string Method = "rdcore/platform/initialize";
}

/// <summary>Request: the caller tells the child what it expects; sent by <see cref="ChildConnection"/> after LSP init.</summary>
[Method(RDCorePlatformHandshake.Method, Direction.ClientToServer)]
public record class PlatformInitializeParams : IRequest, IRequest<PlatformInitializeResult>
{
    /// <summary>The component the caller believes it is talking to.</summary>
    public CoreServerComponent ExpectedComponent { get; init; }
    /// <summary>The platform capabilities the caller expects the child to provide.</summary>
    public CorePlatformClientCapabilities Expected { get; init; } = new();
}

/// <summary>Response: what the child actually is and provides.</summary>
public record class PlatformInitializeResult
{
    /// <summary>The child's own component type.</summary>
    public CoreServerComponent Component { get; init; }
    /// <summary>
    /// Names of the capability types the child provides, reflected from its
    /// <c>[assembly: ProvidesCorePlatformClientCapability&lt;T&gt;]</c> attributes (e.g. <c>"ParseFullDocument"</c>).
    /// </summary>
    public IReadOnlyList<string> Provided { get; init; } = [];

    /// <summary>Whether the child provides a capability of the given type.</summary>
    public bool Provides<TCapability>() where TCapability : CorePlatformClientCapability
        => Provided.Contains(typeof(TCapability).Name);
}
