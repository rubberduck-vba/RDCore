using RDCore.External.Automation;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Types.Complex;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Model.Values.Runtime;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.StdLib;

namespace RDCore.Runtime.Execution.External.Automation;

/// <summary>
/// The objects a server holds for a session, as the language holds them: an identity, and an instance of a class of a referenced library.
/// </summary>
/// <remarks>
/// What a call that returns an object and an event that hands one over have in common. The same server object is the same object to the program, which is what <c>Is</c> asks.
/// </remarks>
/// <param name="session">The session whose objects these are.</param>
/// <param name="server">The server that holds them, and the owner of them in the session.</param>
internal sealed class ServerObjects(IRuntimeSession session, IAutomationServer server)
{
    /// <summary>
    /// The handle behind an object, when the server holds it.
    /// </summary>
    public object? HandleOf(VBRuntimeObjectId identity)
        => session.ExternalObjects.TryGet(identity, out var owner, out var handle) && ReferenceEquals(owner, server) ? handle : null;

    /// <summary>
    /// What the server is given for an object: its handle, for one it holds; for an object of the session's own - an object of a class module, a
    /// <c>Collection</c> - the stand-in its side makes for it, as MS-VBA hands one to a COM server (<see cref="SessionObjectHost"/>). <see langword="null"/>
    /// for an object of another server's.
    /// </summary>
    public object? ToServer(VBRuntimeObjectId identity)
        => session.ExternalObjects.TryGet(identity, out var owner, out var handle)
            ? ReferenceEquals(owner, server) ? handle : null
            : session.Symbols.TryGetInstance(identity, out _) ? SessionObjectHost.Of(session).Export(identity, server) : null;

    /// <summary>
    /// The object the language holds for an object a server returned.
    /// </summary>
    /// <param name="value">The handle of the object.</param>
    /// <param name="declared">The type it was declared to be.</param>
    /// <param name="resolver">Finds the classes of the referenced libraries.</param>
    /// <remarks>
    /// An object whose class no referenced library declares is a late-bound one, whose members are reached by name (<see cref="LateBinding"/>): what
    /// <c>CreateObject</c> gives for a class the project does not reference, and what a member of one returns.
    /// </remarks>
    public VBTypedValue Wrap(object value, VBType declared, ISymbolResolver resolver)
    {
        // an object of the session the server held is the session's object again: `d("key") Is w`.
        if (value is ISessionObjectStandIn standIn && SessionObjectHost.Of(session).TryFind(standIn, out var own))
        {
            return new VBObjectValue(own);
        }

        if (session.ExternalObjects.TryFind(server, value, out var existing))
        {
            return new VBObjectValue(existing);
        }

        var classModule = ClassOf(value, declared, resolver) ?? LateBinding.ClassNamed(server.ClassNameOf(value));

        var identity = session.Objects.CreateObject();
        session.Symbols.CreateInstance(identity, classModule);
        session.ExternalObjects.Bind(identity, server, value);
        return new VBObjectValue(identity);
    }

    /// <summary>
    /// The object the language holds for an enumerator a server returned: one of the standard library's <c>IEnumVARIANT</c>.
    /// </summary>
    public VBTypedValue WrapEnumerator(object enumerator, ISymbolResolver resolver)
    {
        var classModule = Current(resolver, StdLibSymbolProvider.LibraryName, "IEnumVARIANT")
            ?? throw new AutomationException(unchecked((int)0x80020005), "The standard library has no enumerator to enumerate with.");

        var identity = session.Objects.CreateObject();
        session.Symbols.CreateInstance(identity, classModule);
        session.ExternalObjects.Bind(identity, server, enumerator);
        return new VBObjectValue(identity);
    }

    /// <summary>
    /// Lets go of an object that was made for a moment and that nothing holds: the one a call or an event handed over, and that a program did not keep.
    /// </summary>
    /// <param name="identity">The identity of the object.</param>
    public void Discard(VBRuntimeObjectId identity)
    {
        if (session.Objects.RefCount(identity) != 0 || HandleOf(identity) is null)
        {
            return;
        }

        session.ExternalObjects.Release(identity);
        _ = session.Symbols.DestroyInstance(identity);
        _ = session.Objects.TryRemoveObject(identity);
    }

