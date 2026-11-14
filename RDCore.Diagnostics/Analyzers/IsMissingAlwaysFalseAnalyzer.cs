using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics.Model;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Semantics;
using RDCore.SDK.Semantics.Flags;
using RDCore.SDK.Server.ProtocolExtensions;
using ExpressionClassification = RDCore.SDK.Semantics.ExpressionClassification;
using RDCore.SDK.Extensibility.Diagnostics;

namespace RDCore.Diagnostics.Analyzers;

/// <summary>
/// <c>RDC00207</c>: <c>IsMissing</c> of a name that can never hold the Missing value (<strong>MS-VBAL §6.1.2.7.1.6</strong>), so that it is always <c>False</c>.
/// </summary>
/// <remarks>
/// Only a <c>Variant</c> parameter can be missing: an omitted argument leaves a parameter of any other type its type's default value, and gives an
/// <c>Optional</c> <c>Variant</c> its default value when it has one. An <c>IsMissing</c> of such a parameter looks like a test and never is one, which MS-VBA
/// compiles without a word (recorded in Excel). A variable that is not a <c>Variant</c>, and a constant, never hold the Missing value either. A
/// <c>ParamArray</c> with no elements is missing, and a <c>Variant</c> variable holds the Missing value that is assigned to it. The analyzer finds the call of
/// the library's <c>IsMissing</c> by what it is bound to, and reads its argument's classification and declared type; of a parameter, its declaration in the
/// syntax tree.
/// </remarks>
internal sealed class IsMissingAlwaysFalseAnalyzer : IModuleAnalyzer
{
    public IReadOnlyList<RDCoreDiagnosticCode> Codes => [RDCoreDiagnosticId.IsMissingAlwaysFalse.ToCode()];

    private const string IsMissing = "Information.IsMissing";

    public IEnumerable<AnalyzerFinding> Analyze(ModuleAnalysisContext context)
    {
        if (context.ParseResult.SyntaxTree is not { } module || context.Semantics is not { } semantics)
        {
            yield break;
        }

        var calls = module.Descendants().OfType<IndexExpressionNode>().ToDictionary(call => call.Identity);
        var parameters = module.Descendants().OfType<ParameterDeclarationNode>().ToLookup(parameter => parameter.Location.Range);
        var declaredParameters = semantics.Declarations.Where(declaration => declaration.Kind is DeclarationKind.Parameter)
            .ToLookup(declaration => declaration.Symbol.AbsoluteUri, declaration => declaration.Location.Range);
        foreach (var procedure in semantics.Procedures)
        {
            var facts = procedure.Expressions.DistinctBy(fact => fact.Node).ToDictionary(fact => fact.Node);
            foreach (var fact in procedure.Expressions.Where(IsTheLibrarysIsMissing))
            {
                if (!calls.TryGetValue(fact.Node, out var call) || call.Arguments is not [SimpleNameExpressionNode argument]
                    || !facts.TryGetValue(argument.Identity, out var given) || given is not { Binding: { } binding, DeclaredType: { } argumentType })
                {
                    continue;
                }

                var isVariant = string.Equals(argumentType, VBTypeNames.VBVariant, StringComparison.OrdinalIgnoreCase);
                string message;
                if (declaredParameters[binding.AbsoluteUri].SelectMany(range => parameters[range]).FirstOrDefault() is { } parameter)
                {
                    if (parameter.IsParamArray || isVariant && !HasADefaultValue(parameter))
                    {
                        continue;
                    }

                    var reason = isVariant ? RDCoreDiagnosticsResources.IsMissingAlwaysFalse_HasADefault : RDCoreDiagnosticsResources.IsMissingAlwaysFalse_NotAVariant;
                    message = AnalyzerMessages.Format(RDCoreDiagnosticsResources.IsMissingAlwaysFalse_Message, argument.IdentifierName, reason);
                }
                else if (given.Classification is ExpressionClassification.Constant
                    || given.Classification is ExpressionClassification.Variable && !isVariant && argumentType != VBTypeNames.VBUnknown)
                {
                    var reason = given.Classification is ExpressionClassification.Constant
                        ? RDCoreDiagnosticsResources.IsMissingAlwaysFalse_AConstant
                        : RDCoreDiagnosticsResources.IsMissingAlwaysFalse_AVariableNotAVariant;
                    message = AnalyzerMessages.Format(RDCoreDiagnosticsResources.IsMissingAlwaysFalse_NotAParameter_Message, argument.IdentifierName, reason);
                }
                else
                {
                    continue;
                }

                // the call is always False, and its argument is a name, whose evaluation has no other effect: False in its place does what it did.
                yield return new AnalyzerFinding(RDCoreDiagnosticId.IsMissingAlwaysFalse.ToCode(), fact.Location.Range, DiagnosticSeverity.Warning, message)
                {
                    Fixes = new DiagnosticFixes
                    {
                        Edit = new EditFix
                        {
                            Title = AnalyzerMessages.Format(RDCoreDiagnosticsResources.IsMissingAlwaysFalse_Fix, argument.IdentifierName),
                            Edits = [new TextEdit { Range = call.Location.Range.ToLsp(), NewText = "False" }],
                        },
                    },
                };
            }
        }
    }

    private static bool IsTheLibrarysIsMissing(ExpressionFactDto fact)
        => fact is { Binding: { } binding } && fact.Flags.HasFlag(ValueExpressionSemanticFlags.BoundToLibrary | ValueExpressionSemanticFlags.ProcedureCall)
            && string.Equals(SymbolUri.QualifiedNameOf(binding), IsMissing, StringComparison.OrdinalIgnoreCase);

    // the default value is what the declaration is made of besides its As clause: `Optional p As Variant = 5`.
    private static bool HasADefaultValue(ParameterDeclarationNode parameter)
        => !parameter.Children.IsDefault && parameter.Children.Any(child => child is ExpressionNode and not AsTypeExpressionNode);
}
