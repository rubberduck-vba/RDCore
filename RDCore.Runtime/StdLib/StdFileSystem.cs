using RDCore.Runtime.Semantics.Conversion;
using RDCore.SDK.Model.AST.Statements;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Abstract.StdLib;
using RDCore.SDK.Runtime.Shared;

namespace RDCore.Runtime.StdLib;

/// <inheritdoc cref="IStdFileSystemModule"/>
/// <param name="session">The session whose file channels the members ask, and go through to the file system.</param>
public sealed class StdFileSystem(IRuntimeSession session) : IStdFileSystemModule
{
    // MS-VBAL 6.1.2.5.1.7: "Specify the data value 0 (default) to return a file number in the range 1-255,
    // inclusive. Specify the data value 1 to return a file number in the range 256-511, inclusive."
    private const int LowRangeFirst = 1;
    private const int LowRangeLast = 255;
    private const int HighRangeFirst = 256;
    private const int HighRangeLast = 511;

    // the bytes MS-VBA counts the Loc of a sequential file in (MS-VBAL 6.1.2.5.1.8: "Current byte position in the file divided by 128").
    private const int SequentialBlock = 128;

    private static RuntimeSemanticsEvaluationResult<TValue> Failed<TValue>(VBRuntimeErrorId error, string detail)
        where TValue : VBTypedValue
        => RuntimeSemanticsEvaluationResult<TValue>.Error(VBRuntimeErrorInfo.For(error, default, detail));

    private static RuntimeSemanticsEvaluationResult Done(VBRuntimeErrorId? error, string detail)
        => error is { } failed ? RuntimeSemanticsEvaluationResult.Error(VBRuntimeErrorInfo.For(failed, default, detail)) : RuntimeSemanticsEvaluationResult.Success(VBEmptyValue.Empty);

    private static RuntimeSemanticsEvaluationResult<TValue> Answer<TValue>(VBRuntimeErrorId? error, Func<TValue> value, string detail)
        where TValue : VBTypedValue
        => error is { } failed ? Failed<TValue>(failed, detail) : RuntimeSemanticsEvaluationResult<TValue>.Success(value());

    // the text of a Variant argument that names a drive or a file: Null is no text (error 94), and an omitted one is none.
    private RuntimeSemanticsEvaluationResult<VBStringValue>? TextOf(VBVariantValue? argument, out string? text)
    {
        text = null;
        if (argument is null || argument.TypedValue is VBMissingValue)
        {
            return null;
        }

        if (argument.TypedValue is VBNullValue)
        {
            return Failed<VBStringValue>(VBRuntimeErrorId.InvalidUseOfNull, "a path is no Null");
        }

        var converted = ValueConversions.ToText(argument.TypedValue, session.Environment.Culture);
        if (converted is not { IsSuccess: true, Value: VBStringValue value })
        {
            return Failed<VBStringValue>(converted.Error ?? VBRuntimeErrorId.TypeMismatch, "a path is a String");
        }

        text = value.Value ?? string.Empty;
        return null;
    }

    private RuntimeSemanticsEvaluationResult<TValue> OfChannel<TValue>(VBIntegerValue fileNumber, Func<IFileChannel, TValue> value)
        where TValue : VBTypedValue
        => session.Files.TryGet(fileNumber.Value, out var channel)
            ? RuntimeSemanticsEvaluationResult<TValue>.Success(value(channel!))
            : Failed<TValue>(VBRuntimeErrorId.BadFileNameOrNumber, $"#{fileNumber.Value} is not open");

    /// <inheritdoc/>
    public RuntimeSemanticsEvaluationResult<VBVariantValue> CurDir(VBVariantValue? drive = default)
    {
        var text = CurDirString(drive);
        return text.IsSuccess
            ? RuntimeSemanticsEvaluationResult<VBVariantValue>.Success(new VBVariantValue(text.Result!))
            : RuntimeSemanticsEvaluationResult<VBVariantValue>.Error(text.ErrorInfo!);
    }

    /// <inheritdoc/>
    public RuntimeSemanticsEvaluationResult<VBStringValue> CurDirString(VBVariantValue? drive = default)
    {
        if (TextOf(drive, out var letter) is { } failed)
        {
            return failed;
        }

        var error = session.Files.TryGetCurrentDirectory(letter, out var path);
        return Answer(error, () => new VBStringValue(path), $"CurDir({letter})");
    }

    /// <inheritdoc/>
    public RuntimeSemanticsEvaluationResult<VBStringValue> Dir(VBVariantValue? pathName = default, VBFileAttribute attributes = VBFileAttribute.VBNormal)
    {
        if (TextOf(pathName, out var pattern) is { } failed)
        {
            return failed;
        }

        string name;
        var error = pattern is null ? session.Files.TryFindNext(out name) : session.Files.TryFindFirst(pattern, attributes, out name);
        return Answer(error, () => new VBStringValue(name), pattern is null ? "Dir() with no pattern to go on with" : $"Dir({pattern})");
    }

    /// <inheritdoc/>
    public RuntimeSemanticsEvaluationResult<VBBooleanValue> EOF(VBIntegerValue fileNumber)
        => OfChannel(fileNumber, channel => new VBBooleanValue(channel.Mode switch
        {
            VBFileMode.Input => channel.Input.IsEndOfFile,
            VBFileMode.Output or VBFileMode.Append => true,
            _ => channel.IsPastEndOfRecords,
        }));

