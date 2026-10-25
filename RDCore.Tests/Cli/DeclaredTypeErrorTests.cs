namespace RDCore.Tests.Cli;

/// <summary>
/// A declared type is a name that resolves to a type (<strong>MS-VBAL §5.6.4</strong>): a module that declares a variable, a parameter or a result of a type
/// nothing declares - a library that is not referenced, a class that is not there - is not valid, and says which name it could not resolve. Nor is one whose
/// type is a procedural module, the library's or the project's: the name resolves, to something that is not a type (<strong>MS-VBAL §5.6.16.7</strong>).
/// </summary>
[TestClass]
[TestCategory("MS-VBAL 5.6.4 Type Binding Context")]
public sealed class DeclaredTypeErrorTests
{
    private static string Program(params string[] lines)
        => $"Attribute VB_Name = \"Program\"\r\n{string.Join("\r\n", lines)}\r\nPublic Sub Main()\r\nEnd Sub\r\n";

    [TestMethod]
    [DataRow("Public Sub Work()", "Dim ws As Excel.Worksheet", "End Sub", "Excel.Worksheet")]
    [DataRow("Public Sub Work()", "Dim ws As Worksheet", "End Sub", "Worksheet")]
    [DataRow("Public Sub Work(ByVal ws As Excel.Range)", "End Sub", "", "Excel.Range")]
    [DataRow("Public Function Work() As Excel.Application", "End Function", "", "Excel.Application")]
    [DataRow("Public Property Get Sheet() As Worksheet", "End Property", "", "Worksheet")]
    [DataRow("Private ws As Excel.Worksheet", "", "", "Excel.Worksheet")]
    [DataRow("Public Sub Work()", "Dim all() As Excel.Worksheet", "End Sub", "Excel.Worksheet")]
    public async Task ATypeNothingDeclares_IsACompileError_AtTheNameTheModuleWrote(string first, string second, string third, string name)
    {
        var errors = await ModuleWorkspace.LoadErrorsAsync([], Program(first, second, third));

        Assert.HasCount(1, errors);
        StringAssert.Contains(errors[0], $"'{name}'");
    }

    [TestMethod]
    public async Task AQualifierThatIsNothingTheProjectCanSee_IsSaidToBeMaybeAMissingReference_AndOneThatIsNot()
    {
        var unknownQualifier = await ModuleWorkspace.LoadErrorsAsync([], Program("Public ws As Excel.Worksheet"));
        var knownQualifier = await ModuleWorkspace.LoadErrorsAsync([], Program("Public ws As Program.Worksheet"));

        StringAssert.Contains(unknownQualifier.Single(), string.Format(RDCore.SDK.Exceptions.VBCompileError_DeclaredTypeQualifierNotResolved_Verbose, "Excel.Worksheet", "Excel"));
        StringAssert.Contains(knownQualifier.Single(), string.Format(RDCore.SDK.Exceptions.VBCompileError_DeclaredTypeNotResolved_Verbose, "Program.Worksheet"),
            "Program is a module of the project: it is the type that is not there, and no reference is missing");
    }

