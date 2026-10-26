using RDCore.External.Automation;
using RDCore.SDK.Model;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Values.Bindings;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Model.Values.Runtime;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Shared;

namespace RDCore.Runtime.Execution.External.Automation;

/// <summary>
/// Raises, in the session, the events that a server's object raises: the procedures that handle them are those of the <c>WithEvents</c> variables that hold the object
/// (<strong>MS-VBAL §5.4.3.9</strong>).
/// </summary>
/// <remarks>
/// <para>
/// This is the other end of <c>RaiseEvent</c>. An event of the workspace's own is raised by code that has the event's arguments written in it; an event of a server's object is
/// raised by the server, with the arguments it has, at a time and on a thread of its own. Everything else is the same: the handlers are found in the order their variables were
/// assigned, they are invoked with the arguments, and what a <c>ByRef</c> parameter holds after one is what the next starts with - and what the server finds when they are done,
/// which is how a handler cancels what the server was about to do.
/// </para>
/// <para>
/// When the event is handled is the session's turn to say (<see cref="ISessionTurn"/>): at once when a call that the program is waiting for raised it, and between two activations
/// when it was not. The objects the server hands over are the program's for as long as a handler keeps them, and the server's after.
/// </para>
/// </remarks>
/// <param name="session">The session whose procedures handle the events.</param>
/// <param name="server">The server that raises them.</param>
/// <param name="route">
/// What an event is, by its name, and the procedures that handle it, in the order they run: the subscribers and their handlers. <see langword="null"/> for an event the
/// source does not declare.
/// </param>
/// <param name="intercept">
/// What handles an event itself, at the time it is handled, instead of the procedures the route names: a form's close, which the session does as <c>Unload</c> does.
/// Returns whether it handled it.
/// </param>
/// <param name="source">
/// The object of the session the events are raised by, whose connected sinks (<see cref="SessionConnections"/>) are called after the procedures the route names;
/// <see langword="null"/> for one that is not the program's to connect to.
/// </param>
internal sealed class ServerEventRouter(
    IRuntimeSession session, IAutomationServer server, Func<string, ServerEventRoute?> route, Func<string, object?[], bool>? intercept = null,
    VBRuntimeObjectId? source = null) : IAutomationEventSink
{
    private readonly ServerObjects _objects = new(session, server);

    /// <summary>
    /// Routes the events of <paramref name="source"/> to the <c>WithEvents</c> variables that hold it.
    /// </summary>
    /// <param name="session">The session whose procedures handle the events.</param>
    /// <param name="server">The server that raises them.</param>
    /// <param name="source">The object that raises them.</param>
    public ServerEventRouter(IRuntimeSession session, IAutomationServer server, VBRuntimeObjectId source)
        : this(session, server, name => session.Symbols.TryGetInstance(source, out var live) && live.ClassModule.FindEvent(name) is { } raised
            ? new ServerEventRoute(raised, SubscribersOf(session, source, raised))
            : null, source: source)
    {
    }

    /// <summary>
    /// The handlers of the <c>WithEvents</c> variables that hold <paramref name="source"/>, of the event <paramref name="raised"/>, in the order the variables were
    /// assigned.
    /// </summary>
    /// <param name="session">The session whose variables hold the object.</param>
    /// <param name="source">The object.</param>
    /// <param name="raised">The event.</param>
    public static IEnumerable<(VBRuntimeObjectId Subscriber, VBTypeMemberSymbol Handler)> SubscribersOf(IRuntimeSession session, VBRuntimeObjectId source, VBEventMemberSymbol raised)
    {
        foreach (var subscription in session.Objects.EventSubscribers(source))
        {
            if (session.Symbols.TryGetInstance(subscription.Subscriber, out var subscriber)
                && subscription.Variable is VBTypeMemberSymbol variable
                && subscriber.ClassModule.FindEventHandler(variable, raised) is { } handler)
            {
                yield return (subscription.Subscriber, handler);
            }
        }
    }

    // a synchronous event is the answer to the call the program waits for, and is handled inside it; any other waits for the session to be open to it - nothing runs,
    // or the program pumps - doing meanwhile what is asked of the thread it was raised on.
    /// <inheritdoc/>
    public void OnEvent(AutomationEvent raised)
    {
        if (raised.IsSynchronous)
        {
            session.Turn.RunEvent(() => Handle(raised.Name, raised.Arguments, raised.DispatchId));
            return;
        }

        using (session.Turn.Waiting())
        {
            while (!session.Turn.IsOpenForEvents)
            {
                raised.Serve(TimeSpan.FromMilliseconds(10));
            }

            session.Turn.RunEvent(() => Handle(raised.Name, raised.Arguments, raised.DispatchId));
        }
    }

    private void Handle(string name, object?[] arguments, int? dispatchId)
    {
        if (intercept?.Invoke(name, arguments) == true || session.Callables is not { } callables)
        {
            return;
        }

        // what the server hands over is held for as long as the event is handled: a handler that lets go of it does not release it from under the next, whether a
        // handler of the route or a connected sink (Cancel is one object to all of them).
        var handed = new Dictionary<VBRuntimeObjectId, IBindingHandle>();
        try
        {
            if (route(name) is { } routed && routed.Handlers.ToList() is { Count: > 0 } handlers && !Run(callables, routed.Raised.Parameters, arguments, handlers, handed))
            {
                return;
            }

            // then the sinks the program connected to the object itself (ConnectToConnectionPoint), in the order they were connected: each is called by its member of
            // the dispatch identifier the event was raised with, with the arguments as that member declares them, as IDispatch hands them over.
            if (source is not { } connectedTo || dispatchId is not { } raisedWith)
            {
                return;
            }

            // the extender's events are raised as Microsoft Forms' ControlEvents (Enter is 0x80018202), which is what MVVM's ControlEventsPunk answers to.
            foreach (var connection in SessionConnections.Of(session).Of(connectedTo))
            {
                if (session.Symbols.TryGetInstance(connection.Sink, out var sink) && SinkMemberOf(sink.ClassModule, raisedWith) is { } member
                    && !Run(callables, DeclaredParametersOf(member), arguments, [(connection.Sink, member)], handed))
                {
                    return;
                }
            }
        }
        finally
        {
            foreach (var (identity, holder) in handed)
            {
                _ = session.ReleaseReference(identity, holder);
                _objects.Discard(identity);
            }
        }
    }

    // the public member of a sink's class that answers to a dispatch identifier (VB_UserMemId).
    private static VBTypeMemberSymbol? SinkMemberOf(VBClassModuleSymbol sink, int dispatchId)
        => sink.Members.FirstOrDefault(member => member is VBProcedureMemberSymbol or VBReturningMemberSymbol
            && member.AccessModifier is AccessModifier.Public or AccessModifier.Implicit
            && member.TryGetProperty(SymbolProperties.UserMemId, out var identifier) && identifier == dispatchId);

    // the parameters a member of a class declares: its first is its Me, which the call is made on, and no argument of the event's.
    private static VBParameterSymbol[] DeclaredParametersOf(VBTypeMemberSymbol member)
        => RuntimeProcedureInvoker.GetParameters(member) is [{ Name: Tokens.Me }, .. var declared] ? [.. declared] : [.. RuntimeProcedureInvoker.GetParameters(member)];

    // runs the handlers of an event with its arguments, read as `parameters` declares them: whether they all ran.
    private bool Run(
        ICallableBindingFactory callables, IReadOnlyList<VBParameterSymbol> parameters, object?[] arguments,
        IReadOnlyList<(VBRuntimeObjectId Subscriber, VBTypeMemberSymbol Handler)> handlers, Dictionary<VBRuntimeObjectId, IBindingHandle> handed)
    {
        var resolver = session.Symbols.Resolver;
        var temporaries = new List<(int Parameter, MemoryAddress Address)>();
        var bound = new IRuntimeValue[parameters.Count];

        try
        {
            for (var index = 0; index < parameters.Count; index++)
            {
                var parameter = parameters[index];
                var value = AutomationMarshaller.FromAutomation(
                    index < arguments.Length ? arguments[index] : null,
                    parameter.ResolvedType,
                    (handle, declared) =>
                    {
                        var wrapped = (VBObjectValue)_objects.Wrap(handle, declared, resolver);
                        if (!handed.ContainsKey(wrapped.Value))
                        {
                            var holder = new ValueBindingHandle(wrapped.RuntimeValue);
                            session.Objects.AddRef(wrapped.Value, holder);
                            handed[wrapped.Value] = holder;
                        }

                        return wrapped;
                    });

                // MS-VBAL §5.4.2.20: the argument of a ByRef parameter is the location the handlers share, and the one the server reads when they are done.
                if (RuntimeProcedureInvoker.IsByRef(parameter.ParameterKind)
                    && session.Storage.TryAllocate(parameter.ResolvedType.DefaultValue.Size, new ValueBindingHandle(value.RuntimeValue), out var address))
                {
                    temporaries.Add((index, address));
                    bound[index] = new VBRuntimeReference(address);
                }
                else
                {
                    bound[index] = value.RuntimeValue;
                }
            }

            var ran = true;
            foreach (var (subscriber, handler) in handlers)
            {
                var handled = callables.ForMember(handler).Call(resolver, [new VBObjectValue(subscriber).RuntimeValue, .. bound]);
                if (!handled.IsSuccess)
                {
                    Report(handled);
                    ran = false;
                    break;
                }
            }

            // what the handlers left in the parameters the server passed by reference is what it finds - and what the next handlers start with.
            foreach (var (index, address) in temporaries)
            {
                if (resolver.TryRead(address, out var cell))
                {
                    arguments[index] = AutomationMarshaller.ToAutomation(parameters[index].ResolvedType.CreateValue(new ValueBindingHandle(cell.Value)), _objects.ToServer);
                }
            }

            return ran;
        }
        catch (AutomationException)
        {
            // a value that crossed one way and cannot cross the other is one the server gets back as it passed it.
            return true;
        }
        finally
        {
            foreach (var (_, address) in temporaries)
            {
                session.Storage.TryDeallocate(address);
            }
        }
    }

    // a handler that fails has nobody to tell but the person who runs the program: what it printed goes where the program's output does.
    private void Report(RuntimeSemanticsEvaluationResult failed)
    {
        var text = failed.ErrorInfo is { } error ? $"Run-time error '{error.AsErrorInfo.ErrorId}': {error.AsErrorInfo.Description}" : "An event handler could not be run.";
        session.Output.Write(text);
        session.Output.WriteLine();
    }
}

/// <summary>
/// An event a server's object raised, and who handles it.
/// </summary>
/// <param name="Raised">The event, as the class that declares it declares it: its parameters are what the server's arguments are read as.</param>
/// <param name="Handlers">The procedures that handle it, in the order they run, each with the object whose procedure it is.</param>
internal sealed record class ServerEventRoute(VBEventMemberSymbol Raised, IEnumerable<(VBRuntimeObjectId Subscriber, VBTypeMemberSymbol Handler)> Handlers);
