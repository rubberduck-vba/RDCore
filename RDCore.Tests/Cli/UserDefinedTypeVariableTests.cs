namespace RDCore.Tests.Cli;

/// <summary>
/// A variable declared as a user-defined type of its module is that type in the host, with the fields of the type, whether it is a variable of the module or a local, and
/// whatever order the types are declared in.
/// </summary>
[TestClass]
[TestCategory("MS-VBAL 5.2.3.3 Type Declarations")]
public sealed class UserDefinedTypeVariableTests
{
    private static string Program(params string[] lines)
        => "Attribute VB_Name = \"Program\"\r\n" + string.Join("\r\n", lines) + "\r\n";

    private static Task<string[]> RunAsync(params string[] lines) => ModuleWorkspace.RunAsync([], Program(lines));

    // MS-VBAL §5.3.1.11: a field of a record held by a variable is a variable, which a ByRef parameter is a second name for: what the callee assigns is the field's.
    [TestMethod]
    [DataRow("Dim t As TState", "Bump t.Count", "Debug.Print t.Count", DisplayName = "A field of a local")]
    [DataRow("", "Bump State.Count", "Debug.Print State.Count", DisplayName = "A field of a variable of the module")]
    [DataRow("Dim o As TOuter", "Bump o.Inner.Count", "Debug.Print o.Inner.Count", DisplayName = "A field of a record's field")]
    [DataRow("Dim t As TState", "Touch t.Count", "Debug.Print t.Count", DisplayName = "To a Variant parameter")]
    public async Task AFieldOfARecord_PassedByReference_IsWhatTheCalleeAssigns(string declaration, string call, string print)
        => CollectionAssert.AreEqual(new[] { "1" }, await RunAsync(
            "Private Type TState", "Count As Long", "End Type",
            "Private Type TOuter", "Inner As TState", "End Type",
            "Private State As TState",
            "Private Sub Bump(ByRef n As Long)", "n = n + 1", "End Sub",
            "Private Sub Touch(ByRef v As Variant)", "v = 1", "End Sub",
            "Public Sub Main()", declaration, call, print, "End Sub"));

    // MVVM's ControlEventsPunk keeps its state in `This As TState` and passes its fields to the procedures that write them.
    [TestMethod]
    public async Task AFieldOfAClassModulesRecord_PassedByReference_IsWhatTheCalleeAssigns()
        => CollectionAssert.AreEqual(new[] { "42" }, await ModuleWorkspace.RunAsync(
            [("Holder", ModuleWorkspace.ClassModule("Holder", "Option Explicit", "Private Type TState", "Cookie As Long", "End Type", "Private This As TState",
                "Private Sub Connect(ByRef pdwCookie As Long)", "pdwCookie = 42", "End Sub",
                "Public Function Cookie() As Long", "Connect This.Cookie", "Cookie = This.Cookie", "End Function"))],
            Program("Public Sub Main()", "Dim h As New Holder", "Debug.Print h.Cookie", "End Sub")));

    // a field that is a record itself is a variable as well: the callee writes its fields, or assigns it whole, and the field is what the callee left.
    [TestMethod]
    [DataRow("Dim o As TOuter", "Fill o.Inner", "Debug.Print o.Inner.Count; o.Inner.Name", "1 one", DisplayName = "Its fields, written by the callee")]
    [DataRow("Dim o As TOuter", "Swap o.Inner", "Debug.Print o.Inner.Count; o.Inner.Name", "2 two", DisplayName = "Assigned whole by the callee")]
    [DataRow("", "Fill Outer.Inner", "Debug.Print Outer.Inner.Count; Outer.Inner.Name", "1 one", DisplayName = "A field of a variable of the module")]
    [DataRow("Dim o As TOuter", "o.Inner.Count = 7: Peek o.Inner", "Debug.Print o.Inner.Count", "7 | 7", DisplayName = "Read by the callee as it is")]
    public async Task AFieldThatIsARecord_PassedByReference_IsWhatTheCalleeLeaves(string declaration, string call, string print, string printed)
        => CollectionAssert.AreEqual(printed.Split(" | "), await RunAsync(
            "Private Type TState", "Count As Long", "Name As String", "End Type",
            "Private Type TOuter", "Inner As TState", "End Type",
            "Private Outer As TOuter",
            "Private Sub Fill(ByRef s As TState)", "s.Count = 1", "s.Name = \"one\"", "End Sub",
            "Private Sub Swap(ByRef s As TState)", "Dim t As TState", "t.Count = 2", "t.Name = \"two\"", "s = t", "End Sub",
            "Private Sub Peek(ByRef s As TState)", "Debug.Print s.Count", "End Sub",
            "Public Sub Main()", declaration, call, print, "End Sub"));

