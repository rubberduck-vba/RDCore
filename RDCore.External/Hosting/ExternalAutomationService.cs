using RDCore.External.Automation;
using RDCore.External.Protocol;
using RDCore.SDK.Platform.Channels;
using System.Collections.Concurrent;
using System.Globalization;

namespace RDCore.External.Hosting;

/// <summary>
/// Answers the requests of an environment host about the objects of the automation servers, with the servers of the platform the external host runs on.
/// </summary>
/// <remarks>
/// <para>
/// Every request is a call the program is waiting for, and is made as it arrives, on the thread it arrives on: the platform's server takes it to the thread it has to be
/// made from. A call made by a handler of an event is the exception (<see cref="AutomationRequest.HandlingEvent"/>): the thread the server raised the event on is waiting
/// for the handlers, and is the one that makes it.
/// </para>
/// <para>
/// An event goes to the environment host, which says when it is handled, and the thread that raised it waits for the answer - doing meanwhile the calls the handlers make,
/// and for an asynchronous event, the work the server asks of it.
/// </para>
/// </remarks>
/// <param name="platform">The automation servers of the platform.</param>
public sealed class ExternalAutomationService(IAutomationServer platform)
{
    private readonly ExternalHandles _handles = new();
    private readonly ConcurrentDictionary<long, EventWait> _events = new();
    private long _lastEvent;

    /// <summary>
    /// Sends an event to the environment host, and completes once it is handled; set once the connection to it is up.
    /// </summary>
    public Func<AutomationEventParams, Task<AutomationEventResult>>? RaiseOnClient { get; set; }

    /// <summary>
    /// Answers the calls of the environment host that come over <paramref name="channel"/>, and raises the events of the servers' objects over it.
    /// </summary>
    /// <param name="channel">The channel of calls to the environment host; it is started by its owner.</param>
    public void ServeOn(CallChannel channel)
    {
        channel.Handle<AutomationStatusParams, AutomationStatusResult>(ExternalProtocol.AutomationStatus, Status);
        channel.Handle<AutomationCreateParams, AutomationObjectResult>(ExternalProtocol.AutomationCreate, Create);
        channel.Handle<AutomationInvokeParams, AutomationInvokeResult>(ExternalProtocol.AutomationInvoke, Invoke);
        channel.Handle<AutomationClassNameParams, AutomationClassNameResult>(ExternalProtocol.AutomationClassName, ClassName);
        channel.Handle<AutomationMoveNextParams, AutomationMoveNextResult>(ExternalProtocol.AutomationMoveNext, MoveNext);
        channel.Handle<AutomationResetParams, ExternalDoneResult>(ExternalProtocol.AutomationReset, Reset);
        channel.Handle<AutomationAdviseParams, ExternalDoneResult>(ExternalProtocol.AutomationAdvise, Advise);
        channel.Handle<AutomationUnadviseParams, ExternalDoneResult>(ExternalProtocol.AutomationUnadvise, Unadvise);
        channel.Handle<AutomationReleaseParams, ExternalDoneResult>(ExternalProtocol.AutomationRelease, Release);
        RaiseOnClient = raised => channel.CallAsync<AutomationEventParams, AutomationEventResult>(ExternalProtocol.AutomationEvent, raised);
    }

    /// <summary>Answers <see cref="ExternalProtocol.AutomationStatus"/>.</summary>
    public AutomationStatusResult Status(AutomationStatusParams request) => new() { IsAvailable = platform.IsAvailable };

    /// <summary>Answers <see cref="ExternalProtocol.AutomationCreate"/>.</summary>
    public AutomationObjectResult Create(AutomationCreateParams request)
        => Answer(request.HandlingEvent, () => new AutomationObjectResult { Handle = _handles.HandleOf(platform.CreateObject(request.ProgId)) }, Failed<AutomationObjectResult>);

    /// <summary>Answers <see cref="ExternalProtocol.AutomationInvoke"/>.</summary>
    public AutomationInvokeResult Invoke(AutomationInvokeParams request) => Answer(request.HandlingEvent, () =>
    {
        var target = _handles.ObjectOf(request.Target);
        var arguments = request.Arguments.Select(argument => ExternalValues.FromWire(argument, _handles.ObjectOf)).ToArray();
        var returned = platform.Invoke(target, request.Member, request.Invocation, arguments, request.ByReference, CultureInfo.GetCultureInfo(request.Culture));

        // what the server wrote to an argument passed by reference is all the caller reads of the arguments.
        return new AutomationInvokeResult
        {
            Returned = ExternalValues.ToWire(returned, _handles.HandleOf),
            Arguments = [.. arguments.Select((argument, index) => index < request.ByReference.Length && request.ByReference[index]
                ? ExternalValues.ToWire(argument, _handles.HandleOf)
                : new ExternalValue())],
        };
    }, Failed<AutomationInvokeResult>);

    /// <summary>Answers <see cref="ExternalProtocol.AutomationClassName"/>.</summary>
    public AutomationClassNameResult ClassName(AutomationClassNameParams request)
        => Answer(request.HandlingEvent, () => new AutomationClassNameResult { Name = platform.ClassNameOf(_handles.ObjectOf(request.Target)) }, Failed<AutomationClassNameResult>);

    /// <summary>Answers <see cref="ExternalProtocol.AutomationMoveNext"/>.</summary>
    public AutomationMoveNextResult MoveNext(AutomationMoveNextParams request) => Answer(request.HandlingEvent, () =>
    {
        var moved = platform.MoveNext(_handles.ObjectOf(request.Enumerator), out var current);
        return new AutomationMoveNextResult { Moved = moved, Current = ExternalValues.ToWire(current, _handles.HandleOf) };
    }, Failed<AutomationMoveNextResult>);

