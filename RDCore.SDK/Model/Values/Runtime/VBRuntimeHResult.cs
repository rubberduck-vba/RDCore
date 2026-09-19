namespace RDCore.SDK.Model.Values.Runtime;

/// <summary>
/// The runtime value of a call that yields no value: an <c>HRESULT</c>, the result code every COM method returns.
/// </summary>
/// <remarks>
/// 👉 This is what is under the <c>Void</c> value (<see cref="VBVoidValue"/>), the "not a real value" of a <c>Sub</c>: the language
/// gives the call no value to use, the runtime still has the result code of the call. <c>0</c> is <c>S_OK</c>; like any <c>HRESULT</c>,
/// a negative code is a failure.
/// </remarks>
/// <param name="Code">The 32-bit <c>HRESULT</c>.</param>
public readonly record struct VBRuntimeHResult(int Code) : IRuntimeValue<int>
{
    /// <summary>
    /// <c>S_OK</c>: the operation succeeded.
    /// </summary>
    public static VBRuntimeHResult Ok => new(0);

    /// <summary>
    /// Gets whether the code is a success (<c>SUCCEEDED</c>): it is not negative.
    /// </summary>
    public bool IsSuccess => Code >= 0;

    /// <summary>
    /// Gets whether the code is a failure (<c>FAILED</c>): it is negative.
    /// </summary>
    public bool IsFailure => Code < 0;

    /// <inheritdoc/>
    public int StoredValue => Code;

    /// <inheritdoc/>
    public object BoxedValue => Code;
}
