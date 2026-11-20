using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.LanguageServer.Diagnostics;
using RDCore.LanguageServer.Refactoring;
using RDCore.LanguageServer.Symbols;
using RDCore.LanguageServer.Workspace.Services;
using RDCore.SDK.Assistant;
using RDCore.SDK.Extensibility.Diagnostics;
using RDCore.SDK.Server.Assistant;
using System.Globalization;
using System.Text;

namespace RDCore.LanguageServer.Assistant;

// the tools that read the workspace: each a thin projection of what a handler of the same request answers, with 1-based positions and paths from the
// workspace root.

internal sealed class GetDiagnosticsTool(AssistantDocuments documents, IDocumentDiagnosticsService diagnostics, IDocumentLifecycleService lifecycle)
    : AssistantTool<DiagnosticsToolArguments, DiagnosticsToolResult>
{
    public override string Name => CoreAssistantTools.GetDiagnostics;

    protected override ToolAnnotations Annotations => new() { ReadOnlyHint = true, IdempotentHint = true };

    protected override string? TitleOf(CultureInfo culture) => AssistantToolTexts.ResourceManager.GetString(nameof(AssistantToolTexts.GetDiagnostics_Title), culture);

    protected override string DescriptionOf(CultureInfo culture) => AssistantToolTexts.ResourceManager.GetString(nameof(AssistantToolTexts.GetDiagnostics_Description), culture)!;

    protected override string? ArgumentDescriptionOf(string argument, CultureInfo culture)
        => argument == "path" ? AssistantToolTexts.ResourceManager.GetString(nameof(AssistantToolTexts.GetDiagnostics_Path), culture) : null;

    protected override async Task<DiagnosticsToolResult> RunAsync(DiagnosticsToolArguments arguments, CancellationToken token)
    {
        // what the workspace holds is read once it is loaded: a document not parsed yet has no findings to give.
        await documents.LoadedAsync(token);

        // a pull that comes as documents are edited waits for the host to have the edits, as the client's own pulls do.
        var targets = arguments.Path is { } path ? [documents.Resolve(path)] : documents.All.ToList();
        if (arguments.Path is null)
        {
            await lifecycle.WhenAllSyncedAsync(token);
        }
        else
        {
            await lifecycle.WhenSyncedAsync(targets[0].Id.Uri.ToUri(), token);
        }

        var found = new List<ToolDiagnostic>();
        foreach (var document in targets)
        {
            var result = await diagnostics.GetAsync(document.Id.Uri.ToUri(), previousResultId: null, token);
            found.AddRange(result.Diagnostics
                .OrderBy(diagnostic => diagnostic.Range.Start.Line)
                .ThenBy(diagnostic => diagnostic.Range.Start.Character)
                .Select(diagnostic => documents.Project(document.Id.Uri, diagnostic)));
        }

        var capped = ToolResultLimits.Cap(found, ToolResultLimits.Diagnostics);
        return new DiagnosticsToolResult { Diagnostics = capped.Items, Total = capped.Total, Truncated = capped.Truncated };
    }

    protected override string Render(DiagnosticsToolResult result, CultureInfo culture)
    {
        if (result.Total == 0)
        {
            return AssistantToolTexts.ResourceManager.GetString(nameof(AssistantToolTexts.NoDiagnostics), culture)!;
        }

        var fix = AssistantToolTexts.ResourceManager.GetString(nameof(AssistantToolTexts.FixAvailable), culture);
        var text = new StringBuilder();
        foreach (var diagnostic in result.Diagnostics)
        {
            text.Append(CultureInfo.InvariantCulture, $"{diagnostic.Location} {diagnostic.Code} {diagnostic.Severity}: {diagnostic.Message}");
            text.AppendLine(diagnostic.HasFix ? $" ({fix})" : string.Empty);
        }

        return ReadToolText.End(text, new CappedList<ToolDiagnostic>(result.Diagnostics, result.Total), culture);
    }
}

internal sealed class FindSymbolTool(AssistantDocuments documents, IWorkspaceSymbolSearch search) : AssistantTool<FindSymbolToolArguments, SymbolsToolResult>
{
    public override string Name => CoreAssistantTools.FindSymbol;

    protected override ToolAnnotations Annotations => new() { ReadOnlyHint = true, IdempotentHint = true };

    protected override string? TitleOf(CultureInfo culture) => AssistantToolTexts.ResourceManager.GetString(nameof(AssistantToolTexts.FindSymbol_Title), culture);

    protected override string DescriptionOf(CultureInfo culture) => AssistantToolTexts.ResourceManager.GetString(nameof(AssistantToolTexts.FindSymbol_Description), culture)!;

    protected override string? ArgumentDescriptionOf(string argument, CultureInfo culture)
        => argument == "query" ? AssistantToolTexts.ResourceManager.GetString(nameof(AssistantToolTexts.FindSymbol_Query), culture) : null;

    protected override async Task<SymbolsToolResult> RunAsync(FindSymbolToolArguments arguments, CancellationToken token)
    {
        await documents.LoadedAsync(token);
        var found = await search.SearchAsync(arguments.Query, token);
        var capped = ToolResultLimits.Cap(found.Select(entry => new ToolSymbol
        {
            Name = entry.Name,
            Kind = entry.Kind.ToString(),
            Container = entry.ContainerName,
            Location = documents.Locate(entry.Uri, entry.SelectionRange),
        }), ToolResultLimits.Symbols);
        return new SymbolsToolResult { Symbols = capped.Items, Total = capped.Total, Truncated = capped.Truncated };
    }

