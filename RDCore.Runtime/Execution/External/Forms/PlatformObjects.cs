using System.Runtime.CompilerServices;
using RDCore.External.Automation;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Complex;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Bindings;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Model.Values.Runtime;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Workspace.Forms;

namespace RDCore.Runtime.Execution.External.Forms;

/// <summary>
/// The objects of a session that are the platform's own although a server holds them: a program's forms, Visual Basic 6.0 forms and UserForms alike, and
/// what they hand out of their own library. A call on one is answered by the platform (<see cref="ExternalCallRequest.IsAnsweredByPlatform"/>), which the
/// policy over automation lets through (<see cref="AutomationPolicyInterceptor"/>): a form is the program's own user interface, even when it is a COM object
/// of the machine's.
/// </summary>
/// <remarks>
/// An object is one by where the program got it, never by a library description, which a workspace can bring its own of:
/// <list type="number">
/// <item>the form the form host made for an instance of a form's class (<see cref="FormCallProvider"/>);</item>
/// <item>a control on a form, of whichever library: one the form's designer declares (<c>UserForm1.Label1</c>, a <c>WebBrowser</c>), or one a
/// <c>Controls.Add</c> of the form's makes (<c>Controls.Add "Forms.Label.1"</c>, <c>Controls.Add "Shell.Explorer.2"</c>). A control of another library is of
/// that library, for what it hands out in its turn;</item>
/// <item>what one of these hands out, a call's result or an event's argument, when the object is of a class of its own library: Microsoft Forms for a
/// UserForm (<c>Controls</c>, an event's <c>ReturnBoolean</c>), VB for a Visual Basic 6.0 form, the control's own library for a control of another one. The
/// class comes only from the live object's server (<see cref="IAutomationServer.ClassNameOf"/>), never from a workspace's description. An object of no
/// library's class is one only when it is the form host's own (<see cref="IAutomationServer.IsFormHostObject"/>): the RDForms <c>Controls</c> collection and
/// menu arrays. A COM object whose server describes no class is an ordinary one;</item>
/// <item>an object the session already held keeps what it was: a COM object stored in <c>Form1.Tag</c>, or given to a form's property, stays an ordinary
/// one.</item>
/// </list>
/// Creating an object (<c>New</c>, <c>CreateObject</c>, <c>GetObject</c>) is never one of them, of a control's class or not: only a form makes a control.
/// <para>
/// Each object is kept with the handle it was bound to, and is the platform's only while it is still bound to that handle: an identity used again for another
/// object is not one. The set goes with the program: an <c>End</c>, and an error nothing handled, wipe it (<see cref="SessionWipe.End"/>).
/// </para>
/// </remarks>
internal sealed class PlatformObjects
{
    private static readonly ConditionalWeakTable<IRuntimeSession, PlatformObjects> Sessions = [];

    // the library a Visual Basic 6.0 form's classes are of; a UserForm's are Microsoft Forms' (FormDesigner.MSFormsLibrary).
    private const string VisualBasicLibrary = "VB";

    private readonly object _gate = new();
    private readonly Dictionary<VBRuntimeObjectId, Kept> _objects = [];

    // the object a call the platform answers is made on, and the member called, while it is made: what the call hands out is the platform's when that object
    // is (rules 2 and 3).
    private (VBRuntimeObjectId Receiver, string? Member)? _answering;

    // an object of the platform's: its handle, its library and class, and for a form, the names of the controls its designer declares.
    private sealed record Kept(object Handle, string Library, string? ClassName, IReadOnlySet<string>? Controls = null);

    /// <summary>
    /// The platform's objects of a session.
    /// </summary>
    /// <param name="session">The session.</param>
    public static PlatformObjects Of(IRuntimeSession session) => Sessions.GetValue(session, _ => new PlatformObjects());

