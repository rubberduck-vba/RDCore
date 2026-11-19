namespace RDCore.Tests.Cli;

/// <summary>
/// A Let statement whose target is a property with a <c>Property Get</c> and no <c>Property Let</c>, named or reached as an object's default member: the
/// <c>Property Get</c> is invoked with the arguments it is given, and the value is let-assigned to the default member of the object it returns
/// (<strong>MS-VBAL §5.4.3.8</strong>).
/// </summary>
/// <remarks>
/// MS-VBA refuses at compile time a Let to a target of a specific class whose default member has no <c>Property Let</c> (<c>os = 5</c>, <c>Me = 5</c>):
/// "Invalid use of property", or "Type mismatch" for a default member that is a <c>Function</c>. MS-VBAL §5.4.3.8 makes it valid, and RD-VBA runs it: a
/// <c>Property Get</c> is assigned through, and a <c>Function</c>, which is not a default property, is error 438. Every other form here runs in MS-VBA as it
/// does in RD-VBA (observed in Excel).
/// </remarks>
[TestClass]
[TestCategory("MS-VBAL 5.4.3.8 Let Statement")]
public sealed class LetThroughPropertyGetTests
{
    private static readonly string[] SlotGet =
        ["Private mS As Slot", "Public Property Get S() As Slot", "Attribute S.VB_UserMemId = 0", "If mS Is Nothing Then Set mS = New Slot", "Set S = mS", "End Property"];

    private static readonly (string, string)[] Classes =
    [
        ("Slot", ModuleWorkspace.ClassModule("Slot", "Private mV As Long",
            "Public Property Get V() As Long", "Attribute V.VB_UserMemId = 0", "V = mV", "End Property",
            "Public Property Let V(ByVal n As Long)", "mV = n", "End Property")),
        ("OuterSlot", ModuleWorkspace.ClassModule("OuterSlot", SlotGet)),
        ("OuterSlotObj", ModuleWorkspace.ClassModule("OuterSlotObj", "Private mS As Slot", "Public Property Get S() As Object", "Attribute S.VB_UserMemId = 0",
            "If mS Is Nothing Then Set mS = New Slot", "Set S = mS", "End Property")),
        ("OuterSlotVar", ModuleWorkspace.ClassModule("OuterSlotVar", "Private mS As Slot", "Public Property Get S() As Variant", "Attribute S.VB_UserMemId = 0",
            "If mS Is Nothing Then Set mS = New Slot", "Set S = mS", "End Property")),
        ("OuterLong", ModuleWorkspace.ClassModule("OuterLong", "Public Property Get N() As Long", "Attribute N.VB_UserMemId = 0", "N = 7", "End Property")),
        ("OuterVarLong", ModuleWorkspace.ClassModule("OuterVarLong", "Public Property Get N() As Variant", "Attribute N.VB_UserMemId = 0", "N = 7", "End Property")),
        ("OuterSlotIdx", ModuleWorkspace.ClassModule("OuterSlotIdx", "Private mS As Slot", "Public Property Get Item(ByVal i As Long) As Slot",
            "Attribute Item.VB_UserMemId = 0", "If mS Is Nothing Then Set mS = New Slot", "Set Item = mS", "End Property")),
        ("OuterSlotFn", ModuleWorkspace.ClassModule("OuterSlotFn", "Private mS As Slot", "Public Function S() As Slot", "Attribute S.VB_UserMemId = 0",
            "If mS Is Nothing Then Set mS = New Slot", "Set S = mS", "End Function")),
        ("SelfMeDefault", ModuleWorkspace.ClassModule("SelfMeDefault", [.. SlotGet, "Public Sub Assign()", "Me = 5", "End Sub"])),
        ("SelfMeFn", ModuleWorkspace.ClassModule("SelfMeFn", "Private mS As Slot", "Public Function S() As Slot", "Attribute S.VB_UserMemId = 0",
            "If mS Is Nothing Then Set mS = New Slot", "Set S = mS", "End Function", "Public Sub Assign()", "Me = 5", "End Sub")),
        ("HoldsFn", ModuleWorkspace.ClassModule("HoldsFn", "Private mF As OuterSlotFn", "Public Property Get Inner() As OuterSlotFn",
            "If mF Is Nothing Then Set mF = New OuterSlotFn", "Set Inner = mF", "End Property")),
    ];

    private static string Program(string body)
        => $"Attribute VB_Name = \"Program\"\r\nPublic Sub Main()\r\nOn Error Resume Next\r\n{body.Replace(" ;; ", "\r\n")}\r\nDebug.Print \"e\" & Err.Number\r\nEnd Sub\r\n";

