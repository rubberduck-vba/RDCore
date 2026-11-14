using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics.Model;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Extensibility.Diagnostics;

namespace RDCore.Diagnostics.Analyzers;

/// <summary>
/// <c>RDC00201</c>: a declaration of the 16-bit <c>Integer</c> type (<strong>MS-VBAL §2.1</strong> Data Values and Value Types), by an <c>As Integer</c>
/// clause or by the <c>%</c> type-declaration character (<strong>MS-VBAL §3.3.5.3</strong> Special Identifier Forms): <c>Dim n%</c>, <c>p%</c>,
/// <c>Const c% = 1</c>, <c>Function F%()</c>.
/// </summary>
/// <remarks>
/// A <c>Declare</c> statement is not looked into: the types it states are the ones the external procedure takes, which are not the declaring module's to choose.
/// </remarks>
internal sealed class IntegerDataTypeAnalyzer : SyntaxTreeAnalyzer
{
    protected override IEnumerable<AnalyzerFinding> Analyze(ModuleNode module)
        => module.Children
            .Where(member => member is not ExternalMemberDeclarationNode)
            .SelectMany(member => member.Descendants().Prepend(member))
            .Select(node => node switch
            {
                AsTypeExpressionNode asType when IsInteger(asType) => asType.Location.Range,
                VariableDeclarationNode variable => IntegerHintOf(variable.TypeHint, variable.NameRange, variable.SourceLocation),
                ConstantDeclarationNode constant => IntegerHintOf(constant.TypeHint, constant.NameRange, constant.Location),
                ParameterDeclarationNode parameter => IntegerHintOf(parameter.TypeHint, parameter.NameRange, parameter.Location),
                MemberDeclarationNode member => IntegerHintOf(member.TypeHint, member.NameRange, member.SourceLocation),
                _ => null,
            })
            .OfType<SourceRange>()
            .Select(range => new AnalyzerFinding(
                RDCoreDiagnosticId.IntegerDataTypeDeclaration.ToCode(), range, DiagnosticSeverity.Information, RDCoreDiagnosticsResources.IntegerDataTypeDeclaration_Message));

    // `VBA.Integer` is the same type; any other qualifier names a type of some other library that happens to be called that.
    private static bool IsInteger(AsTypeExpressionNode asType)
        => string.Equals(asType.TypeName, VBTypeNames.VBInteger, StringComparison.OrdinalIgnoreCase)
           && (asType.QualifierName is null || string.Equals(asType.QualifierName, "VBA", StringComparison.OrdinalIgnoreCase));

    // the name written with the type-declaration character of an Integer; the whole declaration when the range of its name is not known.
    private static SourceRange? IntegerHintOf(string? hint, SourceRange? name, SourceLocation declaration)
        => hint is not null && IntrinsicVBTypes.TryResolveTypeHint(hint, out var type) && type is VBIntegerType
            ? name ?? declaration.Range
            : null;
}
