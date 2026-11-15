using OmniSharp.Extensions.LanguageServer.Protocol;
using RDCore.LanguageServer.Symbols;
using RDCore.SDK.Assistant;
using RDCore.SDK.Server.Assistant;
using System.Globalization;
using System.Text;

namespace RDCore.LanguageServer.Assistant;

// the tools that say what code is: each a projection of what rdcore/semantics/describe and /outline answer, with 1-based positions and paths from the
// workspace root.

internal sealed class DescribeAtTool(AssistantDocuments documents, ISemanticsDescriptionService semantics) : AssistantTool<PositionToolArguments, DescribeToolResult>
{
    public override string Name => CoreAssistantTools.DescribeAt;

    protected override ToolAnnotations Annotations => new() { ReadOnlyHint = true, IdempotentHint = true };

    protected override string? TitleOf(CultureInfo culture) => AssistantToolTexts.ResourceManager.GetString(nameof(AssistantToolTexts.DescribeAt_Title), culture);

    protected override string DescriptionOf(CultureInfo culture) => AssistantToolTexts.ResourceManager.GetString(nameof(AssistantToolTexts.DescribeAt_Description), culture)!;

    protected override string? ArgumentDescriptionOf(string argument, CultureInfo culture) => ReadToolText.PositionArgument(argument, culture);

    protected override async Task<DescribeToolResult> RunAsync(PositionToolArguments arguments, CancellationToken token)
    {
        var document = documents.Resolve(arguments.Path);
        var described = await semantics.DescribeAsync(document.Id.Uri.ToUri(), AssistantDocuments.PositionOf(arguments), token);
        return DescribeToolResult.Of(documents.Root, described);
    }

    protected override string Render(DescribeToolResult result, CultureInfo culture)
    {
        if (result.Symbol is null && result.Expression is null)
        {
            return Text(nameof(AssistantToolTexts.NothingDescribed), culture);
        }

        var text = new StringBuilder();
        if (result.Symbol is { } symbol)
        {
            text.AppendLine(symbol.Container is { } container ? $"{symbol.Declaration} ({container})" : symbol.Declaration);
            if (symbol.Location is { } location)
            {
                text.AppendLine(string.Format(culture, Text(nameof(AssistantToolTexts.DefinedAt), culture), location));
            }

            if (symbol.Documentation is { } documentation)
            {
                text.AppendLine(documentation);
            }
        }

        if (result.Expression is { } expression)
        {
            text.AppendLine(expression.DeclaredType is { } type
                ? string.Format(culture, Text(nameof(AssistantToolTexts.ExpressionOfType), culture), expression.Range.Start, expression.Classification, type)
                : string.Format(culture, Text(nameof(AssistantToolTexts.Expression), culture), expression.Range.Start, expression.Classification));
            if (expression.ConstantValue is { Value: { } value } constant)
            {
                text.AppendLine(string.Format(culture, Text(nameof(AssistantToolTexts.ConstantValue), culture), value, constant.TypeName));
            }

            if (expression.Error is { } error)
            {
                text.AppendLine($"{error.Code}: {error.Description}");
            }
        }

        return text.ToString().TrimEnd();
    }

    private static string Text(string name, CultureInfo culture) => AssistantToolTexts.ResourceManager.GetString(name, culture)!;
}

internal sealed class OutlineTool(AssistantDocuments documents, ISemanticsDescriptionService semantics) : AssistantTool<OutlineToolArguments, OutlineToolResult>
{
    public override string Name => CoreAssistantTools.Outline;

    protected override ToolAnnotations Annotations => new() { ReadOnlyHint = true, IdempotentHint = true };

    protected override string? TitleOf(CultureInfo culture) => AssistantToolTexts.ResourceManager.GetString(nameof(AssistantToolTexts.Outline_Title), culture);

    protected override string DescriptionOf(CultureInfo culture) => AssistantToolTexts.ResourceManager.GetString(nameof(AssistantToolTexts.Outline_Description), culture)!;

    protected override string? ArgumentDescriptionOf(string argument, CultureInfo culture)
        => argument == "path" ? AssistantToolTexts.ResourceManager.GetString(nameof(AssistantToolTexts.Position_Path), culture) : null;

    protected override async Task<OutlineToolResult> RunAsync(OutlineToolArguments arguments, CancellationToken token)
    {
        var document = documents.Resolve(arguments.Path);
        return await semantics.OutlineAsync(document.Id.Uri.ToUri(), token) is { } outline
            ? OutlineToolResult.Of(documents.Root, outline)
            : throw AssistantDocuments.Refused(AssistantToolTexts.NoOutline);
    }

    protected override string Render(OutlineToolResult result, CultureInfo culture)
    {
        if (result.Total == 0)
        {
            return AssistantToolTexts.ResourceManager.GetString(nameof(AssistantToolTexts.DeclaresNothing), culture)!;
        }

        var text = new StringBuilder();
        foreach (var entry in result.Entries)
        {
            Line(text, entry, indent: string.Empty);
        }

        return ReadToolText.End(text, new CappedList<ToolOutlineEntry>(result.Entries, result.Total), culture);
    }

    // one line for each entry, its children under it: where it is selected, and its declaration.
    private static void Line(StringBuilder text, ToolOutlineEntry entry, string indent)
    {
        text.AppendLine($"{indent}{entry.SelectionRange.Start} {entry.Declaration}");
        foreach (var child in entry.Children)
        {
            Line(text, child, indent + "  ");
        }
    }
}
