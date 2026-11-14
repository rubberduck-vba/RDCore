using RDCore.External.Automation;
using System.Collections.Concurrent;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Runtime.Versioning;

namespace RDCore.External.Windows.Automation;

// A member of a server's object, called as a late-bound call from VBA calls it: its name made a dispatch identifier (GetIDsOfNames), and IDispatch::Invoke
// called with the arguments as VARIANTs, last first. What it returns is read by NativeVariant, which keeps the Error values the runtime's binder reads as Longs.
//
// A name's dispatch identifier is the same for every object of a class: it is asked of the object once, and kept for the class its type information
// describes, and for the object itself. An object whose interface is IDispatchEx can add a member, and give a name another identifier, at any time: its
// identifiers are kept for the object alone.
[SupportedOSPlatform("windows")]
internal static class DispatchCall
{
    private const int DispatchValue = 0;
    private const int DispatchPropertyPut = -3;

    private const ushort Method = 1;
    private const ushort PropertyGet = 2;
    private const ushort PropertyPut = 4;
    private const ushort PropertyPutReference = 8;

    private const int UnknownName = unchecked((int)0x80020006);
    private const int MemberNotFound = unchecked((int)0x80020003);
    private const int Exception = unchecked((int)0x80020009);
    private const int NotAnObject = unchecked((int)0x80020005);

    private static readonly Guid NoInterface = Guid.Empty;

    // the identifiers of the names of a class, by its type information; and of the names of one object, by the object.
    private static readonly ConcurrentDictionary<Guid, ConcurrentDictionary<string, int[]>> ByClass = new();
    private static readonly ConditionalWeakTable<object, Identifiers> ByObject = [];

    // the identifiers an object's names have, and the class's that it shares them with: none, for an object of IDispatchEx or with no type information.
    private sealed class Identifiers(ConcurrentDictionary<string, int[]> own, ConcurrentDictionary<string, int[]>? shared)
    {
        public ConcurrentDictionary<string, int[]> Own { get; } = own;

        public ConcurrentDictionary<string, int[]>? Shared { get; } = shared;
    }

    // the named arguments are the last of the arguments, before a value that is assigned: as many as there are names.
    public static object? Invoke(
        object target, string member, AutomationInvocation invocation, object?[] arguments, bool[] byReference, IReadOnlyList<string> names, CultureInfo culture)
    {
        if (target is not IDispatchCalls dispatch)
        {
            throw new AutomationException(NotAnObject, "The object cannot be called through IDispatch.");
        }

        var locale = culture.LCID;
        var count = arguments.Length;
        var assigning = invocation is AutomationInvocation.Let or AutomationInvocation.Set;
        var positional = count - names.Count - (assigning ? 1 : 0);

        // an argument the caller left out at the end is no argument: a property that takes none is not called with some, and an optional one is the same
        // absent either way. A value that is being assigned is the last argument, and always there; a named one is there because it is named.
        var dropped = 0;
        if (!assigning && names.Count == 0)
        {
            while (count - dropped > 0 && arguments[count - dropped - 1] is Missing)
            {
                dropped++;
            }
        }

        var ids = IdentifiersOf(dispatch, target, member, names, locale);
        var namedCount = names.Count + (assigning ? 1 : 0);
        var passedCount = count - dropped;

        // DISPPARAMS: the named arguments first, in the order of their identifiers, then the others, last first. An assigned value is named
        // DISPID_PROPERTYPUT.
        var order = new int[passedCount];
        var namedIds = new int[namedCount];
        var slot = 0;
        if (assigning)
        {
            order[slot] = count - 1;
            namedIds[slot++] = DispatchPropertyPut;
        }

        for (var index = 0; index < names.Count; index++)
        {
            order[slot] = positional + index;
            namedIds[slot++] = ids[index + 1];
        }

        for (var index = positional - dropped - 1; index >= 0; index--)
        {
            order[slot++] = index;
        }

