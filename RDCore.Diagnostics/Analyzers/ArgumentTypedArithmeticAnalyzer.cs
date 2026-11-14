using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics.Model;
using RDCore.SDK.Model;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Semantics.Flags;
using RDCore.SDK.Extensibility.Diagnostics;

namespace RDCore.Diagnostics.Analyzers;

/// <summary>
/// <c>RDC11018</c>: an addition, a subtraction or a multiplication of <c>Byte</c>, <c>Integer</c> or <c>Long</c> values, an operand of which is <c>Abs</c>,
/// <c>Int</c> or <c>Fix</c> of a number, can overflow where <strong>MS-VBAL</strong>'s <c>Variant</c> operation would widen.
/// </summary>
/// <remarks>
/// <strong>MS-VBAL §6.1.2.10.1.1</strong>, <strong>§6.1.2.3.1.16</strong> and <strong>§6.1.2.3.1.18</strong> declare <c>Abs</c>, <c>Fix</c> and <c>Int</c>
/// <c>As Variant</c>; MS-VBA types a call of one of a number as the number's type, and RD-VBA does as MS-VBA does
/// (<c>ArgumentTypedFunctions</c>): <c>Fix(n) * 7000</c> of an <c>Integer</c> raises error 6, where a <c>Variant</c> product
/// would be a <c>Long</c>. A warning, because the program that relies on MS-VBAL's typing overflows. The analyzer finds the operation and the call in the
/// syntax tree, the library's functions by what the call is bound to, and reads the declared types of the operation and of the argument.
/// </remarks>
internal sealed class ArgumentTypedArithmeticAnalyzer : IModuleAnalyzer
{
    private static readonly HashSet<string> ArgumentTyped = new(StringComparer.OrdinalIgnoreCase) { "Math.Abs", "Conversion.Int", "Conversion.Fix" };

    private static readonly HashSet<string> Integral = new(StringComparer.OrdinalIgnoreCase) { VBTypeNames.VBByte, VBTypeNames.VBInteger, VBTypeNames.VBLong };

    private static readonly HashSet<string> Numeric = new(StringComparer.OrdinalIgnoreCase)
    {
        VBTypeNames.VBByte, VBTypeNames.VBInteger, VBTypeNames.VBLong, VBTypeNames.VBLongLong, VBTypeNames.VBSingle, VBTypeNames.VBDouble,
        VBTypeNames.VBCurrency, VBTypeNames.VBDecimal,
    };

    public IEnumerable<AnalyzerFinding> Analyze(ModuleAnalysisContext context)
    {
        if (context.ParseResult.SyntaxTree is not { } module || context.Semantics is not { } semantics)
        {
            return [];
        }

        var facts = semantics.Procedures.SelectMany(procedure => procedure.Expressions).DistinctBy(fact => fact.Node).ToDictionary(fact => fact.Node);
        bool IsArgumentTypedCall(ExpressionNode operand)
            => operand is IndexExpressionNode { Arguments: [var only] } call
                && facts.TryGetValue(call.Identity, out var called) && called is { Binding: { } binding }
                && called.Flags.HasFlag(ValueExpressionSemanticFlags.ProcedureCall | ValueExpressionSemanticFlags.BoundToLibrary)
                && ArgumentTyped.Contains(SymbolUri.QualifiedNameOf(binding))
                && facts.TryGetValue((only is NamedArgumentNode named ? named.Value : only).Identity, out var argument)
                && argument.DeclaredType is { } type && Numeric.Contains(type);

        // the finding is about the operator, located at its token: the operations of a chain (`Fix(a) * 7000 + Fix(b)`) begin at the same position.
        return module.Descendants().OfType<VBBinaryOperatorExpressionNode>()
            .Where(binary => binary.Token is Tokens.AdditionOp or Tokens.SubtractionOp or Tokens.MultiplicationOp
                && facts.TryGetValue(binary.Identity, out var operation) && operation is { Error: null, DeclaredType: { } type } && Integral.Contains(type)
                && (IsArgumentTypedCall(binary.Left) || IsArgumentTypedCall(binary.Right)))
            .Select(binary => new AnalyzerFinding(
                RDCoreDiagnosticId.ArgumentTypedArithmeticOverflow.ToCode(),
                (binary.OperatorLocation ?? facts[binary.Identity].Location).Range,
                DiagnosticSeverity.Warning,
                AnalyzerMessages.Format(RDCoreDiagnosticsResources.ArgumentTypedArithmeticOverflow_Message, facts[binary.Identity].DeclaredType!)));
    }
}
