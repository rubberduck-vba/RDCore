using RDCore.Runtime.Semantics.Abstract;
using RDCore.Runtime.Semantics.Conversion;
using RDCore.SDK.Model.Values.Meta;
using RDCore.SDK.Model;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Values;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Shared;
using RDCore.SDK.Semantics.Builders;
using RDCore.SDK.Semantics.Context.Abstract;
using RDCore.SDK.Semantics.Flags;
using RDCore.SDK.Services.VerboseMessages;
using System.Globalization;

namespace RDCore.Runtime.Semantics.LetCoercion;

/// <summary>
/// MS-VBAL 5.5.1.2.4 Let-coercion to and from <c>VBStringType</c>
/// </summary>
/// <remarks>
/// The coercion provider dispatches by <em>destination</em> type only, so this class — registered for
/// <see cref="VBStringType"/> — only ever runs when the destination actually is String; it therefore
/// implements the "-&gt; String" half of MS-VBAL 5.5.1.2.4's table (String, any numeric type, Boolean
/// or Date source). The "String -&gt;" half (String as source, coercing to a numeric, Boolean or Date
/// destination) is dispatched by those destination types instead, and lives in
/// <see cref="VBNumericLetCoercionTypeRuntimeSemantics"/>, <see cref="VBBooleanLetCoercionRuntimeSemantics"/>
/// and <see cref="VBDateLetCoercionRuntimeSemantics"/> respectively, for that same reason.
/// <para>
/// 👉 A number or a date is written in the regional settings of the session's environment (<see cref="Regional"/>), the same ones a
/// <c>String</c> is read by: the culture of the thread the interpreter happens to run on is never read.
/// </para>
/// </remarks>
/// <param name="FormatterService">Builds the verbose message of an error.</param>
/// <param name="Culture">The culture a number or a date is written in when no regional settings are given; the invariant culture when none is.</param>
/// <param name="Regional">The regional settings a number or a date is written in.</param>
public record class VBStringLetCoercionRuntimeSemantics(
    IVerboseMessageBuilder FormatterService,
    CultureInfo? Culture = null,
    SDK.Runtime.RegionalSettings? Regional = null)
    : LetCoercionRuntimeSemantics<VBStringType>(FormatterService)
{
    /// <summary>
    /// The culture a number or a date is written in when no regional settings are given.
    /// </summary>
    public CultureInfo EffectiveCulture => Culture ?? CultureInfo.InvariantCulture;

    public override LetCoercionResult EvaluateLetCoercion(
        ISymbolResolver resolver,
        ExpressionNode expression,
        LetCoercionStackFrame frame)
    {
        frame = WithoutVariant(frame);
        return frame.DestinationTypeDesc.Target is VBStringType
            ? FromConversion(ValueConversions.ToText(frame.SourceValue, EffectiveCulture, Regional), expression, frame)
            : LetCoercionResult.NotApplicable(frame);
    }

    protected override ILetCoercionSemanticContextBuilder AnalyzeLetCoercionOperation(
        ILetCoercionSemanticContextBuilder builder,
        ISymbolResolver resolver,
        ExpressionNode expression,
        LetCoercionStackFrame frame)
    {
        // CStr makes the conversion explicit; an Empty source (5.5.1.2.11) and a Byte() source (5.5.1.2.6) are the
        // operands with a rule of their own.
        builder.AddLetCoercionFlags(ConversionSemanticFlags.CTypeAvailable | frame.SourceValue switch
        {
            VBEmptyValue => ConversionSemanticFlags.EmptyOperand,
            VBArrayValue { ItemType: VBByteType } => ConversionSemanticFlags.ByteArrayOperand,
            _ => 0
        }, frame.OperandIndex);
        return builder;
    }
}
