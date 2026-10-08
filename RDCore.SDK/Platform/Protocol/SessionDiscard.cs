using MediatR;
using OmniSharp.Extensions.JsonRpc;
using RDCore.SDK.Client;

namespace RDCore.SDK.Platform.Protocol;

/// <summary>
/// Request for <c>rdcore/session/discard</c>: a client asks the language server to take a module it supplied out of the platform's runtime
/// session.
/// </summary>
/// <remarks>
/// <para>
/// Running a module defines it in the session, and defining it again replaces what it declares - it never removes what it no longer does. The
/// variables a program made therefore outlive the program, and the storage with them. A client that clears its program asks for the module to
/// go, and the next run defines it afresh: <c>NEW</c> and <c>LOAD</c> of a shell, and <c>RUN</c>, which in BASIC clears the variables.
/// </para>
/// <para>
/// The module symbol itself stays: it is the project's, not the code's.
/// </para>
/// </remarks>
[Method(RDCorePlatformProtocol.SessionDiscard, Direction.ClientToServer)]
public record class DiscardSessionParams : IRequest, IRequest<DiscardSessionResult>
{
    /// <summary>
    /// The programmatic name of the module, as it was named when it was run.
    /// </summary>
    public string ModuleName { get; init; } = string.Empty;
}

/// <summary>
/// Request for <c>rdcore/host/discard</c>: the language-server side of <see cref="DiscardSessionParams"/>, addressed to the component that owns the
/// session.
/// </summary>
[Method(RDCorePlatformProtocol.HostDiscard, Direction.ClientToServer)]
public record class HostDiscardParams : IRequest, IRequest<DiscardSessionResult>
{
    /// <summary>
    /// The programmatic name of the module. The host finds the module by it, as it does for a request to run one, so that the address the module
    /// was defined under is the host's to say.
    /// </summary>
    public string ModuleName { get; init; } = string.Empty;
}

/// <summary>
/// What taking a module out of the session took out. The same result answers both hops of the request.
/// </summary>
public record class DiscardSessionResult
{
    /// <summary>
    /// The number of symbols that were undefined: the module's members, and what they declare. Zero when the module declared none, or the session
    /// has no such module.
    /// </summary>
    public int Discarded { get; init; }
}
