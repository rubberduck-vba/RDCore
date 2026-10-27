using Newtonsoft.Json.Linq;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.LanguageServer.Refactoring;
using RDCore.SDK.Semantics.Refactoring;
using RDCore.SDK.Server.ProtocolExtensions;
using Range = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace RDCore.LanguageServer.Navigation;

/// <summary>
/// What an item of a hierarchy is on the wire: its name, its kind, the module that declares it, where it is declared - and, as its <c>data</c>, the identity
/// of its symbol, which the client sends back to ask for its calls or its types.
/// </summary>
internal static class HierarchyItems
{
    public static (string Name, SymbolKind Kind, string? Detail, DocumentUri Uri, Range Range, Range SelectionRange, JToken Data)? Describe(
        RenameWorkspace workspace, HierarchyItem item)
    {
        if (workspace.Locate(item) is not { } located)
        {
            return null;
        }

        var (uri, range, selection) = located;
        var path = item.Symbol.Uri.Fragment.TrimStart('#');
        var dot = path.IndexOf('.');
        return (item.Symbol.Name, item.Symbol.Kind.ToLsp(), dot > 0 ? path[..dot] : null, uri, range, selection, JToken.FromObject(item.Symbol.Uri.AbsoluteUri));
    }

    // the item a client sent back, as the workspace declares it now.
    public static HierarchyItem? ItemOf(RenameWorkspace workspace, JToken? data)
        => data?.Type == JTokenType.String && Uri.TryCreate(data.Value<string>(), UriKind.Absolute, out var symbol) ? workspace.HierarchyItemOf(symbol) : null;

    public static CallHierarchyItem? ToCallItem(RenameWorkspace workspace, HierarchyItem item)
        => Describe(workspace, item) is { } described
            ? new CallHierarchyItem
            {
                Name = described.Name, Kind = described.Kind, Detail = described.Detail, Uri = described.Uri,
                Range = described.Range, SelectionRange = described.SelectionRange, Data = described.Data,
            }
            : null;

    public static TypeHierarchyItem? ToTypeItem(RenameWorkspace workspace, HierarchyItem item)
        => Describe(workspace, item) is { } described
            ? new TypeHierarchyItem
            {
                Name = described.Name, Kind = described.Kind, Detail = described.Detail, Uri = described.Uri,
                Range = described.Range, SelectionRange = described.SelectionRange, Data = described.Data,
            }
            : null;
}

/// <summary>
/// Serves <c>textDocument/prepareCallHierarchy</c> (<strong>LSP 3.17</strong> §Prepare Call Hierarchy Request): the procedure named at a position, as an item
/// of the call hierarchy (<see cref="SymbolHierarchy.PrepareCalls"/>).
/// </summary>
internal sealed class CallHierarchyPrepareHandler(IRenameService workspace) : CallHierarchyPrepareHandlerBase
{
    public override async Task<Container<CallHierarchyItem>?> Handle(CallHierarchyPrepareParams request, CancellationToken cancellationToken)
        => await workspace.ComposeAsync(cancellationToken) is { } composed
            && composed.PrepareHierarchy(request.TextDocument.Uri.ToUri(), request.Position, types: false) is { } item
            && HierarchyItems.ToCallItem(composed, item) is { } prepared
            ? new Container<CallHierarchyItem>(prepared)
            : null;

    protected override CallHierarchyRegistrationOptions CreateRegistrationOptions(CallHierarchyCapability capability, ClientCapabilities clientCapabilities) => new();
}

/// <summary>
/// Serves <c>callHierarchy/incomingCalls</c> (<strong>LSP 3.17</strong> §Call Hierarchy Incoming Calls): the procedures that call an item, each with where in
/// it the calls are written (<see cref="SymbolHierarchy.IncomingCalls"/>).
/// </summary>
internal sealed class CallHierarchyIncomingHandler(IRenameService workspace) : CallHierarchyIncomingHandlerBase
{
    public override async Task<Container<CallHierarchyIncomingCall>?> Handle(CallHierarchyIncomingCallsParams request, CancellationToken cancellationToken)
    {
        if (await workspace.ComposeAsync(cancellationToken) is not { } composed || HierarchyItems.ItemOf(composed, request.Item.Data) is not { } item)
        {
            return new Container<CallHierarchyIncomingCall>();
        }

        return new Container<CallHierarchyIncomingCall>(composed.IncomingCalls(item)
            .Select(call => (Call: call, From: HierarchyItems.ToCallItem(composed, call.Item)))
            .Where(found => found.From is not null)
            .Select(found => new CallHierarchyIncomingCall
            {
                From = found.From!,
                FromRanges = new Container<Range>(found.Call.Ranges.Select(range => composed.InTextOf(found.Call.Item, range))),
            }));
    }
}

