using RDCore.Runtime.Execution.External.Forms;
using RDCore.Runtime.LanguageCore.StdLib.Abstract;
using RDCore.Runtime.Semantics.Conversion;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Shared;
using RDCore.Runtime.LanguageCore.Errors;

namespace RDCore.Runtime.StdLib;

/// <inheritdoc cref="IStdFormsClass"/>
/// <param name="session">The session whose forms these are.</param>
/// <param name="enumerators">What makes the enumerator a <c>For Each</c> over the forms is driven by.</param>
internal sealed class StdForms(IRuntimeSession session, StdEnumVariant enumerators) : IStdFormsClass
{
    /// <inheritdoc/>
    public RuntimeSemanticsEvaluationResult<VBIntegerValue> Count()
        => RuntimeSemanticsEvaluationResult<VBIntegerValue>.Success(new VBIntegerValue((short)SessionForms.Of(session).Loaded.Count));

    /// <inheritdoc/>
    public RuntimeSemanticsEvaluationResult<VBObjectValue> Item(VBVariantValue index)
    {
        // there are no keys: a Null or a String that is no number is a type mismatch, not a name.
        var given = index.TypedValue;
        var converted = given is VBNullValue
            ? ValueConversionResult.Failure(VBRuntimeErrorId.TypeMismatch)
            : ValueConversions.ToNumeric(given, VBIntegerType.TypeInfo, session.Environment.Regional);
        if (!converted.IsSuccess)
        {
            return RuntimeSemanticsEvaluationResult<VBObjectValue>.Error(VBRuntimeErrorInfo.For(converted.Error ?? VBRuntimeErrorId.TypeMismatch, default,
                $"A value of type {given.TypeInfo.Name} is no position of a form."));
        }

        var position = ValueConversions.WholeValueOf(converted.Value!) ?? -1;
        var loaded = SessionForms.Of(session).Loaded;
        return position >= 0 && position < loaded.Count
            ? RuntimeSemanticsEvaluationResult<VBObjectValue>.Success(new VBObjectValue(loaded[(int)position]))
            : RuntimeSemanticsEvaluationResult<VBObjectValue>.Error(VBRuntimeErrorInfo.For(VBRuntimeErrorId.SubscriptOutOfRange, default,
                loaded.Count == 0 ? $"There is no form {position}: no form is loaded." : $"There is no form {position}: the forms are numbered from 0 to {loaded.Count - 1}."));
    }

    /// <inheritdoc/>
    public RuntimeSemanticsEvaluationResult<VBObjectValue> NewEnum()
        => enumerators.Create([.. SessionForms.Of(session).Loaded.Select(form => new VBObjectValue(form))]) is { } enumerator
            ? RuntimeSemanticsEvaluationResult<VBObjectValue>.Success(enumerator)
            : RuntimeSemanticsEvaluationResult<VBObjectValue>.InternalError();
}
