using RDCore.External.Automation;
using RDCore.External.Native;
using RDCore.External.Protocol;
using System.Collections.Concurrent;
using System.Globalization;

namespace RDCore.External.Client;

/// <summary>
/// The automation servers of the machine, reached through the external host: every call is made in that process, and only values and handles cross.
/// </summary>
/// <remarks>
/// <para>
/// A server's object is a <see cref="RemoteObject"/> here: the handle the external host gave it, in the incarnation that holds it. The same object is the same
/// <see cref="RemoteObject"/> for as long as it is held, which is what <c>Is</c> asks.
/// </para>
/// <para>
/// When the external host stops, its objects are gone with it. A call on one fails as a call on the object of an out-of-process server that stopped does in MS-VBA - the
/// server is not available (<c>RPC_S_SERVER_UNAVAILABLE</c>, error 462) - and so does the call that was being made when it stopped (<c>RPC_S_CALL_FAILED</c>).
/// </para>
/// <para>
/// A handler of an event runs while the external host waits for it, and the calls it makes say which event they are made for
/// (<see cref="AutomationRequest.HandlingEvent"/>): the thread that runs the handler is the one that knows.
/// </para>
/// </remarks>
public sealed class RemoteAutomationServer : IAutomationServer
{
    private const int CallFailed = unchecked((int)0x800706BE);
    private const int ServerUnavailable = unchecked((int)0x800706BA);
    private const int ServerCannotStart = unchecked((int)0x80080005);
    private const int Unspecified = unchecked((int)0x80004005);

    private readonly ExternalHost _host;
    private readonly ConcurrentDictionary<(int Incarnation, long Handle), RemoteObject> _objects = new();
    private readonly ConcurrentDictionary<(int Incarnation, long Handle), IAutomationEventSink> _sinks = new();

    [ThreadStatic]
    private static long _handlingEvent;

    /// <summary>
    /// Reaches the machine's automation servers through <paramref name="host"/>, and handles the events they raise.
    /// </summary>
    /// <param name="host">The external host.</param>
    public RemoteAutomationServer(ExternalHost host)
    {
        _host = host;
        _host.AutomationEvent = OnEvent;
    }

    // an external host that cannot be started is not a machine with no servers: the program that asks for one is told the server could not be started.
    /// <inheritdoc/>
    public bool IsAvailable => !_host.TryConnect(out var incarnation) || incarnation!.IsAutomationAvailable;

    // the external host is started on a thread of its own; a call that comes before it is up waits for it as it would have started it.
    /// <inheritdoc/>
    public void Prepare() => _ = Task.Run(() => _host.TryConnect(out _));

    /// <inheritdoc/>
    public object CreateObject(string progId)
    {
        var incarnation = Connect();
        var created = Send<AutomationCreateParams, AutomationObjectResult>(incarnation, ExternalProtocol.AutomationCreate, new AutomationCreateParams { ProgId = progId, HandlingEvent = _handlingEvent });
        return ObjectOf(incarnation, created.Handle);
    }

    /// <inheritdoc/>
    public object? Invoke(object target, string member, AutomationInvocation invocation, object?[] arguments, bool[] byReference, CultureInfo culture)
    {
        var remote = Live(target);
        var incarnation = remote.Incarnation;
        var invoked = Send<AutomationInvokeParams, AutomationInvokeResult>(incarnation, ExternalProtocol.AutomationInvoke, new AutomationInvokeParams
        {
            Target = remote.Handle,
            Member = member,
            Invocation = invocation,
            Arguments = [.. arguments.Select(argument => ExternalValues.ToWire(argument, value => HandleIn(incarnation, value)))],
            ByReference = byReference,
            Culture = culture.Name,
            HandlingEvent = _handlingEvent,
        });

        for (var index = 0; index < arguments.Length && index < byReference.Length && index < invoked.Arguments.Length; index++)
        {
            if (byReference[index])
            {
                arguments[index] = ExternalValues.FromWire(invoked.Arguments[index], handle => ObjectOf(incarnation, handle));
            }
        }

        return ExternalValues.FromWire(invoked.Returned, handle => ObjectOf(incarnation, handle));
    }

    /// <inheritdoc/>
    public string? ClassNameOf(object target)
    {
        if (target is not RemoteObject { Incarnation.IsLost: false } remote)
        {
            return null;
        }

        try
        {
            return Send<AutomationClassNameParams, AutomationClassNameResult>(
                remote.Incarnation, ExternalProtocol.AutomationClassName, new AutomationClassNameParams { Target = remote.Handle, HandlingEvent = _handlingEvent }).Name;
        }
        catch (AutomationException)
        {
            return null;
        }
    }

    /// <inheritdoc/>
    public bool MoveNext(object enumerator, out object? current)
    {
        var remote = Live(enumerator);
        var moved = Send<AutomationMoveNextParams, AutomationMoveNextResult>(
            remote.Incarnation, ExternalProtocol.AutomationMoveNext, new AutomationMoveNextParams { Enumerator = remote.Handle, HandlingEvent = _handlingEvent });
        current = ExternalValues.FromWire(moved.Current, handle => ObjectOf(remote.Incarnation, handle));
        return moved.Moved;
    }

