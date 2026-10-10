using RDCore.External.Automation;
using RDCore.External.Native;
using RDCore.External.Protocol;
using RDCore.Runtime.Execution.External.Automation;
using RDCore.SDK;
using RDCore.SDK.Model;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Types.Complex;
using RDCore.SDK.Model.Values;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Bindings;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Model.Values.Runtime;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Shared;
using System.Globalization;

namespace RDCore.Runtime.Execution.External.Native;

/// <summary>
/// Calls the procedures of native libraries that <c>Declare</c> statements name (<strong>MS-VBAL §5.2.3.5</strong>).
/// </summary>
/// <remarks>
/// <para>
/// "An external procedure is invoked and arguments passed as if the external procedure was a procedure defined in the VBA language": the arguments are bound as
/// they are for any procedure, and what is left to this is to say how each is handed to the function, the way MS-VBA hands it, which is the implementation-defined part:
/// </para>
/// <list type="bullet">
/// <item>a number is passed as the number it is - an <c>Integer</c> as 16 bits, a <c>Long</c> as 32, a <c>LongPtr</c> as a pointer, a <c>Boolean</c> as a
/// <c>VARIANT_BOOL</c>, a <c>Currency</c> as its scaled 64 bits, a <c>Date</c> as its serial number;</item>
/// <item>a <c>ByVal String</c> is a pointer to a copy of it in the ANSI code page of the environment, which the function may write to, and what it wrote is
/// the variable's when it returns - that is what makes <c>GetWindowText hwnd, buffer, 255</c> work;</item>
/// <item>a <c>ByRef String</c> is a pointer to a <c>BSTR</c> of ANSI characters, and a <c>String</c> a function returns is one;</item>
/// <item>anything else <c>ByRef</c> is a pointer to the value, and what the function left there is the variable's;</item>
/// <item>an <c>As Any</c> parameter takes whatever the argument is, the way it would be passed to a parameter of its own type;</item>
/// <item>a <c>Variant</c> is a <c>VARIANT</c>.</item>
/// </list>
/// <para>
/// The call itself is made by what the session was composed with (<see cref="INativeLibraryHost"/>) - the external host, a process of its own. After every call
/// <c>Err.LastDllError</c> is the system error code the function left. A library that is not there is error 53, "File not found", and a function the library does
/// not export is error 453, "Can't find DLL entry point". A function that takes down the process that called it - which MS-VBA would not survive - is error 49,
/// "Bad DLL calling convention": what a native function does to its caller when it is declared wrongly.
/// </para>
/// <para>
/// 🚧 TODO a user-defined type, an array and an object are not passed yet, and neither is a procedure's address (<c>AddressOf</c>): a <c>Declare</c> that takes
/// one cannot be run yet, and says so.
/// </para>
/// <para>
/// Whether a program may call one at all is the policy over library imports (<c>AllowDllImports</c>), which every such call passes before it reaches this.
/// </para>
/// </remarks>
/// <param name="session">The session whose programs make the calls.</param>
/// <param name="libraries">What makes the calls.</param>
public sealed class DeclaredProcedureProvider(IRuntimeSession session, INativeLibraryHost libraries) : IExternalCallProvider
{
    /// <inheritdoc/>
    public bool CanDispatch(ExternalCallRequest request) => request.IsLibraryImport;

