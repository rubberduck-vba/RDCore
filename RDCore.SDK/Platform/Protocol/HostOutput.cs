using MediatR;
using OmniSharp.Extensions.JsonRpc;
using RDCore.SDK.Client;

namespace RDCore.SDK.Platform.Protocol;

/// <summary>
/// Notification <c>rdcore/host/output</c>: lines a program printed, said as they are printed instead of with the answer to the request that runs it.
/// </summary>
/// <remarks>
/// Sent by the environment host for a program run with <see cref="HostExecuteParams.StreamOutput"/>: a debugger shows what a program prints while it runs, and a program
/// that waits at a stop has printed what it printed up to there. A line is said when it is complete; one the program leaves open (a trailing <c>;</c>) is in the answer
/// to the request, with <see cref="ExecuteSessionResult.Output"/>, since there is no telling that it is the last one.
/// <para>
/// A notification and a response travel apart, and the receiver does not see them in the order they were sent. <see cref="Total"/> and
/// <see cref="ExecuteSessionResult.StreamedLines"/> count the lines the host has said since it started, so that the one who got the response can tell whether it got all
/// of the lines that came before it.
/// </para>
/// </remarks>
[Method(RDCorePlatformProtocol.HostOutput, Direction.ServerToClient)]
public record class HostOutputNotification : IRequest
{
    /// <summary>The lines, in the order they were printed, each without its line terminator.</summary>
    public IReadOnlyList<string> Lines { get; init; } = [];

    /// <summary>How many lines the host has said in all, counting these.</summary>
    public long Total { get; init; }
}
