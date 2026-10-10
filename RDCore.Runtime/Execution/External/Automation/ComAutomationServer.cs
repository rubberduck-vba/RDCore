using System.Collections.Concurrent;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Runtime.Versioning;

namespace RDCore.Runtime.Execution.External.Automation;

/// <summary>
/// The automation servers of a Windows machine: COM objects, created by the name their class is registered under and called late-bound through
/// <c>IDispatch</c>.
/// </summary>
/// <remarks>
/// <para>
/// Every call is made from one thread of its own, a single-threaded apartment, whatever thread the pipeline runs a program on. That is the apartment a
/// server written for a desktop expects its client to be in, and it keeps an object on the one thread that created it: a program is a sequence of calls, and
/// nothing about it is concurrent.
/// </para>
/// <para>
/// The call itself is the runtime's own <c>IDispatch</c> binder (<see cref="Type.InvokeMember(string, BindingFlags, Binder, object, object[], ParameterModifier[], CultureInfo, string[])"/>),
/// which is what a late-bound call from VBA is: names resolved to dispatch identifiers by the server, arguments as <c>VARIANT</c>s, and the same
/// <c>DISPATCH_METHOD | DISPATCH_PROPERTYGET</c> a call written without knowing which it is asks for.
/// </para>
/// </remarks>
[SupportedOSPlatform("windows")]
public sealed class ComAutomationServer : IAutomationServer, IDisposable
{
    private const int MemberNotFound = unchecked((int)0x80020003);
    private const int TypeMismatch = unchecked((int)0x80020005);
    private const int ClassNotRegistered = unchecked((int)0x80040154);
    private const int Unspecified = unchecked((int)0x80004005);

    private readonly BlockingCollection<Action> _work = [];
    private readonly Lazy<Thread> _apartment;

    /// <summary>
    /// Creates the server; the apartment is started by the first call that needs one.
    /// </summary>
    public ComAutomationServer()
    {
        _apartment = new Lazy<Thread>(() =>
        {
            var thread = new Thread(Pump) { IsBackground = true, Name = "RDCore automation apartment" };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            return thread;
        });
    }

    /// <inheritdoc/>
    public bool IsAvailable => true;

    /// <inheritdoc/>
    public object CreateObject(string progId) => OnApartment(() =>
    {
        var type = Type.GetTypeFromProgID(progId, throwOnError: false)
            ?? throw new AutomationException(ClassNotRegistered, $"'{progId}' is not registered on this machine.");

        return Activator.CreateInstance(type) ?? throw new AutomationException(ClassNotRegistered, $"'{progId}' could not be created.");
    })!;

    /// <inheritdoc/>
    public object? Invoke(object target, string member, AutomationInvocation invocation, object?[] arguments, bool[] byReference, CultureInfo culture)
        => OnApartment(() => InvokeMember(target, member, invocation, arguments, byReference, culture));

    /// <inheritdoc/>
    public string? ClassNameOf(object target) => OnApartment(() =>
    {
        try
        {
            if (target is not IDispatchTypeInformation dispatch || dispatch.GetTypeInfoCount(out var count) != 0 || count == 0)
            {
                return null;
            }

            dispatch.GetTypeInfo(0, 0, out var typeInfo);
            typeInfo.GetDocumentation(-1, out var className, out _, out _, out _);
            typeInfo.GetContainingTypeLib(out var library, out _);
            library.GetDocumentation(-1, out var libraryName, out _, out _, out _);
            return $"{libraryName}.{className}";
        }
        catch (COMException)
        {
            return null;
        }
    });

    /// <inheritdoc/>
    public bool MoveNext(object enumerator, out object? current)
    {
        var member = OnApartment(() =>
        {
            // the runtime's binder hands the IEnumVARIANT of an enumeration member over as the managed view of one.
            if (enumerator is System.Collections.IEnumerator managed)
            {
                return managed.MoveNext() ? new Member(managed.Current) : null;
            }

            if (enumerator is not IEnumVariant native)
            {
                throw new AutomationException(TypeMismatch, "The object is not an enumerator.");
            }

            // IEnumVARIANT::Next(1, ...) fills one element and says how many it filled; S_FALSE is the end of the members.
            var buffer = new object?[1];
            var fetched = Marshal.AllocCoTaskMem(sizeof(int));
            try
            {
                Marshal.WriteInt32(fetched, 0);
                var status = native.Next(1, buffer, fetched);
                if (status < 0)
                {
                    throw new AutomationException(status, "The enumerator failed.");
                }

                return Marshal.ReadInt32(fetched) == 1 ? new Member(buffer[0]) : null;
            }
            finally
            {
                Marshal.FreeCoTaskMem(fetched);
            }
        });

        current = member?.Value;
        return member is not null;
    }

    /// <inheritdoc/>
    public void Reset(object enumerator) => OnApartment<object?>(() =>
    {
        if (enumerator is System.Collections.IEnumerator managed)
        {
            managed.Reset();
            return null;
        }

        if (enumerator is not IEnumVariant native)
        {
            throw new AutomationException(TypeMismatch, "The object is not an enumerator.");
        }

        var status = native.Reset();
        return status < 0 ? throw new AutomationException(status, "The enumerator cannot be reset.") : null;
    });

