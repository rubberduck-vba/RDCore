using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics.Model;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.Diagnostics;

namespace RDCore.Diagnostics.Analyzers;

/// <summary>
/// <c>RDC00106</c>: a variable or a parameter that is declared without a type, and is a <c>Variant</c> (<strong>MS-VBAL §5.2.3.1.5</strong>).
/// </summary>
/// <remarks>
/// A name that a <c>Def&lt;Type&gt;</c> directive covers is given that type and not a <c>Variant</c>, which <see cref="TypeDefDirectiveAnalyzer"/> reports at the directive.
/// A constant is not among them: its type is the type of its value.
/// </remarks>
internal sealed class ImplicitVariantDeclarationAnalyzer : SyntaxTreeAnalyzer
{
    protected override IEnumerable<AnalyzerFinding> Analyze(ModuleNode module)
    {
        foreach (var node in module.Descendants())
        {
            var (name, typeHint, range) = node switch
            {
                VariableDeclarationNode variable => (variable.Name, variable.TypeHint, variable.NameRange ?? variable.SourceLocation.Range),
                ParameterDeclarationNode parameter => (parameter.Name, parameter.TypeHint, parameter.Location.Range),
                _ => (null, null, default),
            };

            if (name is not null && !ImplicitTypes.IsTypeStated(node, typeHint) && ImplicitTypes.IsVariant(module, name))
            {
                yield return new AnalyzerFinding(
                    RDCoreDiagnosticId.ImplicitVariantDeclaration, range, DiagnosticSeverity.Information, Say(RDCoreDiagnosticsResources.ImplicitVariantDeclaration_Message, name));
            }
        }
    }
}
