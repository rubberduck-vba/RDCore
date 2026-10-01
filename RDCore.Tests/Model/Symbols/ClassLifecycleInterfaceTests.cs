using RDCore.SDK.Model;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Complex;

namespace RDCore.Tests.Model.Symbols;

/// <summary>
/// The interface every class module implicitly implements (<strong>MS-VBAL §5.3.1.10</strong>), and how a class
/// finds what it implements one of its members with (<strong>MS-VBAL §5.3.1.9</strong>).
/// </summary>
[TestClass]
[TestCategory("MS-VBAL 5.3.1.10 Class Module Lifecycle")]
public sealed class ClassLifecycleInterfaceTests
{
    private static readonly Uri Root = new("file://rdcore-test");
    private static readonly SourceRange R = SourceRange.Empty;

    private static VBProcedureMemberSymbol Sub(Uri classUri, string name, AccessModifier access = AccessModifier.Private)
        => new(Root, classUri, name, ScopeKind.Instance, SymbolKindExt.Procedure, VBVoidType.TypeInfo, R, R, access);

    [TestMethod]
    public void TheInterface_HasAnInitializeAndATerminateMember()
    {
        CollectionAssert.AreEquivalent(new[] { "Initialize", "Terminate" }, ClassLifecycleInterface.Interface.Members.Select(member => member.Name).ToArray());
        Assert.AreEqual("Initialize", ClassLifecycleInterface.Initialize.Name);
        Assert.AreEqual("Terminate", ClassLifecycleInterface.Terminate.Name);
    }

    [TestMethod]
    public void EveryClassModule_ImplementsItLikeAnyInterface_WithoutDeclaringIt()
    {
        var widget = new VBClassModuleSymbol(Root, Root, "Widget");

        CollectionAssert.AreEqual(new[] { ClassLifecycleInterface.Interface }, widget.ImplementedInterfaces.ToArray());
        Assert.IsEmpty(widget.ImplementedInterfaceNames, "the source has no Implements directive");
    }

    [TestMethod]
    public void TheInterface_ComesBeforeTheInterfacesTheSourceDeclares()
    {
        var iShape = new VBClassModuleSymbol(Root, Root, "IShape");
        var widget = new VBClassModuleSymbol(Root, Root, "Widget") { ImplementedInterfaces = [iShape] };

        CollectionAssert.AreEqual(new[] { "Class", "IShape" }, widget.ImplementedInterfaces.Select(implemented => implemented.Name).ToArray());
    }

    [TestMethod]
    public void AModuleThatAlreadyListsIt_ImplementsItOnce()
    {
        var widget = new VBClassModuleSymbol(Root, Root, "Widget");
        widget = widget with { ImplementedInterfaces = widget.ImplementedInterfaces };

        Assert.HasCount(1, widget.ImplementedInterfaces);
    }

    [TestMethod]
    public void TheInterfaceItself_DoesNotImplementItself()
        => Assert.IsEmpty(ClassLifecycleInterface.Interface.ImplementedInterfaces);

    [TestMethod]
    public void TheInterface_SaysThatImplementingItIsOptional()
        => Assert.IsTrue(ClassLifecycleInterface.Interface.GetProperty(SymbolProperties.OptionalImplementation));

    [TestMethod]
    public void AnInterfaceTheSourceDeclares_IsNotOptional()
        => Assert.IsFalse(new VBClassModuleSymbol(Root, Root, "IShape").GetProperty(SymbolProperties.OptionalImplementation));

    [TestMethod]
    public void TheInterface_IsASupertypeOfEveryClass_NotOfItself()
    {
        var widget = new VBClassModuleSymbol(Root, Root, "Widget");

        Assert.IsTrue(VBClassType.FromClassModule(widget).Supertypes.OfType<VBClassType>().Any(supertype => supertype.Name == "Class"));
        Assert.IsFalse(VBClassType.FromClassModule(ClassLifecycleInterface.Interface).Supertypes.OfType<VBClassType>().Any());
    }

    [TestMethod]
    public void AClassWithTheHandlers_ImplementsBothMembers()
    {
        var widget = new VBClassModuleSymbol(Root, Root, "Widget");
        var initialize = Sub(widget.Uri, "Class_Initialize");
        var terminate = Sub(widget.Uri, "Class_Terminate");
        widget = widget with { Members = [initialize, terminate, Sub(widget.Uri, "Draw", AccessModifier.Public)] };

        Assert.AreEqual(initialize.Uri, widget.FindImplementation(ClassLifecycleInterface.Interface, ClassLifecycleInterface.Initialize)?.Uri);
        Assert.AreEqual(terminate.Uri, widget.FindImplementation(ClassLifecycleInterface.Interface, ClassLifecycleInterface.Terminate)?.Uri);
    }

    [TestMethod]
    public void AClassWithoutAHandler_DoesNotImplementThatMember()
    {
        var widget = new VBClassModuleSymbol(Root, Root, "Widget");
        widget = widget with { Members = [Sub(widget.Uri, "Class_Initialize")] };

        Assert.IsNull(widget.FindImplementation(ClassLifecycleInterface.Interface, ClassLifecycleInterface.Terminate));
    }

    [TestMethod]
    public void AHandlerIsFoundWhateverTheCaseItIsWrittenIn()
    {
        var widget = new VBClassModuleSymbol(Root, Root, "Widget");
        widget = widget with { Members = [Sub(widget.Uri, "CLASS_initialize")] };

        Assert.IsNotNull(widget.FindImplementation(ClassLifecycleInterface.Interface, ClassLifecycleInterface.Initialize));
    }

    [TestMethod]
    public void AMemberOfAnotherName_IsNotTheHandler()
    {
        var widget = new VBClassModuleSymbol(Root, Root, "Widget");
        widget = widget with { Members = [Sub(widget.Uri, "Initialize"), Sub(widget.Uri, "Widget_Initialize")] };

        Assert.IsNull(widget.FindImplementation(ClassLifecycleInterface.Interface, ClassLifecycleInterface.Initialize));
    }
}
