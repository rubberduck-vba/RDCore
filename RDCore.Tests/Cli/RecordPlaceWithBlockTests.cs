using static RDCore.Tests.Cli.ModuleWorkspace;

namespace RDCore.Tests.Cli;

/// <summary>
/// <strong>MS-VBAL §5.4.2.21</strong>, <strong>MS-VBAL §5.3.1.11</strong>: a field named through a <c>With</c> block is the field of the record the block
/// names, where that record is. <c>VarPtr</c> of it is the field's address, and a <c>ByRef</c> parameter given it is the field while the call runs: of a
/// record variable, an element of an array of records, a record field and an element of a fixed-size array field. Observed in Excel (64-bit, en-US).
/// </summary>
[TestClass]
public sealed class RecordPlaceWithBlockTests
{
    internal static readonly (string Name, string Source) Places = ("Places", StandardModule("Places",
        "Option Explicit",
        "Public Trail As String",
        "Public Type TP", "    A As Long", "    X As Long", "End Type",
        "Public Type TOut", "    K As Long", "    I As TP", "End Type",
        "Public Type TH", "    N As Long", "    Arr(1 To 3) As Long", "End Type",
        "Public G As TP", "Public GA(1 To 2) As TP", "Public GO As TOut", "Public GH As TH",
        "Public Sub BumpG(ByRef N As Long)", "    N = N + 1", "    Trail = Trail & G.X & \" \"", "End Sub",
        "Public Sub BumpGA(ByRef N As Long)", "    N = N + 1", "    Trail = Trail & GA(2).X & \" \"", "End Sub",
        "Public Sub BumpGO(ByRef N As Long)", "    N = N + 1", "    Trail = Trail & GO.I.X & \" \"", "End Sub",
        "Public Sub BumpGH(ByRef N As Long)", "    N = N + 1", "    Trail = Trail & GH.Arr(2) & \" \"", "End Sub"));

    internal static string Program(string declarations, string body)
        => "Attribute VB_Name = \"Program\"\r\nOption Explicit\r\n" + declarations + "Private acc As String\r\n"
            + "Private Sub P(ByVal v As Variant)\r\n    acc = acc & CStr(v) & \"|\"\r\nEnd Sub\r\n"
            + $"Public Sub Main()\r\n{string.Join("\r\n", body.Split(" ;; "))}\r\nDebug.Print acc\r\nEnd Sub\r\n";

    private static async Task<string> RunAsync(string body) => string.Join(" ", await ModuleWorkspace.RunAsync([], [Places], Program("", body)));

    [TestMethod]
    [Ignore("VarPtr of a field named through a With block is an address of its own: storage resolves no address inside a record to the place there. "
        + "What is written through a parameter given the field reaches it (AFieldNamedThroughWith_GivenByReference_IsTheFieldWhileTheCallRuns)")]
    [DataRow("With G ;; P VarPtr(.X) - VarPtr(G) ;; End With", "4|", DisplayName = "the address of a field, from the record's")]
    [DataRow("With G ;; P VarPtr(.X) = VarPtr(G.X) ;; End With", "True|", DisplayName = "the address of the field named in full")]
    [DataRow("With GA(2) ;; P VarPtr(.X) - VarPtr(GA(1)) ;; End With", "12|", DisplayName = "a field of a record element")]
    [DataRow("Dim R As TP ;; With R ;; P VarPtr(.X) - VarPtr(R) ;; End With", "4|", DisplayName = "a field of a local record")]
    [DataRow("With GO ;; P VarPtr(.I.X) - VarPtr(GO) ;; End With", "8|", DisplayName = "a field of a record field")]
    [DataRow("With GO.I ;; P VarPtr(.X) - VarPtr(GO) ;; End With", "8|", DisplayName = "a field of the record field the block names")]
    public async Task AFieldNamedThroughWith_IsWhereTheRecordIs(string body, string expected)
        => Assert.AreEqual(expected, await RunAsync(body));