    // MVVM's ControlEventsPunk keeps its interface identifier in `This.RefIID As GUID`, which ConnectToConnectionPoint takes by reference.
    [TestMethod]
    public async Task AFieldThatIsARecord_OfAClassModulesRecord_PassedByReference_IsWhatTheCalleeLeaves()
        => CollectionAssert.AreEqual(new[] { "1024 70" }, await ModuleWorkspace.RunAsync(
            [("Holder", ModuleWorkspace.ClassModule("Holder", "Option Explicit",
                "Private Type GUID", "Data1 As Long", "Data4(0 To 7) As Byte", "End Type",
                "Private Type TState", "RefIID As GUID", "End Type", "Private This As TState",
                "Private Sub Fill(ByRef riid As GUID)", "riid.Data1 = &H400", "riid.Data4(7) = &H46", "End Sub",
                "Public Function Show() As String", "Fill This.RefIID", "Show = This.RefIID.Data1 & \" \" & This.RefIID.Data4(7)", "End Function"))],
            Program("Public Sub Main()", "Dim h As New Holder", "Debug.Print h.Show", "End Sub")));

    [TestMethod]
    public async Task ALocal_OfAUserDefinedType_HasItsFields()
        => CollectionAssert.AreEqual(new[] { "9" }, await RunAsync(
            "Private Type TPoint", "X As Long", "Y As Long", "End Type",
            "Public Sub Main()", "Dim p As TPoint", "p.X = 4", "p.Y = 5", "Debug.Print p.X + p.Y", "End Sub"));

    [TestMethod]
    public async Task ALocal_OfAUserDefinedTypeQualifiedByItsModule_HasItsFields()
        => CollectionAssert.AreEqual(new[] { "9" }, await RunAsync(
            "Public Type TPoint", "X As Long", "Y As Long", "End Type",
            "Public Sub Main()", "Dim p As Program.TPoint", "p.X = 4", "p.Y = 5", "Debug.Print p.X + p.Y", "End Sub"));

    // a type of a class module is private to it (VBC09338): a local of it is the class's own.
    [TestMethod]
    public async Task ALocal_OfAUserDefinedTypeOfAClassModule_HasItsFields()
        => CollectionAssert.AreEqual(new[] { "7" }, await ModuleWorkspace.RunAsync(
            [("Shapes", ModuleWorkspace.ClassModule("Shapes", "Private Type TPoint", "X As Long", "End Type",
                "Public Function Seven() As Long", "Dim p As Shapes.TPoint", "p.X = 7", "Seven = p.X", "End Function"))],
            Program("Public Sub Main()", "Dim s As New Shapes", "Debug.Print s.Seven", "End Sub")));

    [TestMethod]
    public async Task AVariableOfTheModule_OfAUserDefinedType_HasItsFields()
        => CollectionAssert.AreEqual(new[] { "5" }, await RunAsync(
            "Private Type TPoint", "X As Long", "Y As Long", "End Type",
            "Public Pt As TPoint",
            "Public Sub Main()", "Pt.Y = 5", "Debug.Print Pt.Y", "End Sub"));

    [TestMethod]
    public async Task ATypeDeclaredAfterTheVariableThatUsesIt_IsStillThatType()
        => CollectionAssert.AreEqual(new[] { "7" }, await RunAsync(
            "Public Pt As TPoint",
            "Private Type TPoint", "X As Long", "End Type",
            "Public Sub Main()", "Pt.X = 7", "Debug.Print Pt.X", "End Sub"));

