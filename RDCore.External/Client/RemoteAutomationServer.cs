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
/// <para>
/// It is the form host of the machine too (<see cref="Forms.IFormHost"/>): a form is an object of the external host's automation servers, whose members are called as
/// any other's, and what the host adds - its window - is asked of the same process, for the same handle.
/// </para>
/// </remarks>
public sealed class RemoteAutomationServer : IAutomationServer, Forms.IFormHost
{
    private const int CallFailed = unchecked((int)0x800706BE);
    private const int ServerUnavailable = unchecked((int)0x800706BA);
    private const int ServerCannotStart = unchecked((int)0x80080005);
    private const int Unspecified = unchecked((int)0x80004005);

    private readonly ExternalHost _host;
    private readonly ConcurrentDictionary<(int Incarnation, long Handle), RemoteObject> _objects = new();
    private readonly ConcurrentDictionary<(int Incarnation, long Handle), IAutomationEventSink> _sinks = new();
    private readonly object _holdGate = new();

    // whether a program runs, as it was last said (Hold), and the incarnation that was told it.
    private bool _running;
    private Incarnation? _told;

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
        _host.SessionInvoke = OnSessionInvoke;
        _host.SessionRelease = released =>
        {
            if (_sessionHosts.TryGetValue(released.SessionObject, out var owner))
            {
                owner.Release(released.SessionObject, released.Crossings);
            }
        };
    }

    // what answers for each object of a session this end handed over, by its identity: the sessions that share the external host each answer for their own.
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, ISessionObjectHost> _sessionHosts = new();

    // the objects of a session among the values that cross: what answers for each is remembered, for the calls and the releases that come back.
    private void Remember(object? value)
    {
        switch (value)
        {
            case SessionObjectReference reference:
                _sessionHosts[reference.SessionObject] = reference.Host;
                break;
            case Array elements:
                foreach (var element in elements)
                {
                    Remember(element);
                }

                break;
        }
    }

    // a call the external host's server made to an object of the session, through its stand-in: answered here as an event is, with the calls the session makes
    // meanwhile made on the external host's waiting thread.
    private SessionInvokeResult OnSessionInvoke(Incarnation incarnation, SessionInvokeParams call)
    {
        if (!_sessionHosts.TryGetValue(call.SessionObject, out var owner))
        {
            return new SessionInvokeResult { Failure = new ExternalFailure { HResult = unchecked((int)0x800A01A8), Message = "The object of the session is gone." } };
        }

        var outer = _handlingEvent;
        _handlingEvent = call.Event;
        try
        {
            var arguments = call.Arguments.Select(argument => ExternalValues.FromWire(argument, handle => ObjectOf(incarnation, handle))).ToArray();
            var returned = owner.Invoke(new SessionObjectCall(call.SessionObject, call.Name, call.DispatchId, call.Invocation, arguments, call.IsSynchronous, Thread.Sleep));
            Remember(returned);
            foreach (var argument in arguments)
            {
                Remember(argument);
            }

            return new SessionInvokeResult
            {
                Returned = ExternalValues.ToWire(returned, value => HandleIn(incarnation, value)),
                Arguments = [.. arguments.Select(argument => ExternalValues.ToWire(argument, value => HandleIn(incarnation, value)))],
            };
        }
        catch (AutomationException failure)
        {
            return new SessionInvokeResult { Failure = ExternalFailure.Of(failure) };
        }
        finally
        {
            _handlingEvent = outer;
        }
    }

    // an external host that cannot be started is not a machine with no servers: the program that asks for one is told the server could not be started.
    /// <inheritdoc/>
    public bool IsAvailable => !_host.TryConnect(out var incarnation) || incarnation!.IsAutomationAvailable;

    // as for the servers: an external host that cannot be started is not a machine with no forms, and the program that shows one is told it could not be started.
    bool Forms.IFormHost.IsAvailable => !_host.TryConnect(out var incarnation) || incarnation!.IsFormHostAvailable;

    /// <inheritdoc/>
    public object Create(Forms.FormDefinition form)
    {
        // a host the form starts - the session got it ready in the background, and it was not up yet when the program ran - is told first that a program runs,
        // so that what Microsoft Forms posts as it creates the form waits as it would in a host that was up.
        var incarnation = Connect();
        TellHold(incarnation, onlyIfUntold: true);
        var created = Send<FormCreateParams, AutomationObjectResult>(incarnation, ExternalProtocol.FormCreate, FormCreateParams.Of(form, _handlingEvent));
        return ObjectOf(incarnation, created.Handle);
    }

    /// <inheritdoc/>
    public void Show(object form, bool modal)
    {
        var remote = Live(form);
        _ = Send<FormShowParams, ExternalDoneResult>(remote.Incarnation, ExternalProtocol.FormShow, new FormShowParams { Target = remote.Handle, Modal = modal, HandlingEvent = _handlingEvent });
    }

    /// <inheritdoc/>
    public void Hide(object form)
    {
        var remote = Live(form);
        _ = Send<FormParams, ExternalDoneResult>(remote.Incarnation, ExternalProtocol.FormHide, new FormParams { Target = remote.Handle, HandlingEvent = _handlingEvent });
    }

    /// <inheritdoc/>
    public bool IsShown(object form)
    {
        var remote = Live(form);
        return Send<FormParams, FormIsShownResult>(remote.Incarnation, ExternalProtocol.FormIsShown, new FormParams { Target = remote.Handle, HandlingEvent = _handlingEvent }).IsShown;
    }

    /// <inheritdoc/>
    public string GetCaption(object form)
    {
        var remote = Live(form);
        return Send<FormCaptionParams, FormCaptionResult>(remote.Incarnation, ExternalProtocol.FormCaption, new FormCaptionParams { Target = remote.Handle, HandlingEvent = _handlingEvent }).Caption;
    }

    /// <inheritdoc/>
    public void SetCaption(object form, string caption)
    {
        var remote = Live(form);
        _ = Send<FormCaptionParams, FormCaptionResult>(
            remote.Incarnation, ExternalProtocol.FormCaption, new FormCaptionParams { Target = remote.Handle, Caption = caption, HandlingEvent = _handlingEvent });
    }

    /// <inheritdoc/>
    public void Leave(object form)
    {
        var remote = Live(form);
        _ = Send<FormParams, ExternalDoneResult>(remote.Incarnation, ExternalProtocol.FormLeave, new FormParams { Target = remote.Handle, HandlingEvent = _handlingEvent });
    }

    // an external host that is not running hosts no form to hold the messages of: it is not started for that, and is told when a form starts it (Create).
    /// <inheritdoc/>
    public void Hold(bool running)
    {
        lock (_holdGate)
        {
            _running = running;
        }

        if (_host.Running is { } incarnation)
        {
            TellHold(incarnation, onlyIfUntold: false);
        }
    }

    // says to an incarnation whether a program runs; one that was told already is not told again when only the untold are.
    private void TellHold(Incarnation incarnation, bool onlyIfUntold)
    {
        bool running;
        lock (_holdGate)
        {
            if (onlyIfUntold && ReferenceEquals(_told, incarnation))
            {
                return;
            }

            running = _running;
            _told = incarnation;
        }

        _ = Send<FormHoldParams, ExternalDoneResult>(incarnation, ExternalProtocol.FormHold, new FormHoldParams { Running = running, HandlingEvent = _handlingEvent });
    }

    /// <inheritdoc/>
    public void DoEvents()
    {
        if (_host.Running is { } incarnation)
        {
            _ = Send<FormDoEventsParams, ExternalDoneResult>(incarnation, ExternalProtocol.FormDoEvents, new FormDoEventsParams { HandlingEvent = _handlingEvent });
        }
    }

    // a form is destroyed when it is let go of, as any of the external host's objects is released: there is nothing to ask of it before.
    /// <inheritdoc/>
    public void Destroy(object form) => Release(form);

    // the external host is started on a thread of its own; a call that comes before it is up waits for it as it would have started it.
    /// <inheritdoc/>
    public void Prepare() => _ = Task.Run(() => _host.TryConnect(out _));

    /// <inheritdoc/>
    public object GetObject(SDK.Runtime.Abstract.Execution.ExternalObjectRequest request)
    {
        var incarnation = Connect();
        var obtained = Send<AutomationObtainParams, AutomationObjectResult>(incarnation, ExternalProtocol.AutomationObtain, new AutomationObtainParams
        {
            Source = request.Source,
            ProgId = request.ProgId,
            PathName = request.PathName,
            ServerName = request.ServerName,
            HandlingEvent = _handlingEvent,
        });
        return ObjectOf(incarnation, obtained.Handle);
    }

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
        foreach (var argument in arguments)
        {
            Remember(argument);
        }

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
    public bool? IsInstanceOf(object target, string library, string className)
    {
        if (target is not RemoteObject { Incarnation.IsLost: false } remote)
        {
            return null;
        }

        try
        {
            return Send<AutomationIsInstanceOfParams, AutomationIsInstanceOfResult>(
                remote.Incarnation, ExternalProtocol.AutomationIsInstanceOf,
                new AutomationIsInstanceOfParams { Target = remote.Handle, Library = library, ClassName = className, HandlingEvent = _handlingEvent }).IsInstance;
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
            sink.OnEvent(new AutomationEvent(raised.Name, arguments, raised.IsSynchronous, Thread.Sleep) { DispatchId = raised.DispatchId });
            foreach (var argument in arguments)
            {
                Remember(argument);
            }

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
