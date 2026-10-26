using RDCore.External.Automation;
using RDCore.Runtime.StdLib;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Values.Bindings;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Model.Values.Runtime;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Abstract.StdLib;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

namespace RDCore.Runtime.Execution.External.Automation;

/// <summary>
/// The objects of a session that its automation servers hold: an object of a class module of the program's, or of the standard library's, handed to a server as
/// MS-VBA hands one - <c>d.Add "key", New Widget</c> - is held by the session for as long as the server holds it, is the same object when the server gives it
/// back (<c>d("key") Is w</c>), and answers the calls the server makes to it.
/// </summary>
/// <remarks>
/// <para>
/// The server holds a stand-in for the object (<see cref="ISessionObjectStandIn"/>), which counts the times the object was handed over to it, and says how many
/// when nothing holds it any more. The session holds the object for as long as a crossing is not accounted for: a stand-in let go of is counted down on the
/// session's own thread, the next time the session hands an object over, finds one, or is called, so that an object's <c>Class_Terminate</c> runs where its code
/// can run.
/// </para>
/// <para>
/// A call the server makes is a call by name (<c>CallByName</c>), when it called by a name, or by the member's <c>VB_UserMemId</c> - the default member's 0, an
/// enumerator's -4. It runs as a server's event does (<see cref="ServerEventRouter"/>): at once when a call the program waits for made it, once the session is open
/// to it otherwise.
/// </para>
/// </remarks>
internal sealed class SessionObjectHost : ISessionObjectHost
{
    private static readonly ConditionalWeakTable<IRuntimeSession, SessionObjectHost> Sessions = [];

    private const int MemberNotFound = unchecked((int)0x80020003);
    private const int ObjectRequired = unchecked((int)0x800A01A8);

    private readonly IRuntimeSession _session;
    private readonly object _gate = new();
    private readonly Dictionary<string, Exported> _exported = [];
    private readonly ConcurrentQueue<(string SessionObject, int Crossings)> _released = new();

    private SessionObjectHost(IRuntimeSession session) => _session = session;

    /// <summary>The objects of <paramref name="session"/> that its servers hold.</summary>
    public static SessionObjectHost Of(IRuntimeSession session) => Sessions.GetValue(session, created => new SessionObjectHost(created));

    /// <summary>
    /// Hands an object of the session over to <paramref name="server"/>: one more crossing for the session to hold the object for.
    /// </summary>
    /// <param name="identity">The object.</param>
    /// <param name="server">The server it is handed to, whose objects the calls it makes to the object are given.</param>
    /// <returns>What the server is given: its side makes the stand-in.</returns>
    public SessionObjectReference Export(VBRuntimeObjectId identity, IAutomationServer server)
    {
        Drain();
        var key = identity.ToString();
        lock (_gate)
        {
            if (_exported.TryGetValue(key, out var exported))
            {
                _exported[key] = exported with { Crossings = exported.Crossings + 1, Server = server };
            }
            else
            {
                var holder = new ValueBindingHandle(new VBObjectValue(identity).RuntimeValue);
                _session.Objects.AddRef(identity, holder);
                _exported[key] = new Exported(identity, holder, 1, server);
            }
        }

        return new SessionObjectReference(key, this);
    }

    /// <summary>
    /// The object of the session a stand-in a server gave back is, if the session handed it over.
    /// </summary>
    public bool TryFind(ISessionObjectStandIn standIn, out VBRuntimeObjectId identity)
    {
        Drain();
        lock (_gate)
        {
            if (_exported.TryGetValue(standIn.SessionObject, out var exported))
            {
                identity = exported.Identity;
                return true;
            }
        }

        identity = default;
        return false;
    }

    /// <inheritdoc/>
    public void Release(string sessionObject, int crossings) => _released.Enqueue((sessionObject, crossings));

    // the stand-ins let go of since: an object whose every crossing is accounted for is let go of by the session, on its own thread.
    private void Drain()
    {
        while (_released.TryDequeue(out var released))
        {
            Exported? letGo = null;
            lock (_gate)
            {
                if (_exported.TryGetValue(released.SessionObject, out var exported))
                {
                    var left = exported.Crossings - released.Crossings;
                    if (left > 0)
                    {
                        _exported[released.SessionObject] = exported with { Crossings = left };
                    }
                    else
                    {
                        _ = _exported.Remove(released.SessionObject);
                        letGo = exported;
                    }
                }
            }

            if (letGo is { } done)
            {
                _ = _session.ReleaseReference(done.Identity, done.Holder);
            }
        }
    }