    // The class an object is an instance of: the one its member declares to return, as the project has it now, or - for a member that is declared to return an
    // Object, as `ActiveSheet` is - the one the server says it is, which is how late binding finds the members.
    private VBClassModuleSymbol? ClassOf(object value, VBType declared, ISymbolResolver resolver)
    {
        if (declared is VBClassType { Symbol: { } named })
        {
            var declaredClass = Current(resolver, named.GetProperty(SymbolProperties.Library), named.Name) ?? named;

            // a class's default interface is no class an object is of: the object is of the class (an MSForms.IReturnBoolean is a ReturnBoolean), which is one,
            // and which a variable of the class holds too.
            if (declaredClass.GetProperty(SymbolProperties.DefaultInterfaceOf) is { Length: > 0 } coclass
                && Current(resolver, declaredClass.GetProperty(SymbolProperties.Library), coclass) is { } ofClass)
            {
                declaredClass = ofClass;
            }

            // the extender MS-VBA wraps a form's control in (MSForms.Control) is what Controls.Add and Controls(i) are declared to return, and no object's class: the
            // object is the control it wraps - a CheckBox - which is what TypeName says and what a variable of the control's class holds (oracle\s41 C01, C04, C11).
            return IsControlExtender(declaredClass) && ServerClassOf(value, resolver) is { } control ? control : declaredClass;
        }

        return ServerClassOf(value, resolver);
    }

    private static bool IsControlExtender(VBClassModuleSymbol classModule)
        => classModule.Name == SDK.Semantics.Static.FormControlExtender.ClassName
            && classModule.GetProperty(SymbolProperties.Library) == SDK.Workspace.Forms.FormDesigner.MSFormsLibrary;

    // the class the server says the object is, among the classes the project's libraries declare.
    private VBClassModuleSymbol? ServerClassOf(object value, ISymbolResolver resolver)
    {
        if (server.ClassNameOf(value) is not { Length: > 0 } qualified)
        {
            return null;
        }

        // `Excel._Worksheet`: the interface of a class is named for it, with a leading underscore the library's description leaves out.
        var dot = qualified.IndexOf('.');
        var library = dot < 0 ? null : qualified[..dot];
        var className = (dot < 0 ? qualified : qualified[(dot + 1)..]).TrimStart('_');
        return Current(resolver, library, className);
    }

    /// <summary>
    /// Whether an object of a server is an instance of a class of a library, as the object says (<see cref="IAutomationServer.IsInstanceOf"/>): what
    /// <c>TypeOf ... Is</c> and Set-coercion ask of it (<strong>MS-VBAL §5.6.7, §5.5.2.2</strong>).
    /// </summary>
    /// <remarks>
    /// A form's control is the extender its form wraps it in (<c>MSForms.Control</c>) too, which is no class its own class implements: only the object knows.
    /// <see langword="null"/> for an object of the session's own, a class of no library, and an object its server cannot tell of.
    /// </remarks>
    /// <param name="session">The session that holds the object.</param>
    /// <param name="identity">The object.</param>
    /// <param name="target">The class it is asked of.</param>
    public static bool? IsInstanceOf(IRuntimeSession session, VBRuntimeObjectId identity, VBClassModuleSymbol target)
    {
        if (!session.ExternalObjects.TryGet(identity, out var owner, out var handle)
            || owner is not IAutomationServer server
            || target.GetProperty(SymbolProperties.Library) is not { Length: > 0 } library)
        {
            return null;
        }

        // the program waits for the server, as it does for a call: an event the server raises meanwhile - one a form's message raised - is handled while it does,
        // or the two would wait for each other.
        using (session.Turn.Yield())
        {
            return server.IsInstanceOf(handle, library, target.Name);
        }
    }

    /// <summary>
    /// The class of a library by its name, as the project has it now.
    /// </summary>
    public static VBClassModuleSymbol? Current(ISymbolResolver resolver, string? library, string name)
        => VBProjectSymbol.ResolveQualifiedType(resolver, library, name, StaticSymbol.GlobalUri).Symbol as VBClassModuleSymbol;
}