    /// <inheritdoc/>
    public void Reset(object enumerator)
    {
        var remote = Live(enumerator);
        _ = Send<AutomationResetParams, ExternalDoneResult>(remote.Incarnation, ExternalProtocol.AutomationReset, new AutomationResetParams { Enumerator = remote.Handle, HandlingEvent = _handlingEvent });
    }

    /// <inheritdoc/>
    public void Advise(object source, IAutomationEventSink sink)
    {
        var remote = Live(source);
        _sinks[(remote.Incarnation.Number, remote.Handle)] = sink;
        _ = Send<AutomationAdviseParams, ExternalDoneResult>(remote.Incarnation, ExternalProtocol.AutomationAdvise, new AutomationAdviseParams { Source = remote.Handle, HandlingEvent = _handlingEvent });
    }

    /// <inheritdoc/>
    public void Unadvise(object source)
    {
        if (source is not RemoteObject remote || !_sinks.TryRemove((remote.Incarnation.Number, remote.Handle), out _) || remote.Incarnation.IsLost)
        {
            return;
        }

        _ = Send<AutomationUnadviseParams, ExternalDoneResult>(remote.Incarnation, ExternalProtocol.AutomationUnadvise, new AutomationUnadviseParams { Source = remote.Handle, HandlingEvent = _handlingEvent });
    }

    // an object whose external host stopped is gone already: there is nothing left to let go of.
    /// <inheritdoc/>
    public void Release(object handle)
    {
        if (handle is not RemoteObject remote)
        {
            return;
        }

        var key = (remote.Incarnation.Number, remote.Handle);
        _ = _objects.TryRemove(key, out _);
        _ = _sinks.TryRemove(key, out _);
        if (remote.Incarnation.IsLost)
        {
            return;
        }

        try
        {
            _ = Send<AutomationReleaseParams, ExternalDoneResult>(remote.Incarnation, ExternalProtocol.AutomationRelease, new AutomationReleaseParams { Handle = remote.Handle, HandlingEvent = _handlingEvent });
        }
        catch (AutomationException)
        {
            // an object that cannot be let go of is let go of by the process that holds it, when it stops.
        }
    }

    // an event the external host's servers raised: its handlers run on this thread, and the calls they make say they are made for it.
    private AutomationEventResult OnEvent(Incarnation incarnation, AutomationEventParams raised)
    {
        if (!_sinks.TryGetValue((incarnation.Number, raised.Source), out var sink))
        {
            return new AutomationEventResult { Arguments = raised.Arguments };
        }

        var outer = _handlingEvent;
        _handlingEvent = raised.Event;
        try
        {
            var arguments = raised.Arguments.Select(argument => ExternalValues.FromWire(argument, handle => ObjectOf(incarnation, handle))).ToArray();
            sink.OnEvent(new AutomationEvent(raised.Name, arguments, raised.IsSynchronous, Thread.Sleep));
            return new AutomationEventResult { Arguments = [.. arguments.Select(argument => ExternalValues.ToWire(argument, value => HandleIn(incarnation, value)))] };
        }
        catch (AutomationException failure)
        {
            return new AutomationEventResult { Arguments = raised.Arguments, Failure = ExternalFailure.Of(failure) };
        }
        finally
        {
            _handlingEvent = outer;
        }
    }

    private Incarnation Connect()
    {
        try
        {
            return _host.Connect();
        }
        catch (ExternalHostUnavailableException unavailable)
        {
            throw new AutomationException(ServerCannotStart, unavailable.Message);
        }
    }

    private RemoteObject ObjectOf(Incarnation incarnation, long handle)
        => _objects.GetOrAdd((incarnation.Number, handle), key => new RemoteObject(incarnation, key.Handle));

    // what crosses for an object: its handle, in the incarnation the call is made to - an object of another is not there to be given.
    private static long HandleIn(Incarnation incarnation, object value)
        => value is RemoteObject remote && remote.Incarnation == incarnation
            ? remote.Handle
            : throw new AutomationException(ServerUnavailable, ExternalMessages.ObjectHostStopped);

    private static RemoteObject Live(object value)
        => value is RemoteObject { Incarnation.IsLost: false } remote
            ? remote
            : throw new AutomationException(ServerUnavailable, ExternalMessages.ObjectHostStopped);

    private static TResult Send<TParams, TResult>(Incarnation incarnation, string method, TParams request) where TResult : ExternalResult
    {
        TResult result;
        try
        {
            result = incarnation.Send<TParams, TResult>(method, request);
        }
        catch (ExternalHostStoppedException lost)
        {
            throw new AutomationException(CallFailed, lost.Message);
        }
        catch (Exception exception) when (exception is not AutomationException)
        {
            throw new AutomationException(Unspecified, string.Format(CultureInfo.CurrentCulture, ExternalMessages.HostFailed, exception.Message));
        }

        return result.Failure is { } failure ? throw failure.ToException() : result;
    }
}

/// <summary>
/// An object that the external host holds: its handle, in the incarnation that holds it.
/// </summary>
/// <param name="incarnation">The process that holds it.</param>
/// <param name="handle">The handle that process gave it.</param>
internal sealed class RemoteObject(Incarnation incarnation, long handle)
{
    /// <summary>
    /// The process that holds it.
    /// </summary>
    public Incarnation Incarnation { get; } = incarnation;

    /// <summary>
    /// The handle that process gave it.
    /// </summary>
    public long Handle { get; } = handle;
}
