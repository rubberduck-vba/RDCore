using RDCore.SDK.Runtime.Abstract.Execution;
using static RDCore.Tests.Cli.ModuleWorkspace;

namespace RDCore.Tests.Cli;

/// <summary>
/// The name of a <c>Function</c> or a <c>Property Get</c> in its own code is its result variable (<strong>MS-VBAL §5.3.1</strong>), a variable a <c>Declare</c>d
/// procedure is given by reference as any other (<strong>MS-VBAL §5.3.1.11</strong>): what the library writes there is what the function returns, in both engines.
/// These call the libraries of Windows itself, and are skipped elsewhere.
/// </summary>
[TestClass]
[TestCategory("MS-VBAL 5.2.3.5 External Procedure Declaration")]
[OSCondition(OperatingSystems.Windows)]
public sealed class DeclareResultVariableTests
{
    private static string Program(params string[] members)
        => "Attribute VB_Name = \"Program\"\r\nOption Explicit\r\n" +
            "Private Declare PtrSafe Sub CopyMemory Lib \"kernel32\" Alias \"RtlMoveMemory\" (Destination As Any, Source As Any, ByVal Length As LongPtr)\r\n" +
            "Private Declare PtrSafe Sub CopyDouble Lib \"kernel32\" Alias \"RtlMoveMemory\" (Destination As Double, Source As Double, ByVal Length As LongPtr)\r\n" +
            string.Join("\r\n", members) + "\r\n";

    private static async Task Expect(string[] expected, string program, VBExecutionEngine engine)
        => Assert.AreEqual(string.Join(" | ", expected), string.Join(" | ", await RunAsync([], program, engine)));

    // `As Any` is given the result variable's address, which the library writes 8 bytes to.
    [TestMethod]
    [DataRow(VBExecutionEngine.Interpreter, DisplayName = "Interpreter")]
    [DataRow(VBExecutionEngine.Compiled, DisplayName = "Compiled")]
    public async Task AFunctionsResultVariable_PassedAsAny_ReturnsWhatTheLibraryWrote(VBExecutionEngine engine)
        => await Expect(["2.5"], Program(
            "Private Function Copied() As Double",
            "    Dim Source As Double",
            "    Source = 2.5",
            "    CopyMemory Copied, Source, 8",
            "End Function",
            "Public Sub Main()",
            "    Debug.Print Copied()",
            "End Sub"), engine);

    // a parameter of the result variable's own type is given its address too.
    [TestMethod]
    [DataRow(VBExecutionEngine.Interpreter, DisplayName = "Interpreter")]
    [DataRow(VBExecutionEngine.Compiled, DisplayName = "Compiled")]
    public async Task AFunctionsResultVariable_PassedByRefAsItsType_ReturnsWhatTheLibraryWrote(VBExecutionEngine engine)
        => await Expect(["2.5"], Program(
            "Private Function Copied() As Double",
            "    Dim Source As Double",
            "    Source = 2.5",
            "    CopyDouble Copied, Source, 8",
            "End Function",
            "Public Sub Main()",
            "    Debug.Print Copied()",
            "End Sub"), engine);

    // what the function assigned before the call is what the library is given, and what it assigns after is what it returns.
    [TestMethod]
    [DataRow(VBExecutionEngine.Interpreter, DisplayName = "Interpreter")]
    [DataRow(VBExecutionEngine.Compiled, DisplayName = "Compiled")]
    public async Task AFunctionsResultVariable_PassedAsAny_IsTheVariableBeforeAndAfter(VBExecutionEngine engine)
        => await Expect(["1.5 4"], Program(
            "Private Function Copied() As Double",
            "    Dim Target As Double",
            "    Copied = 1.5",
            "    CopyMemory Target, Copied, 8",
            "    Copied = Target + 2.5",
            "    Debug.Print Target & \" \" & Copied",
            "End Function",
            "Public Sub Main()",
            "    Dim r As Double",
            "    r = Copied()",
            "End Sub"), engine);

    // a Property Get's name in its own code is its result variable as a function's is.
    [TestMethod]
    [DataRow(VBExecutionEngine.Interpreter, DisplayName = "Interpreter")]
    [DataRow(VBExecutionEngine.Compiled, DisplayName = "Compiled")]
    public async Task APropertyGetsResultVariable_PassedAsAny_ReturnsWhatTheLibraryWrote(VBExecutionEngine engine)
        => await Expect(["2.5"], Program(
            "Private Property Get Copied() As Double",
            "    Dim Source As Double",
            "    Source = 2.5",
            "    CopyMemory Copied, Source, 8",
            "End Property",
            "Public Sub Main()",
            "    Debug.Print Copied",
            "End Sub"), engine);
}
