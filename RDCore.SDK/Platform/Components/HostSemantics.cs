using MediatR;
using OmniSharp.Extensions.JsonRpc;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Extensibility.Diagnostics;

namespace RDCore.SDK.Platform.Components;

/// <summary>
/// Request for <c>rdcore/host/semantics</c>: the language server asks the environment host what the static pass found out about the code it holds.
/// </summary>
/// <remarks>
/// The environment host runs the semantic analysis pass (<strong>RD-VBAL §5.0.3</strong>) when it loads the code of a module, and keeps the model of
/// each; this is the language-server side of asking for them, never sent by a client. The answer is the semantic facts, which a diagnostics extension
/// analyzes: the host says what is, and an analyzer decides what is worth saying. A module the host does not have, or has not loaded, has no model.
/// <para>
/// Part of the component protocol (<see cref="RDCoreComponentProtocol"/>), outside the SDK's compatibility promise.
/// </para>
/// </remarks>
[Method(RDCoreComponentProtocol.HostSemantics, Direction.ClientToServer)]
public record class HostSemanticsParams : IRequest, IRequest<HostSemanticsResult>
{
    /// <summary>
    /// The name of the module whose model is asked for, or empty for the model of every module of the workspace.
    /// </summary>
    public string ModuleName { get; init; } = string.Empty;

    /// <summary>
    /// What the answer waits for. <see cref="AnalysisPhase.Static"/> is answered at once with whatever the host has: the facts that are ready when the module
    /// is loaded, and the ones that come later if they happen to be there. Anything that includes <see cref="AnalysisPhase.Runtime"/> is not answered until
    /// the host has evaluated the code of the modules asked for, or the request is cancelled.
    /// </summary>
    public AnalysisPhase Phase { get; init; } = AnalysisPhase.All;
}

/// <summary>
/// The models the host answers with.
/// </summary>
/// <remarks>
/// The models ride a <see cref="System.Text.Json"/> string (<see cref="PlatformJson"/>): the transport's own serializer does not round-trip what they hold.
/// <para>
/// Part of the component protocol (<see cref="RDCoreComponentProtocol"/>), outside the SDK's compatibility promise.
/// </para>
/// </remarks>
public record class HostSemanticsResult
{
    /// <summary>
    /// The <see cref="System.Text.Json"/> representation of a <see cref="SemanticsPayload"/>.
    /// </summary>
    public string Json { get; init; } = string.Empty;
}
