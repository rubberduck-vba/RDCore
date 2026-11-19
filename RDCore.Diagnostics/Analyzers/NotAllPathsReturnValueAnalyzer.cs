using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics.Model;
using RDCore.SDK.Model;
using RDCore.SDK.Model.AST;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.AST.Directives;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Semantics;
using RDCore.SDK.Semantics.Flow;
using RDCore.SDK.Extensibility.Diagnostics;

namespace RDCore.Diagnostics.Analyzers;

/// <summary>
/// <c>RDC00206</c>: a function or a property getter with a code path that ends without assigning its return value (<strong>MS-VBAL §5.3.1</strong>).
/// </summary>
/// <remarks>
/// That a code path does not assign the return value is a fact of the procedure (<see cref="ReturnValueFact"/>), found by following the code paths of its body, which
/// the analyzer reads; the syntax tree is read only for the fix. The procedure is not reported when the fact is not stated. Nor is one whose body has no
/// statement, which is how a member of an interface is written, when it is a <c>Public</c> member of a class that may be an interface
/// (<see cref="UnusedDeclarations.MayBeAnInterface"/>).
/// </remarks>
internal sealed class NotAllPathsReturnValueAnalyzer : IModuleAnalyzer
{
    public IReadOnlyList<RDCoreDiagnosticCode> Codes => [RDCoreDiagnosticId.NotAllPathsReturnValue.ToCode()];

    public IEnumerable<AnalyzerFinding> Analyze(ModuleAnalysisContext context)
    {
        var module = context.ParseResult.SyntaxTree;
        var members = module?.Descendants().OfType<MemberDeclarationNode>().Where(member => member.MemberKind is MemberKind.Function or MemberKind.PropertyGet).ToArray() ?? [];
        var hasDefType = module?.Descendants().OfType<TypeDefDirectiveNode>().Any() ?? false;
        return (context.Semantics?.Procedures ?? [])
            .Select(procedure => procedure.ReturnValue)
            .OfType<ReturnValueFact>()
            .Where(fact => fact.Assignment is not ReturnValueAssignment.Always && !(fact.IsEmpty && MayBeAMemberOfAnInterface(fact, context.Semantics!)))
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
                    && FixOf(context.ParseResult, procedure, hasDefType) is { } fix
                    ? new DiagnosticFixes { Edits = [fix] }
                    : null,
            });
    }

    // the return value assigned its default value before the first statement, which every call starts with (MS-VBAL §5.3.1.6, a fresh function result variable
    // at each call, in a Static procedure too): the code does what it did, and says so. Not offered for a type whose default has no literal (a user-defined
    // type, an array, a class or an Enum that only the declaration knows), nor for a return type a DefType statement may set.
    private static EditFix? FixOf(ModuleParseResult parse, MemberDeclarationNode procedure, bool hasDefType)
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

        // the assignment is written at the indentation of the first statement.
        var indent = new string(' ', first.SourceLocation.Range.Start.Character);
        var assignment = value is "Nothing" ? $"Set {procedure.Name} = Nothing" : $"{procedure.Name} = {value}";
        return SyntaxEdits.Fix(
            AnalyzerMessages.Format(RDCoreDiagnosticsResources.NotAllPathsReturnValue_Fix, procedure.Name, value),
            LineEdits.InsertLinesBefore(parse, first, $"{indent}{assignment}"));
    }

    // the literal of the default value of the return type, as the As clause or the type character states it; a qualified type name has none here.
    private static string? DefaultOf(AsTypeExpressionNode? asType, string? typeHint, bool hasDefType)
    {
        if (asType is not null)
        {
            return asType.QualifierName is not { Length: > 0 } && IntrinsicVBTypes.TryResolve(asType.TypeName, out var declared)
                ? IntrinsicVBTypes.DefaultLiteralOf(declared)
                : null;
        }

        return typeHint switch
        {
            { } hint => IntrinsicVBTypes.TryResolveTypeHint(hint, out var hinted) ? IntrinsicVBTypes.DefaultLiteralOf(hinted) : null,
            null when !hasDefType => IntrinsicVBTypes.DefaultLiteralOf(VBVariantType.TypeInfo),
            _ => null,
        };
    }

    // a Public member of a class that may be an interface (MS-VBAL §5.2.4.2), which no implementation or handler is.
    private static bool MayBeAMemberOfAnInterface(ReturnValueFact fact, ModuleSemanticsDto semantics)
        => UnusedDeclarations.MayBeAnInterface(semantics)
            && semantics.Declarations.Any(declaration => declaration is { Kind: DeclarationKind.Procedure or DeclarationKind.Property, Role: DeclarationRole.None }
                && declaration.Access is AccessModifier.Public or AccessModifier.Implicit
                && string.Equals(declaration.Name, fact.Name, StringComparison.OrdinalIgnoreCase));
}
