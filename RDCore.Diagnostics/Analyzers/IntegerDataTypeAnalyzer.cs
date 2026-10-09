using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics.Model;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Diagnostics;
using RDCore.SDK.Model.Types;

namespace RDCore.Diagnostics.Analyzers;

/// <summary>
/// <c>RDC00201</c>: an <c>As Integer</c> clause, the 16-bit type (<strong>MS-VBAL §2.1</strong> Data Values and Value Types).
/// </summary>
/// <remarks>
/// A <c>Declare</c> statement is not looked into: the types it states are the ones the external procedure takes, which are not the declaring module's to choose.
/// </remarks>
internal sealed class IntegerDataTypeAnalyzer : SyntaxTreeAnalyzer
{
    protected override IEnumerable<AnalyzerFinding> Analyze(ModuleNode module)
        => module.Children
            .Where(member => member is not ExternalMemberDeclarationNode)
            .SelectMany(member => member.Descendants())
            .OfType<AsTypeExpressionNode>()
            .Where(IsInteger)
            .Select(asType => new AnalyzerFinding(
                RDCoreDiagnosticId.IntegerDataTypeDeclaration, asType.Location.Range, DiagnosticSeverity.Information, RDCoreDiagnosticsResources.IntegerDataTypeDeclaration_Message));

    // `VBA.Integer` is the same type; any other qualifier names a type of some other library that happens to be called that.
    private static bool IsInteger(AsTypeExpressionNode asType)
        => string.Equals(asType.TypeName, VBTypeNames.VBInteger, StringComparison.OrdinalIgnoreCase)
           && (asType.QualifierName is null || string.Equals(asType.QualifierName, "VBA", StringComparison.OrdinalIgnoreCase));
}
