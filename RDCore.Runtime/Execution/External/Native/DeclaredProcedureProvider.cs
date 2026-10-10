using RDCore.External.Automation;
using RDCore.Runtime.Execution.External.Automation;
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
using System.Runtime.InteropServices;
using System.Text;

namespace RDCore.Runtime.Execution.External.Native;

/// <summary>
/// Calls the procedures of native libraries that <c>Declare</c> statements name (<strong>MS-VBAL §5.2.3.5</strong>).
/// </summary>
/// <remarks>
/// <para>
/// "An external procedure is invoked and arguments passed as if the external procedure was a procedure defined in the VBA language": the arguments are bound as
/// they are for any procedure, and what is left to this is to hand them to the function the way MS-VBA does, which is the implementation-defined part:
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
/// After every call <c>Err.LastDllError</c> is the system error code the function left. A library that is not there is error 53, "File not found", and a
/// function the library does not export is error 453, "Can't find DLL entry point".
/// </para>
/// <para>
/// 🚧 TODO a user-defined type, an array and an object are not passed yet, and neither is a procedure's address (<c>AddressOf</c>): a <c>Declare</c> that takes
/// one cannot be run yet, and says so.
/// </para>
/// <para>
/// ⚠️ Native code is native code: a function declared wrongly can corrupt the process or end it, as it would MS-VBA's. Whether a program may call one at all is
/// the policy over library imports (<c>AllowDllImports</c>), which every such call passes before it reaches this.
/// </para>
/// </remarks>
/// <param name="session">The session whose programs make the calls.</param>
public sealed class DeclaredProcedureProvider(IRuntimeSession session) : IExternalCallProvider
{
    private static readonly int VariantSize = IntPtr.Size == 8 ? 24 : 16;

    // a library is loaded into the process, whatever session or run asked for it: one table for all of them.
    private static readonly NativeLibraries _libraries = new();
    private readonly Encoding _ansi = CodePagesEncodingProvider.Instance.GetEncoding(session.Environment.AnsiCodePage) ?? Encoding.Latin1;

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

        if (!_libraries.TryResolve(request.Library!, entryPoint, out var function, out var notFound))
        {
            return RuntimeSemanticsEvaluationResult.Error(VBRuntimeErrorInfo.For(notFound, request.CallSite, notFound is VBRuntimeErrorId.FileNotFound
                ? $"{request.Describe()} could not be called: the library '{request.Library}' was not found."
                : $"{request.Describe()} could not be called: the library '{request.Library}' has no entry point '{entryPoint}'."));
        }

