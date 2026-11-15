using RDCore.SDK.Runtime.Libraries;
using RDCore.Tests.Runtime.Libraries;
using static RDCore.Tests.Cli.ModuleWorkspace;

namespace RDCore.Tests.Cli;

/// <summary>
/// <strong>MS-VBAL §5.2.3.1.4</strong>: what a variable can be declared <c>As New</c> of - a class, one the project can create - and <strong>§2.5.1</strong>: that
/// each automatic instantiation variable, an element of an array or a field of a record or an object as much as a variable, is never <c>Nothing</c> when it is
/// referred to. Observed in Excel.
/// </summary>
[TestClass]
public sealed class AsNewDeclarationTests
{
    private static readonly (string Name, string Source) Thing = ("Thing", ClassModule("Thing", "Option Explicit", "Public Value As Long", "Public Event Changed()"));

    private static readonly (string Name, string Source) Sealed = ("Sealed", ClassModule("Sealed", "Attribute VB_Creatable = False", "Option Explicit", "Public Value As Long"));

    private static readonly (string Name, string Source) Globals = ("Globals", StandardModule("Globals",
        "Option Explicit", "Public Type TPair", "    X As Long", "End Type", "Public Enum Kind", "    KindA = 1", "End Enum"));

    // a library with a class that can be created and one that cannot, as a host's object model has (Excel's Workbook).
    private static readonly LibraryDescription Shop = new()
    {
        Name = "Shop",
        Classes =
        [
            new ClassDescription { Name = "Basket", ProgId = "Shop.Basket", IsCreatable = true },
            new ClassDescription { Name = "Receipt", IsCreatable = false },
        ],
    };

    private static string Program(string body, string declarations = "")
        => $"Attribute VB_Name = \"Program\"\r\nOption Explicit\r\n{declarations}\r\nPrivate acc As String\r\nPrivate Sub P(ByVal v As Variant)\r\n    acc = acc & CStr(v) & \"|\"\r\nEnd Sub\r\n"
            + $"Public Sub Main()\r\n{body}\r\nDebug.Print acc\r\nEnd Sub\r\n";

    private static Task<string[]> ErrorsOf(string body, string declarations = "", params string[] holder)
        => LoadErrorsAsync([Thing, Sealed, ("Holder", ClassModule("Holder", ["Option Explicit", .. holder]))], [Globals], Program(body, declarations));

    private static async Task<string> RunAsync(string body, string declarations = "", params string[] holder)
        => string.Join(" ", await ModuleWorkspace.RunAsync([Thing, Sealed, ("Holder", ClassModule("Holder", ["Option Explicit", .. holder]))], [Globals], Program(body, declarations)));

    // MS-VBA reads the name of a type that is no class at all as a syntax error (N01-N04, N07).
    [TestMethod]
    [DataRow("Long")]
    [DataRow("String")]
    [DataRow("Date")]
    [DataRow("Variant")]
    [DataRow("Object")]
    public async Task AVariableDeclaredAsNewOfAnIntrinsicType_IsASyntaxError(string type)
    {
        var errors = await ErrorsOf($"Dim x As New {type}");

        Assert.HasCount(1, errors, string.Join(" / ", errors));
        StringAssert.Contains(errors[0], "Syntax error");
    }

    // a user-defined type and an enumeration are no class: "Invalid use of New keyword" (N05, N06, N30).
    [TestMethod]
    [DataRow("TPair")]
    [DataRow("Kind")]
    [DataRow("VbCompareMethod")]
    public async Task AVariableDeclaredAsNewOfATypeThatIsNoClass_IsAnInvalidUseOfNew(string type)
    {
        var errors = await ErrorsOf($"Dim x As New {type}");

        Assert.HasCount(1, errors, string.Join(" / ", errors));
        StringAssert.Contains(errors[0], "Invalid use of New keyword");
    }

    // a WithEvents variable is assigned the object whose events it handles (N28).
    [TestMethod]
    public async Task AWithEventsVariableDeclaredAsNew_IsAnInvalidUseOfNew()
    {
        var errors = await ErrorsOf("P 1", "", "Private WithEvents w As New Thing");

        Assert.HasCount(1, errors, string.Join(" / ", errors));
        StringAssert.Contains(errors[0], "Invalid use of New keyword");
    }

