using MediatR;
using OmniSharp.Extensions.JsonRpc;
using RDCore.SDK.Client;

namespace RDCore.SDK.Platform.Protocol;

/// <summary>
/// Request for <c>rdcore/host/project/reload</c>: the language server asks the component that owns the runtime session to compose it again from the project
/// file as it is now (<see cref="ProjectReload"/>).
/// </summary>
/// <remarks>
/// <para>
/// What a client that rewrote the <c>.rdproj</c> is given: the modules it lists now, a class among them, are the session's, as they are when the session is
/// composed at start-up. The session is composed anew, empty of what was defined in it: the language server defines the whole workspace again afterwards,
/// in its three phases.
/// </para>
/// <para>
/// A program that is running or waits keeps the session it runs in, and nothing is composed (<see cref="HostProjectReloadResult.IsProgramRunning"/>).
/// </para>
/// </remarks>
[Method(RDCorePlatformProtocol.HostProjectReload, Direction.ClientToServer)]
public record class HostProjectReloadParams : IRequest, IRequest<HostProjectReloadResult>
{
    /// <summary>
    /// The root of the workspace: the folder the project file is in.
    /// </summary>
    public Uri WorkspaceRoot { get; init; } = default!;
}

/// <summary>
/// What composing the runtime session again did.
/// </summary>
public record class HostProjectReloadResult
{
    /// <summary>
    /// Whether the session was composed again, from the project file as it is now.
    /// </summary>
    public bool Composed { get; init; }

    /// <summary>
    /// Whether nothing was composed because a program is running or waits in the session.
    /// </summary>
    public bool IsProgramRunning { get; init; }

    /// <summary>
    /// The number of modules the project lists now, when the session was composed again.
    /// </summary>
    public int ModuleCount { get; init; }
}