    protected override string Render(SymbolsToolResult result, CultureInfo culture)
    {
        if (result.Total == 0)
        {
            return AssistantToolTexts.ResourceManager.GetString(nameof(AssistantToolTexts.NoSymbols), culture)!;
        }

        var text = new StringBuilder();
        foreach (var symbol in result.Symbols)
        {
            text.AppendLine(CultureInfo.InvariantCulture, $"{symbol.Location} {symbol.Kind} {(symbol.Container is null ? symbol.Name : $"{symbol.Container}.{symbol.Name}")}");
        }

        return ReadToolText.End(text, new CappedList<ToolSymbol>(result.Symbols, result.Total), culture);
    }
}

internal sealed class ReferencesTool(AssistantDocuments documents, IRenameService workspace) : AssistantTool<ReferencesToolArguments, LocationsToolResult>
{
    public override string Name => CoreAssistantTools.References;

    protected override ToolAnnotations Annotations => new() { ReadOnlyHint = true, IdempotentHint = true };

    protected override string? TitleOf(CultureInfo culture) => AssistantToolTexts.ResourceManager.GetString(nameof(AssistantToolTexts.References_Title), culture);

    protected override string DescriptionOf(CultureInfo culture) => AssistantToolTexts.ResourceManager.GetString(nameof(AssistantToolTexts.References_Description), culture)!;

    protected override string? ArgumentDescriptionOf(string argument, CultureInfo culture)
        => argument == "includeDeclaration"
            ? AssistantToolTexts.ResourceManager.GetString(nameof(AssistantToolTexts.References_IncludeDeclaration), culture)
            : ReadToolText.PositionArgument(argument, culture);

    protected override async Task<LocationsToolResult> RunAsync(ReferencesToolArguments arguments, CancellationToken token)
    {
        await documents.LoadedAsync(token);
        var document = documents.Resolve(arguments.Path);
        var position = AssistantDocuments.PositionOf(arguments);
        if (await workspace.ComposeAsync(token) is not { } composed)
        {
            return new LocationsToolResult();
        }

        var (locations, incomplete) = composed.References(document.Id.Uri.ToUri(), position, arguments.IncludeDeclaration);
        var capped = ToolResultLimits.Cap(locations.Select(location => documents.Locate(location.Uri, location.Range)), ToolResultLimits.References);
        return new LocationsToolResult { Locations = capped.Items, Total = capped.Total, Truncated = capped.Truncated, Incomplete = incomplete };
    }

    protected override string Render(LocationsToolResult result, CultureInfo culture) => ReadToolText.Of(result, culture);
}

internal sealed class DefinitionTool(AssistantDocuments documents, IRenameService workspace) : AssistantTool<PositionToolArguments, LocationsToolResult>
{
    public override string Name => CoreAssistantTools.Definition;

    protected override ToolAnnotations Annotations => new() { ReadOnlyHint = true, IdempotentHint = true };

    protected override string? TitleOf(CultureInfo culture) => AssistantToolTexts.ResourceManager.GetString(nameof(AssistantToolTexts.Definition_Title), culture);

    protected override string DescriptionOf(CultureInfo culture) => AssistantToolTexts.ResourceManager.GetString(nameof(AssistantToolTexts.Definition_Description), culture)!;

    protected override string? ArgumentDescriptionOf(string argument, CultureInfo culture) => ReadToolText.PositionArgument(argument, culture);

    protected override async Task<LocationsToolResult> RunAsync(PositionToolArguments arguments, CancellationToken token)
    {
        await documents.LoadedAsync(token);
        var document = documents.Resolve(arguments.Path);
        var position = AssistantDocuments.PositionOf(arguments);
        if (await workspace.ComposeAsync(token) is not { } composed)
        {
            return new LocationsToolResult();
        }

        var capped = ToolResultLimits.Cap(composed.Definitions(document.Id.Uri.ToUri(), position)
            .Select(link => documents.Locate(link.TargetUri, link.TargetSelectionRange)), ToolResultLimits.References);
        return new LocationsToolResult { Locations = capped.Items, Total = capped.Total, Truncated = capped.Truncated };
    }

    protected override string Render(LocationsToolResult result, CultureInfo culture) => ReadToolText.Of(result, culture);
}

// the text the read tools answer with: one line for each item, and the line that says a list is cut.
internal static class ReadToolText
{
    public static string? PositionArgument(string argument, CultureInfo culture) => argument switch
    {
        "path" => AssistantToolTexts.ResourceManager.GetString(nameof(AssistantToolTexts.Position_Path), culture),
        "line" => AssistantToolTexts.ResourceManager.GetString(nameof(AssistantToolTexts.Position_Line), culture),
        "column" => AssistantToolTexts.ResourceManager.GetString(nameof(AssistantToolTexts.Position_Column), culture),
        _ => null,
    };

    public static string Of(LocationsToolResult result, CultureInfo culture)
    {
        if (result.Total == 0)
        {
            return AssistantToolTexts.ResourceManager.GetString(nameof(AssistantToolTexts.NoLocations), culture)!;
        }

        var text = new StringBuilder();
        foreach (var location in result.Locations)
        {
            text.AppendLine(location.ToString());
        }

        if (result.Incomplete is { } incomplete)
        {
            text.AppendLine(incomplete);
        }

        return End(text, new CappedList<TextLocation>(result.Locations, result.Total), culture);
    }

    public static string End<T>(StringBuilder text, CappedList<T> list, CultureInfo culture)
    {
        if (ToolResultLimits.TruncatedLine(list, culture) is { } truncated)
        {
            text.AppendLine(truncated);
        }

        return text.ToString().TrimEnd();
    }
}
