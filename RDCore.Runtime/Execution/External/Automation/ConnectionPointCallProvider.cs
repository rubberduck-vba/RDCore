using RDCore.External.Automation;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Bindings;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Model.Values.Runtime;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Shared;

namespace RDCore.Runtime.Execution.External.Automation;

/// <summary>
/// Runs the <c>Declare</c>d shlwapi function a program connects an object of its own to a server's object's events with, <c>ConnectToConnectionPoint</c>
/// (ordinal 168), as MVVM's <c>ControlEventsPunk</c> does: the program's object is no COM object a native function could be given, so the connection is the
/// session's (<see cref="SessionConnections"/>), and the server's object calls the sink's members by the dispatch identifiers it raises its events with.
/// </summary>
/// <remarks>
/// <para>
/// <c>ConnectToConnectionPoint(punk, riidEvent, fConnect, punkTarget, pdwCookie, ppcpOut)</c> connects <c>punk</c> to <c>punkTarget</c>'s connection point for
/// <c>riidEvent</c> and writes the connection's cookie, or disconnects the connection the cookie names; it returns an <c>HRESULT</c>. As Excel has it
/// (<c>oracle\s41</c> P01-P06, Q01-Q04), for a form's control and <c>IDispatch</c>:
/// <list type="bullet">
/// <item>a connection is 0 and a cookie that is not 0; another sink of the same object is another connection;</item>
/// <item>the sink is called after the form's own handler, the sinks in the order they were connected, until it is disconnected - even once the program let go of
/// it;</item>
/// <item>it is called for the control's own events (<c>Change</c>) and for its extender's (<c>Enter</c>, <c>Exit</c>, <c>BeforeUpdate</c>,
/// <c>AfterUpdate</c>), by their dispatch identifiers; <c>Exit</c> and <c>BeforeUpdate</c>'s <c>Cancel</c> cancels as a handler's does;</item>
/// <item>disconnecting is 0, a cookie that is connected no more included;</item>
/// <item>an interface other than <c>IDispatch</c>, or an object that has no connection points (a <c>Collection</c>), is <c>E_NOINTERFACE</c>, and the cookie is
/// 0.</item>
/// </list>
/// </para>
/// <para>
/// Asked of the providers before the one that calls native libraries, and only for that function: every other function of the library is the native one. The
/// policy over library imports sees the call as it sees any <c>Declare</c>d one.
/// </para>
/// </remarks>
/// <param name="session">The session whose objects are connected.</param>
/// <param name="server">The automation server whose objects they are connected to.</param>
public sealed class ConnectionPointCallProvider(IRuntimeSession session, IAutomationServer server) : IExternalCallProvider
{
    private const string EntryPoint = "ConnectToConnectionPoint";
    private const string Ordinal = "#168";

    private const int Succeeded = 0;
    private const int InvalidArgument = unchecked((int)0x80070057);
    private const int NoInterface = unchecked((int)0x80004002);

    /// <summary>The interface MVVM connects through: <c>IDispatch</c>, which every event interface of a server's object is to a sink.</summary>
    internal static readonly Guid DispatchInterface = new("00020400-0000-0000-C000-000000000046");

    /// <inheritdoc/>
    public bool CanDispatch(ExternalCallRequest request)
        => request.IsLibraryImport && IsShellLightweightUtilities(request.Library) && EntryPointOf(request.Member) is { } entry
            && (string.Equals(entry, EntryPoint, StringComparison.OrdinalIgnoreCase) || entry == Ordinal);

