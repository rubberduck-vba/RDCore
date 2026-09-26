using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Values;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Abstract.StdLib;
using RDCore.SDK.Runtime.Shared;

namespace RDCore.Runtime.StdLib;

/// <inheritdoc cref="IStdErrClass"/>
/// <remarks>
/// The <em>error object</em> has no storage of its own: every one of its properties reads and writes the
/// session's <see cref="ISessionErrorState"/>, which is what makes it a singleton over "the error state
/// of the active VBA Environment" rather than an instance anything could have two of.
/// </remarks>
/// <param name="session">The session whose error state this object is the view of.</param>
/// <param name="raiseLocation">
/// Where in the workspace an <see cref="Raise"/> on this object is written. An error carries the location
/// of what raised it, and an activation record does not say where in itself it is suspended, so the
/// invoker dispatching the call is the only thing that knows it.
/// </param>
public sealed class ErrObject(IRuntimeSession session, SourceLocation raiseLocation = default) : IStdErrClass
{
    private ISessionErrorState State => session.Errors;

    public RuntimeSemanticsEvaluationResult Clear()
    {
        State.Clear();
        return RuntimeSemanticsEvaluationResult.Success(VBVoidValue.Void);
    }

    public RuntimeSemanticsEvaluationResult Raise(
        VBLongValue number,
        VBVariantValue? source = default,
        VBVariantValue? description = default,
        VBVariantValue? helpFile = default,
        VBVariantValue? helpContext = default)
    {
        // "If Raise is invoked without specifying some arguments, and the property settings of the Err
        // object contain values that have not been cleared, those values serve as the values for the new
        // error" - so an omitted argument reads the property rather than resetting it.
        var errorNumber = Convert.ToInt32(number.Handle.Value.BoxedValue);
        State.Source = Text(source) ?? State.Source;
        State.HelpFile = Text(helpFile) ?? State.HelpFile;
        State.HelpContext = Number(helpContext) ?? State.HelpContext;

        // "If unspecified, the value in Number is examined. If it can be mapped to a VBA run-time error
        // code, the String that would be returned by the Error function is used as Description. If there
        // is no VBA error corresponding to Number, the 'Application-defined or object-defined error'
        // message is used" - which is what GetErrorString falls back to.
        var raised = Text(description)
            ?? (State.HasError && State.Description.Length > 0
                ? State.Description
                : VBRuntimeErrorInfo.GetErrorString((VBRuntimeErrorId)errorNumber));

        // the error is returned rather than recorded: the executor's own error interception is where
        // every run-time error reaches the session's error state, and where the stack trace is captured.
        return RuntimeSemanticsEvaluationResult.Error(
            VBApplicationErrorInfo.Raised(errorNumber, raiseLocation, $"Err.Raise {errorNumber}", raised),
            VBVoidValue.Void);
    }

    public RuntimeSemanticsEvaluationResult<VBStringValue> Description()
        => RuntimeSemanticsEvaluationResult<VBStringValue>.Success(new VBStringValue(State.Description));

    public RuntimeSemanticsEvaluationResult Description(VBStringValue value)
        => Set(() => State.Description = Text(value));

    public RuntimeSemanticsEvaluationResult<VBLongValue> HelpContext()
        => RuntimeSemanticsEvaluationResult<VBLongValue>.Success(new VBLongValue(State.HelpContext));

    public RuntimeSemanticsEvaluationResult HelpContext(VBLongValue value)
        => Set(() => State.HelpContext = Convert.ToInt32(value.Handle.Value.BoxedValue));

    public RuntimeSemanticsEvaluationResult<VBStringValue> HelpFile()
        => RuntimeSemanticsEvaluationResult<VBStringValue>.Success(new VBStringValue(State.HelpFile));

    public RuntimeSemanticsEvaluationResult HelpFile(VBStringValue value)
        => Set(() => State.HelpFile = Text(value));

    public RuntimeSemanticsEvaluationResult<VBLongValue> LastDllError()
        => RuntimeSemanticsEvaluationResult<VBLongValue>.Success(new VBLongValue(State.LastDllError));

    public RuntimeSemanticsEvaluationResult<VBLongValue> Number()
        => RuntimeSemanticsEvaluationResult<VBLongValue>.Success(new VBLongValue(State.Number));

    public RuntimeSemanticsEvaluationResult Number(VBLongValue value)
        => Set(() => State.Number = Convert.ToInt32(value.Handle.Value.BoxedValue));

    public RuntimeSemanticsEvaluationResult<VBStringValue> Source()
        => RuntimeSemanticsEvaluationResult<VBStringValue>.Success(new VBStringValue(State.Source));

    public RuntimeSemanticsEvaluationResult Source(VBStringValue value)
        => Set(() => State.Source = Text(value));

    public RuntimeSemanticsEvaluationResult<VBStringValue> StackTrace()
        => RuntimeSemanticsEvaluationResult<VBStringValue>.Success(new VBStringValue(State.StackTrace.ToString()));

    // a Property Let yields no value at all, which is Void rather than Empty: Empty is a real Variant
    // subtype source can see (IsEmpty, VarType, a Let-coercion to 0), and the result of an assignment is
    // not a value source can see anything about. Under Void is the HRESULT every call really has.
    private static RuntimeSemanticsEvaluationResult Set(Action assign)
    {
        assign();
        return RuntimeSemanticsEvaluationResult.Success(VBVoidValue.Void);
    }

    private static string Text(VBStringValue value) => value.Handle.Value.BoxedValue as string ?? string.Empty;

    // an omitted Optional Variant argument is Missing, which is not the same as an empty string: it means
    // "leave the property alone", and only a specified one overwrites it.
    private static string? Text(VBVariantValue? value)
        => value?.Handle.Value.BoxedValue is { } boxed ? Convert.ToString(boxed) ?? string.Empty : null;

    private static int? Number(VBVariantValue? value)
        => value?.Handle.Value.BoxedValue is { } boxed ? Convert.ToInt32(boxed) : null;
}