    [TestMethod]
    [DataRow("Public Sub Work()", "Dim s As Strings", "End Sub", "Strings", DisplayName = "A local")]
    [DataRow("Public Sub Work()", "Static s As Strings", "End Sub", "Strings", DisplayName = "A Static local")]
    [DataRow("Public Sub Work()", "Dim s() As Strings", "End Sub", "Strings", DisplayName = "An array")]
    [DataRow("Public Sub Work()", "ReDim s(1) As Strings", "End Sub", "Strings", DisplayName = "An array a ReDim declares")]
    [DataRow("Public Sub Work()", "Dim s As New Strings", "End Sub", "Strings", DisplayName = "As New")]
    [DataRow("Public Sub Work(ByVal s As Strings)", "End Sub", "", "Strings", DisplayName = "A parameter")]
    [DataRow("Public Function Work() As Strings", "End Function", "", "Strings", DisplayName = "A function's result")]
    [DataRow("Public Property Get Work() As Strings", "End Property", "", "Strings", DisplayName = "A property's result")]
    [DataRow("Private s As Strings", "", "", "Strings", DisplayName = "A variable of the module")]
    [DataRow("Private Type TRecord", "s As Strings", "End Type", "Strings", DisplayName = "A field of a user-defined type")]
    [DataRow("Private Type TRecord", "s As VBA.Strings", "End Type", "VBA.Strings", DisplayName = "A field, qualified by its library")]
    [DataRow("Private Type TRecord", "m As Program", "End Type", "Program", DisplayName = "A field of the module itself")]
    [DataRow("Private Const c As Strings = 1", "", "", "Strings", DisplayName = "A constant of the module")]
    [DataRow("Public Sub Work()", "Const c As Strings = 1", "End Sub", "Strings", DisplayName = "A local constant")]
    [DataRow("Public Sub Work()", "Const c As VBA.Strings = 1", "End Sub", "VBA.Strings", DisplayName = "A local constant, qualified by its library")]
    [DataRow("Public Sub Work()", "Const c As Program = 1", "End Sub", "Program", DisplayName = "A local constant of the module itself")]
    [DataRow("Private Declare PtrSafe Function GetTickCount Lib \"kernel32\" () As Strings", "", "", "Strings", DisplayName = "An external function's result")]
    [DataRow("Public Sub Work()", "Dim s As VBA.Strings", "End Sub", "VBA.Strings", DisplayName = "Qualified by its library")]
    [DataRow("Public Sub Work()", "Dim m As Program", "End Sub", "Program", DisplayName = "The module itself")]
    [DataRow("Public Property Let Work(ByVal v As Long)", "Dim s As Strings", "End Property", "Strings", DisplayName = "A Property Let's local")]
    [DataRow("Public Property Set Work(ByVal v As Object)", "Dim s As Strings", "End Property", "Strings", DisplayName = "A Property Set's local")]
    [DataRow("Public Sub Work()", "Dim a() As Variant: ReDim a(1) As Strings", "End Sub", "Strings", DisplayName = "The type a ReDim states")]
    [DataRow("Public Sub Work()", "Dim a() As Variant: ReDim a(1) As Program", "End Sub", "Program", DisplayName = "The type a ReDim states, the module itself")]
    public async Task AProceduralModule_IsNotAType_WhereverADeclarationNamesOne(string first, string second, string third, string name)
    {
        // the type binding context binds a procedural module (MS-VBAL §5.6.10), so the name resolved: the declaration loaded, and the variable failed with
        // error 13 when it was assigned. A procedural module is not a type, so the declaration is not valid. MS-VBA says so in two ways, Excel-verified
        // (oracle\s29): "A module is not a valid type" of the project's, "Automation type not supported in Visual Basic" of a library's.
        var errors = await ModuleWorkspace.LoadErrorsAsync([], Program(first, second, third));

        Assert.HasCount(1, errors, string.Join("; ", errors));
        StringAssert.Contains(errors[0], string.Format(name == "Program"
            ? RDCore.SDK.Exceptions.VBCompileError_DeclaredTypeIsAProceduralModule_Verbose
            : RDCore.SDK.Exceptions.VBCompileError_DeclaredTypeIsALibraryModule_Verbose, name));
    }

    [TestMethod]
    [DataRow("Public Sub Work()", "Dim p As VBA", "End Sub", DisplayName = "A local")]
    [DataRow("Public Sub Work(ByVal p As VBA)", "End Sub", "", DisplayName = "A parameter")]
    [DataRow("Public Function Work() As VBA", "End Function", "", DisplayName = "A function's result")]
    [DataRow("Private p As VBA", "", "", DisplayName = "A variable of the module")]
    public async Task AProject_IsNotAType(string first, string second, string third)
    {
        // MS-VBA: "Expected user-defined type, not project" (Excel-verified, oracle\s29 D01, D08).
        var errors = await ModuleWorkspace.LoadErrorsAsync([], Program(first, second, third));

        Assert.HasCount(1, errors, string.Join("; ", errors));
        StringAssert.Contains(errors[0], string.Format(RDCore.SDK.Exceptions.VBCompileError_DeclaredTypeIsAProject_Verbose, "VBA"));
    }