        var block = NativeVariant.Allocate(passedCount);
        var references = new IntPtr[passedCount];
        var result = NativeVariant.Allocate(1);
        var exception = Marshal.AllocCoTaskMem(Marshal.SizeOf<EXCEPINFO>());
        var namedBlock = Marshal.AllocCoTaskMem(Math.Max(1, namedCount) * sizeof(int));
        try
        {
            for (var position = 0; position < passedCount; position++)
            {
                var argument = order[position];
                var location = block + (position * NativeVariant.Size);
                if (byReference[argument])
                {
                    // what the server writes through the reference is read back from the VARIANT it refers to.
                    references[position] = NativeVariant.Allocate(1);
                    NativeVariant.Write(arguments[argument], references[position]);
                    Marshal.WriteInt16(location, unchecked((short)(0x4000 | 12))); // VT_BYREF | VT_VARIANT
                    Marshal.WriteIntPtr(location, 8, references[position]);
                }
                else
                {
                    NativeVariant.Write(arguments[argument], location);
                }
            }

            Marshal.Copy(namedIds, 0, namedBlock, namedCount);
            for (var offset = 0; offset < Marshal.SizeOf<EXCEPINFO>(); offset++)
            {
                Marshal.WriteByte(exception, offset, 0);
            }

            var parameters = new DispatchParameters
            {
                Arguments = passedCount == 0 ? IntPtr.Zero : block,
                NamedArgumentIds = namedCount == 0 ? IntPtr.Zero : namedBlock,
                ArgumentCount = passedCount,
                NamedArgumentCount = namedCount,
            };

            var flags = invocation switch
            {
                AutomationInvocation.Get => (ushort)(Method | PropertyGet),
                AutomationInvocation.Let => PropertyPut,
                AutomationInvocation.Set => PropertyPutReference,
                _ => Method,
            };

            var noInterface = NoInterface;
            var status = dispatch.Invoke(ids[0], ref noInterface, locale, flags, ref parameters, assigning ? IntPtr.Zero : result, exception, IntPtr.Zero);
            if (status < 0)
            {
                throw Failure(status, exception, member);
            }

            for (var position = 0; position < passedCount; position++)
            {
                if (references[position] != IntPtr.Zero)
                {
                    arguments[order[position]] = NativeVariant.Read(references[position]);
                }
            }

            return assigning ? null : NativeVariant.Read(result);
        }
        finally
        {
            for (var position = 0; position < passedCount; position++)
            {
                NativeVariant.Clear(block + (position * NativeVariant.Size));
                if (references[position] != IntPtr.Zero)
                {
                    NativeVariant.Clear(references[position]);
                    Marshal.FreeCoTaskMem(references[position]);
                }
            }

            NativeVariant.Clear(result);
            Marshal.FreeCoTaskMem(result);
            Marshal.FreeCoTaskMem(block);
            Marshal.FreeCoTaskMem(namedBlock);
            Marshal.FreeCoTaskMem(exception);
        }
    }

    // the identifiers of the member and of the names of its named arguments: [DISPID=n], the name LateBinding gives the default member and the
    // enumerator, is n.
    private static int[] IdentifiersOf(IDispatchCalls dispatch, object target, string member, IReadOnlyList<string> names, int locale)
    {
        if (names.Count == 0 && member.StartsWith("[DISPID=", StringComparison.OrdinalIgnoreCase) && member.EndsWith(']')
            && int.TryParse(member.AsSpan(8, member.Length - 9), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var known))
        {
            return [known];
        }

        var key = names.Count == 0 ? member : $"{member}({string.Join(",", names)})";
        var identifiers = ByObject.GetValue(target, _ => new Identifiers(new(StringComparer.OrdinalIgnoreCase), SharedOf(dispatch, target)));
        if (identifiers.Own.TryGetValue(key, out var own))
        {
            return own;
        }

        if (identifiers.Shared?.TryGetValue(key, out var shared) == true)
        {
            identifiers.Own[key] = shared;
            return shared;
        }

        var asked = Ask(dispatch, [member, .. names], locale);
        identifiers.Own[key] = asked;
        if (identifiers.Shared is not null)
        {
            identifiers.Shared[key] = asked;
        }

        return asked;
    }

    // the identifiers of the class the object's type information describes, which it shares with every object of the class; none for one that can add
    // members (IDispatchEx), or that does not describe itself.
    // The class is known by the interface its type information describes: a GUID, which holds nothing of the server's.
    private static ConcurrentDictionary<string, int[]>? SharedOf(IDispatchCalls dispatch, object target)
    {
        try
        {
            if (dispatch.GetTypeInfo(0, 0, out var typeInfo) < 0 || typeInfo == IntPtr.Zero)
            {
                return null;
            }

            Guid described;
            try
            {
                var info = (ITypeInfo)Marshal.GetObjectForIUnknown(typeInfo);
                info.GetTypeAttr(out var attributes);
                try
                {
                    described = Marshal.PtrToStructure<TYPEATTR>(attributes).guid;
                }
                finally
                {
                    info.ReleaseTypeAttr(attributes);
                }
            }
            finally
            {
                _ = Marshal.Release(typeInfo);
            }

            return described == Guid.Empty || target is IDispatchExMarker ? null : ByClass.GetOrAdd(described, _ => new(StringComparer.OrdinalIgnoreCase));
        }
        catch (COMException)
        {
            return null;
        }
    }

