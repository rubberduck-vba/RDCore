using RDCore.Runtime.Execution;
using RDCore.Runtime.Execution.Frames;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Model.Values.Runtime;
using RDCore.SDK.Runtime.Abstract.Execution;

namespace RDCore.Runtime.Compiled;

// what compiled code writes of a Debug.Print, and the calls of the standard library that give it a String.
public sealed partial class CompiledActivation
{
    /// <summary>
    /// Writes <paramref name="text"/> to the session's output as it is, where <c>Debug.Print</c> writes an item that is a <c>String</c> (MS-VBAL §5.4.5.8).
    /// </summary>
    /// <param name="text">The text of the item; a <see langword="null"/> String writes nothing.</param>
    public void Print(string? text) => _session.Output.Write(text ?? string.Empty);

    /// <summary>
    /// Ends the line of the session's output, as <c>Debug.Print</c> does after its last item when no separator follows it (MS-VBAL §5.4.5.8).
    /// </summary>
    public void PrintLine() => _session.Output.WriteLine();

    /// <summary>
    /// <c>TypeName</c> of the object held in <paramref name="cell"/>, or of <c>Nothing</c> (MS-VBAL §6.1.2.7.1.12): the library's function numbered
    /// <paramref name="library"/> of the body, called as <see cref="CallLibrary"/> calls one, given the object in a <c>Variant</c>.
    /// </summary>
    /// <param name="pc">The offset of the instruction that makes the call.</param>
    /// <param name="library">The number of the call among the body's libraries.</param>
    /// <param name="cell">The object slot's cell, when the argument is not <c>Nothing</c>.</param>
    /// <param name="nothing">Whether the argument is <c>Nothing</c>.</param>
    /// <param name="text">The name the function gave.</param>
    /// <returns>
    /// Whether the function named the object. When not, the interpreter is given the call (<see cref="AfterFailedCall"/>), with the error the function
    /// raised, or to make it as it always does: the cell holds no object compiled code reads.
    /// </returns>
    public bool TryTypeNameOf(int pc, int library, object? cell, bool nothing, out string? text)
    {
        text = null;
        StartLibraryCall(pc, library);
        VBObjectValue value;
        if (nothing)
        {
            value = VBObjectValue.Nothing;
        }
        else if (ObjectOperations.TryRead(cell, out var instance))
        {
            value = new VBObjectValue(instance);
        }
        else
        {
            return false;
        }

        return TryCallLibraryText(library, [VariantCell.Holding(VBObjectType.TypeInfo, value.RuntimeValue)], out text);
    }

    /// <summary>
    /// Calls the function of the standard library numbered <paramref name="library"/> of the body for the <c>String</c> it returns, given the first
    /// <paramref name="count"/> of <paramref name="first"/>, <paramref name="second"/> and <paramref name="third"/>, each the text of a <c>String</c>
    /// argument, as <see cref="CallLibrary"/> calls one: a parameter given none is given what an omitted argument is.
    /// </summary>
    /// <param name="pc">The offset of the instruction that makes the call.</param>
    /// <param name="library">The number of the call among the body's libraries.</param>
    /// <param name="count">How many arguments the call is given.</param>
    /// <param name="first">The first argument, when it is given one.</param>
    /// <param name="second">The second argument, when it is given two.</param>
    /// <param name="third">The third argument, when it is given three.</param>
    /// <param name="text">The text the function returned.</param>
    /// <returns>
    /// Whether the function returned a String compiled code holds. When not, the interpreter is given the call (<see cref="AfterFailedCall"/>), with the
    /// error the function raised, or to make it as it always does.
    /// </returns>
    public bool TryCallLibraryText(int pc, int library, int count, string? first, string? second, string? third, out string? text)
    {
        text = null;
        StartLibraryCall(pc, library);
        var given = new IRuntimeValue[count];
        for (var i = 0; i < count; i++)
        {
            var argument = i == 0 ? first : i == 1 ? second : third;
            given[i] = (argument is null ? VBStringValue.VBNullString : new VBStringValue(argument)).RuntimeValue;
        }

        return TryCallLibraryText(library, given, out text);
    }

    // the call located at, and nothing failed of it yet, before anything can fail: what AfterFailedCall reads of it is this call's.
    private void StartLibraryCall(int pc, int library)
    {
        SetLibraryCall(pc, library);
        (_failed, _suspensionsBeforeCall) = (null, Suspensions);
    }

    // the call made with the arguments given, and the parameters after them given what an omitted argument is; what it returned, a String with no lone byte
    // at its end, which compiled code holds.
    private bool TryCallLibraryText(int library, IRuntimeValue[] given, out string? text)
    {
        text = null;
        var called = _body.Libraries[library];
        var arguments = new IRuntimeValue[called.Parameters.Length];
        for (var i = 0; i < arguments.Length; i++)
        {
            if (i < given.Length)
            {
                arguments[i] = given[i];
                continue;
            }

            var omitted = _executor.Expressions.OmittedArgument(_session, called.Parameters[i]);
            if (!omitted.IsSuccess)
            {
                _failed = omitted.IsInternalError ? RuntimeExecutionOutcome.InternalError : RuntimeExecutionOutcome.Error(omitted.ErrorInfo!);
                return false;
            }

            arguments[i] = omitted.Result!.RuntimeValue;
        }

        if (_executor.Expressions.Bindings is not { } bindings)
        {
            return false;
        }

        var result = bindings.ForMember(called.Function).Call(_session.Symbols.Resolver, arguments);
        if (!result.IsSuccess)
        {
            _failed = result.IsInternalError ? RuntimeExecutionOutcome.InternalError : RuntimeExecutionOutcome.Error(result.ErrorInfo!);
            return false;
        }

        if ((result.Result is VBVariantValue { TypedValue: { } inner } ? inner : result.Result) is not VBStringValue { TrailingByte: null } returned)
        {
            return false;
        }

        text = returned.Value;
        return true;
    }
}