    // an element that can be Empty, which is not the absence of one.
    private sealed record Member(object? Value);

    /// <inheritdoc/>
    public void Release(object handle) => OnApartment<object?>(() =>
    {
        if (Marshal.IsComObject(handle))
        {
            _ = Marshal.FinalReleaseComObject(handle);
        }

        return null;
    });

    /// <summary>
    /// Ends the apartment. Every object that was made on it has been let go of by whatever held it.
    /// </summary>
    public void Dispose() => _work.CompleteAdding();

    // what the locale of a call is to a COM server: a language it has installed. The invariant culture's is not one - Excel answers "old format or invalid type
    // library" to it - so the neutral locale of the environment is the one that every server has, American English.
    private static CultureInfo LocaleOf(CultureInfo culture)
        => culture.Equals(CultureInfo.InvariantCulture) ? CultureInfo.GetCultureInfo("en-US") : culture;

    private static object? InvokeMember(
        object target, string member, AutomationInvocation invocation, object?[] arguments, bool[] byReference, CultureInfo culture)
    {
        // an argument the caller left out at the end is no argument: a property that takes none is not called with some, and an optional one is the same
        // absent either way. A value that is being assigned is the last argument, and always there.
        var count = arguments.Length;
        if (invocation is not (AutomationInvocation.Let or AutomationInvocation.Set))
        {
            while (count > 0 && arguments[count - 1] is Missing)
            {
                count--;
            }
        }

        var passed = arguments[..count];
        // a modifier of no parameters is refused, and a call with no arguments has none to modify.
        ParameterModifier[]? modifiers = null;
        if (count > 0)
        {
            var modifier = new ParameterModifier(count);
            for (var index = 0; index < count; index++)
            {
                modifier[index] = byReference[index];
            }

            modifiers = [modifier];
        }

        var flags = BindingFlags.Public | BindingFlags.Instance | invocation switch
        {
            AutomationInvocation.Get => BindingFlags.GetProperty | BindingFlags.InvokeMethod,
            AutomationInvocation.Let => BindingFlags.SetProperty,
            AutomationInvocation.Set => BindingFlags.PutRefDispProperty,
            _ => BindingFlags.InvokeMethod,
        };

        try
        {
            var returned = target.GetType().InvokeMember(member, flags, null, target, passed, modifiers, LocaleOf(culture), null);
            Array.Copy(passed, arguments, count);
            return returned;
        }
        catch (TargetInvocationException exception) when (exception.InnerException is COMException failure)
        {
            throw new AutomationException(failure.HResult, failure.Message, failure.Source);
        }
        catch (COMException failure)
        {
            throw new AutomationException(failure.HResult, failure.Message, failure.Source);
        }
        catch (MissingMemberException)
        {
            throw new AutomationException(MemberNotFound, $"The object has no member '{member}'.");
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidCastException or FormatException)
        {
            throw new AutomationException(TypeMismatch, exception.Message);
        }
        catch (TargetInvocationException exception)
        {
            throw new AutomationException(Unspecified, exception.InnerException?.Message ?? exception.Message);
        }
    }

    private void Pump()
    {
        foreach (var action in _work.GetConsumingEnumerable())
        {
            action();
        }
    }

    // runs it on the apartment and waits: the pipeline is synchronous, and the object that a call is made on is on that thread.
    private T? OnApartment<T>(Func<T?> call)
    {
        _ = _apartment.Value;
        if (Environment.CurrentManagedThreadId == _apartment.Value.ManagedThreadId)
        {
            return call();
        }

        T? result = default;
        Exception? failure = null;
        using var done = new ManualResetEventSlim();
        _work.Add(() =>
        {
            try
            {
                result = call();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
            finally
            {
                done.Set();
            }
        });

        done.Wait();
        if (failure is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        }

        return result;
    }

    // IDispatch, for the one thing the runtime's binder does not offer: asking an object what it is. The members it is declared with are in the order of the
    // interface's table, and the ones that are not called are only there to keep the later ones where they belong.
    // IEnumVARIANT, which the enumeration member of a server's collection returns.
    [ComImport]
    [Guid("00020404-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IEnumVariant
    {
        [PreserveSig]
        int Next(int count, [Out, MarshalAs(UnmanagedType.LPArray, ArraySubType = UnmanagedType.Struct, SizeParamIndex = 0)] object?[] values, IntPtr fetched);

        [PreserveSig]
        int Skip(int count);

        [PreserveSig]
        int Reset();

        [PreserveSig]
        int Clone(out IEnumVariant enumerator);
    }

    [ComImport]
    [Guid("00020400-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDispatchTypeInformation
    {
        [PreserveSig]
        int GetTypeInfoCount(out int count);

        void GetTypeInfo(int index, int lcid, out ITypeInfo typeInfo);
    }
}
