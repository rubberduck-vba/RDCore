using RDCore.Runtime.Execution;
using RDCore.Runtime.Semantics.Conversion;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Types.Complex;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Model.Values.Runtime;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Abstract.StdLib;
using RDCore.SDK.Runtime.Shared;
using RDCore.SDK.Runtime.StdLib;
using static RDCore.SDK.Runtime.Abstract.StdLib.IStdInformationModule;

namespace RDCore.Runtime.StdLib;

/// <inheritdoc cref="IStdInformationModule"/>
/// <remarks>
/// Every answer here was checked against MS-VBA itself, which is the reference wherever the specification's tables are silent,
/// garbled or wrong; the members' own remarks say where each of those departs from the specification's text, and why.
/// <para>
/// The functions that look at the <em>value</em> of their argument - <c>VarType</c> and the <c>Is</c> functions but
/// <c>IsObject</c> and <c>IsMissing</c> - look at an object's default member's value (<strong>MS-VBAL §5.5.1.2.13</strong>), read once:
/// <c>VarType(Err)</c> is <c>Err.Number</c>'s, and an Excel cell holding nothing <c>IsEmpty</c>. The functions that look at the
/// argument itself - <c>TypeName</c>, <c>IsObject</c> - never call it.
/// </para>
/// </remarks>
/// <param name="session">The session whose state these members report on.</param>
public sealed class StdInformation(IRuntimeSession session) : IStdInformationModule
{
    // the error object is one object for the life of the session (MS-VBAL 6.1.3.2), so it has one identity.
    private readonly VBRuntimeObjectId _errorObject = session.Objects.CreateObject();

    private static RuntimeSemanticsEvaluationResult<TValue> Succeed<TValue>(TValue value)
        where TValue : VBTypedValue
        => RuntimeSemanticsEvaluationResult<TValue>.Success(value);

    private static RuntimeSemanticsEvaluationResult<TValue> Fail<TValue>(VBRuntimeErrorId id, string verbose)
        where TValue : VBTypedValue
        => RuntimeSemanticsEvaluationResult<TValue>.Error(VBRuntimeErrorInfo.For(id, default, verbose));

    private static RuntimeSemanticsEvaluationResult<VBBooleanValue> Answer(bool value) => Succeed(new VBBooleanValue(value));

    /// <summary>
    /// <strong>MS-VBAL 6.1.3.2</strong> The error object: one object for the life of the session, registered as an
    /// instance of the library's <c>ErrObject</c> class so that a member access on it finds that class's members
    /// the same way it finds a workspace class's.
    /// </summary>
    public RuntimeSemanticsEvaluationResult<VBObjectValue> Err()
    {
        if (!session.Symbols.TryGetInstance(_errorObject, out _))
        {
            // the standard library's class, named by its library, whatever else a referenced library declares by that name.
            var declared = VBProjectSymbol.ResolveQualifiedType(session.Symbols.Resolver, StdLibSymbolProvider.LibraryName, "ErrObject", StaticSymbol.GlobalUri).Symbol;
            if (declared is not VBClassModuleSymbol errorClass)
            {
                return Fail<VBObjectValue>(VBRuntimeErrorId.ApplicationDefinedOrObjectDefinedError,
                    "The standard library's 'ErrObject' class is not defined in this session.");
            }

            session.Symbols.CreateInstance(_errorObject, errorClass);
        }

        return Succeed(new VBObjectValue(_errorObject));
    }

    /// <summary>
    /// The line the session's most recent run-time error was raised at, counted the way the environment says
    /// to count it (<c>IRuntimeEnvironmentProfile.ErlLineNumbering</c>).
    /// </summary>
    /// <remarks>
    /// Reads the session's error state, which captured it when the error was raised — by the time source
    /// asks, inside a handler, the activation it happened in has been unwound.
    /// </remarks>
    public RuntimeSemanticsEvaluationResult<VBLongValue> Erl()
        => Succeed(new VBLongValue(checked((int)session.Errors.LineNumber)));

    /// <summary>
    /// <c>vbIMEModeNoControl</c>: the platform has no Input Method Editor to report on, which is what MS-VBA answers on a host whose
    /// language uses none.
    /// </summary>
    public RuntimeSemanticsEvaluationResult<VBLongValue> IMEStatus() => Succeed(new VBLongValue((int)VBIMEStatus.VBIMEModeNoControl));