    [TestMethod]
    public async Task AnotherProceduralModuleOfTheProject_IsNotAType()
    {
        var errors = await ModuleWorkspace.LoadErrorsAsync(
            [], [("Helpers", ModuleWorkspace.StandardModule("Helpers", "Public Total As Long"))], Program("Public Sub Work()", "Dim h As Helpers", "End Sub"));

        Assert.HasCount(1, errors, string.Join("; ", errors));
        StringAssert.Contains(errors[0], string.Format(RDCore.SDK.Exceptions.VBCompileError_DeclaredTypeIsAProceduralModule_Verbose, "Helpers"));
    }

    // a standard module Helpers, and Mod2's Enums named like it and like the module Program.
    private static readonly (string, string)[] HelpersAndItsNamesake =
    [
        ("Helpers", ModuleWorkspace.StandardModule("Helpers", "Public Total As Long")),
        ("Mod2", ModuleWorkspace.StandardModule("Mod2", "Public Enum Helpers", "hOne = 1", "End Enum", "Public Enum Program", "pOne = 1", "End Enum")),
    ];

    [TestMethod]
    [DataRow("Public Sub Work()", "Dim h As {0}", "End Sub", DisplayName = "A local")]
    [DataRow("Public Sub Work()", "Static h As {0}", "End Sub", DisplayName = "A Static local")]
    [DataRow("Public Sub Work()", "Dim h() As {0}", "End Sub", DisplayName = "An array")]
    [DataRow("Public Sub Work(ByVal h As {0})", "End Sub", "", DisplayName = "A parameter")]
    [DataRow("Public Function Work() As {0}", "End Function", "", DisplayName = "A function's result")]
    [DataRow("Public Property Get Work() As {0}", "End Property", "", DisplayName = "A property's result")]
    [DataRow("Private h As {0}", "", "", DisplayName = "A variable of the module")]
    [DataRow("Private Type TRecord", "h As {0}", "End Type", DisplayName = "A field of a user-defined type")]
    [DataRow("Private Const c As {0} = 1", "", "", DisplayName = "A constant of the module")]
    [DataRow("Public Sub Work()", "Const c As {0} = 1", "End Sub", DisplayName = "A local constant")]
    [DataRow("Private Declare PtrSafe Function GetTickCount Lib \"kernel32\" () As {0}", "", "", DisplayName = "An external function's result")]
    [DataRow("Private Declare PtrSafe Sub Sleep Lib \"kernel32\" (ByVal h As {0})", "", "", DisplayName = "An external procedure's parameter")]
    [DataRow("Public Property Let Work(ByVal h As {0})", "End Property", "", DisplayName = "A Property Let's parameter")]
    public async Task ATypeQualifiedByItsModule_WhoseBareNameIsAProceduralModule_IsNotRefused(string first, string second, string third)
    {
        // the name is Mod2's type (MS-VBAL §5.6.12), and the type travels to the host with its module's name, so the host resolves it within Mod2. By its
        // bare name the host would bind the module Helpers, or Program, the module the declaration is in, and refuse it as a procedural module.
        foreach (var type in new[] { "Mod2.Helpers", "Mod2.Program" })
        {
            var errors = await ModuleWorkspace.LoadErrorsAsync(
                [], HelpersAndItsNamesake, Program("Option Explicit", string.Format(first, type), string.Format(second, type), string.Format(third, type)));

            Assert.IsEmpty(errors, $"{type}: {string.Join("; ", errors)}");
        }
    }

    [TestMethod]
    public async Task ATypeQualifiedByItsModule_WhoseBareNameIsAProceduralModule_IsNotRefused_AsAnEventsParameter()
    {
        // an event is declared in a class module, so this position cannot be one of the rows above, whose declarations are Program's.
        foreach (var type in new[] { "Mod2.Helpers", "Mod2.Program" })
        {
            var errors = await ModuleWorkspace.LoadErrorsAsync(
                [("Widget", ModuleWorkspace.ClassModule("Widget", "Option Explicit", $"Public Event Changed(ByVal h As {type})"))], HelpersAndItsNamesake, Program());

            Assert.IsEmpty(errors, $"{type}: {string.Join("; ", errors)}");
        }
    }

