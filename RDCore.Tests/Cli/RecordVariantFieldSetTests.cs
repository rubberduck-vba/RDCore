using RDCore.SDK.Runtime.Abstract.Execution;
using static RDCore.Tests.Cli.ModuleWorkspace;

namespace RDCore.Tests.Cli;

/// <summary>
/// <c>Set</c> of a record's <c>Variant</c> field (<strong>MS-VBAL §5.4.3.9</strong>): the field holds the object as a <c>Variant</c> variable does, and a
/// member of it is called through the field, as MS-VBA does (recorded in Excel).
/// </summary>
[TestClass]
[TestCategory("MS-VBAL 5.4.3.9 Set Statement")]
public sealed class RecordVariantFieldSetTests
{
    private const string Program =
        "Attribute VB_Name = \"Program\"\r\nOption Explicit\r\n" +
        "Private Type TV\r\n    V As Variant\r\n    N As Integer\r\nEnd Type\r\n" +
        "Public Sub Main()\r\n" +
        "    Dim X As TV, V As Variant\r\n" +
        "    Set X.V = New Collection\r\n" +
        "    X.V.Add 1\r\n" +
        "    Debug.Print \"count \" & X.V.Count\r\n" +
        "    Debug.Print \"vt \" & VarType(X.V) & \" \" & TypeName(X.V)\r\n" +
        "    Set V = New Collection\r\n" +
        "    V.Add 1\r\n" +
        "    Debug.Print \"plain \" & V.Count\r\n" +
        "End Sub\r\n";

    // the Set raises no 424, Object required: the field holds the object, as a Variant variable does.
    [TestMethod]
    [DataRow(VBExecutionEngine.Interpreter, DisplayName = "Interpreter")]
    [DataRow(VBExecutionEngine.Compiled, DisplayName = "Compiled")]
    public async Task AVariantFieldSetToAnObject_HoldsIt_AsAVariantVariableDoes(VBExecutionEngine engine)
        => CollectionAssert.AreEqual(new[] { "count 1", "vt 9 Collection", "plain 1" }, await RunAsync([], Program, engine));
}
