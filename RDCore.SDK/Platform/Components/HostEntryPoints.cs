using MediatR;
using OmniSharp.Extensions.JsonRpc;
using RDCore.SDK.Client;
using RDCore.SDK.Workspace;
using RDCore.SDK.Platform.Protocol;

namespace RDCore.SDK.Platform.Components;

/// <summary>
/// Request for <c>rdcore/host/entryPoints</c>: the language server asks the environment host which of a program's modules declare a procedure it can start
/// with (<see cref="ProgramEntryPoint"/>).
/// </summary>
/// <remarks>
/// The host answers from what its runtime session defined, which is what a run runs: a declaration is the one the live branch of its <c>#If</c> block writes,
/// under the project's constants, which only the host evaluates. A module the session does not have declares none.
/// <para>
/// Part of the component protocol (<see cref="RDCoreComponentProtocol"/>), outside the SDK's compatibility promise.
/// </para>
/// </remarks>
[Method(RDCoreComponentProtocol.HostEntryPoints, Direction.ClientToServer)]
public record class HostEntryPointsParams : IRequest, IRequest<HostEntryPointsResult>
{
    /// <summary>
    /// The name of the procedure the program starts with.
    /// </summary>
    public string EntryPoint { get; init; } = ProgramEntryPoint.Name;

    /// <summary>
    /// The modules of the program, by programmatic name: the ones the answer is of.
    /// </summary>
    public string[] Modules { get; init; } = [];
}

/// <summary>
/// The modules that declare the entry point.
/// </summary>
/// <remarks>
/// Part of the component protocol (<see cref="RDCoreComponentProtocol"/>), outside the SDK's compatibility promise.
/// </remarks>
public record class HostEntryPointsResult
{
    /// <summary>
    /// The modules asked about that declare a procedure the program can start with, by programmatic name, in the order they were asked about.
    /// </summary>
    public string[] Modules { get; init; } = [];
}
