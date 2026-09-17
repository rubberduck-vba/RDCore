using RDCore.Runtime.Execution;
using RDCore.SDK.Model;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Values.Runtime;
using RDCore.SDK.Runtime;
using RDCore.SDK.Runtime.Abstract.Execution;

namespace RDCore.Tests.Runtime;

/// <summary>
/// Characterization matrix for <see cref="ISessionSymbols.CreateInstance"/>/<c>TryGetInstance</c>/
/// <c>DestroyInstance</c> — the "live object" half of
/// <c>SessionSymbolsStorageAllocationTests.InstanceField_IsNotAllocated_NeedsALiveObjectFirst</c>:
/// once an object exists, its class module's instance fields become addressable storage, independent
/// per instance, exactly as a procedure's locals are independent per call-stack activation.
/// </summary>
[TestClass]
public sealed class SessionSymbolsObjectInstanceTests
{
    private static readonly Uri Root = new("file://rdcore-test");
    private static readonly SourceRange R = SourceRange.Empty;

    private sealed class Provider(Symbol[] symbols) : ISymbolProvider
    {
        public IEnumerable<Symbol> ProvideSymbols() => symbols;
    }

    private static ISessionSymbols Compose(params Symbol[] symbols)
        => RuntimeSessionComposer.Compose(
            new RuntimeEnvironmentProfile(Is64Bit: true, 0, 1252, false), new Provider(symbols)).Symbols;

    private static VBClassModuleSymbol ClassModule(string name = "Class1") => new(Root, Root, name);

    private static VBInstanceFieldVariableMemberSymbol Field(Uri moduleUri, string name)
        => new(Root, moduleUri, name, R, R, VBLongType.TypeInfo, AccessModifier.Implicit);

    [TestMethod]
    public void CreateInstance_AllocatesEveryDeclaredInstanceField_ReadableThroughTheInstance()
    {
        var classModule = ClassModule();
        var field = Field(classModule.Uri, "State");
        var symbols = Compose(classModule, field);

        var instance = symbols.CreateInstance(new VBRuntimeObjectId(), classModule);

        Assert.AreEqual(VBLongType.TypeInfo.DefaultValue.Handle, instance.GetValue(field));
    }

    [TestMethod]
    public void CreateInstance_UnrelatedFieldOnAnotherClass_IsNotAllocatedOnThisInstance()
    {
        var classModule = ClassModule("Class1");
        var otherClassModule = ClassModule("Class2");
        var ownField = Field(classModule.Uri, "State");
        var otherField = Field(otherClassModule.Uri, "Other");
        var symbols = Compose(classModule, otherClassModule, ownField, otherField);

        var instance = symbols.CreateInstance(new VBRuntimeObjectId(), classModule);

        Assert.IsFalse(instance.TryResolve(otherField, out _));
    }

    [TestMethod]
    public void CreateInstance_TwoInstancesOfTheSameClass_GetIndependentBindingsForTheSameField()
    {
        var classModule = ClassModule();
        var field = Field(classModule.Uri, "State");
        var symbols = Compose(classModule, field);

        var first = symbols.CreateInstance(new VBRuntimeObjectId(), classModule);
        var second = symbols.CreateInstance(new VBRuntimeObjectId(), classModule);

        Assert.AreNotSame(first.GetValue(field), second.GetValue(field));
    }

    [TestMethod]
    public void TryGetInstance_ReturnsTheSameInstance_ForItsObjectId()
    {
        var classModule = ClassModule();
        var symbols = Compose(classModule);
        var objectId = new VBRuntimeObjectId();
        var created = symbols.CreateInstance(objectId, classModule);

        Assert.IsTrue(symbols.TryGetInstance(objectId, out var found));

        Assert.AreSame(created, found);
    }

    [TestMethod]
    public void TryGetInstance_UnknownObjectId_ReturnsFalse()
    {
        var symbols = Compose(ClassModule());
        Assert.IsFalse(symbols.TryGetInstance(new VBRuntimeObjectId(), out _));
    }

    [TestMethod]
    public void DestroyInstance_FreesStorage_AndForgetsTheInstance()
    {
        var classModule = ClassModule();
        var field = Field(classModule.Uri, "State");
        var symbols = Compose(classModule, field);
        var objectId = new VBRuntimeObjectId();
        var instance = symbols.CreateInstance(objectId, classModule);

        Assert.IsTrue(symbols.DestroyInstance(objectId));

        Assert.IsFalse(instance.TryResolve(field, out _));
        Assert.IsFalse(symbols.TryGetInstance(objectId, out _));
    }

    [TestMethod]
    public void DestroyInstance_UnknownObjectId_ReturnsFalse()
    {
        var symbols = Compose(ClassModule());
        Assert.IsFalse(symbols.DestroyInstance(new VBRuntimeObjectId()));
    }
}
