namespace RDCore.Tests.Cli;

/// <summary>
/// A function and a parameter can be declared with a type-declaration character in place of an <c>As</c> clause (<strong>MS-VBAL §3.3.5.3</strong>): <c>Function Title$()</c>
/// returns a <c>String</c>, and <c>n%</c> is an <c>Integer</c>. The declared type is the one the character says, and not a <c>Variant</c> that the character then disagrees with.
/// </summary>
[TestClass]
public sealed class TypeHintedDeclarationTests
{
    private static string Program(params string[] lines)
        => $"Attribute VB_Name = \"Program\"\r\n{string.Join("\r\n", lines)}\r\n";

    private static readonly string[] Source =
    [
        "Option Explicit",
        "Public Function Title$()",
        "    Title$ = \"hello\"",
        "End Function",
        "Public Function Twice%(ByVal n%)",
        "    Twice% = n% * 2",
        "End Function",
        "Public Sub Main()",
        "    Debug.Print Title$()",
        "    Debug.Print Twice%(21)",
        "End Sub",
    ];

    [TestMethod]
    public async Task ATypeDeclarationCharacterOnAFunctionAndAParameter_IsTheDeclaredType_NotACompileError()
    {
        var payload = await ModuleWorkspace.SemanticsAsync([], Program(Source));

        var model = payload.Modules.Single();
        Assert.IsEmpty(model.Procedures.SelectMany(procedure => procedure.CompileErrors), "the character and the declared type agree");
        Assert.IsTrue(model.Procedures.All(procedure => procedure.IsFullyAnalyzed));
    }

    [TestMethod]
    public async Task AFunctionAndAParameterDeclaredWithTheCharacter_RunAsTheTypeItSays()
        => CollectionAssert.AreEqual(new[] { "hello", "42" }, await ModuleWorkspace.RunAsync([], Program(Source)));
}
