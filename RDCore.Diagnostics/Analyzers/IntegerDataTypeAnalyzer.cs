using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics.Model;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Extensibility.Diagnostics;
using RDCore.SDK.Platform.Protocol;

namespace RDCore.Diagnostics.Analyzers;

/// <summary>
/// <c>RDC00201</c>: an <c>As Integer</c> clause, the 16-bit type (<strong>MS-VBAL §2.1</strong> Data Values and Value Types).
/// </summary>
/// <remarks>
/// A <c>Declare</c> statement is not looked into: the types it states are the ones the external procedure takes, which are not the declaring module's to choose.
/// Neither are the parameters and the type of an implementation of an interface member or of a handler of an event, which are the member's or the event's
/// (<see cref="UnusedDeclarations.HasAnothersSignature"/>): their locals are reported like any other.
/// </remarks>
internal sealed class IntegerDataTypeAnalyzer : IModuleAnalyzer
{
    public IEnumerable<AnalyzerFinding> Analyze(ModuleAnalysisContext context)
        => context.ParseResult.SyntaxTree is { } module
            ? module.Children
                .Where(member => member is not ExternalMemberDeclarationNode)
                .SelectMany(member => ChosenBy(member, context.Semantics))
                .OfType<AsTypeExpressionNode>()
                .Where(IsInteger)
                .Select(asType => new AnalyzerFinding(
                    RDCoreDiagnosticId.IntegerDataTypeDeclaration.ToCode(), asType.Location.Range, DiagnosticSeverity.Information, RDCoreDiagnosticsResources.IntegerDataTypeDeclaration_Message))
            : [];

    // what of a member's declaration the module chooses: all of it, but the header of a procedure whose signature is another's.
    private static IEnumerable<SyntaxNode> ChosenBy(SyntaxNode member, ModuleSemanticsDto? semantics)
    {
        if (member is not MemberDeclarationNode procedure || !UnusedDeclarations.HasAnothersSignature(procedure, semantics))
        {
            return member.Descendants();
        }

        var header = procedure.HeaderChildren.SelectMany(child => child.Descendants().Prepend(child)).ToHashSet(ReferenceEqualityComparer.Instance);
        return procedure.Descendants().Where(node => !header.Contains(node));
    }

    // `VBA.Integer` is the same type; any other qualifier names a type of some other library that happens to be called that.
    private static bool IsInteger(AsTypeExpressionNode asType)
        => string.Equals(asType.TypeName, VBTypeNames.VBInteger, StringComparison.OrdinalIgnoreCase)
           && (asType.QualifierName is null || string.Equals(asType.QualifierName, "VBA", StringComparison.OrdinalIgnoreCase));
}