    /// <inheritdoc/>
    public object? Invoke(SessionObjectCall call)
    {
        object? returned = null;
        AutomationException? failure = null;
        void Handle()
        {
            try
            {
                returned = Call(call);
            }
            catch (AutomationException exception)
            {
                failure = exception;
            }
        }

        if (call.IsSynchronous)
        {
            _session.Turn.RunEvent(Handle);
        }
        else
        {
            using (_session.Turn.Waiting())
            {
                while (!_session.Turn.IsOpenForEvents)
                {
                    call.Serve(TimeSpan.FromMilliseconds(10));
                }

                _session.Turn.RunEvent(Handle);
            }
        }

        return failure is null ? returned : throw failure;
    }

    // the member the server called, by its name or its dispatch identifier, with the arguments as the server passed them; what it returned, as the server's value.
    private object? Call(SessionObjectCall call)
    {
        Exported exported;
        lock (_gate)
        {
            if (!_exported.TryGetValue(call.SessionObject, out exported!))
            {
                throw new AutomationException(ObjectRequired, "The object of the session is no longer held by the server that called it.");
            }
        }

        if (!_session.Symbols.TryGetInstance(exported.Identity, out var instance))
        {
            throw new AutomationException(ObjectRequired, "The object of the session is gone.");
        }

        var name = call.Name ?? MemberOf(instance.ClassModule, call.DispatchId)?.Name
            ?? throw new AutomationException(MemberNotFound, $"The object has no member of the dispatch identifier {call.DispatchId}.");

        var objects = new ServerObjects(_session, exported.Server);
        var resolver = _session.Symbols.Resolver;
        var arguments = call.Arguments
            .Select(argument => new VBVariantValue(AutomationMarshaller.FromAutomation(argument, VBVariantType.TypeInfo, (handle, declared) => objects.Wrap(handle, declared, resolver))))
            .ToArray();

        var callType = call.Invocation switch
        {
            AutomationInvocation.Let => VBCallType.VBLet,
            AutomationInvocation.Set => VBCallType.VBSet,
            AutomationInvocation.Method => VBCallType.VBMethod,
            _ => VBCallType.VBGet,
        };

        // a server that reads a member asks for a method and a property at once (DISPATCH_METHOD | DISPATCH_PROPERTYGET): a procedure is called as one.
        if (callType is VBCallType.VBGet && instance.ClassModule.DefaultInterfaceMembers.FirstOrDefault(member => string.Equals(member.Name, name, StringComparison.OrdinalIgnoreCase))
            is VBProcedureMemberSymbol or VBFunctionMemberSymbol)
        {
            callType = VBCallType.VBMethod;
        }

        var answered = new StdInteraction(_session).CallByName(new VBObjectValue(exported.Identity), new VBStringValue(name), callType, arguments);
        if (!answered.IsSuccess)
        {
            var error = answered.ErrorInfo?.AsErrorInfo;
            throw new AutomationException(error is null ? unchecked((int)0x80004005) : unchecked((int)0x800A0000) | error.ErrorId, error?.Description ?? $"'{name}' failed.");
        }

        return answered.Result is { } result ? AutomationMarshaller.ToAutomation(result, objects.ToServer) : null;
    }

    // the member that answers to a dispatch identifier: its VB_UserMemId, which a class's default member (0) and enumerator (-4) are declared by.
    private static VBTypeMemberSymbol? MemberOf(VBClassModuleSymbol classModule, int dispatchId)
        => classModule.DefaultInterfaceMembers.FirstOrDefault(member => member.TryGetProperty(SymbolProperties.UserMemId, out var identifier) && identifier == dispatchId);

    // an object handed over: held by the session for as long as a crossing is not accounted for.
    private sealed record class Exported(VBRuntimeObjectId Identity, IBindingHandle Holder, int Crossings, IAutomationServer Server);
}
