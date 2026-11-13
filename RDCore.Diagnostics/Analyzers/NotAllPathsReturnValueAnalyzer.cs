using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics.Model;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.AST.Directives;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Semantics.Flow;
using RDCore.SDK.Extensibility.Diagnostics;
using Range = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace RDCore.Diagnostics.Analyzers;

/// <summary>
/// <c>RDC00206</c>: a function or a property getter with a code path that ends without assigning its return value (<strong>MS-VBAL §5.3.1</strong>).
/// </summary>
/// <remarks>
/// That a code path does not assign the return value is a fact of the procedure (<see cref="ReturnValueFact"/>), found by following the code paths of its body, which
/// the analyzer reads: it does not look at the syntax tree. The procedure is not reported when the fact is not stated, nor when its body has no statement, which is how a
/// member of an interface is written.
/// </remarks>
internal sealed class NotAllPathsReturnValueAnalyzer : IModuleAnalyzer
{
    public IReadOnlyList<RDCoreDiagnosticCode> Codes => [RDCoreDiagnosticId.NotAllPathsReturnValue.ToCode()];
    private const string NewLine = "\r\n";

    public IEnumerable<AnalyzerFinding> Analyze(ModuleAnalysisContext context)
    {
        var module = context.ParseResult.SyntaxTree;
        var members = module?.Descendants().OfType<MemberDeclarationNode>().Where(member => member.MemberKind is MemberKind.Function or MemberKind.PropertyGet).ToArray() ?? [];
        var hasDefType = module?.Descendants().OfType<TypeDefDirectiveNode>().Any() ?? false;
        return (context.Semantics?.Procedures ?? [])
            .Select(procedure => procedure.ReturnValue)
            .OfType<ReturnValueFact>()
            .Where(fact => fact is { IsEmpty: false, Assignment: not ReturnValueAssignment.Always })
            .Select(fact => new AnalyzerFinding(
                RDCoreDiagnosticId.NotAllPathsReturnValue.ToCode(),
                fact.Location.Range,
                DiagnosticSeverity.Warning,
                AnalyzerMessages.Format(
                    fact.Assignment is ReturnValueAssignment.Never
                        ? RDCoreDiagnosticsResources.NotAllPathsReturnValue_NeverAssigned_Message
                        : RDCoreDiagnosticsResources.NotAllPathsReturnValue_Message,
                    fact.Name))
            {
                Fixes = members.FirstOrDefault(member => member.SourceLocation.Range.Start.Line == fact.Location.Range.Start.Line
                        && string.Equals(member.Name, fact.Name, StringComparison.OrdinalIgnoreCase)) is { } procedure
                    && FixOf(procedure, hasDefType) is { } fix
                    ? new DiagnosticFixes { Edit = fix }
                    : null,
            });
    }

    // the return value assigned its default value before the first statement, which every call starts with (MS-VBAL §5.3.1.6, a fresh function result variable
    // at each call, in a Static procedure too): the code does what it did, and says so. Not offered for a type whose default has no literal (a user-defined
    // type, an array, a class or an Enum that only the declaration knows), nor for a return type a DefType statement may set.
    private static EditFix? FixOf(MemberDeclarationNode procedure, bool hasDefType)
    {
        var asType = procedure.Children.OfType<AsTypeExpressionNode>().FirstOrDefault();
        if (asType is { IsArrayDef: true } || DefaultOf(asType, procedure.TypeHint, hasDefType) is not { } value)
        {
            return null;
        }

        var first = procedure.Children
            .Where(child => child is not (ParameterDeclarationNode or AsTypeExpressionNode or AttributeDirectiveNode))
            .OrderBy(child => child.SourceLocation.Range.Start)
            .FirstOrDefault();
        if (first is null || first.SourceLocation.Range.Start.Line <= procedure.SourceLocation.Range.Start.Line)
        {
            return null;
        }

        var line = first.SourceLocation.Range.Start.Line;
        var indent = new string(' ', first.SourceLocation.Range.Start.Character);
        var assignment = value is "Nothing" ? $"Set {procedure.Name} = Nothing" : $"{procedure.Name} = {value}";
        return new EditFix
        {
            Title = AnalyzerMessages.Format(RDCoreDiagnosticsResources.NotAllPathsReturnValue_Fix, procedure.Name, value),
            Edits = [new TextEdit { Range = new Range(line, 0, line, 0), NewText = $"{indent}{assignment}{NewLine}" }],
        };
    }

    private static string? DefaultOf(AsTypeExpressionNode? asType, string? typeHint, bool hasDefType)
    {
        var typeName = asType is null ? null : asType.QualifierName is { Length: > 0 } qualifier ? $"{qualifier}.{asType.TypeName}" : asType.TypeName;
        return (typeName?.ToUpperInvariant(), typeHint) switch
        {
            ("BYTE" or "INTEGER" or "LONG" or "LONGLONG" or "LONGPTR" or "SINGLE" or "DOUBLE" or "CURRENCY" or "DECIMAL" or "DATE", _) => "0",
            ("STRING", _) => "vbNullString",
            ("BOOLEAN", _) => "False",
            ("VARIANT", _) => "Empty",
            ("OBJECT", _) => "Nothing",
            (null, "%" or "&" or "^" or "!" or "#" or "@") => "0",
            (null, "$") => "vbNullString",
            (null, null) when !hasDefType => "Empty",
            _ => null,
        };
    }
}