/// <summary>
/// Serves <c>callHierarchy/outgoingCalls</c> (<strong>LSP 3.17</strong> §Call Hierarchy Outgoing Calls): what an item calls, each with where in the item the
/// calls are written (<see cref="SymbolHierarchy.OutgoingCalls"/>).
/// </summary>
internal sealed class CallHierarchyOutgoingHandler(IRenameService workspace) : CallHierarchyOutgoingHandlerBase
{
    public override async Task<Container<CallHierarchyOutgoingCall>?> Handle(CallHierarchyOutgoingCallsParams request, CancellationToken cancellationToken)
    {
        if (await workspace.ComposeAsync(cancellationToken) is not { } composed || HierarchyItems.ItemOf(composed, request.Item.Data) is not { } item)
        {
            return new Container<CallHierarchyOutgoingCall>();
        }

        return new Container<CallHierarchyOutgoingCall>(composed.OutgoingCalls(item)
            .Select(call => (Call: call, To: HierarchyItems.ToCallItem(composed, call.Item)))
            .Where(found => found.To is not null)
            .Select(found => new CallHierarchyOutgoingCall
            {
                To = found.To!,
                FromRanges = new Container<Range>(found.Call.Ranges.Select(range => composed.InTextOf(item, range))),
            }));
    }
}

/// <summary>
/// Serves <c>textDocument/prepareTypeHierarchy</c> (<strong>LSP 3.17</strong> §Prepare Type Hierarchy Request): the class named at a position, as an item of
/// the type hierarchy (<see cref="SymbolHierarchy.PrepareTypes"/>).
/// </summary>
internal sealed class TypeHierarchyPrepareHandler(IRenameService workspace) : TypeHierarchyPrepareHandlerBase
{
    public override async Task<Container<TypeHierarchyItem>?> Handle(TypeHierarchyPrepareParams request, CancellationToken cancellationToken)
        => await workspace.ComposeAsync(cancellationToken) is { } composed
            && composed.PrepareHierarchy(request.TextDocument.Uri.ToUri(), request.Position, types: true) is { } item
            && HierarchyItems.ToTypeItem(composed, item) is { } prepared
            ? new Container<TypeHierarchyItem>(prepared)
            : null;

    protected override TypeHierarchyRegistrationOptions CreateRegistrationOptions(TypeHierarchyCapability capability, ClientCapabilities clientCapabilities) => new();
}

/// <summary>
/// Serves <c>typeHierarchy/supertypes</c> (<strong>LSP 3.17</strong> §Type Hierarchy Supertypes): the classes of the workspace an item's class implements.
/// </summary>
internal sealed class TypeHierarchySupertypesHandler(IRenameService workspace) : TypeHierarchySupertypesHandlerBase
{
    public override async Task<Container<TypeHierarchyItem>?> Handle(TypeHierarchySupertypesParams request, CancellationToken cancellationToken)
        => await workspace.ComposeAsync(cancellationToken) is { } composed && HierarchyItems.ItemOf(composed, request.Item.Data) is { } item
            ? new Container<TypeHierarchyItem>(composed.Supertypes(item).Select(type => HierarchyItems.ToTypeItem(composed, type)).OfType<TypeHierarchyItem>())
            : new Container<TypeHierarchyItem>();
}

/// <summary>
/// Serves <c>typeHierarchy/subtypes</c> (<strong>LSP 3.17</strong> §Type Hierarchy Subtypes): the classes of the workspace that implement an item's class.
/// </summary>
internal sealed class TypeHierarchySubtypesHandler(IRenameService workspace) : TypeHierarchySubtypesHandlerBase
{
    public override async Task<Container<TypeHierarchyItem>?> Handle(TypeHierarchySubtypesParams request, CancellationToken cancellationToken)
        => await workspace.ComposeAsync(cancellationToken) is { } composed && HierarchyItems.ItemOf(composed, request.Item.Data) is { } item
            ? new Container<TypeHierarchyItem>(composed.Subtypes(item).Select(type => HierarchyItems.ToTypeItem(composed, type)).OfType<TypeHierarchyItem>())
            : new Container<TypeHierarchyItem>();
}
