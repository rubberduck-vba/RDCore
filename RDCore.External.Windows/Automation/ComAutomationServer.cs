using RDCore.External.Automation;
using System.Collections.Concurrent;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Runtime.Versioning;

namespace RDCore.External.Windows.Automation;

/// <summary>
/// The automation servers of a Windows machine: COM objects, created by the name their class is registered under and called late-bound through
/// <c>IDispatch</c>.
/// </summary>
/// <remarks>
/// <para>
/// Every call is made from one thread, a single-threaded apartment (<see cref="ComApartment"/>), whatever thread the pipeline runs a program on. That is the
/// apartment a server written for a desktop expects its client to be in, and it keeps an object on the one thread that created it: a program is a sequence of
/// calls, and nothing about it is concurrent. The forms of the program live there too, so that their controls are objects of this server like any other.
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
    private const int BadParameterCount = unchecked((int)0x8002000E);

    private readonly ComApartment _apartment;
    private readonly bool _ownsApartment;

    /// <summary>
    /// Creates the server, on an apartment of its own; the apartment is started by the first call that needs one.
    /// </summary>
    public ComAutomationServer()
        : this(new ComApartment())
    {
        _ownsApartment = true;
    }

    /// <summary>
    /// Creates the server, on <paramref name="apartment"/>: the one its objects share with whatever else lives there - the forms that host Microsoft Forms, whose
    /// controls are its objects too.
    /// </summary>
    /// <param name="apartment">The apartment; whoever made it ends it.</param>
    public ComAutomationServer(ComApartment apartment)
    {
        _apartment = apartment;
        _apartment.RaiseEvent = RaiseFor;
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

    // what GetObject asks for that COM says the file, or the moniker, cannot be opened as: MS-VBA's 432 (recorded in Excel, oracle\s25 G02).
    private const int CannotOpenFile = unchecked((int)0x800401EA);

    /// <inheritdoc/>
    public object GetObject(SDK.Runtime.Abstract.Execution.ExternalObjectRequest request) => OnApartment(() =>
    {
        try
        {
            return request.Source switch
            {
                SDK.Runtime.Abstract.Execution.ExternalObjectSource.Create => CreateOn(request.ProgId ?? string.Empty, request.ServerName),
                SDK.Runtime.Abstract.Execution.ExternalObjectSource.Running => Running(request.ProgId ?? string.Empty),
                _ => FromFile(request.PathName ?? string.Empty, request.ProgId),
            };
        }
        catch (COMException failure)
        {
            throw new AutomationException(failure.HResult, failure.Message);
        }
    })!;

    // a new object of the class, on the machine named - this one when none is.
    private static object CreateOn(string progId, string? serverName)
    {
        var type = (serverName is { Length: > 0 } ? Type.GetTypeFromProgID(progId, serverName, throwOnError: false) : Type.GetTypeFromProgID(progId, throwOnError: false))
            ?? throw new AutomationException(ClassNotRegistered, $"'{progId}' is not registered on this machine.");

        return Activator.CreateInstance(type) ?? throw new AutomationException(ClassNotRegistered, $"'{progId}' could not be created.");
    }

    // the object of the class that is running: the one the running object table has for its class identifier.
    private static object Running(string progId)
    {
        var status = CLSIDFromProgID(progId, out var classId);
        if (status < 0)
        {
            throw new AutomationException(status, $"'{progId}' is not registered on this machine.");
        }

        status = GetActiveObject(ref classId, IntPtr.Zero, out var running);
        return status < 0 ? throw new AutomationException(status, $"No object of '{progId}' is running.") : running;
    }

    // the object a file is, or a moniker names: the one its own class opens it as, or the class named (IPersistFile.Load).
    private static object FromFile(string pathName, string? progId)
    {
        if (progId is not { Length: > 0 })
        {
            var dispatch = new Guid("00020400-0000-0000-C000-000000000046");
            var status = CoGetObject(pathName, IntPtr.Zero, ref dispatch, out var named);
            return status < 0 ? throw new AutomationException(status is unchecked((int)0x800401E4) ? CannotOpenFile : status, $"'{pathName}' cannot be opened.") : named;
        }

        var created = CreateOn(progId, serverName: null);
        if (created is not IPersistFile file)
        {
            throw new AutomationException(CannotOpenFile, $"'{progId}' does not open files.");
        }

        file.Load(pathName, 0);
        return created;
    }

    [DllImport("ole32.dll", CharSet = CharSet.Unicode)]
    private static extern int CLSIDFromProgID(string progId, out Guid classId);

    [DllImport("oleaut32.dll")]
    private static extern int GetActiveObject(ref Guid classId, IntPtr reserved, [MarshalAs(UnmanagedType.IUnknown)] out object running);

    [DllImport("ole32.dll", CharSet = CharSet.Unicode)]
    private static extern int CoGetObject(string name, IntPtr bindOptions, ref Guid interfaceId, [MarshalAs(UnmanagedType.IUnknown)] out object named);

    /// <inheritdoc/>
    public object? Invoke(object target, string member, AutomationInvocation invocation, object?[] arguments, bool[] byReference, CultureInfo culture)
        => OnApartment(() =>
        {
            // an object of the session is given to the server as its stand-in; what the server leaves in an argument passed by reference comes back as it is.
            for (var index = 0; index < arguments.Length; index++)
            {
                arguments[index] = ToServer(arguments[index]);
            }

            return InvokeMember(target, member, invocation, arguments, byReference, culture);
        });

    /// <inheritdoc/>
    public string? ClassNameOf(object target) => OnApartment(() =>
    {
        try
        {
            // the class the object says it is, or the class its library makes the interface it answers to the default one of: the class MS-VBA's TypeName
            // names (`Dictionary`, of an object that answers to `IDictionary`; recorded in Excel, oracle\s25).
            var typeInfo = ClassInfoOf(target);
            if (typeInfo is null)
            {
                if (target is not IDispatchTypeInformation dispatch || dispatch.GetTypeInfoCount(out var count) != 0 || count == 0)
                {
                    return null;
                }

                dispatch.GetTypeInfo(0, 0, out typeInfo);
            }

            typeInfo.GetContainingTypeLib(out var library, out _);
            library.GetDocumentation(-1, out var libraryName, out _, out _, out _);
            return $"{libraryName}.{ClassNameOf(typeInfo, library)}";
        }
        catch (COMException)
        {
            return null;
        }
    });

    // IProvideClassInfo, which not every object answers to, nor answers when it does.
    private static ITypeInfo? ClassInfoOf(object target)
    {
        if (target is not IProvideClassInformation provider)
        {
            return null;
        }

        try
        {
            provider.GetClassInfo(out var classInfo);
            return classInfo;
        }
        catch (COMException)
        {
            return null;
        }
    }

    // the name of the class an interface is the default one of, by the interface's identifier: a library's classes are looked through once for each.
    private readonly ConcurrentDictionary<Guid, string> _classesByInterface = new();

    private string ClassNameOf(ITypeInfo typeInfo, ITypeLib library)
    {
        typeInfo.GetDocumentation(-1, out var name, out _, out _, out _);
        var (kind, identifier, _) = AttributesOf(typeInfo);
        if (kind is TYPEKIND.TKIND_COCLASS)
        {
            return name;
        }

        return _classesByInterface.GetOrAdd(identifier, _ => DefaultClassOf(library, identifier) ?? name);
    }

    private static string? DefaultClassOf(ITypeLib library, Guid interfaceIdentifier)
    {
        for (var index = 0; index < library.GetTypeInfoCount(); index++)
        {
            library.GetTypeInfoType(index, out var kind);
            if (kind is not TYPEKIND.TKIND_COCLASS)
            {
                continue;
            }

            library.GetTypeInfo(index, out var candidate);
            var (_, _, implemented) = AttributesOf(candidate);
            for (var implementation = 0; implementation < implemented; implementation++)
            {
                candidate.GetImplTypeFlags(implementation, out var flags);
                if (!flags.HasFlag(IMPLTYPEFLAGS.IMPLTYPEFLAG_FDEFAULT) || flags.HasFlag(IMPLTYPEFLAGS.IMPLTYPEFLAG_FSOURCE))
                {
                    continue;
                }

                candidate.GetRefTypeOfImplType(implementation, out var reference);
                candidate.GetRefTypeInfo(reference, out var implementedInterface);
                if (AttributesOf(implementedInterface).Identifier == interfaceIdentifier)
                {
                    candidate.GetDocumentation(-1, out var className, out _, out _, out _);
                    return className;
                }
            }
        }

        return null;
    }

    private static (TYPEKIND Kind, Guid Identifier, int Implemented) AttributesOf(ITypeInfo typeInfo)
    {
        typeInfo.GetTypeAttr(out var pointer);
        try
        {
            var attributes = Marshal.PtrToStructure<TYPEATTR>(pointer);
            return (attributes.typekind, attributes.guid, attributes.cImplTypes);
        }
        finally
        {
            typeInfo.ReleaseTypeAttr(pointer);
        }
    }

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

    private readonly Dictionary<object, Connection> _connections = new(ReferenceEqualityComparer.Instance);

    private sealed record Connection(IConnectionPoint Point, int Cookie, object Receiver, Action<string, object?[]> Raise);

    /// <inheritdoc/>
    public void Advise(object source, IAutomationEventSink sink) => OnApartment<object?>(() =>
    {
        // an object is listened to through the connection point of the interface its events are declared by; one that has none raises nothing to listen to.
        if (_connections.ContainsKey(source) || source is not IConnectionPointContainer container || ConnectionOf(source, container) is not var (point, events, declaring))
        {
            return null;
        }

        void Raise(string name, object?[] arguments, int? dispatchId)
            => sink.OnEvent(new AutomationEvent(name, arguments, _apartment.IsInCall, _apartment.Serve) { DispatchId = dispatchId });
        var receiver = new EventReceiver(declaring, events, (name, dispatchId, arguments) => Raise(name, arguments, dispatchId));
        point.Advise(receiver, out var cookie);
        _connections[source] = new Connection(point, cookie, receiver, (name, arguments) => Raise(name, arguments, null));
        return null;
    });

    // an event the object did not raise through its connection point, but something else of the apartment raises for it - the form host, for the window a form is
    // in: it goes to whatever listens to the object's events. Called on the apartment.
    private bool RaiseFor(object source, string name, object?[] arguments)
    {
        if (!_connections.TryGetValue(source, out var connection))
        {
            return false;
        }

        connection.Raise(name, arguments);
        return true;
    }

    // The connection point to listen to, and the events it raises by the identifier they are raised with. An object that says which interface is its default source
    // (IProvideClassInfo2) is asked; one that does not - Excel's do not - has the connection points it has, each of an interface its own type library describes.
    private static (IConnectionPoint Point, Dictionary<int, string> Events, Guid Interface)? ConnectionOf(object source, IConnectionPointContainer container)
    {
        if (source is IProvideClassInfo2 info && info.GetGUID(DefaultSourceInterface, out var identifier) == 0)
        {
            container.FindConnectionPoint(ref identifier, out var named);
            return (named, EventsOf(info, identifier), identifier);
        }

        if (source is not IDispatchTypeInformation dispatch)
        {
            return null;
        }

        ITypeLib library;
        try
        {
            dispatch.GetTypeInfo(0, 0, out var typeInfo);
            typeInfo.GetContainingTypeLib(out library, out _);
        }
        catch (COMException)
        {
            return null;
        }

        container.EnumConnectionPoints(out var points);
        var found = new IConnectionPoint[1];
        while (points.Next(1, found, IntPtr.Zero) == 0)
        {
            found[0].GetConnectionInterface(out var interfaceId);
            if (DeclaringOf(library, interfaceId) is { } declaring)
            {
                return (found[0], EventsOfInterface(declaring), interfaceId);
            }
        }

        return null;
    }

    // the interface of a connection point, if the object's own library describes it. A Microsoft Forms control's first connection point is IDispatch itself, and its
    // library answers TYPE_E_ELEMENTNOTFOUND for it - as it does for IPropertyNotifySink: neither is the control's events interface, which comes after them.
    private static ITypeInfo? DeclaringOf(ITypeLib library, Guid interfaceId)
    {
        try
        {
            library.GetTypeInfoOfGuid(ref interfaceId, out var declaring);
            return declaring;
        }
        catch (COMException)
        {
            return null;
        }
    }

    /// <inheritdoc/>
    public void Unadvise(object source) => OnApartment<object?>(() =>
    {
        StopListening(source);
        return null;
    });

    private void StopListening(object source)
    {
        if (_connections.Remove(source, out var connection))
        {
            try
            {
                connection.Point.Unadvise(connection.Cookie);
            }
            catch (COMException)
            {
                // a server that is gone has nobody left to tell.
            }

            _ = Marshal.ReleaseComObject(connection.Point);
        }
    }

    // GUIDKIND_DEFAULT_SOURCE_DISP_IID
    private const int DefaultSourceInterface = 1;

    // the events of a default source interface by the dispatch identifier the server raises them with: the class names the interface among those it implements, and the
    // interface names its functions.
    private static Dictionary<int, string> EventsOf(IProvideClassInfo2 info, Guid identifier)
    {
        var events = new Dictionary<int, string>();
        if (info.GetClassInfo(out var coclass) != 0)
        {
            return events;
        }

        coclass.GetTypeAttr(out var coclassAttributes);
        var implemented = Marshal.PtrToStructure<TYPEATTR>(coclassAttributes).cImplTypes;
        coclass.ReleaseTypeAttr(coclassAttributes);

        for (var index = 0; index < implemented; index++)
        {
            coclass.GetRefTypeOfImplType(index, out var reference);
            coclass.GetRefTypeInfo(reference, out var candidate);
            candidate.GetTypeAttr(out var attributes);
            var matches = Marshal.PtrToStructure<TYPEATTR>(attributes).guid == identifier;
            candidate.ReleaseTypeAttr(attributes);
            if (matches)
            {
                events = EventsOfInterface(candidate);
            }
        }

        return events;
    }

    // the functions of a dispinterface by their dispatch identifiers: what a server raises an event with, and the name of the event.
    private static Dictionary<int, string> EventsOfInterface(ITypeInfo declaring)
    {
        var events = new Dictionary<int, string>();
        declaring.GetTypeAttr(out var attributes);
        var functions = Marshal.PtrToStructure<TYPEATTR>(attributes).cFuncs;
        declaring.ReleaseTypeAttr(attributes);

        for (var function = 0; function < functions; function++)
        {
            declaring.GetFuncDesc(function, out var description);
            var memberId = Marshal.PtrToStructure<FUNCDESC>(description).memid;
            declaring.ReleaseFuncDesc(description);

            var names = new string[1];
            declaring.GetNames(memberId, names, 1, out _);
            events[memberId] = names[0];
        }

        return events;
    }

    // IProvideClassInfo2: what an object that raises events says about them.
    [ComImport]
    [Guid("A6BC3AC0-DBAA-11CE-9DE3-00AA004BB851")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IProvideClassInfo2
    {
        [PreserveSig]
        int GetClassInfo(out ITypeInfo typeInfo);

        [PreserveSig]
        int GetGUID(int kind, out Guid identifier);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DispatchParameters
    {
        public IntPtr Arguments;
        public IntPtr NamedArgumentIds;
        public int ArgumentCount;
        public int NamedArgumentCount;
    }

    // IDispatch, as the sink of a server's dispinterface has to be: the server calls Invoke with the identifier of the event and its arguments, last first.
    [ComImport]
    [Guid("00020400-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IEventDispatch
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

    [ComVisible(true)]
    [ClassInterface(ClassInterfaceType.None)]
    private sealed class EventReceiver(Guid declaring, IReadOnlyDictionary<int, string> events, Action<string, int, object?[]> raise) : IEventDispatch, ICustomQueryInterface
    {
        // A server asks a sink for the interface its events are declared by, not for IDispatch, and refuses one that does not have it. That interface is a dispinterface, which
        // is nothing but IDispatch with the names a type library gives it: the sink is IDispatch, and says so for that identifier.
        private static readonly Guid Dispatch = new("00020400-0000-0000-C000-000000000046");

        public CustomQueryInterfaceResult GetInterface(ref Guid iid, out IntPtr interfacePointer)
        {
            // the runtime offers no IDispatch of its own for a managed object, so the one the class implements is the only one there is.
            if (iid == declaring || iid == Dispatch)
            {
                interfacePointer = Marshal.GetComInterfaceForObject(this, typeof(IEventDispatch), CustomQueryInterfaceMode.Ignore);
                return CustomQueryInterfaceResult.Handled;
            }

            interfacePointer = IntPtr.Zero;
            return CustomQueryInterfaceResult.NotHandled;
        }

        private const int NotImplemented = unchecked((int)0x80004001);
        private const int MemberNotFound = unchecked((int)0x80020003);
        private const ushort ByReference = 0x4000;

        public int GetTypeInfoCount(out int count)
        {
            count = 0;
            return 0;
        }

        public int GetTypeInfo(int index, int locale, out IntPtr typeInfo)
        {
            typeInfo = IntPtr.Zero;
            return NotImplemented;
        }

        // The arrays are the caller's, of the length it says: the marshaller cannot be told that of a parameter it reads, so they are read here.
        public int GetIDsOfNames(ref Guid identifier, IntPtr names, int count, int locale, IntPtr identifiers)
        {
            const int Unknown = -1;
            var all = true;
            for (var index = 0; index < count; index++)
            {
                var name = Marshal.PtrToStringUni(Marshal.ReadIntPtr(names, index * IntPtr.Size));
                var found = events.Where(known => string.Equals(known.Value, name, StringComparison.OrdinalIgnoreCase)).Select(known => (int?)known.Key).FirstOrDefault();
                Marshal.WriteInt32(identifiers, index * sizeof(int), found ?? Unknown);
                all &= found is not null;
            }

            return all ? 0 : unchecked((int)0x80020006); // DISP_E_UNKNOWNNAME
        }

        public int Invoke(int member, ref Guid identifier, int locale, ushort flags, ref DispatchParameters parameters, IntPtr result, IntPtr exception, IntPtr argumentError)
        {
            if (!events.TryGetValue(member, out var name))
            {
                return MemberNotFound;
            }

            // the arguments are in the order they are written in, last first.
            var size = IntPtr.Size == 8 ? 24 : 16;
            var count = parameters.ArgumentCount;
            var first = parameters.Arguments;
            var locations = Enumerable.Range(0, count).Select(index => first + ((count - 1 - index) * size)).ToArray();
            var arguments = locations.Select(Marshal.GetObjectForNativeVariant).ToArray();
            var sent = (object?[])arguments.Clone();

            var outer = _handlingEvent;
            _handlingEvent = true;
            try
            {
                raise(name, member, arguments);
            }
            catch (Exception failure)
            {
                // an exception must not cross into the server that called: it is the server's call that failed.
                LastFailure = failure;
                return unchecked((int)0x80004005);
            }
            finally
            {
                _handlingEvent = outer;
            }

            // what the handlers left in an argument that was passed by reference is what the server reads when this returns.
            for (var index = 0; index < count; index++)
            {
                if (!Equals(sent[index], arguments[index]))
                {
                    WriteBack(locations[index], arguments[index], ByReference);
                }
            }

            return 0;
        }

        public static Exception? LastFailure { get; private set; }

        internal static void WriteBack(IntPtr location, object? value, ushort byReference)
        {
            var kind = (ushort)Marshal.ReadInt16(location);
            if ((kind & byReference) == 0)
            {
                return;
            }

            var target = Marshal.ReadIntPtr(location, 8);
            switch (kind & ~byReference)
            {
                case 11: // VT_BOOL
                    Marshal.WriteInt16(target, (short)(Convert.ToBoolean(value, CultureInfo.InvariantCulture) ? -1 : 0));
                    break;
                case 2: // VT_I2
                    Marshal.WriteInt16(target, Convert.ToInt16(value, CultureInfo.InvariantCulture));
                    break;
                case 3: // VT_I4
                    Marshal.WriteInt32(target, Convert.ToInt32(value, CultureInfo.InvariantCulture));
                    break;
                case 20: // VT_I8
                    Marshal.WriteInt64(target, Convert.ToInt64(value, CultureInfo.InvariantCulture));
                    break;
                case 5: // VT_R8
                    Marshal.WriteInt64(target, BitConverter.DoubleToInt64Bits(Convert.ToDouble(value, CultureInfo.InvariantCulture)));
                    break;
                case 12: // VT_VARIANT
                    Marshal.GetNativeVariantForObject(value, target);
                    break;
            }
        }
    }

    // the stand-ins of the session's objects, by identity: one object is one stand-in for as long as anything holds it, which is what the server compares
    // (`d.Exists(o)`, `d(k) Is o`). Read and written on the apartment.
    private readonly Dictionary<string, WeakReference<SessionObjectStandIn>> _standIns = [];

    // what a server is given for an object of the session: its stand-in, which counts the crossing. Anything else is given as it is.
    private object? ToServer(object? value)
    {
        switch (value)
        {
            case SessionObjectReference reference:
                if (!_standIns.TryGetValue(reference.SessionObject, out var known) || !known.TryGetTarget(out var standIn))
                {
                    standIn = new SessionObjectStandIn(reference.SessionObject, reference.Host, _apartment, ToServer);
                    _standIns[reference.SessionObject] = new WeakReference<SessionObjectStandIn>(standIn);
                }

                standIn.Crossed();
                return standIn;

            case object?[] elements:
                for (var index = 0; index < elements.Length; index++)
                {
                    elements[index] = ToServer(elements[index]);
                }

                return elements;

            default:
                return value;
        }
    }

    /// <summary>
    /// What a server holds for an object of the session: an <c>IDispatch</c> whose calls are the object's members, called in the session
    /// (<see cref="ISessionObjectHost.Invoke"/>), and which is the session's object again when it comes back (<see cref="ISessionObjectStandIn"/>).
    /// </summary>
    /// <remarks>
    /// A name the server asks for is given an identifier of the stand-in's own, and called by that name; an identifier it calls without asking - the default
    /// member's 0, an enumerator's -4, a connection point's - is the session's to read. When nothing holds it any more, the session is told how many times the
    /// object was handed over to it, which is when the session lets go of the object (<see cref="ISessionObjectHost.Release"/>).
    /// </remarks>
    [ComVisible(true)]
    [ClassInterface(ClassInterfaceType.None)]
    private sealed class SessionObjectStandIn(string sessionObject, ISessionObjectHost host, ComApartment apartment, Func<object?, object?> toServer)
        : IEventDispatch, ICustomQueryInterface, ISessionObjectStandIn
    {
        private static readonly Guid Dispatch = new("00020400-0000-0000-C000-000000000046");
        private const ushort ByReference = 0x4000;
        private const ushort PropertyPut = 4;
        private const ushort PropertyPutReference = 8;
        private const ushort Method = 1;
        private const ushort PropertyGet = 2;

        // an identifier the stand-in gives a name of its own: far above the members' own (0x60030000 and up), so that one called without asking is not one of these.
        private const int FirstNamed = 0x7F000000;

        private readonly Dictionary<string, int> _identifiers = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<int, string> _names = [];
        private int _crossings;

        public string SessionObject { get; } = sessionObject;

        public void Crossed() => Interlocked.Increment(ref _crossings);

        // nothing holds it any more: the session lets go of the object once every crossing is accounted for.
        ~SessionObjectStandIn()
        {
            try
            {
                host.Release(SessionObject, Volatile.Read(ref _crossings));
            }
            catch (Exception exception) when (exception is AutomationException or InvalidOperationException or ObjectDisposedException or IOException)
            {
                // a session that is gone has nothing left to let go of.
            }
        }

        public CustomQueryInterfaceResult GetInterface(ref Guid iid, out IntPtr interfacePointer)
        {
            // the runtime offers no IDispatch of its own for a managed object, so the one the class implements is the only one there is.
            if (iid == Dispatch)
            {
                interfacePointer = Marshal.GetComInterfaceForObject(this, typeof(IEventDispatch), CustomQueryInterfaceMode.Ignore);
                return CustomQueryInterfaceResult.Handled;
            }

            interfacePointer = IntPtr.Zero;
            return CustomQueryInterfaceResult.NotHandled;
        }

        public int GetTypeInfoCount(out int count)
        {
            count = 0;
            return 0;
        }

        public int GetTypeInfo(int index, int locale, out IntPtr typeInfo)
        {
            typeInfo = IntPtr.Zero;
            return unchecked((int)0x80004001); // E_NOTIMPL
        }

        // every name is given an identifier: whether the object has the member is the session's to say when it is called.
        public int GetIDsOfNames(ref Guid identifier, IntPtr names, int count, int locale, IntPtr identifiers)
        {
            for (var index = 0; index < count; index++)
            {
                var name = Marshal.PtrToStringUni(Marshal.ReadIntPtr(names, index * IntPtr.Size)) ?? string.Empty;
                if (!_identifiers.TryGetValue(name, out var assigned))
                {
                    assigned = FirstNamed + _identifiers.Count;
                    _identifiers[name] = assigned;
                    _names[assigned] = name;
                }

                Marshal.WriteInt32(identifiers, index * sizeof(int), assigned);
            }

            return 0;
        }

        public int Invoke(int member, ref Guid identifier, int locale, ushort flags, ref DispatchParameters parameters, IntPtr result, IntPtr exception, IntPtr argumentError)
        {
            // the arguments are in the order they are written in, last first: an assignment's value, which the caller names, is the last of them.
            var size = IntPtr.Size == 8 ? 24 : 16;
            var count = parameters.ArgumentCount;
            var first = parameters.Arguments;
            var locations = Enumerable.Range(0, count).Select(index => first + ((count - 1 - index) * size)).ToArray();
            var arguments = locations.Select(Marshal.GetObjectForNativeVariant).ToArray();
            var sent = (object?[])arguments.Clone();

            var invocation = (flags & PropertyPutReference) != 0 ? AutomationInvocation.Set
                : (flags & PropertyPut) != 0 ? AutomationInvocation.Let
                : (flags & Method) != 0 && (flags & PropertyGet) == 0 ? AutomationInvocation.Method
                : AutomationInvocation.Get;

            object? returned;
            var outer = _handlingEvent;
            _handlingEvent = true;
            try
            {
                returned = host.Invoke(new SessionObjectCall(
                    SessionObject, _names.GetValueOrDefault(member), member, invocation, arguments, apartment.IsInCall, apartment.Serve));
            }
            catch (AutomationException failure)
            {
                return failure.HResult;
            }
            catch (Exception failure) when (failure is InvalidOperationException or ObjectDisposedException or IOException)
            {
                // a session that is gone answers nothing: the server's call fails.
                return unchecked((int)0x80004005);
            }
            finally
            {
                _handlingEvent = outer;
            }

            for (var index = 0; index < count; index++)
            {
                if (!Equals(sent[index], arguments[index]))
                {
                    EventReceiver.WriteBack(locations[index], toServer(arguments[index]), ByReference);
                }
            }

            if (result != IntPtr.Zero)
            {
                Marshal.GetNativeVariantForObject(toServer(returned), result);
            }

            return 0;
        }
    }

    /// <inheritdoc/>
    public void Release(object handle) => OnApartment<object?>(() =>
    {
        StopListening(handle);
        _apartment.OnReleasing(handle);
        if (Marshal.IsComObject(handle))
        {
            _ = Marshal.FinalReleaseComObject(handle);
        }

        return null;
    });

    /// <summary>
    /// Ends the apartment, when it is the server's own. Every object that was made on it has been let go of by whatever held it.
    /// </summary>
    public void Dispose()
    {
        if (_ownsApartment)
        {
            _apartment.Dispose();
        }
    }

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
        // the binder's DISP_E_BADPARAMCOUNT: `v = d` of a Dictionary, whose default member takes the key.
        catch (TargetParameterCountException miscounted)
        {
            throw new AutomationException(BadParameterCount, miscounted.Message);
        }
        catch (TargetInvocationException exception) when (exception.InnerException is TargetParameterCountException miscounted)
        {
            throw new AutomationException(BadParameterCount, miscounted.Message);
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

    // runs it on the apartment and waits: the pipeline is synchronous, and the object that a call is made on is on that thread.
    // A server calls the sink of its events on a thread of its own, not on the apartment, which is busy - it is inside the call that raised the event, waiting for the handlers.
    // What the handlers call is therefore called from where they run: COM carries it to the apartment, which handles it while it waits for the server, and the
    // server's objects can be used from a thread that handles an event as they can from the apartment's. Queueing it for the apartment instead would wait on a thread
    // that waits for this one.
    [ThreadStatic]
    private static bool _handlingEvent;

    private T? OnApartment<T>(Func<T?> call)
        => _handlingEvent ? _apartment.Counted(call) : _apartment.Invoke(() => _apartment.Counted(call));

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

    [ComImport]
    [Guid("B196B283-BAB4-101A-B69C-00AA00341D07")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IProvideClassInformation
    {
        void GetClassInfo(out ITypeInfo typeInfo);
    }
}