    /// <inheritdoc/>
    public RuntimeSemanticsEvaluationResult Dispatch(ExternalCallRequest request, ISymbolResolver resolver)
    {
        if (request.Arguments.Length < 5)
        {
            return Returned(InvalidArgument);
        }

        var sink = ObjectOf(request.Arguments[0], resolver);
        var connect = Dereference(request.Arguments[2], resolver, out _) is var flag && VBLongType.TypeInfo.CreateValue(new ValueBindingHandle(flag)) is VBLongValue { Value: not 0 };
        var target = ObjectOf(request.Arguments[3], resolver);
        var cookieValue = Dereference(request.Arguments[4], resolver, out var cookieVariable);
        var parameters = RuntimeProcedureInvoker.GetParameters(request.Member);
        if (target is not { } source || parameters.Length < 2
            || GuidOf(parameters[1].ResolvedType.CreateValue(new ValueBindingHandle(Dereference(request.Arguments[1], resolver, out _)))) is not { } events)
        {
            return Returned(InvalidArgument);
        }

        // an object that is no server's, or no connection point for the interface, is E_NOINTERFACE, and no cookie (oracle\s41 Q02, Q03).
        // TODO measure an object of the program's own whose class declares events (a connection point container in MS-VBA), and IDispatch on an Excel object.
        if (!session.ExternalObjects.TryGet(source, out var owner, out var handle) || !ReferenceEquals(owner, server) || events != DispatchInterface)
        {
            cookieVariable?.SetValue(resolver, new VBLongValue(0).RuntimeValue);
            return Returned(NoInterface);
        }

        var connections = SessionConnections.Of(session);
        if (!connect)
        {
            // a cookie that is no connection - one disconnected already - is disconnected all the same (Q01).
            var cookie = VBLongType.TypeInfo.CreateValue(new ValueBindingHandle(cookieValue)) is VBLongValue { Value: var named } ? named : 0;
            if (connections.Disconnect(source, cookie) is { } disconnected)
            {
                _ = session.ReleaseReference(disconnected.Sink, disconnected.Holder);
                ExternalEvents.StopWhenUnheard(session, source);
            }

            return Returned(Succeeded);
        }

        if (sink is not { } connected)
        {
            return Returned(InvalidArgument);
        }

        // the object raises its events to the session once something listens to them: a form listens to its controls already, and anything else is listened to now.
        ExternalEvents.Listen(session, source);

        var holder = new ValueBindingHandle(new VBObjectValue(connected).RuntimeValue);
        session.Objects.AddRef(connected, holder);
        var made = connections.Connect(source, connected, events, holder);
        cookieVariable?.SetValue(resolver, new VBLongValue(made).RuntimeValue);
        return Returned(Succeeded);
    }

    private static RuntimeSemanticsEvaluationResult Returned(int status) => RuntimeSemanticsEvaluationResult.Success(new VBLongValue(status));

    private static bool IsShellLightweightUtilities(string? library)
        => library is { } named && (string.Equals(named, "shlwapi", StringComparison.OrdinalIgnoreCase) || string.Equals(named, "shlwapi.dll", StringComparison.OrdinalIgnoreCase));

    private static string? EntryPointOf(VBTypeMemberSymbol member) => member switch
    {
        VBExternalFunctionMemberSymbol { Alias: { Length: > 0 } alias } => alias,
        VBExternalFunctionMemberSymbol function => function.Name,
        VBExternalSubMemberSymbol { Alias: { Length: > 0 } alias } => alias,
        VBExternalSubMemberSymbol procedure => procedure.Name,
        _ => null,
    };

    // the value an argument is, through the references a ByRef argument is; the variable it names, which what the function writes is written to.
    private static IRuntimeValue Dereference(IRuntimeValue argument, ISymbolResolver resolver, out IBindingHandle? variable)
    {
        variable = null;
        while (argument is VBRuntimeReference reference && resolver.TryRead(reference.Value, out var cell))
        {
            variable = cell;
            argument = cell.Value;
        }

        return argument;
    }

    private static VBRuntimeObjectId? ObjectOf(IRuntimeValue argument, ISymbolResolver resolver)
        => VBObjectType.TypeInfo.CreateValue(new ValueBindingHandle(Dereference(argument, resolver, out _))) is VBObjectValue { } value && !value.IsNothing()
            ? value.Value
            : null;

    // a GUID, as a program declares one: Data1 As Long, Data2 As Integer, Data3 As Integer, Data4(0 To 7) As Byte.
    private static Guid? GuidOf(VBTypedValue? argument)
    {
        if (argument is not VBUserDefinedTypeValue record || record.Fields.Length != 4
            || record.FieldAt(3) is not VBArrayValue { Length: 8 } data4)
        {
            return null;
        }

        static long Number(VBTypedValue? value) => value?.RuntimeValue.BoxedValue is IConvertible number ? number.ToInt64(System.Globalization.CultureInfo.InvariantCulture) : 0;

        var bytes = Enumerable.Range(0, 8).Select(index => (byte)Number(data4.ElementAt(index))).ToArray();
        return new Guid((int)Number(record.FieldAt(0)), (short)Number(record.FieldAt(1)), (short)Number(record.FieldAt(2)), bytes);
    }
}
