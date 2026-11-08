using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Bindings;
using RDCore.SDK.Model.Values.Meta;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Shared;

namespace RDCore.Runtime.Semantics;

/// <summary>
/// Records what the runtime was doing when it raised an error, as data a client formats its own way (<see cref="VBErrorDetails"/>).
/// </summary>
public interface IErrorDetailsRecorder
{
    /// <summary>
    /// The details of an error raised at <paramref name="expression"/>, in the operation whose frames are <paramref name="frames"/>.
    /// </summary>
    /// <param name="message">Why the operation failed, in a sentence: the error's plain-text verbose message.</param>
    /// <param name="expression">The expression the error is raised at.</param>
    /// <param name="frames">The frames of the operation, innermost first.</param>
    VBErrorDetails Record(string message, ExpressionNode expression, IEnumerable<IStackFrame> frames);
}

/// <summary>
/// Records the details of an error when it is raised, and only then: the expression's kind, identity and location, and each frame's node and inputs, with
/// the let-coercion a frame is. At most <see cref="VBErrorDetails.MaxFrames"/> frames are recorded, the innermost ones.
/// </summary>
public sealed class ErrorDetailsRecorder : IErrorDetailsRecorder
{
    /// <inheritdoc/>
    public VBErrorDetails Record(string message, ExpressionNode expression, IEnumerable<IStackFrame> frames)
    {
        // one frame past the bound says whether there were more, without reading them all.
        var recorded = frames.Take(VBErrorDetails.MaxFrames + 1).ToList();
        var truncated = recorded.Count > VBErrorDetails.MaxFrames;
        return new(message, new VBErrorExpression(expression.GetType().Name, expression.Identity.ToString(), expression.Location),
            [.. recorded.Take(VBErrorDetails.MaxFrames).Select(FrameOf)], truncated);
    }

    private static VBErrorFrame FrameOf(IStackFrame frame)
        => new(frame.NodeId.ToString(), [.. frame.Inputs.Select(OperandOf)],
            frame is LetCoercionStackFrame coercion ? new VBErrorCoercion(OperandOf(coercion.SourceValue), coercion.DestinationTypeDesc.Target.Name) : null);

    // a type an operation is given holds no value to say.
    private static VBErrorOperand OperandOf(VBTypedValue input)
        => new(input.TypeInfo.Name, input is VBTypeDescValue || input.Handle is InvalidBindingHandle ? null : input.RuntimeValue.ToString());
}
