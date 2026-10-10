using System.Collections.Concurrent;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.InteropServices;

namespace RDCore.External.Native;

/// <summary>
/// Calls a native function by its address, with a signature that is known only when the program runs.
/// </summary>
/// <remarks>
/// A <c>Declare</c> says what a function takes and returns, and nothing compiled ahead of time can: the call is a small method emitted once per signature,
/// which loads the arguments and makes an unmanaged <c>calli</c> in the platform's calling convention (<c>stdcall</c> on 32-bit Windows, the one
/// convention there is everywhere else). Every argument has already been made one of the blittable types a native function takes - an integer, a float,
/// a pointer, or a <c>VARIANT</c> by value - so nothing is marshalled here.
/// <para>
/// The system error code the function leaves is read inside the emitted method, right after the call, before anything else on the thread can change it:
/// that is <c>Err.LastDllError</c>.
/// </para>
/// <para>
/// ⚠️ A native function is native code. One that is called with arguments it does not expect can take the whole process down, and nothing here can stop it:
/// that is what the policy over library imports is for.
/// </para>
/// </remarks>
internal static class NativeCalls
{
    private static readonly ConcurrentDictionary<string, DynamicMethod> Methods = new(StringComparer.Ordinal);

    private static readonly MethodInfo ClearLastError = typeof(Marshal).GetMethod(nameof(Marshal.SetLastSystemError))!;
    private static readonly MethodInfo ReadLastError = typeof(Marshal).GetMethod(nameof(Marshal.GetLastSystemError))!;

    /// <summary>
    /// Calls the function at <paramref name="function"/>.
    /// </summary>
    /// <param name="function">The address of the function.</param>
    /// <param name="returns">What it returns, as a blittable type, or <see cref="void"/>.</param>
    /// <param name="takes">What each argument is passed as, as a blittable type.</param>
    /// <param name="arguments">The arguments, each a value of the type <paramref name="takes"/> says.</param>
    /// <param name="lastError">The system error code the function left.</param>
    /// <returns>What it returned, boxed; <see langword="null"/> for a function that returns nothing.</returns>
    public static object? Invoke(IntPtr function, Type returns, Type[] takes, object?[] arguments, out int lastError)
    {
        var method = Methods.GetOrAdd(KeyOf(returns, takes), _ => Emit(returns, takes));
        object?[] passed = [function, 0, .. arguments];
        var returned = method.Invoke(null, passed);
        lastError = (int)passed[1]!;
        return returned;
    }

    private static string KeyOf(Type returns, Type[] takes) => $"{returns.FullName}({string.Join(",", takes.Select(type => type.FullName))})";

    // (IntPtr function, ref int lastError, arguments...): the error is cleared before the call and read after it, the result kept meanwhile.
    private static DynamicMethod Emit(Type returns, Type[] takes)
    {
        var method = new DynamicMethod("RDCore.Declare", returns, [typeof(IntPtr), typeof(int).MakeByRefType(), .. takes], typeof(NativeCalls).Module, skipVisibility: true);
        var il = method.GetILGenerator();

        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Call, ClearLastError);

        for (var index = 0; index < takes.Length; index++)
        {
            il.Emit(OpCodes.Ldarg, (short)(index + 2));
        }

        il.Emit(OpCodes.Ldarg_0);
        il.EmitCalli(OpCodes.Calli, CallingConvention.Winapi, returns, takes);

        var result = returns == typeof(void) ? null : il.DeclareLocal(returns);
        if (result is not null)
        {
            il.Emit(OpCodes.Stloc, result);
        }

        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, ReadLastError);
        il.Emit(OpCodes.Stind_I4);

        if (result is not null)
        {
            il.Emit(OpCodes.Ldloc, result);
        }

        il.Emit(OpCodes.Ret);
        return method;
    }
}

/// <summary>
/// A <c>VARIANT</c> passed by value on a 64-bit platform: 24 bytes.
/// </summary>
[StructLayout(LayoutKind.Sequential, Size = 24)]
internal struct NativeVariant64
{
    public long Word0;
    public long Word1;
    public long Word2;
}

/// <summary>
/// A <c>VARIANT</c> passed by value on a 32-bit platform: 16 bytes.
/// </summary>
[StructLayout(LayoutKind.Sequential, Size = 16)]
internal struct NativeVariant32
{
    public long Word0;
    public long Word1;
}
