using RDCore.External.Automation;
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
/// <param name="source">The object that raises them.</param>
internal sealed class ServerEventRouter(IRuntimeSession session, IAutomationServer server, VBRuntimeObjectId source) : IAutomationEventSink
{
    private readonly ServerObjects _objects = new(session, server);

    // a synchronous event is the answer to the call the program waits for, and is handled inside it; any other waits for the session to be open to it - nothing runs,
    // or the program pumps - doing meanwhile what is asked of the thread it was raised on.
    /// <inheritdoc/>
    public void OnEvent(AutomationEvent raised)
    {
        if (raised.IsSynchronous)
        {
            session.Turn.RunEvent(() => Handle(raised.Name, raised.Arguments));
            return;
        }

        using (session.Turn.Waiting())
        {
            while (!session.Turn.IsOpenForEvents)
            {
                raised.Serve(TimeSpan.FromMilliseconds(10));
            }

            session.Turn.RunEvent(() => Handle(raised.Name, raised.Arguments));
        }
    }

    private void Handle(string name, object?[] arguments)
    {
        if (session.Callables is not { } callables
            || !session.Symbols.TryGetInstance(source, out var live)
            || live.ClassModule.FindEvent(name) is not { } raised
            || session.Objects.EventSubscribers(source) is not { Count: > 0 } subscriptions)
        {
            return;
        }

        var resolver = session.Symbols.Resolver;
        var handed = new List<VBRuntimeObjectId>();
        var temporaries = new List<(int Parameter, MemoryAddress Address)>();
        var parameters = raised.Parameters;
        var bound = new IRuntimeValue[parameters.Length];

        try
        {
            for (var index = 0; index < parameters.Length; index++)
            {
                var parameter = parameters[index];
                var value = AutomationMarshaller.FromAutomation(
                    index < arguments.Length ? arguments[index] : null,
                    parameter.ResolvedType,
                    (handle, declared) =>
                    {
                        var wrapped = _objects.Wrap(handle, declared, resolver);
                        handed.Add(((VBObjectValue)wrapped).Value);
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

            foreach (var subscription in subscriptions)
            {
                if (!session.Symbols.TryGetInstance(subscription.Subscriber, out var subscriber)
                    || subscription.Variable is not VBTypeMemberSymbol variable
                    || subscriber.ClassModule.FindEventHandler(variable, raised) is not { } handler)
                {
                    continue;
                }

                var handled = callables.ForMember(handler).Call(resolver, [new VBObjectValue(subscription.Subscriber).RuntimeValue, .. bound]);
                if (!handled.IsSuccess)
                {
                    Report(handled);
                    break;
                }
            }

            // what the handlers left in the parameters the server passed by reference is what it finds.
            foreach (var (index, address) in temporaries)
            {
                if (resolver.TryRead(address, out var cell))
                {
                    arguments[index] = AutomationMarshaller.ToAutomation(parameters[index].ResolvedType.CreateValue(new ValueBindingHandle(cell.Value)), _objects.HandleOf);
                }
            }
        }
        catch (AutomationException)
        {
            // a value that crossed one way and cannot cross the other is one the server gets back as it passed it.
        }
        finally
        {
            foreach (var (_, address) in temporaries)
            {
                session.Storage.TryDeallocate(address);
            }

            foreach (var identity in handed)
            {
                _objects.Discard(identity);
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
