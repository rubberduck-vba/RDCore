using RDCore.SDK.Model.AST.Statements;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Values.Bindings;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Abstract.StdLib;
using RDCore.SDK.Runtime.Shared;

namespace RDCore.Runtime.StdLib;

/// <inheritdoc cref="IStdHiddenModule"/>
/// <param name="session">The session whose file channels, call stack and environment these members read.</param>
public sealed class StdHidden(IRuntimeSession session) : IStdHiddenModule
{
    // MS-VBAL 5.4.5.7: "If Line width is less than 0 or greater than 255 an error (number 5...) is raised."
    private const int MaxLineWidth = 255;

    private static RuntimeSemanticsEvaluationResult<TValue> Failed<TValue>(VBRuntimeErrorId error, string detail)
        where TValue : SDK.Model.Values.Abstract.VBTypedValue
        => RuntimeSemanticsEvaluationResult<TValue>.Error(VBRuntimeErrorInfo.For(error, default, detail));

    /// <inheritdoc/>
    public RuntimeSemanticsEvaluationResult<VBVariantValue> Array(params VBVariantValue[] arglist)
    {
        // always zero-based: what follows Option Base is the keyword, which is not this - see ArrayExpressionNode.
        var array = new VBResizableArrayValue([(0, arglist.Length - 1)], VBVariantType.TypeInfo);
        for (var index = 0; index < arglist.Length; index++)
        {
            array.TrySetElement(BindingKnowledge.NewCell(arglist[index]), index);
        }

        return RuntimeSemanticsEvaluationResult<VBVariantValue>.Success(new VBVariantValue(array));
    }

    /// <inheritdoc/>
    public RuntimeSemanticsEvaluationResult<VBVariantValue> Input(VBLongValue number, VBIntegerValue fileNumber)
    {
        var read = ReadCharacters(number.Value, fileNumber.Value);
        return read.IsSuccess
            ? RuntimeSemanticsEvaluationResult<VBVariantValue>.Success(new VBVariantValue(new VBStringValue(read.Result!.Value)))
            : RuntimeSemanticsEvaluationResult<VBVariantValue>.Error(read.ErrorInfo!);
    }

    /// <inheritdoc/>
    public RuntimeSemanticsEvaluationResult<VBStringValue> InputString(VBLongValue number, VBIntegerValue fileNumber)
        => ReadCharacters(number.Value, fileNumber.Value);

    // MS-VBAL 5.4.5.1's table of which statement is valid under which mode and access is the one Input # answers to, and a
    // function that reads the same characters answers to it too.
    private RuntimeSemanticsEvaluationResult<VBStringValue> ReadCharacters(int number, int fileNumber)
    {
        if (number < 0)
        {
            return Failed<VBStringValue>(VBRuntimeErrorId.InvalidProcedureCallOrArgument, $"Input({number}, #{fileNumber}): the number of characters cannot be negative.");
        }

        if (!session.Files.TryGet(fileNumber, out var channel))
        {
            return Failed<VBStringValue>(VBRuntimeErrorId.BadFileNameOrNumber, $"#{fileNumber} is not open");
        }

        if (!FileStatementAccess.IsValid(SDK.Model.Tokens.Input, channel!.Mode, channel.Access))
        {
            return Failed<VBStringValue>(VBRuntimeErrorId.BadFileMode, $"Input is not valid on #{fileNumber}, opened For {channel.Mode} Access {channel.Access}");
        }

        var characters = new System.Text.StringBuilder(number);
        for (var read = 0; read < number; read++)
        {
            var character = channel.Input.Read();
            if (character < 0)
            {
                return Failed<VBStringValue>(VBRuntimeErrorId.InputPastEndOfFile, $"Input({number}, #{fileNumber}) read {read}");
            }

            characters.Append((char)character);
        }

        return RuntimeSemanticsEvaluationResult<VBStringValue>.Success(new VBStringValue(characters.ToString()));
    }

    /// <inheritdoc/>
    public RuntimeSemanticsEvaluationResult<VBVariantValue> InputB(VBLongValue number, VBIntegerValue fileNumber)
    {
        var read = ReadBytes(number.Value, fileNumber.Value);
        return read.IsSuccess
            ? RuntimeSemanticsEvaluationResult<VBVariantValue>.Success(new VBVariantValue(read.Result!))
            : RuntimeSemanticsEvaluationResult<VBVariantValue>.Error(read.ErrorInfo!);
    }

    /// <inheritdoc/>
    public RuntimeSemanticsEvaluationResult<VBStringValue> InputBString(VBLongValue number, VBIntegerValue fileNumber)
        => ReadBytes(number.Value, fileNumber.Value);

