using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics.Model;
using RDCore.SDK.Extensibility.Diagnostics;
using RDCore.SDK.Model;
using RDCore.SDK.Model.AST;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.AST.Directives;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Semantics.Flags;

namespace RDCore.Diagnostics.Analyzers;

/// <summary>
/// <c>RDC01017</c>: a public property with a <c>Property Let</c> or a <c>Property Set</c> and no <c>Property Get</c> (<strong>MS-VBAL §5.3.1.7</strong>): code
/// that uses the module can assign the property, and never read it.
/// </summary>
/// <remarks>
/// A property with both a <c>Let</c> and a <c>Set</c> is reported once, at the first of them. A <c>Private</c> or <c>Friend</c> property is not reported.
/// <para>
/// A <c>Property Let</c> with one parameter comes with the fix that adds a <c>Property Get</c>: a <c>Private</c> variable of the module keeps the value the
/// <c>Let</c> is given, and the <c>Get</c> returns it. It is not offered for a class that another module implements, where the <c>Get</c> would be a member
/// of the interface that the implementing modules do not have (<strong>MS-VBAL §5.2.4.2</strong>), nor for a property with a <c>Property Set</c>, whose
/// variable would keep the object it is given alive.
/// </para>
/// </remarks>
internal sealed class WriteOnlyPropertyAnalyzer : IModuleAnalyzer
{
    public IReadOnlyList<RDCoreDiagnosticCode> Codes => [RDCoreDiagnosticId.WriteOnlyProperty.ToCode()];

    public IEnumerable<AnalyzerFinding> Analyze(ModuleAnalysisContext context)
    {
        if (context.ParseResult.SyntaxTree is not { } module)
        {
            return [];
        }

        var members = module.Children.OfType<MemberDeclarationNode>().ToList();
        var readable = members.Where(member => member.MemberKind is MemberKind.PropertyGet).Select(member => member.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

        return members
            .Where(member => member is { MemberKind: MemberKind.PropertyLet or MemberKind.PropertySet, AccessModifier: AccessModifier.Public or AccessModifier.Implicit or AccessModifier.Global }
                && !readable.Contains(member.Name))
            .DistinctBy(member => member.Name, StringComparer.OrdinalIgnoreCase)
            .Select(member => new AnalyzerFinding(
                RDCoreDiagnosticId.WriteOnlyProperty.ToCode(),
                member.NameRange ?? member.SourceLocation.Range,
                DiagnosticSeverity.Information,
                AnalyzerMessages.Format(RDCoreDiagnosticsResources.WriteOnlyProperty_Message, member.Name))
            {
                Fixes = GetterOf(context.ParseResult, module, members, member, context.Semantics) is { } fix ? new DiagnosticFixes { Edit = fix } : null,
            })
            .ToArray();
    }

    private static EditFix? GetterOf(ModuleParseResult parse, ModuleNode module, List<MemberDeclarationNode> members, MemberDeclarationNode let, ModuleSemanticsDto? semantics)
    {
        if (semantics is null || semantics.Class is { } flags && flags.HasFlag(ClassModuleSemanticFlags.Implemented)
            || let.MemberKind is not MemberKind.PropertyLet
            || members.Any(member => member.MemberKind is MemberKind.PropertySet && string.Equals(member.Name, let.Name, StringComparison.OrdinalIgnoreCase))
            || let.Children.OfType<ParameterDeclarationNode>().ToArray() is not [var value]
            || TypeOf(value, module) is not { } type)
        {
            return null;
        }

        // the End Property, or where the member ends, which is on its line.
        var endProperty = let.EndRange ?? new SourceRange(let.SourceLocation.Range.End, let.SourceLocation.Range.End);

        // the Let stores its value first, at the indentation of its body, or of one level when it has none.
        var start = let.SourceLocation.Range.Start.Line;
        var first = let.Children
            .Where(child => child is not (ParameterDeclarationNode or AsTypeExpressionNode or AttributeDirectiveNode))
            .OrderBy(child => child.SourceLocation.Range.Start)
            .FirstOrDefault();
        if (endProperty.Start.Line <= start || first is not null && first.SourceLocation.Range.Start.Line <= start)
        {
            return null;
        }

        var field = FieldName(let.Name, module, semantics);
        if (LineEdits.InsertLinesAfterDeclarations(parse, $"Private {field} As {type}") is not { } declare)
        {
            return null;
        }

        var store = first is null
            ? LineEdits.InsertLinesBefore(parse, endProperty, $"    {field} = {value.Name}")
            : LineEdits.InsertLinesBefore(parse, first, $"{new string(' ', first.SourceLocation.Range.Start.Character)}{field} = {value.Name}");
        var access = let.AccessModifier is AccessModifier.Public ? "Public " : string.Empty;
        var getter = LineEdits.InsertLinesAfter(parse, let, string.Empty, $"{access}Property Get {let.Name}() As {type}", $"    {let.Name} = {field}", "End Property");
        return SyntaxEdits.Fix(AnalyzerMessages.Format(RDCoreDiagnosticsResources.WriteOnlyProperty_Fix, let.Name, field), declare, store, getter);
    }

    // the type of the value as it is written; a value with neither an As clause nor a type character is a Variant, unless a DefType says otherwise.
    private static string? TypeOf(ParameterDeclarationNode value, ModuleNode module)
    {
        if ((value.Children.IsDefault ? null : value.Children.OfType<AsTypeExpressionNode>().FirstOrDefault()) is { } asType)
        {
            return asType.IsArrayDef ? null : asType.QualifierName is { Length: > 0 } qualifier ? $"{qualifier}.{asType.TypeName}" : asType.TypeName;
        }

        return value.TypeHint switch
        {
            { } hint => IntrinsicVBTypes.TryResolveTypeHint(hint, out var hinted) ? hinted.Name : null,
            null when value.IsArray => null,
            null when !module.Descendants().OfType<TypeDefDirectiveNode>().Any() => VBTypeNames.VBVariant,
            _ => null,
        };
    }

    // "m" and the name of the property, or with 2, 3... after it when that names something the module declares or refers to.
    private static string FieldName(string property, ModuleNode module, ModuleSemanticsDto semantics)
    {
        var taken = semantics.Declarations.Select(declaration => declaration.Name)
            .Concat(module.Descendants().OfType<SimpleNameExpressionNode>().Select(name => name.IdentifierName))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var name = $"m{property}";
        var candidate = name;
        for (var n = 2; taken.Contains(candidate); n++)
        {
            candidate = $"{name}{n}";
        }

        return candidate;
    }
}
