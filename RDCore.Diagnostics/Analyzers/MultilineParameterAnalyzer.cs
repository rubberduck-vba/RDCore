using System.Text.RegularExpressions;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics.Model;
using RDCore.SDK.Model;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Extensibility.Diagnostics;

namespace RDCore.Diagnostics.Analyzers;

/// <summary>
/// <c>RDC00203</c>: a parameter whose own declaration is split over several lines with a line continuation (<strong>MS-VBAL §5.3.1.5</strong>).
/// </summary>
/// <remarks>
/// <para>
/// A parameter list with a parameter on each line is not reported: it is the declaration of one parameter that is broken, not the list.
/// </para>
/// <para>
/// The fix writes the parameter on the line it starts on: what is between its parts (its modifiers, its name, its <c>As</c> clause and its default
/// value) is written again with a space, which takes the line continuations out. The parts are what the syntax tree says they are, so it is not offered
/// for a name or a type written between brackets, nor for a default value that is itself split.
/// </para>
/// </remarks>
internal sealed partial class MultilineParameterAnalyzer : SyntaxTreeAnalyzer
{
    public override IReadOnlyList<RDCoreDiagnosticCode> Codes => [RDCoreDiagnosticId.MultilineParameterDeclaration.ToCode()];

    protected override IEnumerable<AnalyzerFinding> Analyze(ModuleNode module)
        => module.Descendants()
            .OfType<ParameterDeclarationNode>()
            .Where(parameter => parameter.Location.Range.Start.Line != parameter.Location.Range.End.Line)
            .Select(parameter => new AnalyzerFinding(
                RDCoreDiagnosticId.MultilineParameterDeclaration.ToCode(),
                parameter.Location.Range,
                DiagnosticSeverity.Information,
                Say(RDCoreDiagnosticsResources.MultilineParameterDeclaration_Message, parameter.Name)) { Fixes = FixOf(parameter) });

    private static DiagnosticFixes? FixOf(ParameterDeclarationNode parameter)
    {
        var asType = parameter.Children.IsDefaultOrEmpty ? null : parameter.Children.OfType<AsTypeExpressionNode>().FirstOrDefault();
        var value = parameter.Children.IsDefaultOrEmpty ? null : parameter.Children.FirstOrDefault(child => child is not AsTypeExpressionNode);
        if (parameter.NameRange is not { } name || !IsPlain(parameter.Name) || name.Start.Line != name.End.Line
            || name.End.Character - name.Start.Character != parameter.Name.Length + (parameter.TypeHint?.Length ?? 0)
            || asType is not null && (!IsPlain(asType.TypeName) || asType.QualifierName is { Length: > 0 } qualifier && !IsPlain(qualifier))
            || value is not null && value.SourceLocation.Range.Start.Line != value.SourceLocation.Range.End.Line)
        {
            return null;
        }

        var edits = new List<TextEdit> { SyntaxEdits.Replace(new SourceRange(parameter.Location.Range.Start, name.Start), PrefixOf(parameter)) };
        var array = parameter.IsArray ? "()" : string.Empty;
        var end = name.End;
        if (asType is not null)
        {
            var typeName = asType.QualifierName is { Length: > 0 } library ? $"{library}.{asType.TypeName}" : asType.TypeName;
            edits.Add(SyntaxEdits.Replace(new SourceRange(name.End, asType.Location.Range.Start), $"{array} "));
            edits.Add(SyntaxEdits.Replace(asType.Location.Range, $"As {typeName}"));
            end = asType.Location.Range.End;
        }
        else if (value is null)
        {
            edits.Add(SyntaxEdits.Replace(new SourceRange(name.End, parameter.Location.Range.End), array));
        }

        if (value is not null)
        {
            edits.Add(SyntaxEdits.Replace(new SourceRange(end, value.SourceLocation.Range.Start), " = "));
        }

        return new DiagnosticFixes { Edits = [SyntaxEdits.Fix(Say(RDCoreDiagnosticsResources.MultilineParameterDeclaration_Fix, parameter.Name), [.. edits])] };
    }

    // the keywords the parameter is declared with, which the tree says by its properties, each followed by a space.
    private static string PrefixOf(ParameterDeclarationNode parameter)
        => (parameter.IsOptional ? "Optional " : string.Empty)
            + (parameter.IsByRefIgnored || parameter.ParameterKind is ParameterKind.ExplicitByRef ? "ByRef "
                : parameter.ParameterKind is ParameterKind.ExplicitByVal ? "ByVal "
                : string.Empty)
            + (parameter.IsParamArray ? "ParamArray " : string.Empty);

    private static bool IsPlain(string name) => PlainName().IsMatch(name);

    [GeneratedRegex(@"^\p{L}[\p{L}\p{Nd}_]*$")]
    private static partial Regex PlainName();
}
