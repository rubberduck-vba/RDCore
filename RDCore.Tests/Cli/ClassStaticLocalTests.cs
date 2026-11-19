using static RDCore.Tests.Cli.ModuleWorkspace;

namespace RDCore.Tests.Cli;

/// <summary>
/// The <c>Static</c> locals of the members of a class module: <strong>MS-VBA</strong> gives each object its own, which goes with the object, where
/// <strong>MS-VBAL §5.4.3.1</strong> gives a <c>Static</c> local module extent. A <c>Static</c> local of a standard module's procedure is one for the module.
/// Observed in Excel.
/// </summary>
[TestClass]
public sealed class ClassStaticLocalTests
{
    private const string Bump = "Public Function Bump() As Long\r\n    Static n As Long\r\n    n = n + 1\r\n    Bump = n\r\nEnd Function";

    private static readonly (string Name, string Source) Child = ("Child", ClassModule("Child",
        "Option Explicit", "Private Sub Class_Terminate()", "    Log = Log & \"T\"", "End Sub"));

    private static readonly (string Name, string Source) IBump = ("IBump", ClassModule("IBump", "Option Explicit", "Public Function Bump() As Long", "End Function"));

    private static readonly (string Name, string Source) Globals = ("Globals", StandardModule("Globals",
        "Option Explicit", "Public Log As String", "Public Function Count() As Long", "    Static n As Long", "    n = n + 1", "    Count = n", "End Function"));

    private static string Program(string body)
        => "Attribute VB_Name = \"Program\"\r\nOption Explicit\r\nPrivate acc As String\r\nPrivate Sub P(ByVal v As Variant)\r\n    acc = acc & CStr(v) & \"|\"\r\nEnd Sub\r\n"
            + $"Public Sub Main()\r\n{body}\r\nDebug.Print acc\r\nEnd Sub\r\n";

    private static async Task<string> RunAsync(string body, params string[] holder)
        => string.Join(" ", await ModuleWorkspace.RunAsync([Child, IBump, ("Holder", ClassModule("Holder", ["Option Explicit", .. holder]))], [Globals], Program(body)));

    // S01
    [TestMethod]
    public async Task EachObject_HasItsOwnStaticLocal()
        => Assert.AreEqual("1|2|1|3|", await RunAsync(
            "Dim a As New Holder, b As New Holder\r\nP a.Bump\r\nP a.Bump\r\nP b.Bump\r\nP a.Bump", Bump));

    // S02: every local of a Static procedure is Static.
    [TestMethod]
    public async Task EachObject_HasItsOwnLocalsOfAStaticFunction()
        => Assert.AreEqual("1|2|1|", await RunAsync(
            "Dim a As New Holder, b As New Holder\r\nP a.Bump\r\nP a.Bump\r\nP b.Bump",
            "Public Static Function Bump() As Long", "    Dim n As Long", "    n = n + 1", "    Bump = n", "End Function"));

    // S03
    [TestMethod]
    public async Task AnotherObject_StartsItsStaticLocalFromNothing()
        => Assert.AreEqual("1|2|1|", await RunAsync(
            "Dim a As New Holder\r\nP a.Bump\r\nP a.Bump\r\nSet a = Nothing\r\nP a.Bump", Bump));

    // S04
    [TestMethod]
    public async Task TwoMembers_HaveTwoStaticLocalsOfTheSameName()
        => Assert.AreEqual("1|10|2|", await RunAsync(
            "Dim a As New Holder\r\nP a.Bump\r\nP a.Bump2\r\nP a.Bump", Bump,
            "Public Function Bump2() As Long", "    Static n As Long", "    n = n + 10", "    Bump2 = n", "End Function"));

    // S05
    [TestMethod]
    public async Task EachObject_HasItsOwnStaticArray()
        => Assert.AreEqual("1|2|1|", await RunAsync(
            "Dim a As New Holder, b As New Holder\r\nP a.Grow\r\nP a.Grow\r\nP b.Grow",
            "Public Function Grow() As Long", "    Static g() As Long", "    Static n As Long", "    n = n + 1", "    ReDim Preserve g(n)", "    Grow = UBound(g)", "End Function"));

    // S06: the object a Static local holds is let go of with the object whose local it is.
    [TestMethod]
    public async Task AStaticLocalsObject_IsLetGoOfWithItsHolder()
        => Assert.AreEqual("[]|[T]|", await RunAsync(
            "Dim a As New Holder\r\na.Keep\r\na.Keep\r\nP \"[\" & Log & \"]\"\r\nSet a = Nothing\r\nP \"[\" & Log & \"]\"",
            "Public Sub Keep()", "    Static c As Child", "    If c Is Nothing Then Set c = New Child", "End Sub"));

    // S07: a standard module's Static local, called from members of two objects, is still the module's.
    [TestMethod]
    public async Task AStandardModulesStaticLocal_IsOneForTheModule_WhoeverCallsIt()
        => Assert.AreEqual("1|2|3|", await RunAsync(
            "Dim a As New Holder, b As New Holder\r\nP a.Through\r\nP b.Through\r\nP Count",
            "Public Function Through() As Long", "    Through = Count", "End Function"));

    // S08
    [TestMethod]
    public async Task EachObject_HasItsOwnStaticLocalOfAPropertyGet()
        => Assert.AreEqual("1|2|1|", await RunAsync(
            "Dim a As New Holder, b As New Holder\r\nP a.Value\r\nP a.Value\r\nP b.Value",
            "Public Property Get Value() As Long", "    Static n As Long", "    n = n + 1", "    Value = n", "End Property"));

    // S09: through the interface the class implements, or from another member, the object's own.
    [TestMethod]
    public async Task AnImplementedMember_HasAStaticLocalForEachObject()
        => Assert.AreEqual("1|2|1|3|", await RunAsync(
            "Dim a As New Holder, b As New Holder, i As IBump\r\nSet i = a\r\nP i.Bump\r\nP i.Bump\r\nSet i = b\r\nP i.Bump\r\nP a.Plain",
            "Implements IBump", "Private Function IBump_Bump() As Long", "    Static n As Long", "    n = n + 1", "    IBump_Bump = n", "End Function",
            "Public Function Plain() As Long", "    Plain = IBump_Bump", "End Function"));
}