    /// <inheritdoc/>
    public RuntimeSemanticsEvaluationResult<VBLongValue> FileAttr(VBIntegerValue fileNumber, VBIntegerValue? returnType = default)
    {
        if (!session.Files.TryGet(fileNumber.Value, out var channel))
        {
            return Failed<VBLongValue>(VBRuntimeErrorId.BadFileNameOrNumber, $"#{fileNumber.Value} is not open");
        }

        // 2, the handle, is refused by 64-bit MS-VBA as any other is (oracle\s25 A01); RD-VBA has no handle to give.
        if ((returnType?.Value ?? 1) != 1)
        {
            return Failed<VBLongValue>(VBRuntimeErrorId.InvalidProcedureCallOrArgument, $"FileAttr(#{fileNumber.Value}, {returnType!.Value}): only 1, the mode");
        }

        return RuntimeSemanticsEvaluationResult<VBLongValue>.Success(new VBLongValue(channel!.Mode switch
        {
            VBFileMode.Input => 1,
            VBFileMode.Output => 2,
            VBFileMode.Random => 4,
            VBFileMode.Append => 8,
            _ => 32,
        }));
    }

    /// <inheritdoc/>
    public RuntimeSemanticsEvaluationResult<VBVariantValue> FileDateTime(VBStringValue pathName)
    {
        var path = pathName.Value ?? string.Empty;
        var error = session.Files.TryGetLastWriteTime(path, out var modified);
        return Answer(error, () => new VBVariantValue(new VBDateValue(modified.ToOADate())), $"FileDateTime({path})");
    }

    /// <inheritdoc/>
    public RuntimeSemanticsEvaluationResult<VBLongValue> FileLen(VBStringValue pathName)
    {
        var path = pathName.Value ?? string.Empty;
        var error = session.Files.TryGetLength(path, out var length);
        return Answer(error, () => new VBLongValue(unchecked((int)length)), $"FileLen({path})");
    }

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

    /// <inheritdoc/>
    public RuntimeSemanticsEvaluationResult<VBLongValue> Loc(VBIntegerValue fileNumber)
        => OfChannel(fileNumber, channel => new VBLongValue(unchecked((int)(channel.Mode switch
        {
            // the last record, or byte, read or written: the one before the next (oracle\s25 O02, O03).
            VBFileMode.Random or VBFileMode.Binary => channel.Position - 1,
            // what MS-VBA has read in blocks of 128 bytes, the first one read when the file is opened; written, the blocks written (Q01, Q02).
            VBFileMode.Input => (channel.Position - 1) / SequentialBlock + 1,
            _ => (channel.Position - 1 - channel.Origin) / SequentialBlock,
        }))));

    /// <inheritdoc/>
    public RuntimeSemanticsEvaluationResult<VBLongValue> LOF(VBIntegerValue fileNumber)
        => OfChannel(fileNumber, channel => new VBLongValue(unchecked((int)channel.Length)));

    /// <inheritdoc/>
    public RuntimeSemanticsEvaluationResult<VBLongValue> Seek(VBIntegerValue fileNumber)
        => OfChannel(fileNumber, channel => new VBLongValue(unchecked((int)(channel.Position - channel.Origin))));

    /// <inheritdoc/>
    public RuntimeSemanticsEvaluationResult ChDir(VBStringValue path)
        => Done(session.Files.TryChangeDirectory(path.Value ?? string.Empty), $"ChDir {path.Value}");

    /// <inheritdoc/>
    public RuntimeSemanticsEvaluationResult ChDrive(VBStringValue drive)
        => Done(session.Files.TryChangeDrive(drive.Value ?? string.Empty), $"ChDrive {drive.Value}");

    /// <inheritdoc/>
    public RuntimeSemanticsEvaluationResult FileCopy(VBStringValue source, VBStringValue destination)
        => Done(session.Files.TryCopy(source.Value ?? string.Empty, destination.Value ?? string.Empty), $"FileCopy {source.Value}, {destination.Value}");

    /// <inheritdoc/>
    public RuntimeSemanticsEvaluationResult Kill(VBVariantValue pathName)
    {
        if (TextOf(pathName, out var pattern) is { } failed)
        {
            return RuntimeSemanticsEvaluationResult.Error(failed.ErrorInfo!);
        }

        return Done(session.Files.TryDelete(pattern ?? string.Empty), $"Kill {pattern}");
    }

    /// <inheritdoc/>
    public RuntimeSemanticsEvaluationResult MkDir(VBStringValue path)
        => Done(session.Files.TryCreateDirectory(path.Value ?? string.Empty), $"MkDir {path.Value}");

    /// <inheritdoc/>
    public RuntimeSemanticsEvaluationResult RmDir(VBStringValue path)
        => Done(session.Files.TryRemoveDirectory(path.Value ?? string.Empty), $"RmDir {path.Value}");

    /// <inheritdoc/>
    public RuntimeSemanticsEvaluationResult SetAttr(VBStringValue pathName, VBFileAttribute attributes)
        => Done(session.Files.TrySetAttributes(pathName.Value ?? string.Empty, attributes), $"SetAttr {pathName.Value}, {(int)attributes}");
}
