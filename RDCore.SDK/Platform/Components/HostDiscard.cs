using MediatR;
using OmniSharp.Extensions.JsonRpc;
using RDCore.SDK.Client;
using RDCore.SDK.Platform.Protocol;

namespace RDCore.SDK.Platform.Components;

/// <summary>
/// Request for <c>rdcore/host/discard</c>: the language-server side of <see cref="SessionDiscardParams"/>, addressed to the component that owns the
/// session.
/// </summary>
/// <remarks>
/// Part of the component protocol (<see cref="RDCoreComponentProtocol"/>), outside the SDK's compatibility promise.
/// </remarks>
[Method(RDCoreComponentProtocol.HostDiscard, Direction.ClientToServer)]
public record class HostDiscardParams : IRequest, IRequest<DiscardResult>
{
    /// <summary>
    /// The address of the module symbol: the one the language server defined the module under, which it derives once and for every request about a
    /// module. A module that is not the project's - a document the client opened - has no symbol of its own to find by name, and still has
    /// what it declared.
    /// </summary>
    public Uri ModuleUri { get; init; } = default!;

    /// <summary>
    /// The programmatic name of the module.
    /// </summary>
    public string ModuleName { get; init; } = string.Empty;

    /// <summary>
    /// Whether the program that is running or waits is ended, and the session wiped, before the module goes. See <see cref="SessionDiscardParams.EndProgram"/>.
    /// </summary>
    public bool EndProgram { get; init; }
}
