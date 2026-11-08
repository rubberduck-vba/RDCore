using MediatR;
using OmniSharp.Extensions.JsonRpc;
using RDCore.SDK.Client;
using NewtonsoftConverter = Newtonsoft.Json.JsonConverterAttribute;

namespace RDCore.SDK.Platform.Protocol;

// the rdcore/formatting/indenterSettings requests the language server sends a client, as the platform's JSON-RPC handlers and senders know them. Their
// Protocol DTOs have an enumeration written by name and JSON values, which only System.Text.Json writes as the wire has them: the wrappers that reach the
// wire are written through it (SystemTextJsonBridge).

/// <summary><see cref="RubberduckIndenterSettingsParams"/> as a request of <c>rdcore/formatting/indenterSettings</c>.</summary>
[Method(RDCorePlatformProtocol.RubberduckIndenterSettings, Direction.ServerToClient)]
public sealed record class RubberduckIndenterSettingsRequest : RubberduckIndenterSettingsParams, IRequest<RubberduckIndenterSettingsReply>;

/// <summary>
/// <see cref="RubberduckIndenterSettingsResult"/> as the answer to <see cref="RubberduckIndenterSettingsRequest"/>.
/// </summary>
[NewtonsoftConverter(typeof(SystemTextJsonBridge<RubberduckIndenterSettingsReply>))]
public sealed record class RubberduckIndenterSettingsReply : RubberduckIndenterSettingsResult
{
    /// <summary>An answer that found nothing.</summary>
    public RubberduckIndenterSettingsReply() { }

    /// <summary>An answer of what a client read.</summary>
    /// <param name="read">What the client read.</param>
    public RubberduckIndenterSettingsReply(RubberduckIndenterSettingsResult read) : base(read) { }
}

/// <summary><see cref="RubberduckIndenterSettingsApplyParams"/> as a request of <c>rdcore/formatting/indenterSettings/apply</c>.</summary>
[Method(RDCorePlatformProtocol.RubberduckIndenterSettingsApply, Direction.ServerToClient)]
[NewtonsoftConverter(typeof(SystemTextJsonBridge<RubberduckIndenterSettingsApplyRequest>))]
public sealed record class RubberduckIndenterSettingsApplyRequest : RubberduckIndenterSettingsApplyParams, IRequest<RubberduckIndenterSettingsApplyResult>
{
    /// <summary>A request with no settings.</summary>
    public RubberduckIndenterSettingsApplyRequest() { }

    /// <summary>A request of the settings an import mapped.</summary>
    /// <param name="settings">The settings.</param>
    public RubberduckIndenterSettingsApplyRequest(RubberduckIndenterSettingsApplyParams settings) : base(settings) { }
}
