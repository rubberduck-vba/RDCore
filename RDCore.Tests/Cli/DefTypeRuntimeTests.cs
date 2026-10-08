namespace RDCore.Tests.Cli;

/// <summary>
/// A <c>Def&lt;Type&gt;</c> directive gives its type to the declarations that name none and whose first letter it covers, and that is the type the value of the variable has:
/// a value assigned to it is coerced to that type.
/// </summary>
[TestClass]
[TestCategory("MS-VBAL 5.2.2 Implicit Definition Directives")]
public sealed class DefTypeRuntimeTests
{
    private static Task<string[]> RunAsync(string directives, params string[] body)
        => ModuleWorkspace.RunAsync([], $"Attribute VB_Name = \"Program\"\r\n{directives}\r\nPublic Sub Main()\r\n{string.Join("\r\n", body)}\r\nEnd Sub\r\n");

    [TestMethod]
    public async Task ACoveredName_HasTheTypeOfTheDirective()
        => CollectionAssert.AreEqual(new[] { "4" }, await RunAsync("DefInt I-N", "Dim idx", "idx = 3.7", "Debug.Print idx"));

    [TestMethod]
    public async Task ANameThatIsNotCovered_IsAVariant()
        => CollectionAssert.AreEqual(new[] { "3.7" }, await RunAsync("DefInt I-N", "Dim abc", "abc = 3.7", "Debug.Print abc"));

    [TestMethod]
    public async Task ADescendingSpan_CoversTheSameNames()
        => CollectionAssert.AreEqual(new[] { "4" }, await RunAsync("DefInt N-I", "Dim idx", "idx = 3.7", "Debug.Print idx"));

    [TestMethod]
    public async Task ADirectiveWrittenInLowerCase_IsTheSameDirective()
        => CollectionAssert.AreEqual(new[] { "True" }, await RunAsync("defbool b", "Dim Bit", "Bit = 5", "Debug.Print Bit"));

    [TestMethod]
    public async Task ADeclaredType_IsNotOverriddenByTheDirective()
        => CollectionAssert.AreEqual(new[] { "3.7" }, await RunAsync("DefInt I-N", "Dim idx As Double", "idx = 3.7", "Debug.Print idx"));

    [TestMethod]
    public async Task AFunction_WithNoReturnType_ReturnsTheTypeOfTheDirectiveForItsName()
        => CollectionAssert.AreEqual(new[] { "8" }, await RunAsync("DefInt F\r\nPublic Function Fn()\r\nFn = 7.6\r\nEnd Function", "Debug.Print Fn"));
}
