using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Semantics.Runtime.Abstract;
using System.Collections.Immutable;

namespace RDCore.SDK.Semantics.Runtime.LetCoercion
{
    /// <summary>
    /// Represents the result of a let-coercion (implicit type conversion) semantic operation.
    /// </summary>
    /// <param name="IsApplicable"><c>true</c> if the let-coercion strategy is applicable for the specified inputs, <c>false</c> otherwise (another implementation shall then handle the conversion).</param>
    /// <param name="Result">The let-coerced result of the conversion operation, if one was produced.</param>
    /// <param name="ErrorInfo">The error metadata for the run-time error to be thrown, if applicable.</param>
    /// <param name="Frames">The let-coercion evaluation frames associated with this result, if any.</param>
    public readonly record struct LetCoercionResult(
        bool IsApplicable, 
        VBTypedValue? Result, 
        VBRuntimeErrorInfo? ErrorInfo, 
        ImmutableArray<LetCoercionStackFrame> Frames)
    {
        /// <summary>
        /// <c>true</c> if the let-coercion semantic operation was successfully completed.
        /// </summary>
        public bool IsSuccess => IsApplicable && ErrorInfo is null;

        /// <summary>
        /// Gets the current (top-most) frame in the evaluation stack.
        /// </summary>
        public LetCoercionStackFrame Frame => Frames[0];

        /// <summary>
        /// Creates a new (successful) <c>LetCoercionResult</c> with the specified <c>result</c> value.
        /// </summary>
        /// <param name="result">The successfully let-coerced result value.</param>
        public static LetCoercionResult Success(VBTypedValue result, params LetCoercionStackFrame[] frames) => new(true, result, null, [.. frames]);
        /// <summary>
        /// Creates a new (failed) <c>LetCoercionResult</c> with the specified <c>VBRuntimeErrorInfo</c> error information metadata.
        /// </summary>
        /// <param name="info">The runtime error metadata describing the let-coercion failure.</param>
        /// <param name="frames">All the <see cref="LetCoercionStackFrame"/> frames in the let-coercion evaluation stack.</param>
        public static LetCoercionResult Error(VBRuntimeErrorInfo info, params LetCoercionStackFrame[] frames) => new(true, null, info, [.. frames]);
        /// <summary>
        /// Creates a new (not applicable) <c>LetCoercionResult</c> indicating that a let-coercion strategy is not applicable in the context of the evaluated expression.
        /// </summary>
        public static LetCoercionResult NotApplicable(LetCoercionStackFrame frame) => new(false, null, null, [frame]);

        /// <summary>
        /// Gets a copy of this let-coercion result, with the specified <see cref="LetCoercionStackFrame"/> appended to the <c>Frames</c> array.
        /// </summary>
        /// <param name="frame">The let-coercion evaluation stack frame to append to the new result.</param>
        public LetCoercionResult WithFrame(LetCoercionStackFrame frame) => this with { Frames = [frame, ..Frames] };
    }
}
