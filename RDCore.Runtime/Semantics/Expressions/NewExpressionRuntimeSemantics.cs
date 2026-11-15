using RDCore.Runtime.Execution.External;
using RDCore.Runtime.Semantics.Abstract;
using RDCore.Runtime.Semantics.Literals;
using RDCore.SDK;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Model.Values.Meta;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Shared;
using RDCore.SDK.Semantics.Builders;
using RDCore.SDK.Semantics.Context;
using RDCore.SDK.Semantics.Context.Abstract;
using RDCore.SDK.Semantics.Flags;
using RDCore.Runtime.LanguageCore.Errors;

namespace RDCore.Runtime.Semantics.Expressions;

/// <summary>
/// <strong>MS-VBAL 5.6.8</strong> New Expressions (runtime semantics). Instantiates a new object of
/// the class referenced by the expression's <c>TypeExpression</c> and yields that object — the target
/// flows through the operand pipeline as a <see cref="VBSymbolDescValue"/> (the class module symbol
/// static semantics already resolved), the same meta-value pattern
/// <c>BinaryLetAssignmentOperatorRuntimeSemantics</c> uses for its assignment target.
/// </summary>
/// <remarks>
/// Only allocates the object's identity and instance-field storage
/// (<see cref="ISessionObjects.CreateObject"/> + <see cref="ISessionSymbols.CreateInstance"/>) — it
/// does not invoke <c>Class_Initialize</c> (needs procedure-invocation machinery, a separate concern)
/// and does not itself track the resulting reference's lifetime (that's
/// <see cref="ISessionObjects.AddRef"/>, driven by whatever binds the result — an assignment target).
/// </remarks>
public sealed record class NewExpressionRuntimeSemantics : RuntimeSemantics<ValueExpressionSemanticContext, ValueExpressionSemanticFlags>
{
    private static readonly Lazy<NewExpressionRuntimeSemantics> _instance = new(() => new(), LazyThreadSafetyMode.PublicationOnly);
    public static NewExpressionRuntimeSemantics Instance => _instance.Value;

    public override ISemanticFlagsAccumulator<ValueExpressionSemanticFlags> Analyze(
        IRuntimeSession session,
        ConversionOperationSemanticContext conversionContext,
        ISemanticFlagsAccumulator<ValueExpressionSemanticFlags> builder,
        SyntaxNode node,
        params VBTypedValue[] inputs) => builder;

    public override RuntimeSemanticsEvaluationResult Evaluate(
        IRuntimeSession session,
        ValueExpressionSemanticContext context,
        SyntaxNode node,
        params VBTypedValue[] inputs)
    {
        if (inputs is not [VBSymbolDescValue { Symbol: VBClassModuleSymbol classModule }])
        {
            // MS-VBAL static semantics should already have rejected a TypeExpression that doesn't
            // reference an instantiable class — reaching here means that check was skipped.
            var typeName = inputs is [VBSymbolDescValue { Symbol: { } symbol }] ? symbol.Name : "?";
            return RuntimeSemanticsEvaluationResult.Error(OnRuntimeError(VBRuntimeErrorId.ActiveXComponentCantCreateObject, node,
                Exceptions.VBRuntimeError_ActiveXComponentCantCreateObject_NotAClass_Verbose.Replace("{$TYPENAME}", typeName)));
        }

        if (CreationFailure(classModule, node.SourceLocation) is { } refused)
        {
            return refused;
        }

        var objectId = session.Objects.CreateObject();
        session.Symbols.CreateInstance(objectId, classModule);
        if (ExternalObjectCreation.Create(session, classModule, objectId, node.SourceLocation) is { } failed)
        {
            return failed;
        }

        return RuntimeSemanticsEvaluationResult.Success(new VBObjectValue(objectId));
    }

    // REGDB_E_CLASSNOTREG, which MS-VBA reports by its HRESULT.
    private const int ClassNotRegistered = unchecked((int)0x80040154);

    /// <summary>
    /// The error of creating an object of <paramref name="classModule"/>, when no server is registered for it (<see cref="SymbolProperties.Unregistered"/>):
    /// run-time error -2147221164 <em>Class not registered</em>, as MS-VBA raises it for <c>New ErrObject</c> and for a reference to a variable declared
    /// <c>As New ErrObject</c> (recorded in Excel). <see langword="null"/> for any other class.
    /// </summary>
    internal static RuntimeSemanticsEvaluationResult? CreationFailure(VBClassModuleSymbol classModule, SourceLocation site)
        => classModule.GetProperty(SymbolProperties.Unregistered)
            ? RuntimeSemanticsEvaluationResult.Error(VBApplicationErrorInfo.Raised(ClassNotRegistered, site,
                string.Format(Exceptions.VBRuntimeError_ClassNotRegistered_CreationAlwaysFails_Verbose, classModule.Name,
                    classModule.GetProperty(SymbolProperties.Library)),
                Exceptions.VBRuntimeError_ClassNotRegistered_Description))
            : null;
}