    /// <summary>
    /// A form the form host made for the instance <paramref name="form"/> of a form's class, bound to <paramref name="handle"/> (rule 1).
    /// </summary>
    /// <param name="form">The identity of the instance of the form's class.</param>
    /// <param name="handle">The form object the form host made for it.</param>
    /// <param name="designer">What the form's designer declares: its kind (a Visual Basic 6.0 form's classes are of VB, a UserForm's of Microsoft Forms) and
    /// its controls, which are the platform's whatever their library.</param>
    public void AdoptForm(VBRuntimeObjectId form, object handle, FormDescriptor designer)
    {
        var controls = designer.Controls.SelectMany(control => control.DescendantsAndSelf()).Select(control => control.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        lock (_gate)
        {
            var library = designer.Kind == FormDesignerKind.VisualBasic6 ? VisualBasicLibrary : FormDesigner.MSFormsLibrary;
            _objects[form] = new Kept(handle, library, null, controls);
        }
    }

    /// <summary>
    /// Whether the platform answers <paramref name="request"/>: a member a form's designer declares, or a member of one of the platform's objects, a
    /// <c>Controls.Add</c> of a class of any library included. Never a creation or an obtaining.
    /// </summary>
    /// <param name="session">The session the call is made in.</param>
    /// <param name="request">The call.</param>
    public bool Answers(IRuntimeSession session, ExternalCallRequest request)
    {
        if (request.IsCreation || request.Obtains is not null)
        {
            return false;
        }

        if (FormObject.IsDeclaredBy(request.Member))
        {
            return true;
        }

        // a Controls.Add makes a control on the form, of whichever library its ProgID names: the form makes it, as it makes the controls its designer
        // declares.
        return ReceiverOf(request) is { } receiver && TryGet(session, receiver, out _);
    }

    /// <summary>
    /// Makes the call <paramref name="request"/> the one being made, until the result is disposed of: what it hands out is the platform's when the platform
    /// answers it. A call made inside it (an event's handler, a nested call) is made the one being made in its turn, and this one is again once it returns.
    /// </summary>
    /// <param name="request">The call, its <see cref="ExternalCallRequest.IsAnsweredByPlatform"/> already said.</param>
    public Scope Answering(ExternalCallRequest request)
    {
        lock (_gate)
        {
            var outer = _answering;
            _answering = request.IsAnsweredByPlatform && ReceiverOf(request) is { } receiver ? (receiver, request.Member.Name) : null;
            return new Scope(this, outer);
        }
    }

    /// <summary>
    /// An object a server returned, which the session bound to <paramref name="identity"/> now: the platform's when the call that returned it (or the object
    /// <paramref name="handedOutBy"/>, which raised the event that handed it over) is, and its class is of the same library (rules 2 and 3).
    /// </summary>
    /// <param name="session">The session.</param>
    /// <param name="server">The server that holds the object, which says its class.</param>
    /// <param name="identity">The identity the session bound the object to.</param>
    /// <param name="handle">The server's handle of the object.</param>
    /// <param name="handedOutBy">The object whose event handed it over; <see langword="null"/> for a call's result.</param>
    public void HandedOut(IRuntimeSession session, IAutomationServer server, VBRuntimeObjectId identity, object handle, VBRuntimeObjectId? handedOutBy = null)
    {
        (VBRuntimeObjectId Receiver, string? Member)? source;
        lock (_gate)
        {
            source = handedOutBy is { } raiser ? (raiser, null) : _answering;
        }

        if (source is not { } from || !TryGet(session, from.Receiver, out var kept))
        {
            return;
        }

        // the class is the live object's, as its server says: "MSForms.Label", "VB.Label", "SHDocVw.WebBrowser".
        var className = server.ClassNameOf(handle);

        // a control on the form, which its designer declares or a Controls.Add of the form's makes, is the platform's whatever its library: of its own
        // library, for what it hands out in its turn (rule 2).
        if (from.Member is { } member && (kept.Controls?.Contains(member) == true || IsControlsAdd(member, kept.ClassName)))
        {
            Adopt(identity, handle, LibraryOf(className) ?? kept.Library, className);
            return;
        }

        // anything else is the platform's when it is of the same library. An object of no library's class is the platform's only when the form host made it;
        // a COM object whose server describes no class is an ordinary one (rule 3).
        if (className is null ? server.IsFormHostObject(handle) : className.StartsWith(kept.Library + ".", StringComparison.OrdinalIgnoreCase))
        {
            Adopt(identity, handle, kept.Library, className);
        }
    }

    /// <summary>
    /// The enumerator a server returned for a <c>For Each</c>, which the session bound to <paramref name="identity"/> now: the platform's when the collection
    /// it enumerates is (rule 3). It is of no library's class, the standard library's <c>IEnumVARIANT</c>; what it hands out is judged by its own class.
    /// </summary>
    /// <param name="session">The session.</param>
    /// <param name="identity">The identity the session bound the enumerator to.</param>
    /// <param name="handle">The server's handle of the enumerator.</param>
    public void EnumeratorHandedOut(IRuntimeSession session, VBRuntimeObjectId identity, object handle)
    {
        (VBRuntimeObjectId Receiver, string? Member)? source;
        lock (_gate)
        {
            source = _answering;
        }

        if (source is { } from && TryGet(session, from.Receiver, out var kept))
        {
            Adopt(identity, handle, kept.Library, className: null);
        }
    }

    /// <summary>
    /// Forgets every object: the program ended (<see cref="SessionWipe.End"/>).
    /// </summary>
    public void Clear()
    {
        lock (_gate)
        {
            _objects.Clear();
            _answering = null;
        }
    }

    private void Adopt(VBRuntimeObjectId identity, object handle, string library, string? className)
    {
        lock (_gate)
        {
            _objects[identity] = new Kept(handle, library, className);
        }
    }

    // whether identity is one of the platform's: kept, and still bound to the handle it was kept with.
    private bool TryGet(IRuntimeSession session, VBRuntimeObjectId identity, out Kept kept)
    {
        lock (_gate)
        {
            if (!_objects.TryGetValue(identity, out kept!))
            {
                return false;
            }
        }

        if (!session.ExternalObjects.TryGet(identity, out _, out var handle) || !ReferenceEquals(handle, kept.Handle))
        {
            lock (_gate)
            {
                _ = _objects.Remove(identity);
            }

            return false;
        }

        return true;
    }

    private static VBRuntimeObjectId? ReceiverOf(ExternalCallRequest request)
        => request.Arguments is [VBRuntimeValue<VBRuntimeObjectId> { StoredValue: var receiver }, ..] ? receiver : null;

    // a Controls collection's Add: Microsoft Forms' (a class of its own), or RDForms' (of no library's class).
    private static bool IsControlsAdd(string member, string? className)
        => string.Equals(member, "Add", StringComparison.OrdinalIgnoreCase)
        && (className is null || className.EndsWith(".Controls", StringComparison.OrdinalIgnoreCase));

    // the library of a class the server names, "SHDocVw" of "SHDocVw.WebBrowser"; none for a class it does not name, or names without one.
    private static string? LibraryOf(string? className)
        => className is { } name && name.IndexOf('.') is > 0 and var dot ? name[..dot] : null;

    /// <summary>
    /// The call being made (<see cref="Answering"/>), until it is over.
    /// </summary>
    /// <param name="objects">The platform's objects of the session.</param>
    /// <param name="outer">The receiver and member of the call being made before this one, which is the call being made again once this one
    /// is over.</param>
    public readonly struct Scope(PlatformObjects objects, (VBRuntimeObjectId Receiver, string? Member)? outer) : IDisposable
    {
        /// <inheritdoc/>
        public void Dispose()
        {
            lock (objects._gate)
            {
                objects._answering = outer;
            }
        }
    }
}
