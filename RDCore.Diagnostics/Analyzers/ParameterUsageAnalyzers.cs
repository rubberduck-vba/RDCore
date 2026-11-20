using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics.Model;
using RDCore.SDK.Extensibility.Diagnostics;
using RDCore.SDK.Model;
using RDCore.SDK.Model.AST;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.AST.Directives;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.AST.Statements;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Semantics;
using RDCore.SDK.Semantics.Refactoring;

namespace RDCore.Diagnostics.Analyzers;

/// <summary>
/// <c>RDC01013</c>: a parameter that nothing in its procedure refers to.
/// </summary>
/// <remarks>
/// A procedure whose signature is not its own to choose is left out: an implementation of an interface member, a handler of an event, a <c>Declare</c>, an
/// event, and a <c>Public</c> member of a class module, which may be a member of an interface (<strong>MS-VBAL §5.2.4.2</strong>). A parameter of an
/// interface member or of an event is reported where it has implementations or handlers and none refers to its own parameter at that position, as the
/// host states it over the project (<see cref="RDCore.SDK.Semantics.DeclarationFact.IsReferredToByABoundMember"/>). The fix removes a parameter of an
/// interface member from its implementations too; an event's has none, since an event's parameters are not refactored.
/// </remarks>
internal sealed class ParameterNotUsedAnalyzer : IModuleAnalyzer
{
    public IReadOnlyList<RDCoreDiagnosticCode> Codes => [RDCoreDiagnosticId.ParameterNotUsed.ToCode()];

    public IEnumerable<AnalyzerFinding> Analyze(ModuleAnalysisContext context)
    {
        if (context is not { ParseResult.SyntaxTree: { } module, Semantics: { } semantics })
        {
            yield break;
        }

        foreach (var fact in semantics.Declarations.Where(declaration => declaration is { Kind: DeclarationKind.Parameter, IsImplicit: false }
            && (declaration.References?.Total ?? 0) == 0 && (declaration.References is not null || declaration.IsReferredToByABoundMember is false)))
        {
            // the implicit Me of a class member is a parameter fact too, with no declaration in the source.
            if (UnusedDeclarations.ParameterOf(module, fact) is not { } parameter
                || UnusedDeclarations.ProcedureAt(module, fact.Location.Range.Start) is not { } procedure)
            {
                continue;
            }

            if (fact.IsReferredToByABoundMember is false)
            {
                var isEvent = procedure.MemberKind is MemberKind.Event;
                var message = isEvent
                    ? RDCoreDiagnosticsResources.ParameterNotUsed_Handlers_Message
                    : RDCoreDiagnosticsResources.ParameterNotUsed_Implementations_Message;
                yield return new AnalyzerFinding(
                    RDCoreDiagnosticId.ParameterNotUsed.ToCode(),
                    parameter.NameRange ?? fact.Location.Range,
                    DiagnosticSeverity.Information,
                    AnalyzerMessages.Format(message, fact.Name, procedure.Name))
                {
                    Fixes = isEvent ? null : new DiagnosticFixes { Refactoring = new RefactoringFix { Kind = RefactoringKinds.RemoveParameter } },
                };
                continue;
            }

            if (fact.References is null || !UnusedDeclarations.ChoosesItsSignature(procedure, semantics))
            {
                continue;
            }

            yield return new AnalyzerFinding(
                RDCoreDiagnosticId.ParameterNotUsed.ToCode(),
                parameter.NameRange ?? fact.Location.Range,
                DiagnosticSeverity.Information,
                AnalyzerMessages.Format(RDCoreDiagnosticsResources.ParameterNotUsed_Message, fact.Name))
            {
                // the language server removes the parameter, with its argument from every call.
                Fixes = new DiagnosticFixes { Refactoring = new RefactoringFix { Kind = RefactoringKinds.RemoveParameter } },
            };
        }
    }
}

/// <summary>
/// <c>RDC01014</c>: a parameter passed by value that its procedure assigns (<strong>MS-VBAL §5.3.1.5</strong>): the assignment changes the procedure's own
/// copy, and the caller's argument does not change.
/// </summary>
/// <remarks>
/// The value parameter of a property <c>Let</c> or <c>Set</c> is passed by value whatever it states (<strong>MS-VBAL §5.3.1.7</strong>), and is reported too.
/// The fix declares a local variable of the parameter's type at the start of the procedure, copies the parameter into it, and uses it in place of the
/// parameter: the procedure does the same, and the parameter keeps the caller's value.
/// </remarks>
internal sealed class AssignedByValParameterAnalyzer : IModuleAnalyzer
{
    public IReadOnlyList<RDCoreDiagnosticCode> Codes => [RDCoreDiagnosticId.AssignedByValParameter.ToCode()];

    public IEnumerable<AnalyzerFinding> Analyze(ModuleAnalysisContext context)
    {
        if (context is not { ParseResult.SyntaxTree: { } module, Semantics: { } semantics })
        {
            yield break;
        }

        var hasDefType = module.Descendants().OfType<TypeDefDirectiveNode>().Any();
        foreach (var (fact, _) in UnusedDeclarations.Counted(semantics, DeclarationKind.Parameter).Where(counted => counted.References.Writes > 0))
        {
            if (UnusedDeclarations.ParameterOf(module, fact) is { } parameter && (parameter.ParameterKind is ParameterKind.ExplicitByVal || parameter.IsByRefIgnored))
            {
                yield return new AnalyzerFinding(
                    RDCoreDiagnosticId.AssignedByValParameter.ToCode(),
                    parameter.NameRange ?? fact.Location.Range,
                    DiagnosticSeverity.Information,
                    AnalyzerMessages.Format(RDCoreDiagnosticsResources.AssignedByValParameter_Message, fact.Name))
                {
                    Fixes = LocalCopyOf(context.ParseResult, module, semantics, parameter, hasDefType) is { } fix ? new DiagnosticFixes { Edits = [fix] } : null,
                };
            }
        }
    }