        var parameters = RuntimeProcedureInvoker.GetParameters(member);
        var call = new NativeCall(_ansi);
        try
        {
            for (var index = 0; index < parameters.Length; index++)
            {
                if (!call.TryPass(parameters[index], index < request.Arguments.Length ? request.Arguments[index] : VBEmptyValue.Empty.RuntimeValue, resolver))
                {
                    return RuntimeSemanticsEvaluationResult.InternalError();
                }
            }

            var declared = member is VBExternalFunctionMemberSymbol { ResolvedType: var type } ? type : VBVoidType.TypeInfo;
            if (NativeCall.ReturnSlotOf(declared) is not { } returns)
            {
                return RuntimeSemanticsEvaluationResult.InternalError();
            }

            var returned = NativeCalls.Invoke(function, returns, call.Slots, call.Values, out var lastError);
            session.Errors.RecordDllError(lastError);

            call.WriteBack(resolver);
            return RuntimeSemanticsEvaluationResult.Success(call.ReturnedValue(returned, declared));
        }
        finally
        {
            call.Free();
        }
    }

    // One call's arguments as the function takes them, the memory they live in for the length of the call, and what the variables they name are told afterwards.
    private sealed class NativeCall(Encoding ansi)
    {
        private readonly List<Type> _slots = [];
        private readonly List<object?> _values = [];
        private readonly List<Action> _afterCall = [];
        private readonly List<Action> _free = [];

        public Type[] Slots => [.. _slots];

        public object?[] Values => [.. _values];

        public bool TryPass(VBParameterSymbol parameter, IRuntimeValue argument, ISymbolResolver resolver)
        {
            // the variable an argument names, when it names one: what the function writes to is written to it.
            IBindingHandle? variable = null;
            while (argument is VBRuntimeReference reference && resolver.TryRead(reference.Value, out var cell))
            {
                variable = cell;
                argument = cell.Value;
            }

            var declared = parameter.ResolvedType;
            var byReference = RuntimeProcedureInvoker.IsByRef(parameter.ParameterKind);

            // As Any: the argument is passed the way a parameter of its own type would take it.
            var typed = declared is VBUnknownType ? TypedOf(argument) : declared.CreateValue(new ValueBindingHandle(argument));
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
                return PassVariant(typed, byReference, variable, resolver);
            }

            // vbNullString is "a string whose value is zero" to the function: a null pointer, which a zero-length string is not.
            if (typed is VBStringValue { IsNullString: true } && !byReference)
            {
                Add(typeof(IntPtr), IntPtr.Zero);
                return true;
            }

            if (typed is VBStringValue text)
            {
                return byReference ? PassStringReference(text.Value, variable, resolver) : PassString(text.Value, variable, resolver);
            }

            if (Scalar(typed) is not var (slot, value))
            {
                return false;
            }

            if (!byReference)
            {
                Add(slot, value);
                return true;
            }

            // anything else by reference is the address of the value, and what the function left there is the variable's afterwards.
            var buffer = Marshal.AllocHGlobal(8);
            _free.Add(() => Marshal.FreeHGlobal(buffer));
            WriteScalar(buffer, value);
            Add(typeof(IntPtr), buffer);
            if (variable is not null)
            {
                var type = typed.TypeInfo;
                _afterCall.Add(() => variable.SetValue(resolver, ReadScalar(buffer, slot, type).RuntimeValue));
            }

            return true;
        }

        // a copy in the ANSI code page, which the function may write into; a variable's buffer takes it back, the length it was.
        private bool PassString(string text, IBindingHandle? variable, ISymbolResolver resolver)
        {
            var bytes = ansi.GetBytes(text);
            var buffer = Marshal.AllocHGlobal(bytes.Length + 1);
            _free.Add(() => Marshal.FreeHGlobal(buffer));
            Marshal.Copy(bytes, 0, buffer, bytes.Length);
            Marshal.WriteByte(buffer, bytes.Length, 0);
            Add(typeof(IntPtr), buffer);

            if (variable is not null)
            {
                _afterCall.Add(() =>
                {
                    var written = new byte[bytes.Length];
                    Marshal.Copy(buffer, written, 0, written.Length);
                    variable.SetValue(resolver, new VBStringValue(ansi.GetString(written)).RuntimeValue);
                });
            }

            return true;
        }

        // the address of a BSTR of ANSI characters, which the function may replace.
        private bool PassStringReference(string text, IBindingHandle? variable, ISymbolResolver resolver)
        {
            if (!OperatingSystem.IsWindows())
            {
                return false;
            }

            var bytes = ansi.GetBytes(text);
            var bstr = SysAllocStringByteLen(bytes, (uint)bytes.Length);
            var cell = Marshal.AllocHGlobal(IntPtr.Size);
            Marshal.WriteIntPtr(cell, bstr);
            _free.Add(() =>
            {
                var current = Marshal.ReadIntPtr(cell);
                if (current != IntPtr.Zero)
                {
                    SysFreeString(current);
                }

                Marshal.FreeHGlobal(cell);
            });

            Add(typeof(IntPtr), cell);
            if (variable is not null)
            {
                _afterCall.Add(() => variable.SetValue(resolver, new VBStringValue(AnsiOf(Marshal.ReadIntPtr(cell))).RuntimeValue));
            }

            return true;
        }

        private bool PassVariant(VBTypedValue typed, bool byReference, IBindingHandle? variable, ISymbolResolver resolver)
        {
            if (!OperatingSystem.IsWindows())
            {
                return false;
            }

            object? automation;
            try
            {
                automation = AutomationMarshaller.ToAutomation(typed, _ => null);
            }
            catch (AutomationException)
            {
                return false;
            }

            var buffer = Marshal.AllocHGlobal(VariantSize);
            Marshal.GetNativeVariantForObject(automation, buffer);
            _free.Add(() =>
            {
                _ = VariantClear(buffer);
                Marshal.FreeHGlobal(buffer);
            });

            if (byReference)
            {
                Add(typeof(IntPtr), buffer);
                if (variable is not null)
                {
                    _afterCall.Add(() => variable.SetValue(resolver,
                        AutomationMarshaller.FromAutomation(Marshal.GetObjectForNativeVariant(buffer), VBVariantType.TypeInfo, (_, _) => VBObjectValue.Nothing).RuntimeValue));
                }

                return true;
            }

            if (IntPtr.Size == 8)
            {
                Add(typeof(NativeVariant64), Marshal.PtrToStructure<NativeVariant64>(buffer));
            }
            else
            {
                Add(typeof(NativeVariant32), Marshal.PtrToStructure<NativeVariant32>(buffer));
            }

            return true;
        }

        public void WriteBack(ISymbolResolver resolver)
        {
            foreach (var afterCall in _afterCall)
            {
                afterCall();
            }
        }

        public void Free()
        {
            foreach (var free in _free)
            {
                free();
            }
        }

        private void Add(Type slot, object? value)
        {
            _slots.Add(slot);
            _values.Add(value);
        }

        // what a function declared to return a type returns, natively.
        public static Type? ReturnSlotOf(VBType declared) => declared switch
        {
            VBVoidType => typeof(void),
            VBByteType => typeof(byte),
            VBIntegerType or VBBooleanType => typeof(short),
            VBLongType or VBEnumType => typeof(int),
            VBLongLongType or VBCurrencyType => typeof(long),
            VBLongPtrType_x64 or VBLongPtrType_x86 => typeof(IntPtr),
            VBSingleType => typeof(float),
            VBDoubleType or VBDateType => typeof(double),
            VBStringType when OperatingSystem.IsWindows() => typeof(IntPtr),
            VBVariantType when OperatingSystem.IsWindows() => IntPtr.Size == 8 ? typeof(NativeVariant64) : typeof(NativeVariant32),
            _ => null,
        };

        public VBTypedValue ReturnedValue(object? returned, VBType declared)
        {
            switch (declared)
            {
                case VBVoidType:
                    return VBVoidValue.Void;
                case VBStringType:
                    var bstr = (IntPtr)returned!;
                    var text = AnsiOf(bstr);
                    if (bstr != IntPtr.Zero)
                    {
                        SysFreeString(bstr);
                    }

                    return new VBStringValue(text);
                case VBVariantType:
                    var buffer = Marshal.AllocHGlobal(VariantSize);
                    try
                    {
                        Marshal.StructureToPtr(returned!, buffer, fDeleteOld: false);
                        var value = Marshal.GetObjectForNativeVariant(buffer);
                        _ = VariantClear(buffer);
                        return AutomationMarshaller.FromAutomation(value, VBVariantType.TypeInfo, (_, _) => VBObjectValue.Nothing);
                    }
                    finally
                    {
                        Marshal.FreeHGlobal(buffer);
                    }

                default:
                    return Typed(returned!, declared);
            }
        }

        // a BSTR whose characters are ANSI: its length is in bytes.
        private string AnsiOf(IntPtr bstr)
        {
            if (bstr == IntPtr.Zero)
            {
                return string.Empty;
            }

            var bytes = new byte[SysStringByteLen(bstr)];
            Marshal.Copy(bstr, bytes, 0, bytes.Length);
            return ansi.GetString(bytes);
        }

        // the typed value an argument of an As Any parameter is, from what it is stored as.
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

        // a number as the native type it is passed as.
        private static (Type Slot, object Value)? Scalar(VBTypedValue typed) => typed switch
        {
            VBByteValue value => (typeof(byte), value.Value),
            VBIntegerValue value => (typeof(short), value.Value),
            VBLongValue value => (typeof(int), value.Value),
            VBLongLongValue value => (typeof(long), value.Value),
            VBLongPtrValue value => (typeof(IntPtr), (IntPtr)value.Value),
            VBBooleanValue value => (typeof(short), (short)((bool)value.Value ? -1 : 0)),
            VBSingleValue value => (typeof(float), value.Value),
            VBDoubleValue value => (typeof(double), value.Value),
            VBDateValue value => (typeof(double), value.SerialValue),
            VBCurrencyValue value => (typeof(long), value.Value.StoredValue),
            VBObjectValue value when value.IsNothing() => (typeof(IntPtr), IntPtr.Zero),
            _ => null,
        };

        private static void WriteScalar(IntPtr buffer, object value)
        {
            switch (value)
            {
                case byte number: Marshal.WriteByte(buffer, number); break;
                case short number: Marshal.WriteInt16(buffer, number); break;
                case int number: Marshal.WriteInt32(buffer, number); break;
                case long number: Marshal.WriteInt64(buffer, number); break;
                case IntPtr pointer: Marshal.WriteIntPtr(buffer, pointer); break;
                case float number: Marshal.WriteInt32(buffer, BitConverter.SingleToInt32Bits(number)); break;
                case double number: Marshal.WriteInt64(buffer, BitConverter.DoubleToInt64Bits(number)); break;
            }
        }

        private static VBTypedValue ReadScalar(IntPtr buffer, Type slot, VBType declared)
        {
            // each boxed as what it is: a conditional of numbers would make every one of them a double.
            object value = slot == typeof(byte) ? (object)Marshal.ReadByte(buffer)
                : slot == typeof(short) ? (object)Marshal.ReadInt16(buffer)
                : slot == typeof(int) ? (object)Marshal.ReadInt32(buffer)
                : slot == typeof(IntPtr) ? (object)Marshal.ReadIntPtr(buffer)
                : slot == typeof(float) ? (object)BitConverter.Int32BitsToSingle(Marshal.ReadInt32(buffer))
                : slot == typeof(double) ? (object)BitConverter.Int64BitsToDouble(Marshal.ReadInt64(buffer))
                : (object)Marshal.ReadInt64(buffer);
            return Typed(value, declared);
        }

        // the value of a declared type from what is stored natively for it.
        private static VBTypedValue Typed(object value, VBType declared) => declared switch
        {
            VBByteType => new VBByteValue((byte)value),
            VBIntegerType => new VBIntegerValue((short)value),
            VBBooleanType => new VBBooleanValue((short)value != 0),
            VBLongType or VBEnumType => new VBLongValue((int)value),
            VBLongLongType => new VBLongLongValue((long)value),
            VBLongPtrType_x64 or VBLongPtrType_x86 => new VBLongPtrValue((long)(IntPtr)value),
            VBSingleType => new VBSingleValue((float)value),
            VBDoubleType => new VBDoubleValue((double)value),
            VBDateType => new VBDateValue((double)value),
            VBCurrencyType => new VBCurrencyValue(new VBRuntimeCurrencyValue((long)value).Value),
            _ => new VBVariantValue(value switch
            {
                byte number => new VBByteValue(number),
                short number => new VBIntegerValue(number),
                int number => new VBLongValue(number),
                long number => new VBLongLongValue(number),
                IntPtr pointer => new VBLongPtrValue((long)pointer),
                float number => new VBSingleValue(number),
                double number => new VBDoubleValue(number),
                _ => VBEmptyValue.Empty,
            }),
        };

        [DllImport("oleaut32", ExactSpelling = true)]
        private static extern IntPtr SysAllocStringByteLen(byte[] bytes, uint length);

        [DllImport("oleaut32", ExactSpelling = true)]
        private static extern void SysFreeString(IntPtr bstr);

        [DllImport("oleaut32", ExactSpelling = true)]
        private static extern uint SysStringByteLen(IntPtr bstr);

        [DllImport("oleaut32", ExactSpelling = true)]
        private static extern int VariantClear(IntPtr variant);
    }
}