    /// <inheritdoc/>
    public RuntimeSemanticsEvaluationResult Dispatch(ExternalCallRequest request, ISymbolResolver resolver)
    {
        var member = request.Member;
        var entryPoint = member switch
        {
            VBExternalFunctionMemberSymbol { Alias: { Length: > 0 } alias } => alias,
            VBExternalSubMemberSymbol { Alias: { Length: > 0 } alias } => alias,
            _ => member.Name,
        };

        var parameters = RuntimeProcedureInvoker.GetParameters(member);
        var arguments = new NativeArgument[parameters.Length];
        var variables = new (IBindingHandle? Variable, VBType Declared)[parameters.Length];
        for (var index = 0; index < parameters.Length; index++)
        {
            var argument = index < request.Arguments.Length ? request.Arguments[index] : VBEmptyValue.Empty.RuntimeValue;
            if (!TryDescribe(parameters[index], argument, resolver, out arguments[index], out variables[index]))
            {
                return RuntimeSemanticsEvaluationResult.InternalError();
            }
        }

        var declared = member is VBExternalFunctionMemberSymbol { ResolvedType: var type } ? type : VBVoidType.TypeInfo;
        if (SlotOf(declared) is not { } returns)
        {
            return RuntimeSemanticsEvaluationResult.InternalError();
        }

        NativeCallResult result;
        try
        {
            result = libraries.Call(new NativeCallParams
            {
                Library = request.Library!,
                EntryPoint = entryPoint,
                Returns = returns,
                Arguments = arguments,
                AnsiCodePage = session.Environment.AnsiCodePage,
            });
        }
        catch (ExternalHostStoppedException)
        {
            return Failed(VBRuntimeErrorId.BaddDllCallingConvention, Exceptions.VBDeclare_HostStopped_Verbose, request.Describe());
        }

        switch (result.Lookup)
        {
            case NativeLookup.NoLibrary:
                return Failed(VBRuntimeErrorId.FileNotFound, Exceptions.VBDeclare_LibraryNotFound_Verbose, request.Describe(), request.Library!);
            case NativeLookup.NoEntryPoint:
                return Failed(VBRuntimeErrorId.SpecifiedDllFunctionNotFound, Exceptions.VBDeclare_EntryPointNotFound_Verbose, request.Describe(), request.Library!, entryPoint);
            case NativeLookup.Unsupported:
                return RuntimeSemanticsEvaluationResult.InternalError();
        }

        session.Errors.RecordDllError(result.LastDllError);

        // what the function left in an argument that names a variable is the variable's, as the type the variable is declared.
        for (var index = 0; index < arguments.Length && index < result.Written.Length; index++)
        {
            if (arguments[index].WritesBack && variables[index] is { Variable: { } variable, Declared: var variableType })
            {
                variable.SetValue(resolver, Typed(ExternalValues.FromWire(result.Written[index], NoObject), variableType).RuntimeValue);
            }
        }

        return RuntimeSemanticsEvaluationResult.Success(declared is VBVoidType ? VBVoidValue.Void : Typed(ExternalValues.FromWire(result.Returned, NoObject), declared));

        RuntimeSemanticsEvaluationResult Failed(VBRuntimeErrorId error, string verbose, params object[] parts)
            => RuntimeSemanticsEvaluationResult.Error(VBRuntimeErrorInfo.For(error, request.CallSite, string.Format(CultureInfo.CurrentCulture, verbose, parts)));
    }

    // how an argument is handed to the function, and the variable it names, if any, with the type the variable is declared.
    private static bool TryDescribe(
        VBParameterSymbol parameter, IRuntimeValue argument, ISymbolResolver resolver, out NativeArgument described, out (IBindingHandle? Variable, VBType Declared) named)
    {
        described = new NativeArgument();
        named = default;

        // the variable an argument names, when it names one: what the function writes to is written to it.
        IBindingHandle? variable = null;
        VBType? referenced = null;
        while (argument is VBRuntimeReference reference && resolver.TryRead(reference.Value, out var cell))
        {
            variable = cell;
            referenced ??= reference.DeclaredType;
            argument = cell.Value;
        }

        var declared = parameter.ResolvedType;
        var byReference = RuntimeProcedureInvoker.IsByRef(parameter.ParameterKind);

        // As Any: the argument is passed the way a parameter of its own type would take it - the declared type of the variable it names, or what a value is.
        // A Variant given a variable of another type is a Variant that holds the variable's value, of its type (MS-VBAL §5.3.1.11).
        var typed = declared switch
        {
            VBUnknownType => referenced?.CreateValue(new ValueBindingHandle(argument)) ?? TypedOf(argument),
            VBVariantType when referenced is not null => new VBVariantValue(referenced.CreateValue(new ValueBindingHandle(argument))),
            _ => declared.CreateValue(new ValueBindingHandle(argument)),
        };
        if (typed is VBVariantValue { TypedValue: var held } && declared is VBUnknownType)
        {
            typed = held;
        }

        if (typed is null)
        {
            return false;
        }

        if (declared is VBVariantType)
        {
            object? automation;
            try
            {
                automation = AutomationMarshaller.ToAutomation(typed, _ => null);
            }
            catch (AutomationException)
            {
                return false;
            }

            described = new NativeArgument
            {
                Slot = NativeSlot.Variant, ByReference = byReference, Value = ExternalValues.ToWire(automation, NoHandle), WritesBack = byReference && variable is not null,
            };
            named = (variable, referenced ?? VBVariantType.TypeInfo);
            return true;
        }

        // vbNullString is "a string whose value is zero" to the function: a null pointer, which a zero-length string is not.
        if (typed is VBStringValue { IsNullString: true } && !byReference)
        {
            described = new NativeArgument { Slot = NativeSlot.Pointer, Value = ExternalValues.ToWire(0L, NoHandle) };
            return true;
        }

        if (typed is VBStringValue text)
        {
            described = new NativeArgument
            {
                Slot = byReference ? NativeSlot.AnsiBstr : NativeSlot.AnsiBuffer, ByReference = byReference, Value = ExternalValues.ToWire(text.Value, NoHandle),
                WritesBack = variable is not null,
            };
            named = (variable, VBStringType.TypeInfo);
            return true;
        }

        if (Scalar(typed) is not var (slot, value))
        {
            return false;
        }

        described = new NativeArgument
        {
            Slot = slot, ByReference = byReference, Value = ExternalValues.ToWire(value, NoHandle), WritesBack = byReference && variable is not null,
        };
        named = (variable, typed.TypeInfo);
        return true;
    }