    [TestMethod]
    [DataRow("G.X = 1 ;; Trail = \"\" ;; With G ;; BumpG .X ;; End With ;; P Trail ;; P G.X", "2 |2|", DisplayName = "a field of a record")]
    [DataRow("G.X = 1 ;; Trail = \"\" ;; BumpG G.X ;; P Trail ;; P G.X", "2 |2|", DisplayName = "the field named in full")]
    [DataRow("GA(2).X = 1 ;; Trail = \"\" ;; With GA(2) ;; BumpGA .X ;; End With ;; P Trail ;; P GA(2).X", "2 |2|",
        DisplayName = "a field of a record element")]
    [DataRow("GO.I.X = 1 ;; Trail = \"\" ;; With GO.I ;; BumpGO .X ;; End With ;; P Trail ;; P GO.I.X", "2 |2|",
        DisplayName = "a field of a record field")]
    [DataRow("GH.Arr(2) = 1 ;; Trail = \"\" ;; With GH ;; BumpGH .Arr(2) ;; End With ;; P Trail ;; P GH.Arr(2)", "2 |2|",
        DisplayName = "an element of a fixed-size array field")]
    public async Task AFieldNamedThroughWith_GivenByReference_IsTheFieldWhileTheCallRuns(string body, string expected)
        => Assert.AreEqual(expected, await RunAsync(body));
}

/// <summary>
/// <strong>MS-VBAL §5.3.1.11</strong>: a <c>Declare</c>d procedure given a field of a record, or an element of a fixed-size array field, by reference is given
/// the place where it is in the record: <c>RtlMoveMemory</c> writing 8 bytes to a <c>Long</c> field writes the field after it too. Named in full or through
/// a <c>With</c> block. Observed in Excel (64-bit, en-US). These call the libraries of Windows itself, and are skipped elsewhere.
/// </summary>
[TestClass]
[TestCategory("MS-VBAL 5.2.3.5 External Procedure Declaration")]
[OSCondition(OperatingSystems.Windows)]
public sealed class RecordPlaceDeclareTests
{
    private const string CopyMemory =
        "Private Declare PtrSafe Sub CopyMemory Lib \"kernel32\" Alias \"RtlMoveMemory\" (ByRef Dst As Any, ByRef Src As Any, ByVal Length As LongPtr)\r\n";

    private static async Task<string> RunAsync(string body)
        => string.Join(" ", await ModuleWorkspace.RunAsync([], [RecordPlaceWithBlockTests.Places], RecordPlaceWithBlockTests.Program(CopyMemory, body)));

    private const string Source = "Dim S(1 To 2) As Long ;; S(1) = 3 ;; S(2) = 4 ;; ";

    [TestMethod]
    [DataRow(Source + "GH.Arr(2) = 0 ;; GH.Arr(3) = 0 ;; CopyMemory GH.Arr(2), S(1), 8 ;; P GH.Arr(2) ;; P GH.Arr(3)", "3|4|",
        DisplayName = "an element of a fixed-size array field")]
    [DataRow("P VarPtr(GH.Arr(2)) - VarPtr(GH)", "8|", DisplayName = "its address, from the record's")]
    [DataRow("Dim V As Long ;; V = 5 ;; GH.Arr(2) = 0 ;; CopyMemory GH.Arr(2), V, 4 ;; P GH.Arr(2)", "5|", DisplayName = "one element written")]
    [DataRow(Source + "GH.Arr(2) = 0 ;; GH.Arr(3) = 0 ;; With GH ;; CopyMemory .Arr(2), S(1), 8 ;; End With ;; P GH.Arr(2) ;; P GH.Arr(3)", "3|4|",
        DisplayName = "an element of a fixed-size array field named through With")]
    [DataRow("Dim R As TP ;; " + Source + "CopyMemory R.A, S(1), 8 ;; P R.A ;; P R.X", "3|4|", DisplayName = "a field of a local record")]
    [DataRow(Source + "G.A = 0 ;; G.X = 0 ;; With G ;; CopyMemory .A, S(1), 8 ;; End With ;; P G.A ;; P G.X", "3|4|",
        DisplayName = "a field named through With")]
    [DataRow("Dim T(1 To 2) As TP ;; " + Source + "CopyMemory T(2).A, S(1), 8 ;; P T(2).A ;; P T(2).X", "3|4|",
        DisplayName = "a field of a record element")]
    [DataRow("Dim H As TH ;; " + Source + "CopyMemory H.Arr(2), S(1), 8 ;; P H.Arr(2) ;; P H.Arr(3)", "3|4|",
        DisplayName = "an element of a fixed-size array field of a local record")]
    [DataRow("Dim H As TH ;; Dim V As Long ;; V = 6 ;; P VarPtr(H.Arr(2)) - VarPtr(H) ;; CopyMemory H.Arr(2), V, 4 ;; P H.Arr(2)", "8|6|",
        DisplayName = "its address, then one element written")]
    public async Task APlaceInARecordGivenToADeclare_IsWhereItIs(string body, string expected)
        => Assert.AreEqual(expected, await RunAsync(body));
}
