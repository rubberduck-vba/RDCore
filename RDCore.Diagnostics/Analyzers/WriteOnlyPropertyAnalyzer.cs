using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics.Model;
using RDCore.SDK.Extensibility.Diagnostics;
using RDCore.SDK.Model;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.AST.Directives;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Semantics.Flags;
using Range = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

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
    private const string NewLine = "\r\n";

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
                Fixes = GetterOf(module, members, member, context.Semantics) is { } fix ? new DiagnosticFixes { Edit = fix } : null,
            })
            .ToArray();
    }

    private static EditFix? GetterOf(ModuleNode module, List<MemberDeclarationNode> members, MemberDeclarationNode let, ModuleSemanticsDto? semantics)
    {
        if (semantics is null || semantics.Class is { } flags && flags.HasFlag(ClassModuleSemanticFlags.Implemented)
            || let.MemberKind is not MemberKind.PropertyLet
            || members.Any(member => member.MemberKind is MemberKind.PropertySet && string.Equals(member.Name, let.Name, StringComparison.OrdinalIgnoreCase))
            || let.Children.OfType<ParameterDeclarationNode>().ToArray() is not [var value]
            || TypeOf(value, module) is not { } type)
        {
            return null;
        }

        // the Let stores its value first, at the indentation of its body, or of one level when it has none.
        var start = let.SourceLocation.Range.Start.Line;
        var end = let.SourceLocation.Range.End.Line;
        var first = let.Children
            .Where(child => child is not (ParameterDeclarationNode or AsTypeExpressionNode or AttributeDirectiveNode))
            .OrderBy(child => child.SourceLocation.Range.Start)
            .FirstOrDefault();
        if (end <= start || first is not null && first.SourceLocation.Range.Start.Line <= start)
        {
            return null;
        }

        var (storeLine, indent) = first is null ? (end, "    ") : (first.SourceLocation.Range.Start.Line, new string(' ', first.SourceLocation.Range.Start.Character));

        // the variable goes after the last declaration before the first procedure, and before the procedure when there is none.
        var firstMember = members.MinBy(member => member.SourceLocation.Range.Start)!;
        var declarations = module.Children
            .Where(child => child is not MemberDeclarationNode && child.SourceLocation.Range.End < firstMember.SourceLocation.Range.Start)
            .ToArray();
        var fieldLine = declarations.Length == 0 ? firstMember.SourceLocation.Range.Start.Line : declarations.Max(child => child.SourceLocation.Range.End.Line) + 1;

        var field = FieldName(let.Name, module, semantics);
        var access = let.AccessModifier is AccessModifier.Public ? "Public " : string.Empty;
        return new EditFix
        {
            Title = AnalyzerMessages.Format(RDCoreDiagnosticsResources.WriteOnlyProperty_Fix, let.Name, field),
            Edits =
            [
                Insert(fieldLine, $"Private {field} As {type}{NewLine}"),
                Insert(storeLine, $"{indent}{field} = {value.Name}{NewLine}"),
                Insert(end + 1, $"{NewLine}{access}Property Get {let.Name}() As {type}{NewLine}    {let.Name} = {field}{NewLine}End Property{NewLine}"),
            ],
        };
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
            "%" => "Integer",
            "&" => "Long",
            "^" => "LongLong",
            "!" => "Single",
            "#" => "Double",
            "@" => "Currency",
            "$" => "String",
            null when value.IsArray => null,
            null when !module.Descendants().OfType<TypeDefDirectiveNode>().Any() => "Variant",
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

    private static TextEdit Insert(int line, string text) => new() { Range = new Range(line, 0, line, 0), NewText = text };
}
