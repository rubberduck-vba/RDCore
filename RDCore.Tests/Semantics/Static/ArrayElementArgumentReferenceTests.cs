using RDCore.SDK.Semantics;
using RDCore.Tests.Cli;

namespace RDCore.Tests.Semantics.Static;

/// <summary>
/// How an element of an array, passed as an argument, counts as a reference of the array (<strong>MS-VBAL §5.3.1.11</strong>): by reference, the procedure
/// may assign the array through it, as it may a variable passed by reference.
/// </summary>
[TestClass]
[TestCategory("RD-VBAL §5.0.3 Semantic Analysis")]
public sealed class ArrayElementArgumentReferenceTests
{
    private const string Program = """
        Attribute VB_Name = "Program"
        Option Explicit
        Public Sub Main()
            Dim filled(3) As Long
            Fill filled(3)
            Debug.Print filled(3)
            Dim peeked(3) As Long
            Peek peeked(1)
            Dim relayed(3) As Long
            Relay relayed(0)
            Dim shown(3) As Long
            Show shown(0)
            Dim named(3) As Long
            Fill x:=named(2)
            Dim written(3) As Long
            Fill (written(1))
        End Sub
        Private Sub Fill(ByRef x As Long)
            x = 1
        End Sub
        Private Sub Peek(ByRef x As Long)
            Debug.Print x
        End Sub
        Private Sub Relay(ByRef x As Long)
            Fill x
        End Sub
        Private Sub Show(ByVal x As Long)
            Debug.Print x
        End Sub
        """;

    [TestMethod]
    [DataRow("filled", 1, 0, 1, DisplayName = "an element given to a procedure that assigns it, then read")]
    [DataRow("peeked", 1, 0, 0, DisplayName = "an element given by reference to a procedure that only reads it")]
    [DataRow("relayed", 0, 0, 1, DisplayName = "an element given to a procedure that passes it on to one that assigns it")]
    [DataRow("shown", 1, 0, 0, DisplayName = "an element given to a ByVal parameter")]
    [DataRow("named", 0, 0, 1, DisplayName = "an element given by name")]
    [DataRow("written", 1, 0, 0, DisplayName = "an element in parentheses, which is a value")]
    public async Task AnElementPassedAsAnArgument_IsAReferenceOfItsArray(string name, int reads, int writes, int passed)
    {
        var model = await ModuleWorkspace.ModelAsync([], Program.ReplaceLineEndings("\r\n") + "\r\n");

        Assert.IsTrue(model.IsValid, string.Join("; ", model.CompileErrors.Select(error => error.Verbose)));
        var array = model.Declarations.Single(declaration => declaration.Name == name);
        Assert.AreEqual(new DeclarationReferences(reads, writes, passed), array.References);
    }
}