    public RuntimeSemanticsEvaluationResult<VBBooleanValue> IsArray(VBVariantValue arg)
        => Test(arg, value => value is VBArrayValue);

    public RuntimeSemanticsEvaluationResult<VBBooleanValue> IsDate(VBVariantValue arg)
        => Test(arg, value => value switch
        {
            VBDateValue => true,
            VBStringValue text => ValueConversions.TryInterpretDate(text.Value, out _, session.Environment.Regional),
            _ => false,
        });

    public RuntimeSemanticsEvaluationResult<VBBooleanValue> IsEmpty(VBVariantValue arg)
        => Test(arg, value => value is VBEmptyValue);

    public RuntimeSemanticsEvaluationResult<VBBooleanValue> IsError(VBVariantValue arg)
        => Test(arg, value => value is VBErrorValue);

    // a zero-element Variant array is how a ParamArray that collected nothing arrives, and MS-VBA says it is missing - but not one a variable declared as an
    // array holds, which is passed by reference as the array it is (StdLibByRefAttribute).
    public RuntimeSemanticsEvaluationResult<VBBooleanValue> IsMissing(VBVariantValue arg)
        => Answer(Unwrapped(arg) switch
        {
            VBMissingValue => true,
            VBArrayValue { Length: 0, IsInitialized: true, ItemType: VBVariantType } => arg.ReferencedType is not VBArrayType,
            _ => false,
        });

    public RuntimeSemanticsEvaluationResult<VBBooleanValue> IsNull(VBVariantValue arg)
        => Test(arg, value => value is VBNullValue);

    public RuntimeSemanticsEvaluationResult<VBBooleanValue> IsNumeric(VBVariantValue arg)
        => Test(arg, value => value switch
        {
            // MS-VBAL 5.5.1.2.11: Empty is 0, whatever it is coerced to.
            VBEmptyValue or VBBooleanValue => true,
            VBDateValue => false,
            VBNumericTypedValue => true,
            VBStringValue text => ValueConversions.ToNumeric(text, VBDoubleType.TypeInfo, session.Environment.Regional).IsSuccess,
            _ => false,
        });

    public RuntimeSemanticsEvaluationResult<VBBooleanValue> IsObject(VBVariantValue arg)
        => Answer(Unwrapped(arg) is VBObjectValue);

    public RuntimeSemanticsEvaluationResult<VBLongValue> QBColor(VBIntegerValue color)
        => StdInformationQBColors.TryGetQBColorRGBValue((StdInformationQBColor)color.Value, out var rgb)
            ? Succeed(new VBLongValue(rgb))
            : Fail<VBLongValue>(VBRuntimeErrorId.InvalidProcedureCallOrArgument, $"QBColor takes a color from 0 to 15, not {color.Value}.");

    public RuntimeSemanticsEvaluationResult<VBLongValue> RGB(VBIntegerValue red, VBIntegerValue green, VBIntegerValue blue)
    {
        if (red.Value < 0 || green.Value < 0 || blue.Value < 0)
        {
            return Fail<VBLongValue>(VBRuntimeErrorId.InvalidProcedureCallOrArgument, "A color component cannot be negative.");
        }

        return Succeed(new VBLongValue(Math.Min((int)blue.Value, 255) * 65536 + Math.Min((int)green.Value, 255) * 256 + Math.Min((int)red.Value, 255)));
    }

    public RuntimeSemanticsEvaluationResult<VBStringValue> TypeName(VBVariantValue arg)
        => Succeed(new VBStringValue(Unwrapped(arg) switch
        {
            VBObjectValue reference => ClassNameOf(reference),
            VBArrayValue array => $"{ElementTypeName(array.ItemType)}()",
            VBUserDefinedTypeValue record => record.TypeInfo.Name,
            var value => NameOf(VarTypeOf(value)),
        }));

    public RuntimeSemanticsEvaluationResult<VBLongValue> VarType(VBVariantValue varName)
        => Succeed(new VBLongValue((int)VarTypeOf(ValueOf(varName))));

    // a Variant that holds a Variant holds what that one does.
    private static VBTypedValue Unwrapped(VBTypedValue value)
    {
        while (value is VBVariantValue variant)
        {
            value = variant.TypedValue;
        }

        return value;
    }

