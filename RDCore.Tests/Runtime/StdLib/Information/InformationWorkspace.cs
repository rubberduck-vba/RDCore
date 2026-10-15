using static RDCore.Tests.Cli.ModuleWorkspace;

namespace RDCore.Tests.Runtime.StdLib.Information;

/// <summary>
/// Runs a program against the classes every test of the <c>Information</c> module may use, through the whole platform: the language
/// server's composition, the host's session, the module's code - the same program MS-VBA was given.
/// </summary>
/// <remarks>
/// Every value the tests expect is what MS-VBA printed for the same source, run in Excel (64-bit, en-US): the specification's tables
/// for these functions are garbled in places and wrong in others, and it is what MS-VBA does that RD-VBA does.
/// </remarks>
internal static class InformationWorkspace
{
    // a class whose default member returns what `body` assigns to Value, declared as `type`.
    private static (string, string) Defaulted(string name, string type, string body)
        => (name, ClassModule(name,
            $"Public Property Get Value(){type}",
            "Attribute Value.VB_UserMemId = 0",
            body,
            "End Property"));

    /// <summary>
    /// The classes: one with no default member, an interface and a class implementing it, and one for each kind of default member.
    /// </summary>
    public static readonly IReadOnlyList<(string Name, string Source)> Classes =
    [
        ("Class1", ClassModule("Class1")),
        ("IFoo", ClassModule("IFoo", "Public Sub DoIt()", "End Sub")),
        ("Impl", ClassModule("Impl", "Implements IFoo", "Private Sub IFoo_DoIt()", "End Sub")),
        Defaulted("DfltStr", " As String", "    Value = \"duck\""),
        Defaulted("DfltNum", " As Long", "    Value = 42"),
        Defaulted("DfltDbl", " As Double", "    Value = 1.5"),
        Defaulted("DfltNull", string.Empty, "    Value = Null"),
        Defaulted("DfltEmpty", string.Empty, string.Empty),
        Defaulted("DfltObj", " As Object", "    Set Value = New Collection"),
        Defaulted("DfltChain", " As Object", "    Set Value = New DfltNum"),
        Defaulted("DfltNothing", " As Object", string.Empty),
        Defaulted("DfltArr", " As Long()", "    Dim a(1) As Long: Value = a"),
        Defaulted("DfltRaise", " As Long", "    Err.Raise 5"),
        ("DfltArgs", ClassModule("DfltArgs",
            "Public Function Item(ByVal i As Long) As Long",
            "Attribute Item.VB_UserMemId = 0",
            "    Item = i",
            "End Function")),
        ("DfltOpt", ClassModule("DfltOpt",
            "Public Function Value(Optional ByVal n As Long = 7, Optional v As Variant) As Variant",
            "Attribute Value.VB_UserMemId = 0",
            "    Value = IsMissing(v) & \" \" & n",
            "End Function")),
        ("Gauge", ClassModule("Gauge",
            "Public Property Get Value() As Double",
            "Attribute Value.VB_UserMemId = 0",
            "    Value = 1.5",
            "End Property",
            "Public Sub Report()",
            "    Debug.Print TypeName(Me)",
            "    Debug.Print VarType(Me)",
            "    Debug.Print IsNumeric(Me)",
            "End Sub")),
    ];

    /// <summary>
    /// Runs <c>Main</c>, whose body is <paramref name="body"/>, in a <c>Program</c> module that declares <paramref name="declarations"/>
    /// at module level, and gives what it printed.
    /// </summary>
    /// <param name="declarations">Module-level declarations and procedures, written before <c>Main</c>.</param>
    /// <param name="body">The statements of <c>Main</c>.</param>
    public static Task<string[]> Run(string declarations, params string[] body)
        => RunAsync(Classes,
            $"Attribute VB_Name = \"Program\"\r\nOption Explicit\r\n{declarations}\r\nPublic Sub Main()\r\n{string.Join("\r\n", body)}\r\nEnd Sub\r\n");

    /// <summary>
    /// What <c>TypeName</c> and <c>VarType</c> say of <paramref name="expression"/>, after <paramref name="setup"/> has run.
    /// </summary>
    public static async Task<(string TypeName, string VarType)> TypeOf(string declarations, string expression, params string[] setup)
    {
        var printed = await Run(declarations, [.. setup, $"Debug.Print TypeName({expression})", $"Debug.Print VarType({expression})"]);
        Assert.HasCount(2, printed, string.Join(" | ", printed));
        return (printed[0], printed[1]);
    }
}
