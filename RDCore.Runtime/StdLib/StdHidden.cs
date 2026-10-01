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
/// <remarks>
/// 🚧 <see cref="StrPtr"/>, <see cref="VarPtr"/>, <see cref="InputB"/> and <see cref="InputBString"/> are declared and not written:
/// each says why, in the declaration.
/// </remarks>
/// <param name="session">The session whose file channels, call stack and environment these members read.</param>
public sealed class StdHidden(IRuntimeSession session) : IStdHiddenModule
{
    // MS-VBAL 5.4.5.7: "If Line width is less than 0 or greater than 255 an error (number 5...) is raised."
    private const int MaxLineWidth = 255;

    private static RuntimeSemanticsEvaluationResult<TValue> NotImplemented<TValue>(string member, string because)
        where TValue : SDK.Model.Values.Abstract.VBTypedValue
        => RuntimeSemanticsEvaluationResult<TValue>.Error(VBRuntimeErrorInfo.For(
            VBRuntimeErrorId.ApplicationDefinedOrObjectDefinedError, default, $"'{member}' is declared but not implemented yet: {because}."));

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
            array.TrySetElement(new ValueBindingHandle(arglist[index].RuntimeValue), index);
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
        => NotImplemented<VBVariantValue>(nameof(InputB), "a file channel reads characters, and nothing in it reads a byte");

    /// <inheritdoc/>
    public RuntimeSemanticsEvaluationResult<VBStringValue> InputBString(VBLongValue number, VBIntegerValue fileNumber)
        => NotImplemented<VBStringValue>("InputB$", "a file channel reads characters, and nothing in it reads a byte");

    /// <inheritdoc/>
    public RuntimeSemanticsEvaluationResult<VBLongPtrValue> ObjPtr(VBObjectValue @object)
    {
        var identity = @object.IsNothing() ? 0 : @object.Value.Identity;
        return RuntimeSemanticsEvaluationResult<VBLongPtrValue>.Success(
            session.Environment.Is64Bit ? new VBLongPtrValue(identity) : new VBLongPtrValue(unchecked((int)identity)));
    }

    /// <inheritdoc/>
    public RuntimeSemanticsEvaluationResult<VBLongPtrValue> StrPtr(VBStringValue @string)
        => NotImplemented<VBLongPtrValue>(nameof(StrPtr), "the characters of a string live in its value, not at an address of the session's memory");

    /// <inheritdoc/>
    public RuntimeSemanticsEvaluationResult<VBLongPtrValue> VarPtr(VBVariantValue variable)
        => NotImplemented<VBLongPtrValue>(nameof(VarPtr), "it takes its argument by reference, which a standard-library member cannot yet declare");

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