    // MS-VBAL §5.2.3.1.4: VBA's type library says ErrObject can be created, and MS-VBA compiles the declaration; it raises "Class not registered" when the
    // variable is first referred to (observed in Excel), which NewOfAClassNotCreatedTests runs.
    [TestMethod]
    public async Task AVariableDeclaredAsNewOfErrObject_IsValid()
        => Assert.IsEmpty(await ErrorsOf("Dim x As New ErrObject"));

    // MS-VBAL §5.2.3.1.4: a class of another project that cannot be created - its type library does not say it can (observed in Excel).
    [TestMethod]
    public async Task AVariableDeclaredAsNewOfALibrarysClass_IsValidWhenTheClassCanBeCreated()
    {
        var libraries = new WorkspaceLibraries(["Shop"], new InMemoryLibrarySource(Shop));

        Assert.IsEmpty(await LoadErrorsAsync([], Program("Dim b As New Basket"), libraries));
        var refused = await LoadErrorsAsync([], Program("Dim r As New Receipt"), libraries);
        Assert.HasCount(1, refused, string.Join(" / ", refused));
        StringAssert.Contains(refused[0], "Invalid use of New keyword");
    }

    // a class of the project can be created by its own code, whatever its VB_Creatable says (N08).
    [TestMethod]
    public async Task AVariableDeclaredAsNewOfAClassOfTheProject_IsValidWhetherOrNotItIsCreatable()
        => Assert.AreEqual("0|0|", await RunAsync("Dim t As New Thing, s As New Sealed\r\nP t.Value\r\nP s.Value"));

    // MS-VBAL §5.4.3.3: a ReDim gives an array its dimensions; its element type is the one it is declared with, As New included (N18, N33, N35).
    [TestMethod]
    [DataRow("Dim a() As Long", "ReDim a(2) As String")]
    [DataRow("Dim a() As Collection", "ReDim a(2) As New Collection")]
    [DataRow("Dim a() As New Collection", "ReDim a(2) As Collection")]
    public async Task AReDimOfAnotherElementType_CantChangeTheDataTypesOfTheElements(string declaration, string redim)
    {
        var errors = await ErrorsOf($"{declaration}\r\n{redim}");

        Assert.HasCount(1, errors, string.Join(" / ", errors));
        StringAssert.Contains(errors[0], "Can't change data types of array elements");
    }

    // MS-VBAL §5.4.3.3 says a ReDim's As clause MUST NOT be As New; MS-VBA compiles one that keeps the array's type, and runs it (N32, N34).
    [TestMethod]
    public async Task AReDimOfTheSameElementType_IsValid_AsNewIncluded()
        => Assert.AreEqual("0|True|", await RunAsync(
            "Dim a() As New Collection, b() As Collection\r\nReDim a(2) As New Collection\r\nP a(2).Count\r\nReDim b(2) As Collection\r\nP (b(2) Is Nothing)"));

    // MS-VBAL §2.5.1: each element of an array declared As New is an automatic instantiation variable, fixed-size or resized (N16, N17).
    [TestMethod]
    public async Task EachElementOfAnArrayDeclaredAsNew_IsANewObjectWhenItIsReferredTo()
        => Assert.AreEqual("1|0|0|False|", await RunAsync(
            "Dim a(2) As New Collection, d() As New Collection\r\na(1).Add 1\r\nP a(1).Count\r\nP a(2).Count\r\nReDim d(2)\r\nP d(2).Count\r\nSet a(1) = Nothing\r\nP (a(1) Is Nothing)"));

    // a field of a record declared As New (N31): a copy of the record holds the same object, and one set to Nothing is a new object the next time.
    [TestMethod]
    public async Task AFieldOfARecordDeclaredAsNew_IsANewObjectWhenItIsReferredTo()
        => Assert.AreEqual("1|True|False|0|", await RunAsync(
            "Dim b As TBag, c As TBag\r\nb.C.Add 1\r\nP b.C.Count\r\nc = b\r\nP (c.C Is b.C)\r\nSet b.C = Nothing\r\nP (b.C Is Nothing)\r\nP b.C.Count",
            "Private Type TBag\r\n    C As New Collection\r\nEnd Type"));

    // a public variable of an object declared As New, referred to from outside the object (N36).
    [TestMethod]
    public async Task APublicVariableOfAnObjectDeclaredAsNew_IsANewObjectWhenItIsReferredTo()
        => Assert.AreEqual("1|False|0|", await RunAsync(
            "Dim h As New Holder\r\nh.C.Add 1\r\nP h.C.Count\r\nSet h.C = Nothing\r\nP (h.C Is Nothing)\r\nP h.C.Count", "", "Public C As New Collection"));
}
