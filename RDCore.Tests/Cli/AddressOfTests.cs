using static RDCore.Tests.Cli.ModuleWorkspace;

namespace RDCore.Tests.Cli;

/// <summary>
/// <c>AddressOf</c>: the address of a procedure of a standard module in the session's memory (<strong>MS-VBAL §5.6.16.8</strong>), as <strong>MS-VBA</strong>
/// gives it (recorded in Excel, oracle\s17 A rows) - a <c>LongPtr</c>, not <c>0</c>, the same every time, another for another procedure.
/// </summary>
[TestClass]
public sealed class AddressOfTests
{
    private static readonly (string Name, string Source) Mem = ("Mem", StandardModule("Mem",
        "Option Explicit",
        "Public Function FnPtr(ByVal f As LongPtr) As LongPtr", "    FnPtr = f", "End Function",
        "Public Function Held(ByVal v As Variant) As String", "    Held = TypeName(v) & \" \" & CStr(v <> 0)", "End Function",
        "Public Sub SomeSub()", "End Sub",
        "Public Sub OtherSub()", "End Sub",
        "Public Function SomeFunction() As Long", "End Function",
        "Public Property Get SomeProperty() As Long", "End Property",
        "Public Function Mine() As LongPtr", "    Mine = FnPtr(AddressOf Local1)", "End Function",
        "Private Sub Local1()", "End Sub"));

    private static string Program(string body)
        => "Attribute VB_Name = \"Program\"\r\nOption Explicit\r\nPrivate acc As String\r\nPrivate Sub P(ByVal v As Variant)\r\n    acc = acc & CStr(v) & \"|\"\r\nEnd Sub\r\n"
            + $"Public Sub Main()\r\nOn Error Resume Next\r\n{string.Join("\r\n", body.Split(" ;; "))}\r\nDebug.Print acc\r\nEnd Sub\r\n";

    private static async Task<string> RunAsync(string body)
        => string.Join(" ", await ModuleWorkspace.RunAsync([], [Mem], Program(body)));

    [TestMethod]
    [DataRow("P (FnPtr(AddressOf SomeSub) <> 0) ;; P (FnPtr(AddressOf SomeSub) = FnPtr(AddressOf SomeSub)) ;; P (FnPtr(AddressOf SomeSub) <> FnPtr(AddressOf OtherSub)) ;; P TypeName(FnPtr(AddressOf SomeSub))",
        "True|True|True|LongLong|", DisplayName = "A01 not 0, the same every time, another for another procedure, a LongPtr")]
    [DataRow("P (FnPtr(AddressOf Mem.SomeSub) = FnPtr(AddressOf SomeSub))", "True|", DisplayName = "A06 bare or qualified by its module")]
    [DataRow("P Held(AddressOf SomeSub)", "LongLong True|", DisplayName = "A04 to a Variant")]
    [DataRow("P (Mine() <> 0) ;; P (Mine() = Mine())", "True|True|", DisplayName = "A07 a Private procedure, in its own module")]
    [DataRow("P (FnPtr(AddressOf SomeFunction) <> FnPtr(AddressOf SomeProperty))", "True|", DisplayName = "a Function and a Property Get")]
    [DataRow("Dim f As LongPtr ;; f = FnPtr(AddressOf SomeSub) ;; P (f = FnPtr(AddressOf SomeSub))", "True|", DisplayName = "A09 held in a variable")]
    public async Task AddressOf_IsTheAddressOfAProcedure(string body, string expected)
        => Assert.AreEqual(expected, await RunAsync(body));

    // A02: AddressOf is an argument only, as MS-VBAL's grammar has it (MS-VBA: Syntax error).
    [TestMethod]
    public void AddressOfThatIsNoArgument_IsASyntaxError()
    {
        var source = StandardModule("Assigned", "Option Explicit", "Public Sub Assign()", "    Dim x As LongPtr", "    x = AddressOf Assign", "End Sub");

        var parse = new RDCore.Parsing.ModuleParser().Parse(new Uri("file:///rdcore-test/Assigned.bas"), source);

        Assert.IsFalse(parse.IsSuccess);
        Assert.IsNotEmpty(parse.SyntaxErrors);
    }
}
