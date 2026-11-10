using RDCore.Runtime.StdLib;
using RDCore.SDK.Model.Values.Intrinsic;

namespace RDCore.Runtime.Compiled;

// what compiled code calls of the standard library itself: a member that only computes what it returns from its arguments, called as the library's
// implementation is. An argument it raises an error for is the interpreter's, which makes the call again and raises it.
public sealed partial class CompiledActivation
{
    // the library's members of the Strings module, as the session's: what they compute depends on the session's environment (its code page) alone.
    private StdStrings? _strings;

    /// <summary>
    /// <c>Chr$</c> of <paramref name="code"/> (MS-VBAL §6.1.2.11.1.4), as the library's implementation makes it: the character the code names in the code
    /// page of the session's environment.
    /// </summary>
    /// <param name="code">The character code.</param>
    /// <param name="text">The character, as a String of one.</param>
    /// <returns>
    /// <see langword="false"/> when the interpreter makes the call: the code is outside the range of a <c>Long</c>, or names no character, which is an error the interpreter
    /// raises; or the String made ends with a lone byte, which the interpreter holds.
    /// </returns>
    public bool TryChr(long code, out string? text)
    {
        text = null;
        if (FailsAccessAtRandom() || code is < int.MinValue or > int.MaxValue
            || (_strings ??= new StdStrings(_session)).ChrString(new VBLongValue((int)code)) is not { IsSuccess: true, Result: { TrailingByte: null } character })
        {
            return false;
        }

        text = character.Value;
        return true;
    }
}
