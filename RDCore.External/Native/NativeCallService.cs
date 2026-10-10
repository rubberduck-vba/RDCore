using RDCore.External.Protocol;
using RDCore.SDK.Platform.Channels;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace RDCore.External.Native;

/// <summary>
/// Calls the functions of native libraries, in the process it runs in: the external host's side of a <c>Declare</c>, and what a runtime that makes its calls
/// in-process is given.
/// </summary>
/// <remarks>
/// <para>
/// A call arrives described: which function, what it returns, and for each argument the native type it is passed as and its value. What is left to do is the
/// machine's part - the memory an argument passed by its address lives in for the length of the call, the ANSI copy of a string, the <c>BSTR</c> and the
/// <c>VARIANT</c> - and to say afterwards what the function left in the arguments that are variables.
/// </para>
/// <para>
/// ⚠️ Native code is native code: a function declared wrongly can corrupt the process or end it. That is why this runs in the external host.
/// </para>
/// </remarks>
/// <param name="platform">What a call needs of the platform beyond a function and its arguments.</param>
public sealed class NativeCallService(INativePlatform platform) : INativeLibraryHost
{
    // a library is loaded into the process, whatever session or run asked for it: one table for all of them.
    private readonly NativeLibraries _libraries = new(platform);

    /// <summary>
    /// Answers the native calls that come over <paramref name="channel"/>; it is started by its owner.
    /// </summary>
    public void ServeOn(CallChannel channel) => channel.Handle<NativeCallParams, NativeCallResult>(ExternalProtocol.NativeCall, Call);

    /// <inheritdoc/>
    public NativeCallResult Call(NativeCallParams call)
    {
        if (!_libraries.TryResolve(call.Library, call.EntryPoint, out var function, out var lookup))
        {
            return new NativeCallResult { Lookup = lookup };
        }

        var frame = new Frame(platform, CodePagesEncodingProvider.Instance.GetEncoding(call.AnsiCodePage) ?? Encoding.Latin1);
        try
        {
            if (!call.Arguments.All(frame.TryPass) || frame.TypeOf(call.Returns) is not { } returns)
            {
                return new NativeCallResult { Lookup = NativeLookup.Unsupported };
            }

            var returned = NativeCalls.Invoke(function, returns, frame.Slots, frame.Values, out var lastError);
            return new NativeCallResult
            {
                Lookup = NativeLookup.Found,
                Returned = frame.Returned(returned, call.Returns),
                Written = frame.Written(),
                LastDllError = lastError,
            };
        }
        finally
        {
            frame.Free();
        }
    }

    // One call's arguments as the function takes them, the memory they live in for the length of the call, and what the function left in them.
    private sealed class Frame(INativePlatform platform, Encoding ansi)
    {
        private readonly List<Type> _slots = [];
        private readonly List<object?> _values = [];
        private readonly List<Func<ExternalValue>?> _written = [];
        private readonly List<Action> _free = [];

        public Type[] Slots => [.. _slots];

        public object?[] Values => [.. _values];

        public ExternalValue[] Written() => [.. _written.Select(read => read?.Invoke() ?? new ExternalValue())];

        public void Free()
        {
            foreach (var free in _free)
            {
                free();
            }
        }

        public bool TryPass(NativeArgument argument)
        {
            Func<ExternalValue>? written = null;
            var passed = argument.Slot switch
            {
                NativeSlot.AnsiBuffer => PassBuffer(argument, out written),
                NativeSlot.AnsiBstr => platform.HasAutomationTypes && PassBstr(argument, out written),
                NativeSlot.Variant => platform.HasAutomationTypes && PassVariant(argument, out written),
                _ => PassScalar(argument, out written),
            };

            _written.Add(argument.WritesBack ? written : null);
            return passed;
        }

        // a number, or the address of one: what the function left there is read back as the same native type.
        private bool PassScalar(NativeArgument argument, out Func<ExternalValue>? written)
        {
            written = null;
            if (ScalarTypeOf(argument.Slot) is not { } slot || Scalar(slot, ExternalValues.FromWire(argument.Value, Unhandled)) is not { } value)
            {
                return false;
            }

            if (!argument.ByReference)
            {
                Add(slot, value);
                return true;
            }

            var cell = Marshal.AllocHGlobal(8);
            _free.Add(() => Marshal.FreeHGlobal(cell));
            Marshal.WriteInt64(cell, 0);
            Write(cell, value);
            Add(typeof(IntPtr), cell);
            written = () => ExternalValues.ToWire(Unpointed(Read(cell, slot)), Unhandled);
            return true;
        }

        private static void Write(IntPtr cell, object value)
        {
            switch (value)
            {
                case byte number: Marshal.WriteByte(cell, number); break;
                case short number: Marshal.WriteInt16(cell, number); break;
                case int number: Marshal.WriteInt32(cell, number); break;
                case long number: Marshal.WriteInt64(cell, number); break;
                case IntPtr pointer: Marshal.WriteIntPtr(cell, pointer); break;
                case float number: Marshal.WriteInt32(cell, BitConverter.SingleToInt32Bits(number)); break;
                case double number: Marshal.WriteInt64(cell, BitConverter.DoubleToInt64Bits(number)); break;
            }
        }

        // each boxed as what it is: a conditional of numbers would make every one of them a double.
        private static object Read(IntPtr cell, Type slot)
            => slot == typeof(byte) ? (object)Marshal.ReadByte(cell)
            : slot == typeof(short) ? (object)Marshal.ReadInt16(cell)
            : slot == typeof(int) ? (object)Marshal.ReadInt32(cell)
            : slot == typeof(IntPtr) ? (object)Marshal.ReadIntPtr(cell)
            : slot == typeof(float) ? (object)BitConverter.Int32BitsToSingle(Marshal.ReadInt32(cell))
            : slot == typeof(double) ? (object)BitConverter.Int64BitsToDouble(Marshal.ReadInt64(cell))
            : (object)Marshal.ReadInt64(cell);

