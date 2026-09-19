namespace RDCore.SDK.Model.Errors;

/// <summary>
/// The exception a run-time error is carried by, where an operation can only return a value: an error <em>raised</em> while a
/// callable binding is invoked (<see cref="Values.Bindings.IBindingHandle.Invoke"/>) and that nothing handled.
/// </summary>
/// <remarks>
/// 👉 The semantics that evaluate expressions and statements never throw; they return the error in their result. This exception
/// is the way an error crosses an interface that has no result to return it in.
/// </remarks>
/// <param name="error">The run-time error that was raised.</param>
public sealed class VBRuntimeErrorException(VBRuntimeErrorInfo error) : Exception(error.Description)
{
    /// <summary>
    /// Gets the run-time error that was raised.
    /// </summary>
    public VBRuntimeErrorInfo Error { get; } = error;
}
