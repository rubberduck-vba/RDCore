using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics.Model;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Extensibility.Diagnostics;
using RDCore.SDK.Model.Source;

namespace RDCore.Diagnostics.Analyzers;

/// <summary>
/// <c>RDC00106</c>: a variable or a parameter that is declared without a type, and is a <c>Variant</c> (<strong>MS-VBAL §5.2.3.1.5</strong>).
/// </summary>
/// <remarks>
/// A name that a <c>Def&lt;Type&gt;</c> directive covers is given that type and not a <c>Variant</c>, which <see cref="TypeDefDirectiveAnalyzer"/> reports at the directive.
/// A constant is not among them: its type is the type of its value. The fix writes <c>As Variant</c> after the name and its array bounds, before the default
/// value of an optional parameter: the variable is the <c>Variant</c> it was.
/// </remarks>
internal sealed class ImplicitVariantDeclarationAnalyzer : SyntaxTreeAnalyzer
{
    public override IReadOnlyList<RDCoreDiagnosticCode> Codes => [RDCoreDiagnosticId.ImplicitVariantDeclaration.ToCode()];

    protected override IEnumerable<AnalyzerFinding> Analyze(ModuleNode module)
    {
        foreach (var node in module.Descendants())
        {
            var (name, typeHint, range, asClauseAt) = node switch
            {
                VariableDeclarationNode variable => (variable.Name, variable.TypeHint, variable.NameRange ?? variable.SourceLocation.Range, variable.SourceLocation.Range.End),
                ParameterDeclarationNode parameter => (parameter.Name, parameter.TypeHint, parameter.Location.Range, AsClauseOf(parameter)),
                _ => (null, null, default, null),
            };

            if (name is not null && !ImplicitTypes.IsTypeStated(node, typeHint) && ImplicitTypes.IsVariant(module, name))
            {
                yield return new AnalyzerFinding(
                    RDCoreDiagnosticId.ImplicitVariantDeclaration.ToCode(), range, DiagnosticSeverity.Information, Say(RDCoreDiagnosticsResources.ImplicitVariantDeclaration_Message, name))
                {
                    Fixes = asClauseAt is { } at
                        ? new DiagnosticFixes { Edit = SyntaxEdits.Fix(Say(RDCoreDiagnosticsResources.ImplicitVariantDeclaration_Fix, name), SyntaxEdits.Insert(at, " As Variant")) }
                        : null,
                };
            }
        }
    }

    // where a parameter's As clause goes: where the parameter ends, or right after its name when a default value follows it (an optional parameter's is its
    // only child, with no As clause). An array parameter takes no default value.
    private static SourcePosition? AsClauseOf(ParameterDeclarationNode parameter)
        => parameter.Children.IsDefaultOrEmpty ? parameter.Location.Range.End
            : parameter.IsArray ? null
            : parameter.NameRange?.End;
}