        // a copy in the ANSI code page, which the function may write into; a variable's buffer takes it back, the length it was.
        private bool PassBuffer(NativeArgument argument, out Func<ExternalValue>? written)
        {
            var bytes = ansi.GetBytes(argument.Value.Text ?? string.Empty);
            var buffer = Marshal.AllocHGlobal(bytes.Length + 1);
            _free.Add(() => Marshal.FreeHGlobal(buffer));
            Marshal.Copy(bytes, 0, buffer, bytes.Length);
            Marshal.WriteByte(buffer, bytes.Length, 0);
            Add(typeof(IntPtr), buffer);

            written = () =>
            {
                var back = new byte[bytes.Length];
                Marshal.Copy(buffer, back, 0, back.Length);
                return ExternalValues.ToWire(ansi.GetString(back), Unhandled);
            };
            return true;
        }

        // the address of a BSTR of ANSI characters, which the function may replace.
        private bool PassBstr(NativeArgument argument, out Func<ExternalValue>? written)
        {
            var cell = Marshal.AllocHGlobal(IntPtr.Size);
            Marshal.WriteIntPtr(cell, platform.AllocateByteString(ansi.GetBytes(argument.Value.Text ?? string.Empty)));
            _free.Add(() =>
            {
                platform.FreeString(Marshal.ReadIntPtr(cell));
                Marshal.FreeHGlobal(cell);
            });

            Add(typeof(IntPtr), cell);
            written = () => ExternalValues.ToWire(ansi.GetString(platform.ReadByteString(Marshal.ReadIntPtr(cell))), Unhandled);
            return true;
        }

        // a VARIANT, by value or by its address.
        private bool PassVariant(NativeArgument argument, out Func<ExternalValue>? written)
        {
            written = null;
            object? value;
            try
            {
                value = ExternalValues.FromWire(argument.Value, Unhandled);
            }
            catch (NotSupportedException)
            {
                return false;
            }

            var buffer = Marshal.AllocHGlobal(platform.VariantSize);
            platform.WriteVariant(value, buffer);
            _free.Add(() =>
            {
                platform.ClearVariant(buffer);
                Marshal.FreeHGlobal(buffer);
            });

            if (argument.ByReference)
            {
                Add(typeof(IntPtr), buffer);
                written = () => Neutral(platform.ReadVariant(buffer));
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

        // what a function returns, natively; a BSTR and a VARIANT are a platform's to have.
        public Type? TypeOf(NativeSlot returns) => returns switch
        {
            NativeSlot.Void => typeof(void),
            NativeSlot.AnsiBstr when platform.HasAutomationTypes => typeof(IntPtr),
            NativeSlot.Variant when platform.HasAutomationTypes => IntPtr.Size == 8 ? typeof(NativeVariant64) : typeof(NativeVariant32),
            NativeSlot.AnsiBstr or NativeSlot.Variant or NativeSlot.AnsiBuffer => null,
            _ => ScalarTypeOf(returns),
        };

        public ExternalValue Returned(object? returned, NativeSlot returns)
        {
            switch (returns)
            {
                case NativeSlot.Void:
                    return new ExternalValue();
                case NativeSlot.AnsiBstr:
                    var bstr = (IntPtr)returned!;
                    var text = ansi.GetString(platform.ReadByteString(bstr));
                    platform.FreeString(bstr);
                    return ExternalValues.ToWire(text, Unhandled);
                case NativeSlot.Variant:
                    var buffer = Marshal.AllocHGlobal(platform.VariantSize);
                    try
                    {
                        Marshal.StructureToPtr(returned!, buffer, fDeleteOld: false);
                        var value = platform.ReadVariant(buffer);
                        platform.ClearVariant(buffer);
                        return Neutral(value);
                    }
                    finally
                    {
                        Marshal.FreeHGlobal(buffer);
                    }

                default:
                    return ExternalValues.ToWire(Unpointed(returned), Unhandled);
            }
        }

        private void Add(Type slot, object? value)
        {
            _slots.Add(slot);
            _values.Add(value);
        }

        // a VARIANT that holds an object holds what no handle is the handle of here: it crosses as nothing.
        private static ExternalValue Neutral(object? value)
        {
            try
            {
                return ExternalValues.ToWire(value, Unhandled);
            }
            catch (NotSupportedException)
            {
                return new ExternalValue();
            }
        }

        private static Type? ScalarTypeOf(NativeSlot slot) => slot switch
        {
            NativeSlot.Byte => typeof(byte),
            NativeSlot.Int16 => typeof(short),
            NativeSlot.Int32 => typeof(int),
            NativeSlot.Int64 => typeof(long),
            NativeSlot.Pointer => typeof(IntPtr),
            NativeSlot.Single => typeof(float),
            NativeSlot.Double => typeof(double),
            _ => null,
        };

        // the value of a number as the native type it is passed as.
        private static object? Scalar(Type slot, object? value)
        {
            if (value is null or DBNull)
            {
                value = 0;
            }

            return slot == typeof(IntPtr)
                ? (IntPtr)Convert.ToInt64(value, CultureInfo.InvariantCulture)
                : Convert.ChangeType(value, slot, CultureInfo.InvariantCulture);
        }

        // a pointer crosses as the number it is.
        private static object? Unpointed(object? value) => value is IntPtr pointer ? (long)pointer : value;

        // what a native call passes is values, never objects: there is no handle for one, either way.
        private static long Unhandled(object value) => throw new NotSupportedException();

        private static object Unhandled(long handle) => throw new NotSupportedException();
    }
}
