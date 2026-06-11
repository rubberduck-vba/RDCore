using RDCore.SDK.Model;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Meta;
using RDCore.SDK.Runtime;
using RDCore.SDK.Runtime.Abstract;
using RDCore.SDK.Semantics;
using RDCore.SDK.Semantics.Runtime.Abstract;
using RDCore.SDK.Semantics.Runtime.LetCoercion;
using RDCore.SDK.Semantics.Runtime.Operators.Context;

namespace RDCore.Runtime.StdLib;

/// <summary>
/// <strong>MS-VBAL 6.1.2.3 Conversion Module</strong><br/>
/// This class implements the runtime semantics of <c>CType</c> <em>explicit-coercion</em> functions.
/// </summary>
public record class ConversionFunctionSemantics(
    IStaticSymbolsProvider symbolProvider, ISymbolResolver symbolResolver,
    ILetCoercionRuntimeSemanticsProvider letCoercionProvider)
    : RuntimeSemantics<ConversionOperationSemanticContext, ConversionSemanticFlags>()
{
    private readonly ISymbolResolver _resolver = symbolResolver;
    private readonly ILetCoercionRuntimeSemanticsProvider _letCoercion = letCoercionProvider;

    public override ISemanticFlagsAccumulator<ConversionSemanticFlags> Analyze(
        ISymbolResolver resolver,
        ConversionOperationSemanticContext conversionContext,
        ISemanticFlagsAccumulator<ConversionSemanticFlags> builder,
        BoundNode<ConversionOperationSemanticContext, ConversionSemanticFlags> node,
        params VBTypedValue[] inputs) => builder.AddFlags(ConversionSemanticFlags.Explicit 
            | (conversionContext.Errors.Any() ? ConversionSemanticFlags.Failed : 0));

    protected override RuntimeSemanticsEvaluationResult EvaluateSemanticNodeResult(
        ISymbolResolver resolver, 
        SemanticContext<ConversionSemanticFlags> context, 
        BoundStatementNode<ConversionOperationSemanticContext, ConversionSemanticFlags> statement, 
        VBType effectiveType, 
        params VBTypedValue[] inputs)
    {
        var coercionResult = _letCoercion.EvaluateLetCoercionSemantics(_resolver, statement, new LetCoercionStackFrame(
                NodeUri: statement.SemanticId,
                StaticSymbol: symbolProvider.TryGetByName(Tokens.CBool, out var symbol) ? symbol : throw new InvalidOperationException(),
                InputIndex: InputIndex.CTypeSourceValue,
                SourceValue: inputs[(int)InputIndex.CTypeSourceValue],
                DestinationTypeDesc: (VBTypeDescValue)inputs[(int)InputIndex.CTypeTargetType]));

        return coercionResult.IsSuccess
            ? RuntimeSemanticsEvaluationResult.Success(coercionResult.Result!)
            : coercionResult.ErrorInfo is not null
                ? RuntimeSemanticsEvaluationResult.Error(coercionResult.ErrorInfo!)
                : RuntimeSemanticsEvaluationResult.InternalError();
    }
}