    [TestMethod]
    public async Task AFieldOfAUserDefinedType_ThatIsAUserDefinedType_HasItsOwnFields()
        => CollectionAssert.AreEqual(new[] { "9" }, await RunAsync(
            "Private Type TPoint", "X As Long", "End Type",
            "Private Type TLine", "A As TPoint", "B As TPoint", "End Type",
            "Public Sub Main()", "Dim l As TLine", "l.B.X = 9", "Debug.Print l.B.X", "End Sub"));

    // MS-VBAL §5.4.2.21: "If <expression> is classified as a variable, that variable is the With block variable": what the block assigns is the
    // variable's, a field or a whole array field.
    [TestMethod]
    public async Task AWithBlockOnARecordVariable_AssignsTheVariablesFields()
        => CollectionAssert.AreEqual(new[] { "5 2" }, await RunAsync(
            "Private Type TPoint", "X As Long", "Items() As Long", "End Type",
            "Public Sub Main()", "Dim p As TPoint", "With p", ".X = 5", "ReDim .Items(1 To 2)", "End With",
            "Debug.Print p.X & \" \" & UBound(p.Items)", "End Sub"));

    [TestMethod]
    public async Task AParameter_OfAUserDefinedType_IsPassedWithItsFields()
        => CollectionAssert.AreEqual(new[] { "4" }, await RunAsync(
            "Private Type TPoint", "X As Long", "End Type",
            "Public Sub Main()", "Dim p As TPoint", "p.X = 4", "Show p", "End Sub",
            "Private Sub Show(ByRef q As TPoint)", "Debug.Print q.X", "End Sub"));

    [TestMethod]
    public async Task AnObjectField_OfAUserDefinedType_IsSetToAnObject()
        => CollectionAssert.AreEqual(new[] { "1", "True" }, await RunAsync(
            "Private Type TState", "Items As Collection", "End Type",
            "Public Sub Main()", "Dim s As TState", "Set s.Items = New Collection", "s.Items.Add 5", "Debug.Print s.Items.Count",
            "Set s.Items = Nothing", "Debug.Print s.Items Is Nothing", "End Sub"));

    [TestMethod]
    public async Task AnObjectField_OfAUserDefinedTypeOfAClass_IsSetThroughAProperty()
        => CollectionAssert.AreEqual(new[] { "2" }, await ModuleWorkspace.RunAsync(
            [("Holder", ModuleWorkspace.ClassModule(
                "Holder", "Private Type TState", "Items As Collection", "End Type", "Private this As TState",
                "Public Property Get Items() As Collection", "Set Items = this.Items", "End Property",
                "Public Property Set Items(ByVal value As Collection)", "Set this.Items = value", "End Property"))],
            Program(
                "Public Sub Main()", "Dim h As New Holder", "Dim c As New Collection", "c.Add 1", "c.Add 2", "Set h.Items = c", "Debug.Print h.Items.Count", "End Sub")));

    // MS-VBAL §5.2.3.3: a field is declared as a variable is, a fixed-size array included, with the dimensions its declaration gives it.
    [TestMethod]
    public async Task AFixedSizeArrayField_HasTheDimensionsItWasDeclaredWith()
        => CollectionAssert.AreEqual(new[] { "1 3", "10 20 30" }, await RunAsync(
            "Private Type TBox", "Count As Integer", "Values(1 To 3) As Long", "End Type",
            "Public Sub Main()", "Dim b As TBox", "b.Values(1) = 10", "b.Values(2) = 20", "b.Values(3) = 30",
            "Debug.Print LBound(b.Values) & \" \" & UBound(b.Values)",
            "Debug.Print b.Values(1) & \" \" & b.Values(2) & \" \" & b.Values(3)", "End Sub"));

    // a bound is a constant expression. An omitted lower bound of a field is 0 whatever the module's Option Base, as MS-VBA has it (recorded in
    // Excel) - where MS-VBAL §5.2.1.2 reads as covering every array dimension of the module, and the variable beside it does start at 1.
    [TestMethod]
    public async Task AFixedSizeArrayField_BoundedByAConstant_StartsAtZero_WhateverTheOptionBase()
        => CollectionAssert.AreEqual(new[] { "0 4", "1 4" }, await RunAsync(
            "Option Base 1", "Private Const Size As Long = 4",
            "Private Type TBox", "Values(Size) As Long", "End Type",
            "Public Sub Main()", "Dim b As TBox", "Dim plain(Size) As Long",
            "Debug.Print LBound(b.Values) & \" \" & UBound(b.Values)", "Debug.Print LBound(plain) & \" \" & UBound(plain)", "End Sub"));

