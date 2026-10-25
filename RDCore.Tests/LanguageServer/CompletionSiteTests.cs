using RDCore.SDK.Model.Symbols.Completion;

namespace RDCore.Tests.LanguageServer;

/// <summary>
/// What is being written where a completion list is asked for, read off the line before the cursor: the name, the statement it is in, and what comes next in it.
/// </summary>
[TestClass]
public sealed class CompletionSiteTests
{
    [TestMethod]
    [DataRow("", "", true, "")]
    [DataRow("    Ms", "Ms", true, "")]
    [DataRow("100 Pr", "Pr", true, "")]
    [DataRow("x = 1: Deb", "Deb", true, "")]
    [DataRow("If x Then Ex", "Ex", true, "")]
    [DataRow("x = Le", "Le", false, "x =")]
    [DataRow("Call Foo(1, Tw", "Tw", false, "Call Foo(1,")]
    [DataRow("10 If a Then b = Le", "Le", false, "b =")]
    public void AName_IsWhatIsTyped_InTheStatementItIsIn(string line, string prefix, bool statementStart, string statement)
        => Assert.AreEqual(new CompletionSite(CompletionSiteKind.Name, prefix, IsStatementStart: statementStart, Statement: statement), Trimmed(CompletionSite.Read(line)));

    // the statement is the text before the name, as it is typed; what it ends with is the space before the name.
    private static CompletionSite Trimmed(CompletionSite site) => site with { Statement = site.Statement.TrimEnd() };

    [TestMethod]
    [DataRow("x = Widget.Gr", "Widget", "Gr")]
    [DataRow("x = Widget.", "Widget", "")]
    [DataRow("Debug.Print VBA.Strings.Le", "VBA.Strings", "Le")]
    [DataRow("x = Items(i + 1).Na", "Items(i + 1)", "Na")]
    [DataRow("x = Foo(Bar(\"(\")).Baz.Q", "Foo(Bar(\"(\")).Baz", "Q")]
    [DataRow("x = Name$.", "Name$", "")]
    [DataRow("x = Sheets![My Sheet].Ra", "Sheets![My Sheet]", "Ra")]
    [DataRow("    .Na", "", "Na")]
    [DataRow("    .Items(1).Na", ".Items(1)", "Na")]
    public void AMember_IsWhatIsTyped_AfterTheExpressionItIsAccessedOn(string line, string qualifier, string prefix)
        => Assert.AreEqual(new CompletionSite(CompletionSiteKind.Member, prefix, qualifier), CompletionSite.Read(line));

    [TestMethod]
    public void AType_IsWhatFollowsAs_OrNew()
    {
        Assert.AreEqual(new CompletionSite(CompletionSiteKind.Type, "Lo", Statement: "Dim x As"), Trimmed(CompletionSite.Read("Dim x As Lo")));
        Assert.AreEqual(new CompletionSite(CompletionSiteKind.NewType, "Co"), CompletionSite.Read("Dim x As New Co"));
        Assert.AreEqual(new CompletionSite(CompletionSiteKind.NewType, ""), CompletionSite.Read("Set x = New "));
        Assert.AreEqual(new CompletionSite(CompletionSiteKind.Type, "Coll", "VBA"), CompletionSite.Read("Dim x As VBA.Coll"));
        Assert.AreEqual(new CompletionSite(CompletionSiteKind.NewType, "", "VBA"), CompletionSite.Read("Set x = New VBA."));
    }

    [TestMethod]
    [DataRow("x = \"Hello wor")]
    [DataRow("x = 1 ' a comm")]
    [DataRow("Rem a comm")]
    [DataRow("x = 12")]
    [DataRow("x = 1.5")]
    public void NothingIsOffered_InALiteral_OrAComment(string line)
        => Assert.AreEqual(CompletionSiteKind.None, CompletionSite.Read(line).Kind);

    [TestMethod]
    public void AStringThatIsClosed_IsNotWhereTheCursorIs()
        => Assert.AreEqual(new CompletionSite(CompletionSiteKind.Name, "Le", Statement: "x = \"it's\" &"), Trimmed(CompletionSite.Read("x = \"it's\" & Le")));

    [TestMethod]
    public void ANameThatStartsWithRem_IsNotAComment()
        => Assert.AreEqual(new CompletionSite(CompletionSiteKind.Name, "Remark", IsStatementStart: true), CompletionSite.Read("Remark"));

    [TestMethod]
    [DataRow("", CompletionExpectation.Statement)]
    [DataRow("MsgBox ", CompletionExpectation.Operand)]
    [DataRow("x = ", CompletionExpectation.Operand)]
    [DataRow("x = a ", CompletionExpectation.Operator)]
    [DataRow("If a And ", CompletionExpectation.Operand)]
    [DataRow("If a ", CompletionExpectation.Operator)]
    [DataRow("x = Foo(1) ", CompletionExpectation.Operator)]
    [DataRow("x = \"s\" ", CompletionExpectation.Operator)]
    [DataRow("Foo 1, ", CompletionExpectation.Operand)]
    [DataRow("Foo Bar:=", CompletionExpectation.Operand)]
    [DataRow("For i = 1 ", CompletionExpectation.Operator)]
    [DataRow("For i = 1 To ", CompletionExpectation.Operand)]
    public void WhatComesNext_IsAnOperand_OrAnOperator(string line, CompletionExpectation expected)
        => Assert.AreEqual(expected, CompletionSite.Read(line).Expectation);

    [TestMethod]
    public void TheTokens_AreTheLexersNotTheParsers()
        => CollectionAssert.AreEqual(
            new[] { "x$", "=", "\"\"", "&", "[My Field]", "<>", "12.5", ":=" },
            CompletionStatement.Tokenize("x$ = \"a \"\"b\"\"\" & [My Field] <> 12.5 :=").ToArray());
}
