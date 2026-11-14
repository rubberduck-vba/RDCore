using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics.Model;
using RDCore.SDK.Model;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Semantics;
using RDCore.SDK.Semantics.Flags;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Server.ProtocolExtensions;
using RDCore.SDK.Extensibility.Diagnostics;
using ExpressionClassification = RDCore.SDK.Semantics.ExpressionClassification;

namespace RDCore.Diagnostics.Analyzers;

/// <summary>
/// <c>RDC00218</c>: <c>IsMissing</c> of a <c>Variant</c> that is not an <c>Optional</c> parameter (<strong>MS-VBAL §6.1.2.7.1.6</strong>): a required
/// <c>Variant</c> parameter, or a <c>Variant</c> variable.
/// </summary>
/// <remarks>
/// Such a name holds the Missing value only when it was given one, from an <c>Optional</c> parameter whose argument was omitted: <c>IsMissing</c> of it does
/// not tell whether an argument of the procedure was omitted, which is what it is written for. The analyzer finds the call of the library's <c>IsMissing</c>
/// by what it is bound to, and reads its argument's classification and declared type; of a parameter, its declaration in the syntax tree.
/// </remarks>
internal sealed class IsMissingOfAVariantThatIsNotOptionalAnalyzer : IModuleAnalyzer
{
    private const string IsMissing = "Information.IsMissing";

    public IEnumerable<AnalyzerFinding> Analyze(ModuleAnalysisContext context)
    {
        if (context.ParseResult.SyntaxTree is not { } module || context.Semantics is not { } semantics)
        {
            yield break;
        }

        // each call, with the procedure it is written in.
        var calls = new Dictionary<SyntaxNodeId, (IndexExpressionNode Call, MemberDeclarationNode Member)>();
        foreach (var member in module.Descendants().OfType<MemberDeclarationNode>())
        {
            foreach (var call in member.Descendants().OfType<IndexExpressionNode>())
            {
                calls.TryAdd(call.Identity, (call, member));
            }
        }

        var parameters = module.Descendants().OfType<ParameterDeclarationNode>().ToLookup(parameter => parameter.Location.Range);
        var declaredParameters = semantics.Declarations.Where(declaration => declaration.Kind is DeclarationKind.Parameter)
            .ToLookup(declaration => declaration.Symbol.AbsoluteUri, declaration => declaration.Location.Range);
        foreach (var procedure in semantics.Procedures)
        {
            var facts = procedure.Expressions.DistinctBy(fact => fact.Node).ToDictionary(fact => fact.Node);
            foreach (var fact in procedure.Expressions.Where(IsTheLibrarysIsMissing))
            {
                if (!calls.TryGetValue(fact.Node, out var written) || written.Call.Arguments is not [SimpleNameExpressionNode argument]
                    || !facts.TryGetValue(argument.Identity, out var given)
                    || given is not { Binding: { } binding, DeclaredType: { } argumentType }
                    || !string.Equals(argumentType, VBTypeNames.VBVariant, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var parameter = declaredParameters[binding.AbsoluteUri].SelectMany(range => parameters[range]).FirstOrDefault();
                if (parameter is { IsOptional: true } or { IsParamArray: true } || parameter is null && given.Classification is not ExpressionClassification.Variable)
                {
                    continue;
                }

                var isParameter = parameter is not null;
                var what = isParameter
                    ? RDCoreDiagnosticsResources.IsMissingOfAVariantThatIsNotOptional_RequiredParameter
                    : RDCoreDiagnosticsResources.IsMissingOfAVariantThatIsNotOptional_Variable;
                yield return new AnalyzerFinding(
                    RDCoreDiagnosticId.IsMissingOfAVariantThatIsNotOptional.ToCode(),
                    fact.Location.Range,
                    DiagnosticSeverity.Warning,
                    AnalyzerMessages.Format(RDCoreDiagnosticsResources.IsMissingOfAVariantThatIsNotOptional_Message, argument.IdentifierName, what))
                {
                    Fixes = isParameter && FixOf(written.Member, argument.IdentifierName, semantics) is { } fix ? new DiagnosticFixes { Edit = fix } : null,
                };
            }
        }
    }

    private static bool IsTheLibrarysIsMissing(ExpressionFactDto fact)
        => fact is { Binding: { } binding } && fact.Flags.HasFlag(ValueExpressionSemanticFlags.BoundToLibrary | ValueExpressionSemanticFlags.ProcedureCall)
            && string.Equals(SymbolUri.QualifiedNameOf(binding), IsMissing, StringComparison.OrdinalIgnoreCase);

    // Optional before the parameter. MS-VBAL §5.3.1.5 lets an Optional parameter be followed only by Optional parameters, never with a ParamArray, and makes the
    // accessors of a property declare the same parameters; a handler or an implementation is declared as the event or the interface says, and a class that
    // Implements this one declares each of its Public members as it is.
    private static EditFix? FixOf(MemberDeclarationNode member, string name, ModuleSemanticsDto semantics)
    {
        if (member.MemberKind is not (MemberKind.Procedure or MemberKind.Function)
            || semantics.Class is not null && member.AccessModifier is AccessModifier.Public or AccessModifier.Implicit
            || semantics.Declarations.Any(declaration => declaration is { Kind: DeclarationKind.Procedure, Role: not DeclarationRole.None }
                && string.Equals(declaration.Name, member.Name, StringComparison.OrdinalIgnoreCase)))
        {
            return null;
        }

        var parameters = member.Children.OfType<ParameterDeclarationNode>().OrderBy(parameter => parameter.Location.Range.Start).ToArray();
        var index = Array.FindIndex(parameters, parameter => string.Equals(parameter.Name, name, StringComparison.OrdinalIgnoreCase));
        if (index < 0 || parameters.Any(parameter => parameter.IsParamArray) || parameters.Skip(index + 1).Any(parameter => !parameter.IsOptional))
        {
            return null;
        }

        var start = parameters[index].Location.Range.Start;
        return new EditFix
        {
            Title = AnalyzerMessages.Format(RDCoreDiagnosticsResources.IsMissingOfAVariantThatIsNotOptional_Fix, name),
            Edits = [new TextEdit { Range = new SourceRange(start, start).ToLsp(), NewText = "Optional " }],
        };
    }
}