    [TestMethod]
    public async Task AFixedSizeArrayField_OfAVariableOfTheModule_HasItsDimensions()
        => CollectionAssert.AreEqual(new[] { "2" }, await RunAsync(
            "Private Const Last As Long = 2",
            "Private Type TBox", "Values(Last) As String", "End Type",
            "Public Box As TBox",
            "Public Sub Main()", "Box.Values(2) = \"2\"", "Debug.Print Box.Values(UBound(Box.Values))", "End Sub"));

    [TestMethod]
    public async Task AFixedSizeArrayField_OfANestedRecord_HasItsDimensions()
        => CollectionAssert.AreEqual(new[] { "5" }, await RunAsync(
            "Private Type TBox", "Values(0 To 1) As Long", "End Type",
            "Private Type TCrate", "Box As TBox", "End Type",
            "Public Sub Main()", "Dim c As TCrate", "c.Box.Values(1) = 5", "Debug.Print c.Box.Values(1)", "End Sub"));

    // a variable of the module is defined with the module, before a later pass resolves the types of its type's fields: the record it holds is one of the
    // resolved type, whose field that is a record is one (it was a field of a type nothing knew, and reading it failed).
    [TestMethod]
    [DataRow("Debug.Print Outer.Inner.Count", "0", DisplayName = "Read")]
    [DataRow("Outer.Inner.Count = 3: Debug.Print Outer.Inner.Count", "3", DisplayName = "Written")]
    [DataRow("Dim o As TOuter: Outer.Inner.Count = 4: o = Outer: Debug.Print o.Inner.Count", "4", DisplayName = "Copied")]
    public async Task ARecordFieldOfARecord_OfAVariableOfTheModule_HasItsFields(string body, string printed)
        => CollectionAssert.AreEqual(new[] { printed }, await RunAsync(
            "Private Type TState", "Count As Long", "End Type",
            "Private Type TOuter", "Inner As TState", "End Type",
            "Private Outer As TOuter",
            "Public Sub Main()", body, "End Sub"));

    [TestMethod]
    public async Task AFixedSizeArrayField_OfAVariableOfAClass_HasItsDimensions()
        => CollectionAssert.AreEqual(new[] { "3" }, await ModuleWorkspace.RunAsync(
            [("Holder", ModuleWorkspace.ClassModule(
                "Holder", "Private Type TState", "Values(1 To 3) As Long", "End Type", "Private this As TState",
                "Public Function Last() As Long", "this.Values(3) = 3", "Last = this.Values(UBound(this.Values))", "End Function"))],
            Program("Public Sub Main()", "Dim h As New Holder", "Debug.Print h.Last", "End Sub")));

    // assigning a record copies its fields (MS-VBAL §5.4.3.1.2), an array field's elements included: the two do not share them.
    [TestMethod]
    public async Task AFixedSizeArrayField_IsCopiedByAnAssignmentOfTheRecord()
        => CollectionAssert.AreEqual(new[] { "10 99" }, await RunAsync(
            "Private Type TBox", "Values(1 To 2) As Long", "End Type",
            "Public Sub Main()", "Dim p As TBox, q As TBox", "p.Values(1) = 10", "q = p", "p.Values(1) = 99",
            "Debug.Print q.Values(1) & \" \" & p.Values(1)", "End Sub"));

    // MS-VBAL §5.4.3.6: an LSet of a record copies its data, which an array field's elements are: the two do not share them either.
    [TestMethod]
    public async Task AFixedSizeArrayField_IsCopiedByAnLSetOfTheRecord()
        => CollectionAssert.AreEqual(new[] { "10 99" }, await RunAsync(
            "Private Type TBox", "Values(1 To 2) As Long", "End Type",
            "Public Sub Main()", "Dim p As TBox, q As TBox", "p.Values(1) = 10", "LSet q = p", "p.Values(1) = 99",
            "Debug.Print q.Values(1) & \" \" & p.Values(1)", "End Sub"));

