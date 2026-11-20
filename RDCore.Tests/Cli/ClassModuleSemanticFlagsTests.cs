using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Semantics.Flags;
using static RDCore.Tests.Cli.ModuleWorkspace;

namespace RDCore.Tests.Cli;

/// <summary>
/// What the semantic analysis states about a class module (<see cref="ClassModuleSemanticFlags"/>): whether the class has a default instance (<strong>MS-VBAL
/// §5.2.4.1.2</strong>), and whether its objects hold state - variables declared at module level, which have object extent (<strong>§5.2.3.1</strong>). A
/// default instance that holds none is the usual factory; one that does keeps what its members assign, where every module can reach it. A class that another
/// class of the project names in its <c>Implements</c> directive is implemented (<strong>§5.2.4.2</strong>). A class whose <c>VB_Exposed</c> attribute is
/// <c>True</c> is exposed to the projects that reference its own (<strong>§5.2.4.1.1</strong>).
/// </summary>
[TestClass]
public sealed class ClassModuleSemanticFlagsTests
{
    private static async Task<ModuleSemanticsDto?> ModelOfAsync(string name, params (string Name, string Source)[] classes)
    {
        var payload = await SemanticsAsync(classes, "Attribute VB_Name = \"Program\"\r\nPublic Sub Main()\r\nEnd Sub\r\n");
        return payload.Modules.SingleOrDefault(module => module.Module.Fragment.TrimStart('#') == name);
    }

    [TestMethod]
    public async Task APredeclaredClassThatDeclaresNoVariable_HasAStatelessDefaultInstance()
    {
        var model = await ModelOfAsync("Factory", ("Factory", ClassModule("Factory",
            "Attribute VB_PredeclaredId = True",
            "Option Explicit",
            "Private Const Prefix As String = \"x\"",
            "Public Function Create() As Collection", "    Set Create = New Collection", "End Function")));

        Assert.AreEqual(ClassModuleSemanticFlags.DefaultInstance, model?.Class);
    }

    [TestMethod]
    public async Task APredeclaredClassThatDeclaresAVariable_HasAStatefulDefaultInstance()
    {
        var model = await ModelOfAsync("Thing", ("Thing", ClassModule("Thing",
            "Attribute VB_PredeclaredId = True",
            "Option Explicit",
            "Private Type TThing", "    Value As Long", "End Type",
            "Private This As TThing",
            "Public Property Get Value() As Long", "    Value = This.Value", "End Property")));

        Assert.AreEqual(ClassModuleSemanticFlags.DefaultInstance | ClassModuleSemanticFlags.Stateful, model?.Class);
    }

    [TestMethod]
    public async Task AClassThatIsNotPredeclared_HasNoDefaultInstance_AndItsObjectsHoldItsVariables()
    {
        var model = await ModelOfAsync("Counter", ("Counter", ClassModule("Counter",
            "Option Explicit",
            "Private mValue As Long",
            "Public Sub Increment()", "    mValue = mValue + 1", "End Sub")));

        Assert.AreEqual(ClassModuleSemanticFlags.Stateful, model?.Class);
    }

    // MS-VBA gives each object of the class its own Static local (observed in Excel), where MS-VBAL gives a static local module extent.
    [TestMethod]
    public async Task APredeclaredClassWhoseMemberDeclaresAStaticLocal_HasAStatefulDefaultInstance()
    {
        var model = await ModelOfAsync("Ticker", ("Ticker", ClassModule("Ticker",
            "Attribute VB_PredeclaredId = True",
            "Option Explicit",
            "Public Function Tick() As Long", "    Static n As Long", "    n = n + 1", "    Tick = n", "End Function")));

        Assert.AreEqual(ClassModuleSemanticFlags.DefaultInstance | ClassModuleSemanticFlags.Stateful, model?.Class);
    }

    private static readonly (string, string) Shape = ("IShape", ClassModule("IShape", "Option Explicit", "Public Function Area() As Double", "End Function"));

    [TestMethod]
    public async Task AClassThatAnotherClassImplements_IsImplemented()
    {
        var model = await ModelOfAsync("IShape", Shape, ("Square", ClassModule("Square",
            "Option Explicit",
            "Implements IShape",
            "Private Function IShape_Area() As Double", "    IShape_Area = 4", "End Function")));

        Assert.AreEqual(ClassModuleSemanticFlags.Implemented, model?.Class);
    }

    [TestMethod]
    public async Task AClassThatImplementsAnother_IsNotItselfImplemented()
    {
        var model = await ModelOfAsync("Square", Shape, ("Square", ClassModule("Square",
            "Option Explicit",
            "Implements IShape",
            "Private Function IShape_Area() As Double", "End Function")));

        Assert.AreEqual(ClassModuleSemanticFlags.None, model?.Class);
    }

    // what the Implements directive names is known from the declarations, whether or not the code of the module that writes it compiles.
    [TestMethod]
    public async Task AClassImplementedByAModuleWhoseCodeDoesNotCompile_IsImplemented()
    {
        var model = await ModelOfAsync("IShape", Shape, ("Square", ClassModule("Square",
            "Option Explicit",
            "Implements IShape",
            "Private Function IShape_Area() As Double", "    undeclared = 1", "End Function")));

        Assert.AreEqual(ClassModuleSemanticFlags.Implemented, model?.Class);
    }

    [TestMethod]
    public async Task AClassWhoseExposedAttributeIsTrue_IsExposed()
    {
        var model = await ModelOfAsync("Widget", ("Widget", ClassModule("Widget", "Attribute VB_Exposed = True", "Option Explicit")));

        Assert.AreEqual(ClassModuleSemanticFlags.Exposed, model?.Class);
    }

    [TestMethod]
    public async Task AClassWhoseExposedAttributeIsFalseOrAbsent_IsPrivate()
    {
        var stated = await ModelOfAsync("Widget", ("Widget", ClassModule("Widget", "Attribute VB_Exposed = False", "Option Explicit")));
        var absent = await ModelOfAsync("Gadget", ("Gadget", ClassModule("Gadget", "Option Explicit")));

        Assert.AreEqual(ClassModuleSemanticFlags.None, stated?.Class);
        Assert.AreEqual(ClassModuleSemanticFlags.None, absent?.Class);
    }

    [TestMethod]
    public async Task AStandardModule_IsNoClass()
        => Assert.IsNull((await ModelOfAsync("Program"))?.Class);
}
