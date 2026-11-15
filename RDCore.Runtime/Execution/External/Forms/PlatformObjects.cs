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
/// The objects of a session that are the platform's own although a server holds them: a program's forms, Visual Basic 6.0 forms and UserForms alike, and what
/// they hand out of their own library. A call on one is answered by the platform (<see cref="ExternalCallRequest.IsAnsweredByPlatform"/>), which the policy over
/// automation lets through (<see cref="AutomationPolicyInterceptor"/>): a form is the program's own user interface, even when it is a COM object of the machine's.
/// </summary>
/// <remarks>
/// An object is one by where the program got it, never by a library description, which a workspace can bring its own of:
/// <list type="number">
/// <item>the form the form host made for an instance of a form's class (<see cref="FormCallProvider"/>);</item>
/// <item>what a member the form's designer declares returns, a control (<c>UserForm1.Label1</c>);</item>
/// <item>what one of these hands out, a call's result or an event's argument, when the object is of a class of the form's own library: Microsoft Forms for a
/// UserForm (<c>Controls</c>, <c>Controls.Add "Forms.Label.1"</c>, an event's <c>ReturnBoolean</c>), VB for a Visual Basic 6.0 form. The class comes only from
/// the live object's server (<see cref="IAutomationServer.ClassNameOf"/>), never from a workspace's description. An object its server describes no class of is
/// one too: the RDForms <c>Controls</c> collection and menu arrays, which no library declares;</item>
/// <item>an object the session already held keeps what it was: a COM object stored in <c>Form1.Tag</c>, or given to a form's property, stays an ordinary one.</item>
/// </list>
/// A control of another library on a form (a <c>WebBrowser</c>, a <c>ListView</c>) is an ordinary object, and so is what it hands out; so is the control a
/// <c>Controls.Add</c> of another library's class makes, which is a creation of another COM class and is not answered here. Creating an object (<c>New</c>,
/// <c>CreateObject</c>, <c>GetObject</c>) never is.
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
    private readonly Dictionary<VBRuntimeObjectId, (object Handle, string Library, string? ClassName)> _objects = [];

    // the object a call the platform answers is made on, while it is made: what the call hands out is the platform's when that object is (rule 3).
    private VBRuntimeObjectId? _answering;

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
    /// <param name="kind">The form's designer: a Visual Basic 6.0 form's classes are of VB, a UserForm's of Microsoft Forms.</param>
    public void AdoptForm(VBRuntimeObjectId form, object handle, FormDesignerKind kind)
        => Adopt(form, handle, kind == FormDesignerKind.VisualBasic6 ? VisualBasicLibrary : FormDesigner.MSFormsLibrary, className: null);

    /// <summary>
    /// Whether the platform answers <paramref name="request"/>: a member a form's designer declares, or a member of one of the platform's objects. Never a
    /// creation, an obtaining, or a <c>Controls.Add</c> of a class of another library.
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

        if (ReceiverOf(request) is not { } receiver || !TryGet(session, receiver, out var library, out var className))
        {
            return false;
        }

        // Controls.Add makes a control of the class its ProgID names: of the form's own library ("Forms.Label.1", "VB.Label"), or of another one, which is
        // creating an object of that one's.
        return !IsControlsAdd(request, className) || ProgIdOf(request) is { } progId && progId.StartsWith(ProgIdPrefixOf(library), StringComparison.OrdinalIgnoreCase);
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
            _answering = request.IsAnsweredByPlatform ? ReceiverOf(request) : null;
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
        VBRuntimeObjectId? source;
        lock (_gate)
        {
            source = handedOutBy ?? _answering;
        }

        if (source is not { } from || !TryGet(session, from, out var library, out _))
        {
            return;
        }

        // the class is the live object's, as its server says: "MSForms.Label", "VB.Label"; none for an object of no library's class.
        var className = server.ClassNameOf(handle);
        if (className is null || className.StartsWith(library + ".", StringComparison.OrdinalIgnoreCase))
        {
            Adopt(identity, handle, library, className);
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
        VBRuntimeObjectId? source;
        lock (_gate)
        {
            source = _answering;
        }

        if (source is { } from && TryGet(session, from, out var library, out _))
        {
            Adopt(identity, handle, library, className: null);
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
            _objects[identity] = (handle, library, className);
        }
    }

    // whether identity is one of the platform's: kept, and still bound to the handle it was kept with.
    private bool TryGet(IRuntimeSession session, VBRuntimeObjectId identity, out string library, out string? className)
    {
        (library, className) = (string.Empty, null);
        (object Handle, string Library, string? ClassName) kept;
        lock (_gate)
        {
            if (!_objects.TryGetValue(identity, out kept))
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

        (library, className) = (kept.Library, kept.ClassName);
        return true;
    }

    private static VBRuntimeObjectId? ReceiverOf(ExternalCallRequest request)
        => request.Arguments is [VBRuntimeValue<VBRuntimeObjectId> { StoredValue: var receiver }, ..] ? receiver : null;

    // a Controls collection's Add: Microsoft Forms' (a class of its own), or RDForms' (of no library's class).
    private static bool IsControlsAdd(ExternalCallRequest request, string? className)
        => string.Equals(request.Member.Name, "Add", StringComparison.OrdinalIgnoreCase)
        && (className is null || className.EndsWith(".Controls", StringComparison.OrdinalIgnoreCase));

    // the ProgID Controls.Add is given, its first argument: a String parameter's, or the first of the arguments a late-bound Add collects (the RDForms Controls
    // collection is of no library's class, and its members are reached by name).
    private static string? ProgIdOf(ExternalCallRequest request)
    {
        if (request.Arguments is not [_, var given, ..])
        {
            return null;
        }

        var first = RuntimeProcedureInvoker.GetParameters(request.Member) is [_, ParamArrayParameterSymbol collecting, ..]
            ? collecting.ResolvedType.CreateValue(new ValueBindingHandle(given)) is VBArrayValue { Length: > 0 } collected ? collected.ElementAt(0) : null
            : VBStringType.TypeInfo.CreateValue(new ValueBindingHandle(given));
        return first switch
        {
            VBStringValue { Value: var text } => text,
            VBVariantValue { TypedValue: VBStringValue { Value: var text } } => text,
            _ => null,
        };
    }

    // the ProgIDs of a library's controls: Microsoft Forms' are "Forms.<Class>.1", Visual Basic 6.0's "VB.<Class>".
    private static string ProgIdPrefixOf(string library)
        => string.Equals(library, VisualBasicLibrary, StringComparison.OrdinalIgnoreCase) ? "VB." : "Forms.";

    /// <summary>
    /// The call being made (<see cref="Answering"/>), until it is over.
    /// </summary>
    /// <param name="objects">The platform's objects of the session.</param>
    /// <param name="outer">The receiver of the call being made before this one, which is again once this one is over.</param>
    public readonly struct Scope(PlatformObjects objects, VBRuntimeObjectId? outer) : IDisposable
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