    private static int[] Ask(IDispatchCalls dispatch, string[] names, int locale)
    {
        var strings = names.Select(Marshal.StringToCoTaskMemUni).ToArray();
        var pointers = Marshal.AllocCoTaskMem(names.Length * IntPtr.Size);
        var identifiers = Marshal.AllocCoTaskMem(names.Length * sizeof(int));
        try
        {
            for (var index = 0; index < names.Length; index++)
            {
                Marshal.WriteIntPtr(pointers, index * IntPtr.Size, strings[index]);
            }

            var noInterface = NoInterface;
            var status = dispatch.GetIDsOfNames(ref noInterface, pointers, names.Length, locale, identifiers);
            if (status < 0)
            {
                throw status == UnknownName
                    ? new AutomationException(MemberNotFound, names.Length == 1 ? $"The object has no member '{names[0]}'." : $"The object has no member '{names[0]}' with arguments named {string.Join(", ", names.Skip(1))}.")
                    : new AutomationException(status, Marshal.GetExceptionForHR(status)?.Message);
            }

            var result = new int[names.Length];
            Marshal.Copy(identifiers, result, 0, names.Length);
            return result;
        }
        finally
        {
            foreach (var text in strings)
            {
                Marshal.FreeCoTaskMem(text);
            }

            Marshal.FreeCoTaskMem(pointers);
            Marshal.FreeCoTaskMem(identifiers);
        }
    }

    // what a failed call is: the error the server described (DISP_E_EXCEPTION), whose code is its scode, or the code of the server's own error
    // number when it gives none; or the status the call returned.
    private static AutomationException Failure(int status, IntPtr exception, string member)
    {
        if (status != Exception)
        {
            return new AutomationException(status, Marshal.GetExceptionForHR(status)?.Message ?? $"'{member}' failed.");
        }

        var filled = Marshal.ReadIntPtr(exception, Marshal.OffsetOf<EXCEPINFO>(nameof(EXCEPINFO.pfnDeferredFillIn)).ToInt32());
        if (filled != IntPtr.Zero)
        {
            _ = Marshal.GetDelegateForFunctionPointer<DeferredFillIn>(filled)(exception);
        }

        var information = Marshal.PtrToStructure<EXCEPINFO>(exception);
        try
        {
            var code = information.scode != 0 ? information.scode : unchecked((int)(0x800A0000u | (ushort)information.wCode));
            return new AutomationException(code, information.bstrDescription, information.bstrSource);
        }
        finally
        {
            Free(exception, nameof(EXCEPINFO.bstrSource));
            Free(exception, nameof(EXCEPINFO.bstrDescription));
            Free(exception, nameof(EXCEPINFO.bstrHelpFile));
        }
    }

    // PtrToStructure copied the BSTRs of the EXCEPINFO as strings: the server's are the caller's to free.
    private static void Free(IntPtr exception, string field)
    {
        var text = Marshal.ReadIntPtr(exception, Marshal.OffsetOf<EXCEPINFO>(field).ToInt32());
        if (text != IntPtr.Zero)
        {
            Marshal.FreeBSTR(text);
        }
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int DeferredFillIn(IntPtr exception);

    [StructLayout(LayoutKind.Sequential)]
    private struct DispatchParameters
    {
        public IntPtr Arguments;
        public IntPtr NamedArgumentIds;
        public int ArgumentCount;
        public int NamedArgumentCount;
    }

    // IDispatch, called: every member as the table has it, the status returned and not thrown.
    [ComImport]
    [Guid("00020400-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDispatchCalls
    {
        [PreserveSig]
        int GetTypeInfoCount(out int count);

        [PreserveSig]
        int GetTypeInfo(int index, int locale, out IntPtr typeInfo);

        [PreserveSig]
        int GetIDsOfNames(ref Guid identifier, IntPtr names, int count, int locale, IntPtr identifiers);

        [PreserveSig]
        int Invoke(int member, ref Guid identifier, int locale, ushort flags, ref DispatchParameters parameters, IntPtr result, IntPtr exception, IntPtr argumentError);
    }

    // IDispatchEx, asked of an object only to know whether it is one: an object that can add members to itself.
    [ComImport]
    [Guid("A6EF9860-C720-11D0-9337-00A0C90DCAA9")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDispatchExMarker
    {
    }
}
