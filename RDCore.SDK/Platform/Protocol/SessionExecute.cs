using MediatR;
using OmniSharp.Extensions.JsonRpc;
using RDCore.SDK.Client;
using RDCore.SDK.Model.AST;

namespace RDCore.SDK.Platform.Protocol;

/// <summary>
/// <see cref="SessionExecuteParams"/> as the platform's JSON-RPC handlers and senders know it: the method it is a request of, and what it is answered with.
/// </summary>
[Method(RDCorePlatformProtocol.SessionExecute, Direction.ClientToServer)]
public sealed record class SessionExecuteRequest : SessionExecuteParams, IRequest, IRequest<ExecuteResult>;

/// <summary>
/// Request for <c>rdcore/host/execute</c>: the language-server side of
/// <see cref="SessionExecuteParams"/>, addressed to the component that owns the runtime session.
/// </summary>
/// <remarks>
/// The parsed module rides a <see cref="System.Text.Json"/> string for the same reason
/// <see cref="DiagnoseDocumentParams"/>'s does: the AST is polymorphic and the JSON-RPC
/// transport's own serializer cannot round-trip it (see <see cref="PlatformJson"/>). The language
/// server has already parsed and defined the module's symbols by the time it sends this, so the host
/// lowers and runs rather than re-deriving any of it.
/// </remarks>
[Method(RDCoreComponentProtocol.HostExecute, Direction.ClientToServer)]
public record class HostExecuteParams : IRequest, IRequest<ExecuteResult>
{
    /// <summary>
    /// The <see cref="System.Text.Json"/> representation of a <see cref="HostExecutePayload"/>.
    /// </summary>
    public string Json { get; init; } = string.Empty;

    /// <summary>
    /// The module's programmatic name: the scope <see cref="EntryPoint"/> is resolved in.
    /// </summary>
    public string ModuleName { get; init; } = string.Empty;

    /// <summary>
    /// The name of the parameterless procedure to invoke.
    /// </summary>
    public string EntryPoint { get; init; } = string.Empty;

    /// <summary>
    /// Whether the program runs under a debugger. See <see cref="SessionExecuteParams.Debug"/>.
    /// </summary>
    public bool Debug { get; init; }

    /// <summary>
    /// Whether the entry point is a statement typed at a prompt. See <see cref="SessionExecuteParams.Immediate"/>.
    /// </summary>
    public bool Immediate { get; init; }

    /// <summary>
    /// Whether the lines the program prints are said as they are printed (<see cref="HostOutputNotification"/>) instead of with the answer, for the whole of the run:
    /// the requests that resume the program too. Only a program run under a debugger streams.
    /// </summary>
    public bool StreamOutput { get; init; }

    /// <summary>
    /// Whether the program runs unattended (<see cref="Runtime.Abstract.Execution.IRuntimeSession.RunsUnattended"/>): nobody is there to answer a dialog it opens,
    /// which fails the run. Ignored for a statement run alongside a program that waits, which runs as that program does.
    /// </summary>
    public bool Unattended { get; init; }
}

/// <summary>
/// The <see cref="System.Text.Json"/> payload carried in <see cref="HostExecuteParams.Json"/>.
/// </summary>
/// <param name="DocumentUri">The document the module was parsed from.</param>
/// <param name="ParseResult">The parsed module: AST plus syntax errors.</param>
public record class HostExecutePayload(Uri DocumentUri, ModuleParseResult ParseResult);
