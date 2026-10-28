using RDCore.Parsing;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Semantics;
using RDCore.SDK.Semantics.Static;

namespace RDCore.Tests.Cli;

/// <summary>
/// A module declares at most 10,000 names at module level, all kinds together, and the 10,001st is "Out of memory" (VBC09380): RD-VBA's cap, below the 10,800
/// to 11,000 at which MS-VBA refuses a module (recorded in Excel: oracle\size-limits G01-G10, J01-J05).
/// </summary>
/// <remarks>
/// One test loads such a module end to end; the others ask the rule of the declarations of a parsed module, which is all the rule reads, because loading ten
/// thousand declarations takes a couple of seconds.
/// </remarks>
[TestClass]
[TestCategory("MS-VBAL 5.2.3 Module Declarations")]
public sealed class ModuleNameLimitTests
{
    private static string Module(params IEnumerable<string>[] parts)
        => "Attribute VB_Name = \"Program\"\r\n" + string.Join("\r\n", parts.SelectMany(part => part)) + "\r\nPublic Sub Main()\r\nEnd Sub\r\n";

    private static IEnumerable<string> Variables(int count) => Enumerable.Range(0, count).Select(i => $"Private v{i} As Long");

    private static IEnumerable<string> Subs(int count) => Enumerable.Range(0, count).Select(i => $"Private Sub s{i}()\r\nEnd Sub");

    private static VBCompileErrorInfo[] Errors(string program)
    {
        var parsed = new ModuleParser().Parse(new Uri("file:///C:/Program.bas"), program);
        return [.. DeclarationStaticSemanticsEvaluator.CheckSyntax(parsed.SyntaxTree!, ConditionalCompilationBlocks.None)];
    }

    [TestMethod]
    public async Task TheTenThousandAndFirstName_IsOutOfMemory()
    {
        // Main is a name of the module too.
        var error = Assert.ContainsSingle(await ModuleWorkspace.LoadErrorsAsync([], Module(Variables(10_000))));

        StringAssert.Contains(error, "Out of memory");
    }

    [TestMethod]
    public void TenThousandNames_Compile()
        => Assert.IsEmpty(Errors(Module(Variables(9_999))));

    [TestMethod]
    public void NamesOfEveryKind_AreCountedTogether_AndTheErrorIsAtTheOneTooMany()
    {
        var error = Assert.ContainsSingle(Errors(Module(Variables(6_000), Subs(4_001))));

        Assert.AreEqual(VBCompileErrorId.OutOfMemory, error.VBCompileErrorId);
        // the 10,001st name is the 4,001st Sub: line 0 is the attribute, then 6,000 variables, then two lines a Sub.
        Assert.AreEqual(1 + 6_000 + 2 * 4_000, error.Location.Range.Start.Line);
    }

    [TestMethod]
    public void APropertyIsOneName_WhateverItsAccessors()
        => Assert.IsEmpty(Errors(Module(Variables(9_998), ["Public Property Get P() As Long", "End Property", "Public Property Let P(ByVal value As Long)", "End Property"])));
}