    [TestMethod]
    public async Task TheBareNameOfAProceduralModule_IsStillNotAType_BesideAnotherModulesTypeOfThatName()
    {
        // MS-VBAL §5.6.10 ranks a procedural module of the project ahead of another module's type: bare, Helpers is the module.
        var errors = await ModuleWorkspace.LoadErrorsAsync([], HelpersAndItsNamesake, Program("Public Sub Work()", "Dim h As Helpers", "End Sub"));

        Assert.HasCount(1, errors, string.Join("; ", errors));
        StringAssert.Contains(errors[0], string.Format(RDCore.SDK.Exceptions.VBCompileError_DeclaredTypeIsAProceduralModule_Verbose, "Helpers"));
    }

    [TestMethod]
    public async Task AProceduralModule_IsNotTheTypeOfAnEventsParameter()
    {
        var errors = await ModuleWorkspace.LoadErrorsAsync([("Widget", ModuleWorkspace.ClassModule("Widget", "Public Event Changed(ByVal s As Strings)"))], Program());

        Assert.HasCount(1, errors, string.Join("; ", errors));
        StringAssert.Contains(errors[0], string.Format(RDCore.SDK.Exceptions.VBCompileError_DeclaredTypeIsALibraryModule_Verbose, "Strings"));
    }

    [TestMethod]
    public async Task NewNamingAProceduralModule_IsStillATypeMismatch()
    {
        // New was refused before a procedural module was refused as a declared type, as a name that is no instantiable class (MS-VBAL §5.6.8), and still is.
        var errors = await ModuleWorkspace.LoadErrorsAsync([], Program("Public Sub Work()", "Dim o As Object", "Set o = New Strings", "End Sub"));

        Assert.HasCount(1, errors, string.Join("; ", errors));
        StringAssert.Contains(errors[0], $"{RDCore.SDK.Exceptions.VBCompileError_TypeMismatch}: 'Strings' does not reference an instantiable class");
    }

    private static string Running(string[] declarations, params string[] main)
        => $"Attribute VB_Name = \"Program\"\r\n{string.Join("\r\n", declarations)}\r\nPublic Sub Main()\r\n{string.Join("\r\n", main)}\r\nEnd Sub\r\n";

    [TestMethod]
    public async Task AFieldOrALocalConstantOfATypeNothingDeclares_IsNotChecked_AndTheModuleRuns()
    {
        // a field's and a local constant's types are refused only for a procedural module: a name that does not resolve is not checked there, and the module
        // loads and runs as it did before either type was checked. The constant is declared under Option Explicit: without it, loading the module fails, as
        // it did before.
        var field = await ModuleWorkspace.RunAsync([], Running(["Private Type TRecord", "s As Missing", "End Type"], "Dim t As TRecord", "Debug.Print 1"));
        var constant = await ModuleWorkspace.RunAsync([], Running(["Option Explicit", "Public Sub Work()", "Const c As Missing = 1", "End Sub"], "Debug.Print 2"));

        CollectionAssert.AreEqual(new[] { "1" }, field);
        CollectionAssert.AreEqual(new[] { "2" }, constant);
    }

    [TestMethod]
    public async Task AFieldOrALocalConstantOfATypeTheProjectQualifies_LoadsAndRuns_AsItDidBefore()
    {
        // a type qualified by the enclosing project's name does not resolve: the host defines no symbol for the enclosing project, and a variable declared
        // `As Project1.Widget` is refused. A field's and a local constant's types are not checked for a name that does not resolve, so these load and run
        // as they did before either type was checked.
        var widget = ("Widget", ModuleWorkspace.ClassModule("Widget", "Public X As Long"));
        var field = await ModuleWorkspace.RunAsync([widget], Running(["Private Type TRecord", "W As Project1.Widget", "End Type"], "Dim t As TRecord", "Debug.Print 1"));
        var constant = await ModuleWorkspace.RunAsync(
            [], Running(["Option Explicit", "Public Enum Colours", "cRed = 1", "End Enum", "Public Sub Work()", "Const c As Project1.Colours = 1", "End Sub"],
            "Debug.Print 2"));

        CollectionAssert.AreEqual(new[] { "1" }, field);
        CollectionAssert.AreEqual(new[] { "2" }, constant);
    }