    // in memory a fixed-size array field is its elements inline: an Integer, then three Longs on their four-byte boundary.
    [TestMethod]
    public async Task AFixedSizeArrayField_IsItsElementsInline_InTheSizeOfTheRecord()
        => CollectionAssert.AreEqual(new[] { "16" }, await RunAsync(
            "Private Type TBox", "Count As Integer", "Values(1 To 3) As Long", "End Type",
            "Public Sub Main()", "Dim b As TBox", "Debug.Print LenB(b)", "End Sub"));

    [TestMethod]
    public async Task AFixedSizeArrayField_OfAFunctionsResult_HasItsDimensions()
        => CollectionAssert.AreEqual(new[] { "2" }, await RunAsync(
            "Private Type TBox", "Values(1 To 2) As Long", "End Type",
            "Private Function Make() As TBox", "Make.Values(2) = 2", "End Function",
            "Public Sub Main()", "Dim b As TBox", "b = Make()", "Debug.Print b.Values(2)", "End Sub"));

    [TestMethod]
    public async Task AFixedSizeArrayField_OfAnElementOfAnArrayOfRecords_HasItsDimensions()
        => CollectionAssert.AreEqual(new[] { "5 0" }, await RunAsync(
            "Private Type TBox", "Values(1 To 2) As Long", "End Type",
            "Public Sub Main()", "Dim a(1) As TBox", "a(0).Values(1) = 5",
            "Debug.Print a(0).Values(1) & \" \" & a(1).Values(1)", "End Sub"));

    // each element of an array of records is a record of its own (MS-VBAL §5.2.3.1.3), which keeps what its fields are assigned.
    [TestMethod]
    public async Task AnElementOfAnArrayOfRecords_KeepsWhatItsFieldsAreAssigned()
        => CollectionAssert.AreEqual(new[] { "5 7" }, await RunAsync(
            "Private Type TPoint", "X As Long", "End Type",
            "Public Sub Main()", "Dim a(1) As TPoint", "a(0).X = 5", "a(1).X = 7", "Debug.Print a(0).X & \" \" & a(1).X", "End Sub"));

    [TestMethod]
    public async Task AnElementOfAnArrayOfRecords_IsCopiedByAnAssignment()
        => CollectionAssert.AreEqual(new[] { "5 9" }, await RunAsync(
            "Private Type TPoint", "X As Long", "End Type",
            "Public Sub Main()", "Dim a(0) As TPoint, p As TPoint", "a(0).X = 5", "p = a(0)", "a(0).X = 9", "Debug.Print p.X & \" \" & a(0).X", "End Sub"));

    [TestMethod]
    public async Task AnArrayOfRecords_Redimensioned_HasRecordsWithTheirDimensions()
        => CollectionAssert.AreEqual(new[] { "3" }, await RunAsync(
            "Private Type TBox", "Values(1 To 3) As Long", "End Type",
            "Public Sub Main()", "Dim a() As TBox", "ReDim a(1)", "a(1).Values(3) = 3", "Debug.Print a(1).Values(3)", "End Sub"));

    // "each of the extra elements is set to its default data value" (MS-VBAL §5.4.3.3), and the ones kept are what they were.
    [TestMethod]
    public async Task AnArrayOfRecords_RedimensionedPreserved_KeepsItsElements_AndTheNewOnesAreRecords()
        => CollectionAssert.AreEqual(new[] { "4 6" }, await RunAsync(
            "Private Type TPoint", "X As Long", "End Type",
            "Public Sub Main()", "Dim a() As TPoint", "ReDim a(0)", "a(0).X = 4", "ReDim Preserve a(1)", "a(1).X = 6",
            "Debug.Print a(0).X & \" \" & a(1).X", "End Sub"));

    [TestMethod]
    public async Task AFieldThatIsNotOneOfTheType_IsStillAnError()
    {
        var errors = await ModuleWorkspace.LoadErrorsAsync([], Program(
            "Private Type TPoint", "X As Long", "End Type",
            "Public Sub Main()", "Dim p As TPoint", "p.Z = 1", "End Sub"));

        Assert.IsNotEmpty(errors);
    }
}