    // the bytes themselves, at the same file-pointer-position characters are read at, as a String of those bytes: "ABC" read 2 bytes at a time is one
    // character, of the bytes 41 42 (StrConv(s, vbUnicode) is "AB"). Valid where Input is, and failing as it does (MS-VBA, Excel-verified, oracle\s40
    // I01-I11).
    private RuntimeSemanticsEvaluationResult<VBStringValue> ReadBytes(int number, int fileNumber)
    {
        if (number < 0)
        {
            return Failed<VBStringValue>(VBRuntimeErrorId.InvalidProcedureCallOrArgument, $"InputB({number}, #{fileNumber}): the number of bytes cannot be negative.");
        }

        if (!session.Files.TryGet(fileNumber, out var channel))
        {
            return Failed<VBStringValue>(VBRuntimeErrorId.BadFileNameOrNumber, $"#{fileNumber} is not open");
        }

        if (!FileStatementAccess.IsValid(SDK.Model.Tokens.Input, channel!.Mode, channel.Access))
        {
            return Failed<VBStringValue>(VBRuntimeErrorId.BadFileMode, $"InputB is not valid on #{fileNumber}, opened For {channel.Mode} Access {channel.Access}");
        }

        var bytes = new byte[number];
        for (var read = 0; read < number; read++)
        {
            var next = channel.Input.ReadByte();
            if (next < 0)
            {
                return Failed<VBStringValue>(VBRuntimeErrorId.InputPastEndOfFile, $"InputB({number}, #{fileNumber}) read {read}");
            }

            bytes[read] = (byte)next;
        }

        return RuntimeSemanticsEvaluationResult<VBStringValue>.Success(VBStringValue.FromBytes(bytes));
    }

    /// <inheritdoc/>
    public RuntimeSemanticsEvaluationResult<VBLongPtrValue> ObjPtr(VBLongLongValue ptr)
    {
        // what is at the address is an object, or a Variant holding one: the evaluator passes nothing else here (StdLibByAddressAttribute).
        if (session.Storage is not Execution.Memory.SessionStorage storage || !storage.TryGetObjectPointer(new MemoryAddress(unchecked((int)ptr.Value)), out var pointer))
        {
            return Failed<VBLongPtrValue>(VBRuntimeErrorId.OutOfMemory, "no room for the interface of the object");
        }

        return RuntimeSemanticsEvaluationResult<VBLongPtrValue>.Success(
            session.Environment.Is64Bit ? new VBLongPtrValue(pointer) : new VBLongPtrValue(unchecked((int)pointer)));
    }

    /// <inheritdoc/>
    public RuntimeSemanticsEvaluationResult<VBLongPtrValue> StrPtr(VBLongLongValue ptr)
    {
        // what is at the address is a String, or a Variant holding one: the evaluator passes nothing else here (StdLibByAddressAttribute).
        if (session.Storage is not Execution.Memory.SessionStorage storage || !storage.TryGetStringPointer(new MemoryAddress(unchecked((int)ptr.Value)), out var pointer))
        {
            return Failed<VBLongPtrValue>(VBRuntimeErrorId.OutOfMemory, "no room for the characters of the string");
        }

        return RuntimeSemanticsEvaluationResult<VBLongPtrValue>.Success(
            session.Environment.Is64Bit ? new VBLongPtrValue(pointer) : new VBLongPtrValue(unchecked((int)pointer)));
    }

    /// <inheritdoc/>
    public RuntimeSemanticsEvaluationResult<VBLongPtrValue> VarPtr(VBLongLongValue ptr)
        => RuntimeSemanticsEvaluationResult<VBLongPtrValue>.Success(
            session.Environment.Is64Bit ? new VBLongPtrValue(ptr.Value) : new VBLongPtrValue(unchecked((int)ptr.Value)));

    /// <inheritdoc/>
    public RuntimeSemanticsEvaluationResult Width(VBIntegerValue fileNumber, VBIntegerValue width)
    {
        if (!session.Files.TryGet(fileNumber.Value, out var channel))
        {
            return Fail(VBRuntimeErrorId.BadFileNameOrNumber, $"#{fileNumber.Value} is not open");
        }

        if (!FileStatementAccess.IsValid(SDK.Model.Tokens.Width, channel!.Mode, channel.Access))
        {
            return Fail(VBRuntimeErrorId.BadFileMode, $"Width is not valid on #{fileNumber.Value}, opened For {channel.Mode} Access {channel.Access}");
        }

        if (width.Value is < 0 or > MaxLineWidth)
        {
            return Fail(VBRuntimeErrorId.InvalidProcedureCallOrArgument, $"Width #{fileNumber.Value}, {width.Value}");
        }

        // a file that is read and written in records or bytes has no line to be wide.
        if (channel.Mode is not (VBFileMode.Binary or VBFileMode.Random))
        {
            channel.Output.MaxLineLength = width.Value;
        }

        // a Sub yields no value, which the result has no way to say: Empty is what a call of one is the value of.
        return RuntimeSemanticsEvaluationResult.Success(VBEmptyValue.Empty);
    }

    private static RuntimeSemanticsEvaluationResult Fail(VBRuntimeErrorId error, string detail)
        => RuntimeSemanticsEvaluationResult.Error(VBRuntimeErrorInfo.For(error, default, detail));
}