    // MS-VBAL 5.5.1.2.13: an object stands for what its default member returns, read once - a default member that returns an object leaves
    // an object. One that has no default member it can call with no arguments stands for itself.
    // A default member that fails is not an error of the function's: MS-VBA answers as if the object had no default member to call - VarType
    // 9, every Is function False - and leaves the failure in Err, where a program that looks finds it, without raising it to a handler.
    private VBTypedValue ValueOf(VBTypedValue argument)
    {
        var value = Unwrapped(argument);
        if (value is not VBObjectValue reference || ObjectDefaultMember.Call(session, reference) is not { } called)
        {
            return value;
        }

        if (!called.IsSuccess)
        {
            // an error raised by workspace code was set on Err as it was raised; one a library member reported was not.
            if (called.ErrorInfo is { } failure && session.Errors.Number != failure.ErrorId)
            {
                session.Errors.Raise(failure);
            }

            return value;
        }

        return Unwrapped(called.Result is VBTypedValue typed ? typed : value);
    }

    private RuntimeSemanticsEvaluationResult<VBBooleanValue> Test(VBVariantValue arg, Func<VBTypedValue, bool> test)
        => Answer(test(ValueOf(arg)));

    private VBVarType VarTypeOf(VBTypedValue value) => value switch
    {
        // an array's own type says only that it is one: what it is an array of is the value's.
        VBArrayValue array => VBVarType.VBArray | ElementVarType(array.ItemType),
        VBObjectValue reference => !reference.IsNothing() && session.Symbols.TryGetInstance(reference.Value, out var instance)
            ? instance.ClassModule.VarType()
            : VBVarType.VBObject,
        VBLongPtrValue => LongPtrVarType,
        _ => value.TypeInfo.VarType(),
    };

    private VBVarType ElementVarType(VBType elementType) => elementType switch
    {
        VBLongPtrType_x64 or VBLongPtrType_x86 => LongPtrVarType,
        _ => elementType.VarType(),
    };

    // a LongPtr is the integer a pointer is on the platform the session runs as.
    private VBVarType LongPtrVarType => session.Environment.Is64Bit ? VBVarType.VBLongLong : VBVarType.VBLong;

    // MS-VBA names an array of objects "Object()" whatever class its elements are declared as.
    private string ElementTypeName(VBType elementType) => elementType switch
    {
        VBUserDefinedType record => record.Name,
        _ => NameOf(ElementVarType(elementType)),
    };

    // "The name of the object type" is its class's, which a reference does not carry: the instance it refers to does.
    private string ClassNameOf(VBObjectValue reference)
    {
        if (reference.IsNothing())
        {
            return "Nothing";
        }

        if (!session.Symbols.TryGetInstance(reference.Value, out var instance))
        {
            return "Unknown";
        }

        // a form that was unloaded is an object whose form is gone, which MS-VBA calls a UserForm whatever form it was; a class of the library whose objects MS-VBA
        // names otherwise is what it says (oracle\s41 U15, U01).
        return Execution.External.Forms.SessionForms.Of(session).IsUnloaded(reference.Value)
            ? SDK.Workspace.Forms.FormObject.ClassName
            : instance.ClassModule.GetProperty(SymbolProperties.ReportedTypeName) ?? instance.ClassModule.Name;
    }

    // MS-VBAL 6.1.2.7.1.12's table, by the subtype each name is the name of.
    private static string NameOf(VBVarType varType) => varType switch
    {
        VBVarType.VBEmpty => "Empty",
        VBVarType.VBNull => "Null",
        VBVarType.VBInteger => "Integer",
        VBVarType.VBLong => "Long",
        VBVarType.VBSingle => "Single",
        VBVarType.VBDouble => "Double",
        VBVarType.VBCurrency => "Currency",
        VBVarType.VBDate => "Date",
        VBVarType.VBString => "String",
        VBVarType.VBObject => "Object",
        VBVarType.VBError => "Error",
        VBVarType.VBBoolean => "Boolean",
        VBVarType.VBVariant => "Variant",
        VBVarType.VBDecimal => "Decimal",
        VBVarType.VBByte => "Byte",
        VBVarType.VBLongLong => "LongLong",
        _ => "Unknown",
    };
}
