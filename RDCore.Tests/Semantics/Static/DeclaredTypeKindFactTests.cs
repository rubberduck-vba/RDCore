using RDCore.SDK.Semantics;
using RDCore.Tests.Cli;
using System.Collections.Immutable;

namespace RDCore.Tests.Semantics.Static;

/// <summary>
/// What the facts of a declaration say of the type it is declared as (<strong>MS-VBAL §5.2.3.1.4</strong>, <strong>§5.2.3.1.5</strong>), and of an array
/// (<strong>MS-VBAL §5.2.3.1.3</strong>, and <strong>§5.4.3.3</strong> for one a <c>ReDim</c> declares).
/// </summary>
[TestClass]
[TestCategory("RD-VBAL §5.0.3 Semantic Analysis")]
public sealed class DeclaredTypeKindFactTests
{
    private static readonly (string, string)[] Classes =
    [
        ("Leaf", ModuleWorkspace.ClassModule("Leaf", "Public Value As Long")),
    ];

    private const string Program = """
        Attribute VB_Name = "Program"
        Option Explicit
        Private Type Point
            X As Long
        End Type
        Private Enum Colour
            Red
        End Enum
        Private mLeaf As Leaf
        Public Sub Main()
            Dim n As Long, s As String * 4, p As LongPtr, v, w As Variant, o As Object, c As Leaf, a As New Leaf, pt As Point, k As Colour
            Dim numbers() As Long, leaves(3) As Leaf, items(1 To 2), points() As Point
            ReDim grown(2) As String
            Debug.Print n, s, p, v, w, k, pt.X, numbers(0), items(1), points(0).X, grown(0)
            Set o = c: Set c = a: Set leaves(0) = c
        End Sub
        Private Function Make() As Leaf
        End Function
        Private Property Get Count() As Integer
        End Property
        Private Function Whatever()
        End Function
        Private Sub Nothing2()
        End Sub
        """;

    private static async Task<ImmutableDictionary<string, DeclarationFact>> DeclarationsAsync()
    {
        var model = await ModuleWorkspace.ModelAsync(Classes, Program.ReplaceLineEndings("\r\n") + "\r\n");

        Assert.IsTrue(model.IsValid, string.Join("; ", model.CompileErrors.Select(error => error.Verbose)));
        return model.Declarations.ToImmutableDictionary(declaration => declaration.Name, StringComparer.OrdinalIgnoreCase);
    }

    [TestMethod]
    [DataRow("n", DeclaredTypeKind.Intrinsic, false, DisplayName = "a Long")]
    [DataRow("s", DeclaredTypeKind.Intrinsic, false, DisplayName = "a fixed-length String")]
    [DataRow("p", DeclaredTypeKind.Intrinsic, false, DisplayName = "a LongPtr")]
    [DataRow("v", DeclaredTypeKind.Variant, false, DisplayName = "a variable with no As clause")]
    [DataRow("w", DeclaredTypeKind.Variant, false, DisplayName = "a Variant")]
    [DataRow("o", DeclaredTypeKind.Object, false, DisplayName = "an Object")]
    [DataRow("c", DeclaredTypeKind.Class, false, DisplayName = "a class")]
    [DataRow("a", DeclaredTypeKind.Class, false, DisplayName = "a class, As New")]
    [DataRow("mLeaf", DeclaredTypeKind.Class, false, DisplayName = "a class, a variable of the module")]
    [DataRow("pt", DeclaredTypeKind.UserDefinedType, false, DisplayName = "a user-defined type")]
    [DataRow("k", DeclaredTypeKind.Enum, false, DisplayName = "an Enum")]
    [DataRow("numbers", DeclaredTypeKind.Intrinsic, true, DisplayName = "an array of Long, with no bounds")]
    [DataRow("leaves", DeclaredTypeKind.Class, true, DisplayName = "an array of a class, with bounds")]
    [DataRow("items", DeclaredTypeKind.Variant, true, DisplayName = "an array of Variant")]
    [DataRow("points", DeclaredTypeKind.UserDefinedType, true, DisplayName = "an array of a user-defined type")]
    [DataRow("grown", DeclaredTypeKind.Intrinsic, true, DisplayName = "an array a ReDim declares")]
    [DataRow("Make", DeclaredTypeKind.Class, false, DisplayName = "a function, by what it returns")]
    [DataRow("Count", DeclaredTypeKind.Intrinsic, false, DisplayName = "a Property Get, by what it returns")]
    [DataRow("Whatever", DeclaredTypeKind.Variant, false, DisplayName = "a function with no As clause")]
    public async Task ADeclaration_StatesTheKindOfItsType(string name, DeclaredTypeKind kind, bool isArray)
    {
        var declarations = await DeclarationsAsync();

        Assert.IsTrue(declarations.TryGetValue(name, out var declaration), string.Join(", ", declarations.Keys));
        Assert.AreEqual(kind, declaration.TypeKind);
        Assert.AreEqual(isArray, declaration.IsArray);
    }

    [TestMethod]
    public async Task ASub_HasNoType()
    {
        var declarations = await DeclarationsAsync();

        Assert.IsNull(declarations["Nothing2"].TypeKind);
        Assert.IsFalse(declarations["Nothing2"].IsArray);
    }
}