    /// <summary>Answers <see cref="ExternalProtocol.AutomationReset"/>.</summary>
    public ExternalDoneResult Reset(AutomationResetParams request) => Answer(request.HandlingEvent, () =>
    {
        platform.Reset(_handles.ObjectOf(request.Enumerator));
        return new ExternalDoneResult();
    }, Failed<ExternalDoneResult>);

    /// <summary>Answers <see cref="ExternalProtocol.AutomationAdvise"/>.</summary>
    public ExternalDoneResult Advise(AutomationAdviseParams request) => Answer(request.HandlingEvent, () =>
    {
        platform.Advise(_handles.ObjectOf(request.Source), new ForwardingSink(this, request.Source));
        return new ExternalDoneResult();
    }, Failed<ExternalDoneResult>);

    /// <summary>Answers <see cref="ExternalProtocol.AutomationUnadvise"/>.</summary>
    public ExternalDoneResult Unadvise(AutomationUnadviseParams request) => Answer(request.HandlingEvent, () =>
    {
        platform.Unadvise(_handles.ObjectOf(request.Source));
        return new ExternalDoneResult();
    }, Failed<ExternalDoneResult>);

    /// <summary>Answers <see cref="ExternalProtocol.AutomationRelease"/>.</summary>
    public ExternalDoneResult Release(AutomationReleaseParams request) => Answer(request.HandlingEvent, () =>
    {
        if (_handles.TryRemove(request.Handle, out var released))
        {
            platform.Release(released!);
        }

        return new ExternalDoneResult();
    }, Failed<ExternalDoneResult>);

    private static TResult Failed<TResult>(AutomationException failure) where TResult : ExternalResult, new() => new() { Failure = ExternalFailure.Of(failure) };

    // makes the call where it has to be made - on the thread waiting for the event whose handler makes it, if one does - and answers what the server reported.
    private TResult Answer<TResult>(long handlingEvent, Func<TResult> call, Func<AutomationException, TResult> failed)
    {
        try
        {
            return handlingEvent != 0 && _events.TryGetValue(handlingEvent, out var waiting) ? waiting.Run(call) : call();
        }
        catch (AutomationException failure)
        {
            return failed(failure);
        }
    }

    // sends an event to the environment host, and waits for the handlers on the thread that raised it.
    private void Raise(long source, AutomationEvent raised)
    {
        if (RaiseOnClient is not { } send)
        {
            return;
        }

        var number = Interlocked.Increment(ref _lastEvent);
        var waiting = new EventWait();
        _events[number] = waiting;
        try
        {
            var reply = send(new AutomationEventParams
            {
                Event = number,
                Source = source,
                Name = raised.Name,
                Arguments = [.. raised.Arguments.Select(argument => ExternalValues.ToWire(argument, _handles.HandleOf))],
                IsSynchronous = raised.IsSynchronous,
            });

            waiting.Serve(reply, raised.IsSynchronous ? null : raised.Serve);
            if (!reply.IsCompletedSuccessfully || reply.Result.Failure is not null)
            {
                return;
            }

            // what the handlers left in the arguments is what the server finds in the ones it passed by reference.
            var answered = reply.Result.Arguments;
            for (var index = 0; index < answered.Length && index < raised.Arguments.Length; index++)
            {
                raised.Arguments[index] = ExternalValues.FromWire(answered[index], _handles.ObjectOf);
            }
        }
        catch (AutomationException)
        {
            // an argument that cannot cross either way is one the server gets back as it passed it.
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException or InvalidOperationException)
        {
            // an environment host that is gone handles nothing: the server gets its arguments back as it passed them.
        }
        finally
        {
            _ = _events.TryRemove(number, out _);
        }
    }

    // the environment host's side of the events of an object, as the platform's server sees it.
    private sealed class ForwardingSink(ExternalAutomationService service, long source) : IAutomationEventSink
    {
        public void OnEvent(AutomationEvent raised) => service.Raise(source, raised);
    }

    // the thread that raised an event, while it waits for the handlers: the calls they make are made on it.
    private sealed class EventWait
    {
        private readonly BlockingCollection<Action> _calls = [];
        private readonly object _gate = new();
        private bool _over;

        // makes the call on the waiting thread, and waits for it; a call that comes once the thread has stopped waiting is made where it comes.
        public T Run<T>(Func<T> call)
        {
            T result = default!;
            Exception? failure = null;
            using var done = new ManualResetEventSlim();
            void Make()
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
            }

            lock (_gate)
            {
                if (_over)
                {
                    return call();
                }

                _calls.Add(Make);
            }

            done.Wait();
            if (failure is not null)
            {
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
            }

            return result;
        }

        // makes the calls the handlers make until they are done; an asynchronous event also does the work the server asks of its thread meanwhile.
        public void Serve(Task reply, Action<TimeSpan>? serve)
        {
            // the answer wakes the thread as a call would.
            _ = reply.ContinueWith(_ => Wake(), TaskScheduler.Default);
            var patience = serve is null ? Timeout.InfiniteTimeSpan : TimeSpan.FromMilliseconds(5);
            while (!reply.IsCompleted)
            {
                if (_calls.TryTake(out var call, patience))
                {
                    call();
                    continue;
                }

                serve?.Invoke(TimeSpan.FromMilliseconds(5));
            }

            // a call that came in with the answer is made before the thread goes back to the server.
            lock (_gate)
            {
                _over = true;
            }

            while (_calls.TryTake(out var call))
            {
                call();
            }
        }

        private void Wake()
        {
            lock (_gate)
            {
                if (!_over)
                {
                    _calls.Add(() => { });
                }
            }
        }
    }
}