    // what a function declared to return a type returns, natively.
    private static NativeSlot? SlotOf(VBType declared) => declared switch
    {
        VBVoidType => NativeSlot.Void,
        VBByteType => NativeSlot.Byte,
        VBIntegerType or VBBooleanType => NativeSlot.Int16,
        VBLongType or VBEnumType => NativeSlot.Int32,
        VBLongLongType or VBCurrencyType => NativeSlot.Int64,
        VBLongPtrType_x64 or VBLongPtrType_x86 => NativeSlot.Pointer,
        VBSingleType => NativeSlot.Single,
        VBDoubleType or VBDateType => NativeSlot.Double,
        VBStringType => NativeSlot.AnsiBstr,
        VBVariantType => NativeSlot.Variant,
        _ => null,
    };

    // a number as the native type it is passed as.
    private static (NativeSlot Slot, object Value)? Scalar(VBTypedValue typed) => typed switch
    {
        VBByteValue value => (NativeSlot.Byte, value.Value),
        VBIntegerValue value => (NativeSlot.Int16, value.Value),
        VBLongValue value => (NativeSlot.Int32, value.Value),
        VBLongLongValue value => (NativeSlot.Int64, value.Value),
        VBLongPtrValue value => (NativeSlot.Pointer, (long)value.Value),
        VBBooleanValue value => (NativeSlot.Int16, (short)((bool)value.Value ? -1 : 0)),
        VBSingleValue value => (NativeSlot.Single, value.Value),
        VBDoubleValue value => (NativeSlot.Double, value.Value),
        VBDateValue value => (NativeSlot.Double, value.SerialValue),
        VBCurrencyValue value => (NativeSlot.Int64, value.Value.StoredValue),
        VBObjectValue value when value.IsNothing() => (NativeSlot.Pointer, 0L),
        _ => null,
    };

    // the typed value an argument of an As Any parameter is, from what it is stored as, when it names no variable whose declared type says.
    private static VBTypedValue? TypedOf(IRuntimeValue argument) => argument switch
    {
        VBRuntimeVariantValue variant => variant.WrappedValue,
        VBRuntimeBooleanValue boolean => new VBBooleanValue((bool)boolean),
        VBRuntimeCurrencyValue currency => new VBCurrencyValue(currency.Value),
        VBRuntimeValue<VBRuntimeCurrencyValue> currency => new VBCurrencyValue(currency.Value.Value),
        VBRuntimeEmptyValue or VBRuntimeNullValue => new VBLongValue(0),
        VBRuntimeValue<VBRuntimeObjectId> identity => new VBObjectValue(identity.StoredValue),
        _ => argument.BoxedValue switch
        {
            VBTypedValue typed => typed,
            byte value => new VBByteValue(value),
            short value => new VBIntegerValue(value),
            int value => new VBLongValue(value),
            long value => new VBLongLongValue(value),
            float value => new VBSingleValue(value),
            double value => new VBDoubleValue(value),
            string value => new VBStringValue(value),
            _ => null,
        },
    };

    // the value of a declared type from what the function returned or left, as the native type it was passed as.
    private static VBTypedValue Typed(object? value, VBType declared)
    {
        var invariant = CultureInfo.InvariantCulture;
        return declared switch
        {
            VBByteType => new VBByteValue(Convert.ToByte(value, invariant)),
            VBIntegerType => new VBIntegerValue(Convert.ToInt16(value, invariant)),
            VBBooleanType => new VBBooleanValue(Convert.ToInt16(value, invariant) != 0),
            VBLongType or VBEnumType => new VBLongValue(Convert.ToInt32(value, invariant)),
            VBLongLongType => new VBLongLongValue(Convert.ToInt64(value, invariant)),
            VBLongPtrType_x64 or VBLongPtrType_x86 => new VBLongPtrValue(Convert.ToInt64(value, invariant)),
            VBSingleType => new VBSingleValue(Convert.ToSingle(value, invariant)),
            VBDoubleType => new VBDoubleValue(Convert.ToDouble(value, invariant)),
            VBDateType => new VBDateValue(Convert.ToDouble(value, invariant)),
            VBCurrencyType => new VBCurrencyValue(new VBRuntimeCurrencyValue(Convert.ToInt64(value, invariant)).Value),
            VBStringType => new VBStringValue(Convert.ToString(value, invariant) ?? string.Empty),
            _ => AutomationMarshaller.FromAutomation(value, declared, (_, _) => VBObjectValue.Nothing),
        };
    }

    // what a native call passes is values, never objects: there is no handle for one, either way.
    private static long NoHandle(object value) => throw new AutomationException(unchecked((int)0x80020005), Exceptions.VBDeclare_ObjectNotPassed_Verbose);

    private static object NoObject(long handle) => throw new AutomationException(unchecked((int)0x80020005), Exceptions.VBDeclare_ObjectNotPassed_Verbose);
}