    private static EditFix? LocalCopyOf(
        ModuleParseResult parse, ModuleNode module, ModuleSemanticsDto semantics, ParameterDeclarationNode parameter, bool hasDefType)
    {
        var procedure = module.Descendants().OfType<MemberDeclarationNode>().FirstOrDefault(member => member.Children.Any(child => ReferenceEquals(child, parameter)));
        var first = procedure?.Children
            .Where(child => child is not (ParameterDeclarationNode or AsTypeExpressionNode or AttributeDirectiveNode))
            .OrderBy(child => child.SourceLocation.Range.Start)
            .FirstOrDefault();
        if (procedure is null || first is null || first.SourceLocation.Range.Start.Line <= procedure.SourceLocation.Range.Start.Line)
        {
            return null;
        }

        // the names of the parameter in the body: no member of an object is named for it, and nothing in a procedure can declare its name again.
        var body = procedure.Descendants().Where(node => node.SourceLocation.Range.Start >= first.SourceLocation.Range.Start).ToArray();
        var members = body.OfType<MemberAccessExpressionNode>().Select(access => access.Member.Identity).ToHashSet();
        var names = body.OfType<SimpleNameExpressionNode>()
            .Where(name => string.Equals(name.IdentifierName, parameter.Name, StringComparison.OrdinalIgnoreCase) && !members.Contains(name.Identity))
            .ToArray();
        var targets = names.Select(name => name.Identity).ToHashSet();
        var assignments = body.OfType<AssignmentStatementNode>().Where(assignment => assignment.Target is SimpleNameExpressionNode target && targets.Contains(target.Identity)).ToArray();

        var local = LocalName(parameter.Name, body, semantics);
        if (TypeOf(parameter, hasDefType) is not { } type || CopyOf(type, local, parameter.Name, assignments) is not { } copy)
        {
            return null;
        }

        // the local is declared and assigned at the indentation of the body's first line.
        var indent = new string(' ', first.SourceLocation.Range.Start.Character);
        return SyntaxEdits.Fix(
            AnalyzerMessages.Format(RDCoreDiagnosticsResources.AssignedByValParameter_Fix, parameter.Name, local),
            [
                LineEdits.InsertLinesBefore(parse, first, $"{indent}Dim {local} As {type.Name}", $"{indent}{copy}"),
                .. names.Select(name => SyntaxEdits.Replace(name.Location.Range, local)),
            ]);
    }

    // a Let, a Set, whichever a Variant holds, or whichever the procedure's assignments to the parameter use.
    private enum Copy { Value, Object, Either, AsAssigned }

    // the type the parameter is declared with, as it is written, and how a copy of it is made.
    private static (string Name, Copy Copy)? TypeOf(ParameterDeclarationNode parameter, bool hasDefType)
    {
        var asType = parameter.Children.IsDefault ? null : parameter.Children.OfType<AsTypeExpressionNode>().FirstOrDefault();
        if (parameter.IsArray || parameter.IsParamArray || asType is { IsArrayDef: true })
        {
            return null;
        }

        if (asType is null)
        {
            return parameter.TypeHint switch
            {
                { } hint => IntrinsicVBTypes.TryResolveTypeHint(hint, out var hinted) ? (hinted.Name, Copy.Value) : null,
                null when !hasDefType => (VBTypeNames.VBVariant, Copy.Either),
                _ => null,
            };
        }

        if (asType.QualifierName is { Length: > 0 } qualifier)
        {
            return ($"{qualifier}.{asType.TypeName}", Copy.AsAssigned);
        }

        // a class, or an Enum: what the procedure assigns to the parameter says which.
        return IntrinsicVBTypes.TryResolve(asType.TypeName, out var intrinsic)
            ? (asType.TypeName, intrinsic switch { VBVariantType => Copy.Either, VBObjectType => Copy.Object, _ => Copy.Value })
            : (asType.TypeName, Copy.AsAssigned);
    }

    // the statement that copies the parameter into the local: a Set for an object, a Let for a value, and for a Variant whichever it holds.
    private static string? CopyOf((string Name, Copy Copy) type, string local, string parameter, AssignmentStatementNode[] assignments)
    {
        var copy = type.Copy;
        if (copy is Copy.AsAssigned)
        {
            var sets = assignments.Count(assignment => assignment.Kind is AssignmentKind.Set);
            copy = assignments.Length == 0 ? Copy.Either : sets == assignments.Length ? Copy.Object : sets == 0 ? Copy.Value : Copy.Either;
            if (copy is Copy.Either)
            {
                return null;
            }
        }

        return copy switch
        {
            Copy.Value => $"{local} = {parameter}",
            Copy.Object => $"Set {local} = {parameter}",
            _ => $"If IsObject({parameter}) Then Set {local} = {parameter} Else {local} = {parameter}",
        };
    }

    // "local" and the name, or with 2, 3... after it when that names something the module declares or the procedure refers to.
    private static string LocalName(string parameter, SyntaxNode[] body, ModuleSemanticsDto semantics)
    {
        var taken = semantics.Declarations.Select(declaration => declaration.Name)
            .Concat(body.OfType<SimpleNameExpressionNode>().Select(name => name.IdentifierName))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var name = $"local{char.ToUpperInvariant(parameter[0])}{parameter[1..]}";
        var candidate = name;
        for (var n = 2; taken.Contains(candidate); n++)
        {
            candidate = $"{name}{n}";
        }

        return candidate;
    }
}
