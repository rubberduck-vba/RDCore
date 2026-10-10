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
    /// The object the language holds for an object a server returned.
    /// </summary>
    /// <param name="value">The handle of the object.</param>
    /// <param name="declared">The type it was declared to be.</param>
    /// <param name="resolver">Finds the classes of the referenced libraries.</param>
    /// <exception cref="AutomationException">The class of the object is not one of the libraries'.</exception>
    public VBTypedValue Wrap(object value, VBType declared, ISymbolResolver resolver)
    {
        if (session.ExternalObjects.TryFind(server, value, out var existing))
        {
            return new VBObjectValue(existing);
        }

        var classModule = ClassOf(value, declared, resolver)
            ?? throw new AutomationException(unchecked((int)0x80020005), "The class of an object the server returned is not one of the referenced libraries'.");

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
            return Current(resolver, named.GetProperty(SymbolProperties.Library), named.Name) ?? named;
        }

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
    /// The class of a library by its name, as the project has it now.
    /// </summary>
    public static VBClassModuleSymbol? Current(ISymbolResolver resolver, string? library, string name)
        => VBProjectSymbol.ResolveQualifiedType(resolver, library, name, StaticSymbol.GlobalUri).Symbol as VBClassModuleSymbol;
}
