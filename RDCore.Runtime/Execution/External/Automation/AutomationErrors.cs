using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Errors.Abstract;
using RDCore.SDK.Model.Source;

namespace RDCore.Runtime.Execution.External.Automation;

/// <summary>
/// Which run-time error an automation server's failure is.
/// </summary>
/// <remarks>
/// A server reports an <c>HRESULT</c>, and VBA has always shown the ones it has a name for as the error of that name: a type mismatch of an argument is error
/// <c>13</c>, a member the object does not have <c>438</c>. An error a server defines for itself arrives in the facility VBA's own errors are in
/// (<c>0x800A....</c>, which is what an Excel <c>1004</c> is), and is the server's error number to the program: it keeps its number and its message, which is the
/// only reason to have called it. Anything else is reported by its <c>HRESULT</c>, as VBA does.
/// </remarks>
internal static class AutomationErrors
{
    // the facility the error numbers of VBA and of the servers that report them as VBA's are in: the low word is the number.
    private const uint VbaFacility = 0x800A0000;
    private const uint FacilityMask = 0xFFFF0000;

    private static readonly Dictionary<uint, VBRuntimeErrorId> Known = new()
    {
        [0x80020005] = VBRuntimeErrorId.TypeMismatch,                        // DISP_E_TYPEMISMATCH
        [0x8002000A] = VBRuntimeErrorId.Overflow,                            // DISP_E_OVERFLOW
        [0x8002000B] = VBRuntimeErrorId.SubscriptOutOfRange,                 // DISP_E_BADINDEX
        [0x80020012] = VBRuntimeErrorId.DivisionByZero,                      // DISP_E_DIVBYZERO
        [0x80020003] = VBRuntimeErrorId.ObjectDoesntSupportThisPropertyOrMethod, // DISP_E_MEMBERNOTFOUND
        [0x80020006] = VBRuntimeErrorId.ObjectDoesntSupportThisPropertyOrMethod, // DISP_E_UNKNOWNNAME
        [0x80020004] = VBRuntimeErrorId.ArgumentNotOptional,                 // DISP_E_PARAMNOTFOUND
        [0x8002000F] = VBRuntimeErrorId.ArgumentNotOptional,                 // DISP_E_PARAMNOTOPTIONAL
        [0x8002000E] = VBRuntimeErrorId.WrongNumberOfArgumentsOrInvalidPropertyAssignment, // DISP_E_BADPARAMCOUNT
        [0x80040154] = VBRuntimeErrorId.ActiveXComponentCantCreateObject,    // REGDB_E_CLASSNOTREG
        [0x800401F3] = VBRuntimeErrorId.ActiveXComponentCantCreateObject,    // CO_E_CLASSSTRING
        [0x800401E3] = VBRuntimeErrorId.ActiveXComponentCantCreateObject,    // MK_E_UNAVAILABLE
        [0x80080005] = VBRuntimeErrorId.ActiveXComponentCantCreateObject,    // CO_E_SERVER_EXEC_FAILURE
        [0x800706BA] = VBRuntimeErrorId.RemoteMachineNotAvailable,           // RPC_S_SERVER_UNAVAILABLE
        [0x800706BE] = VBRuntimeErrorId.RemoteMachineNotAvailable,           // RPC_S_CALL_FAILED
        [0x80010108] = VBRuntimeErrorId.RemoteMachineNotAvailable,           // RPC_E_DISCONNECTED
        [0x80070005] = VBRuntimeErrorId.PermissionDenied,                    // E_ACCESSDENIED
    };

    /// <summary>
    /// The run-time error that <paramref name="failure"/> is.
    /// </summary>
    /// <param name="failure">What the server reported.</param>
    /// <param name="site">Where the call that failed is written.</param>
    /// <param name="call">The call, as the error says which one failed.</param>
    public static IVBRaisableError ToError(AutomationException failure, SourceLocation site, string call)
    {
        var code = unchecked((uint)failure.HResult);
        var said = string.IsNullOrWhiteSpace(failure.Message) ? null : failure.Message;
        var verbose = said is null ? $"{call} failed (0x{code:X8})." : $"{call} failed (0x{code:X8}): {said}";

        if (Known.TryGetValue(code, out var known))
        {
            return VBRuntimeErrorInfo.For(known, site, verbose);
        }

        // an error the server raised itself, in VBA's facility: its number is the program's Err.Number, and its message the program's Err.Description.
        return VBApplicationErrorInfo.Raised((code & FacilityMask) == VbaFacility ? (int)(code & 0xFFFF) : failure.HResult, site, verbose, said);
    }
}
