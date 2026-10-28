using RDCore.Parsing;
using RDCore.SDK.Model.AST;

namespace RDCore.Tests.Model;

/// <summary>
/// The surface of a module is what the other modules can be told by it: what it declares, and the names its procedures use; not where any of it is written,
/// nor what a procedure does with the names it uses.
/// </summary>
[TestClass]
public sealed class ModuleSurfaceTests
{
    private const string Module = """
        Option Explicit
        Public Const Size As Long = 10
        Public Enum Shade
            Light = 1
            Dark = 2
        End Enum
        Private mCount As Long

        Public Function Twice(ByVal Value As Long, Optional ByVal Times As Long = 2) As Long
            ' twice is twice
            Twice = Value * Times + 0
            mCount = mCount + 1
        End Function

        Public Sub Report()
            Debug.Print "Reported"; Twice(1)
        End Sub
        """;

    private static string? SurfaceOf(string source) => ModuleSurface.Of(new ModuleParser().Parse(TestUri.TestModuleUri(), source));

    private static void AssertSame(string edited)
        => Assert.AreEqual(SurfaceOf(Module), SurfaceOf(edited), "no other module can tell the two apart");

    private static void AssertChanged(string edited)
        => Assert.AreNotEqual(SurfaceOf(Module), SurfaceOf(edited));

    [TestMethod]
    public void IsTheSame_ForTheSameText()
        => AssertSame(Module);

    [TestMethod]
    [DataRow("Value * Times + 0", "Value * Times + 1", DisplayName = "a literal")]
    [DataRow("Value * Times + 0", "Value + Times - 0", DisplayName = "an operator")]
    [DataRow("' twice is twice", "' twice is two times", DisplayName = "a comment")]
    [DataRow("\"Reported\"", "\"Done\"", DisplayName = "a string")]
    [DataRow("mCount = mCount + 1", "If mCount < 0 Then mCount = 0", DisplayName = "a statement that uses the same names")]
    public void IsTheSame_ForAChangeOfABody_ThatDeclaresAndNamesNothingNew(string written, string edited)
        => AssertSame(Module.Replace(written, edited));

    [TestMethod]
    public void IsTheSame_WhenADeclarationMoves()
        => AssertSame("\n\n" + Module.Replace("Option Explicit", "Option Explicit\n"));

    [TestMethod]
    [DataRow("Public Function Twice", "Private Function Twice", DisplayName = "a procedure's access")]
    [DataRow("Public Function Twice", "Public Function Double2", DisplayName = "a procedure's name")]
    [DataRow("ByVal Value As Long,", "ByVal Value As String,", DisplayName = "a parameter's type")]
    [DataRow("ByVal Value As Long,", "ByRef Value As Long,", DisplayName = "a parameter's passing")]
    [DataRow("Times As Long = 2", "Times As Long = 3", DisplayName = "an optional parameter's default")]
    [DataRow(") As Long\n", ") As Double\n", DisplayName = "the type a function returns")]
    [DataRow("Size As Long = 10", "Size As Long = 11", DisplayName = "a constant's value")]
    [DataRow("Dark = 2", "Dark = 3", DisplayName = "an enum member's value")]
    [DataRow("Private mCount As Long", "Public mCount As Long", DisplayName = "a field's access")]
    [DataRow("Option Explicit", "Option Private Module", DisplayName = "an option")]
    public void Changes_WithWhatTheModuleDeclares(string written, string edited)
    {
        Assert.Contains(written, Module.ReplaceLineEndings("\n"));
        AssertChanged(Module.ReplaceLineEndings("\n").Replace(written, edited));
    }

    [TestMethod]
    [DataRow("Debug.Print \"Reported\"; Twice(1)", "Debug.Print \"Reported\"; Other.Twice(1)", DisplayName = "a member of another module")]
    [DataRow("mCount = mCount + 1", "mCount = mCount + Size", DisplayName = "another name")]
    [DataRow("mCount = mCount + 1", "Dim Local As Long", DisplayName = "a declaration")]
    public void Changes_WithTheNamesABodyUses_AndWhatItDeclares(string written, string edited)
        => AssertChanged(Module.Replace(written, edited));

    [TestMethod]
    public void IsNull_ForAModuleWithNoSyntaxTree()
        => Assert.IsNull(ModuleSurface.Of(new ModuleParseResult()));
}