    [TestMethod]
    public async Task TheLibrarysCollection_IsStillTheClass_WhenItRuns()
    {
        var program = "Attribute VB_Name = \"Program\"\r\nOption Explicit\r\nPublic Sub Main()\r\n"
            + "Dim c As Collection\r\nSet c = New Collection\r\nc.Add 1\r\nDim d As New Collection\r\nd.Add 1\r\nd.Add 2\r\nDebug.Print c.Count\r\nDebug.Print d.Count\r\nEnd Sub\r\n";

        CollectionAssert.AreEqual(new[] { "1", "2" }, await ModuleWorkspace.RunAsync([], program));
    }

    [TestMethod]
    public async Task ATypeOfAnotherModuleOfTheWorkspace_IsFound_WhicheverModuleIsDefinedFirst()
    {
        var errors = await ModuleWorkspace.LoadErrorsAsync(
            [("Alpha", ModuleWorkspace.ClassModule("Alpha", "Public Peer As Zulu")), ("Zulu", ModuleWorkspace.ClassModule("Zulu", "Public Peer As Alpha"))],
            Program("Public Both As Alpha", "Public Other As Zulu"));

        CollectionAssert.AreEqual(Array.Empty<string>(), errors);
    }

    [TestMethod]
    [DataRow("Dim n As Long")]
    [DataRow("Dim s As String")]
    [DataRow("Dim v As Variant")]
    [DataRow("Dim o As Object")]
    [DataRow("Dim c As Collection")]
    [DataRow("Dim c As VBA.Collection")]
    [DataRow("Dim e As ErrObject")]
    [DataRow("Dim d As Date")]
    [DataRow("Dim b() As Byte")]
    public async Task ATypeTheLibraryOfTheLanguageDeclares_IsFound(string declaration)
    {
        var errors = await ModuleWorkspace.LoadErrorsAsync([], Program("Public Sub Work()", declaration, "End Sub"));

        CollectionAssert.AreEqual(Array.Empty<string>(), errors);
    }

    // a type of a class module is private to it (VBC09338), and the class names it from within; a type another module can name is a standard module's.
    private static readonly (string, string) Holder = ("Holder", ModuleWorkspace.ClassModule(
        "Holder", "Private Type TSecret", "X As Long", "End Type", "Private Hidden As Holder.TSecret"));

    private static readonly (string, string) Shared = ("Shared", ModuleWorkspace.StandardModule("Shared", "Public Type TPublic", "X As Long", "End Type"));

    [TestMethod]
    public async Task AModuleQualifiesTheTypesItDeclares_FromWithinAndFromWithout_WhenTheyArePublic()
    {
        var errors = await ModuleWorkspace.LoadErrorsAsync(
            [Holder], [Shared], Program("Private Type TLocal", "X As Long", "End Type", "Public A As Program.TLocal", "Public B As Shared.TPublic"));

        Assert.IsEmpty(errors, string.Join("; ", errors));
    }

    [TestMethod]
    [DataRow("Public S As Holder.TSecret", "Holder.TSecret")]
    [DataRow("Public S As Holder.TMissing", "Holder.TMissing")]
    [DataRow("Public S As Program.TPublic", "Program.TPublic")]
    public async Task ATypeThatTheQualifyingModuleDoesNotDeclareForTheModule_IsNotFound(string declaration, string name)
    {
        // TSecret is Private to Holder; TMissing is nowhere; and TPublic is Shared's, which does not make it Program's.
        var errors = await ModuleWorkspace.LoadErrorsAsync([Holder], [Shared], Program(declaration));

        Assert.HasCount(1, errors);
        StringAssert.Contains(errors[0], $"'{name}'");
    }

    [TestMethod]
    public async Task ATypeDeclaredInTheModule_AndAnEnumOfAnotherModule_AreFound()
    {
        var errors = await ModuleWorkspace.LoadErrorsAsync(
            [("IShip", ModuleWorkspace.ClassModule("IShip", "Public Enum ShipOrientation", "Horizontal", "Vertical", "End Enum"))],
            Program(
                "Private Type TState", "Name As String", "End Type", "Private this As TState",
                "Public Sub Work()", "Dim o As IShip.ShipOrientation", "Dim p As ShipOrientation", "End Sub"));

        Assert.IsEmpty(errors, string.Join("; ", errors));
    }
}
