using NSubstitute;
using RDCore.Runtime.Execution;
using RDCore.SDK.Model;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Values.Bindings;
using RDCore.SDK.Runtime;
using RDCore.SDK.Runtime.Abstract.Execution;

namespace RDCore.Tests.Runtime;

/// <summary>
/// Characterization matrix for <see cref="IRuntimeSession.ReleaseReference"/> — the only correct way
/// to drop an object reference: it bundles <c>ISessionObjects.RemoveRef</c>/<c>TryRemoveObject</c>
/// with <c>ISessionSymbols.DestroyInstance</c> so a reference count reaching zero can never leave an
/// instance's field storage allocated forever.
/// </summary>
[TestClass]
public sealed class RuntimeSessionReleaseReferenceTests
{
    private static readonly Uri Root = new("file://rdcore-test");
    private static readonly SourceRange R = SourceRange.Empty;

    private sealed class Provider(Symbol[] symbols) : ISymbolProvider
    {
        public IEnumerable<Symbol> ProvideSymbols() => symbols;
    }

    private static IRuntimeSession ComposeSession(params Symbol[] symbols)
        => RuntimeSessionComposer.Compose(new RuntimeEnvironmentProfile(Is64Bit: true, 0, 1252, false), new Provider(symbols));

    private static VBClassModuleSymbol ClassModule(string name = "Class1") => new(Root, Root, name);

    private static VBInstanceFieldVariableMemberSymbol Field(Uri moduleUri, string name)
        => new(Root, moduleUri, name, R, R, VBLongType.TypeInfo, AccessModifier.Implicit);

    private static IBindingHandle Handle() => Substitute.For<IBindingHandle>();

    [TestMethod]
    public void ReleaseReference_LastReference_DestroysTheObject_AndFreesItsInstanceStorage()
    {
        var classModule = ClassModule();
        var field = Field(classModule.Uri, "State");
        var session = ComposeSession(classModule, field);
        var objectId = session.Objects.CreateObject();
        session.Symbols.CreateInstance(objectId, classModule);
        var handle = Handle();
        session.Objects.AddRef(objectId, handle);

        var destroyed = session.ReleaseReference(objectId, handle);

        Assert.IsTrue(destroyed);
        Assert.IsFalse(session.Symbols.TryGetInstance(objectId, out _), "the instance's storage must be freed, not just forgotten by Objects");
    }

    [TestMethod]
    public void ReleaseReference_OneOfSeveralReferences_DoesNotDestroyTheObject()
    {
        var classModule = ClassModule();
        var session = ComposeSession(classModule);
        var objectId = session.Objects.CreateObject();
        session.Symbols.CreateInstance(objectId, classModule);
        var first = Handle();
        var second = Handle();
        session.Objects.AddRef(objectId, first);
        session.Objects.AddRef(objectId, second);

        var destroyed = session.ReleaseReference(objectId, first);

        Assert.IsFalse(destroyed);
        Assert.IsTrue(session.Symbols.TryGetInstance(objectId, out _), "still referenced by 'second' - must stay alive");
    }

    [TestMethod]
    public void ReleaseReference_UnreferencedObject_DestroysIt()
        // CreateObject starts with zero roots - releasing a reference that was never added still
        // finds the count already at zero and tears the (never-referenced) object down.
    {
        var classModule = ClassModule();
        var session = ComposeSession(classModule);
        var objectId = session.Objects.CreateObject();
        session.Symbols.CreateInstance(objectId, classModule);

        var destroyed = session.ReleaseReference(objectId, Handle());

        Assert.IsTrue(destroyed);
        Assert.IsFalse(session.Symbols.TryGetInstance(objectId, out _));
    }

    [TestMethod]
    public void ReleaseReference_UnknownObject_ReturnsFalse_DoesNotThrow()
        => Assert.IsFalse(ComposeSession().ReleaseReference(new(), Handle()));
}