    [TestMethod]
    [DataRow("Dim os As New OuterSlot ;; os = 5 ;; Debug.Print os.S.V", "5|e0", DisplayName = "the default member returns a class")]
    [DataRow("Dim os As New OuterSlot ;; Let os = 5 ;; Debug.Print os.S.V", "5|e0", DisplayName = "the default member returns a class, Let written")]
    [DataRow("Dim oo As New OuterSlotObj ;; oo = 5 ;; Debug.Print oo.S.V", "5|e0", DisplayName = "the default member returns an Object")]
    [DataRow("Dim ov As New OuterSlotVar ;; ov = 5 ;; Debug.Print ov.S.V", "5|e0", DisplayName = "the default member returns a Variant that holds an object")]
    [DataRow("Dim sd As New SelfMeDefault ;; sd.Assign ;; Debug.Print sd.S.V", "5|e0", DisplayName = "Me, in the class")]
    [DataRow("Dim ol As New OuterLong ;; ol = 5 ;; Debug.Print ol.N", "7|e451", DisplayName = "the default member returns a Long")]
    // MS-VBAL §5.4.3.8 raises 451 here. MS-VBA raises 424 for the same property named (`ovl.N = 5`), as RD-VBA does for both.
    [DataRow("Dim ovl As New OuterVarLong ;; ovl = 5 ;; Debug.Print ovl.N", "7|e424", DisplayName = "the default member returns a Variant that holds a number")]
    public async Task ALetToAnObjectWhoseDefaultMemberHasNoPropertyLet_AssignsThroughWhatItReturns(string body, string printed)
        => Assert.AreEqual(printed, string.Join("|", await ModuleWorkspace.RunAsync(Classes, Program(body))));

    [TestMethod]
    [DataRow("Dim oi As New OuterSlotIdx ;; oi(1) = 5 ;; Debug.Print oi(1).V", "5|e0", DisplayName = "the default member, given its argument")]
    [DataRow("Dim oi As New OuterSlotIdx ;; oi.Item(1) = 5 ;; Debug.Print oi(1).V", "5|e0", DisplayName = "named, given its argument")]
    [DataRow("Dim os As New OuterSlot ;; os.S = 5 ;; Debug.Print os.S.V", "5|e0", DisplayName = "named")]
    [DataRow("Dim o As Object ;; Set o = New OuterSlot ;; o = 5 ;; Debug.Print o.S.V", "5|e0", DisplayName = "late-bound")]
    [DataRow("Dim ofn As New OuterSlotFn ;; ofn() = 5 ;; Debug.Print ofn.S.V", "5|e0", DisplayName = "a Function default member, given an empty argument list")]
    public async Task ALetToAPropertyGetNamedOrGivenArguments_AssignsThroughWhatItReturnsAsMSVBADoes(string body, string printed)
        => Assert.AreEqual(printed, string.Join("|", await ModuleWorkspace.RunAsync(Classes, Program(body))));

    // MS-VBAL §5.4.3.8 raises 438: a function is not a default property. MS-VBA refuses the early-bound forms at compile time ("Type mismatch"), and raises 450
    // for the late-bound ones (observed in Excel).
    [TestMethod]
    [DataRow("Dim ofn As New OuterSlotFn ;; ofn = 5 ;; Debug.Print ofn.S.V", "0|e438", DisplayName = "a variable of the class")]
    [DataRow("Dim sf As New SelfMeFn ;; sf.Assign ;; Debug.Print sf.S.V", "0|e438", DisplayName = "Me, in the class")]
    [DataRow("Dim a(0) As OuterSlotFn ;; Set a(0) = New OuterSlotFn ;; a(0) = 5 ;; Debug.Print a(0).S.V", "0|e438", DisplayName = "an element of an array of the class")]
    [DataRow("Dim h As New HoldsFn ;; h.Inner = 5 ;; Debug.Print h.Inner.S.V", "0|e438", DisplayName = "a Property Get that returns the class")]
    [DataRow("Dim o As Object ;; Set o = New OuterSlotFn ;; o = 5 ;; Debug.Print o.S.V", "0|e450", DisplayName = "late-bound, an Object")]
    [DataRow("Dim v As Variant ;; Set v = New HoldsFn ;; v.Inner = 5 ;; Debug.Print v.Inner.S.V", "0|e450", DisplayName = "late-bound, a Variant's Property Get")]
    public async Task ALetThroughAFunctionDefaultMember_IsAnError(string body, string printed)
        => Assert.AreEqual(printed, string.Join("|", await ModuleWorkspace.RunAsync(Classes, Program(body))));
}
