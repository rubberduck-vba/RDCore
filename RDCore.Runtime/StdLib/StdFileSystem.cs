using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Abstract.StdLib;
using RDCore.SDK.Runtime.Shared;

namespace RDCore.Runtime.StdLib;

/// <inheritdoc cref="IStdFileSystemModule"/>
/// <remarks>
/// 🚧 Only <see cref="FreeFile"/> so far, because it is the one member of this module that needs nothing but
/// the session's own channel table — the rest touch the file system, and belong with the statements that do.
/// </remarks>
/// <param name="session">The session whose open channels decide which numbers are free.</param>
public sealed class StdFileSystem(IRuntimeSession session) : IStdFileSystemModule
{
    // MS-VBAL 6.1.2.5.1.7: "Specify the data value 0 (default) to return a file number in the range 1-255,
    // inclusive. Specify the data value 1 to return a file number in the range 256-511, inclusive."
    private const int LowRangeFirst = 1;
    private const int LowRangeLast = 255;
    private const int HighRangeFirst = 256;
    private const int HighRangeLast = 511;

    /// <inheritdoc/>
    public RuntimeSemanticsEvaluationResult<VBIntegerValue> FreeFile(VBVariantValue? rangeNumber = default)
    {
        var range = rangeNumber?.Handle.Value.BoxedValue is { } boxed ? Convert.ToInt32(boxed) : 0;
        var (first, last) = range switch
        {
            0 => (LowRangeFirst, LowRangeLast),
            1 => (HighRangeFirst, HighRangeLast),
            // there are two ranges and no others; VBA's own answer to a third is to refuse the call.
            _ => (0, 0),
        };

        if (first == 0)
        {
            return RuntimeSemanticsEvaluationResult<VBIntegerValue>.Error(VBRuntimeErrorInfo.For(
                VBRuntimeErrorId.InvalidProcedureCallOrArgument, default,
                $"FreeFile({range}): a range number is 0 (file numbers 1-255) or 1 (file numbers 256-511)."));
        }

        for (var candidate = first; candidate <= last; candidate++)
        {
            if (!session.Files.TryGet(candidate, out _))
            {
                return RuntimeSemanticsEvaluationResult<VBIntegerValue>.Success(new VBIntegerValue((short)candidate));
            }
        }

        // every number in the range is taken. 67 is what VBA says when it cannot give out another one.
        return RuntimeSemanticsEvaluationResult<VBIntegerValue>.Error(VBRuntimeErrorInfo.For(
            VBRuntimeErrorId.TooManyFiles, default,
            $"every file number from {first} to {last} is already open."));
    }
}
