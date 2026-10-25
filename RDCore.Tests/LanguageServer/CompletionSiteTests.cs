using RDCore.SDK.Model.Symbols.Completion;

namespace RDCore.Tests.LanguageServer;

/// <summary>
/// What is being written where a completion list is asked for, read off the line before the cursor.
/// </summary>
[TestClass]
public sealed class CompletionSiteTests
{
    [TestMethod]
    [DataRow("", "", true)]
    [DataRow("    Ms", "Ms", true)]
    [DataRow("100 Pr", "Pr", true)]
    [DataRow("x = 1: Deb", "Deb", true)]
    [DataRow("If x Then Ex", "Ex", true)]
    [DataRow("x = Le", "Le", false)]
    [DataRow("Call Foo(1, Tw", "Tw", false)]
    public void AName_IsWhatIsTyped_AndWhetherItStartsAStatement(string line, string prefix, bool statementStart)
        => Assert.AreEqual(new CompletionSite(CompletionSiteKind.Name, prefix, IsStatementStart: statementStart), CompletionSite.Read(line));

    [TestMethod]
    [DataRow("x = Widget.Gr", "Widget", "Gr")]
    [DataRow("x = Widget.", "Widget", "")]
    [DataRow("Debug.Print VBA.Strings.Le", "VBA.Strings", "Le")]
    [DataRow("x = Items(i + 1).Na", "Items(i + 1)", "Na")]
    [DataRow("x = Foo(Bar(\"(\")).Baz.Q", "Foo(Bar(\"(\")).Baz", "Q")]
    [DataRow("x = Name$.", "Name$", "")]
    [DataRow("x = Sheets![My Sheet].Ra", "Sheets![My Sheet]", "Ra")]
    [DataRow("    .Na", "", "Na")]
    public void AMember_IsWhatIsTyped_AfterTheExpressionItIsAccessedOn(string line, string qualifier, string prefix)
        => Assert.AreEqual(new CompletionSite(CompletionSiteKind.Member, prefix, qualifier), CompletionSite.Read(line));

    [TestMethod]
    public void AType_IsWhatFollowsAs_OrNew()
    {
        Assert.AreEqual(new CompletionSite(CompletionSiteKind.Type, "Lo"), CompletionSite.Read("Dim x As Lo"));
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
        => Assert.AreEqual(new CompletionSite(CompletionSiteKind.Name, "Le", IsStatementStart: false), CompletionSite.Read("x = \"it's\" & Le"));

    [TestMethod]
    public void ANameThatStartsWithRem_IsNotAComment()
        => Assert.AreEqual(new CompletionSite(CompletionSiteKind.Name, "Remark", IsStatementStart: true), CompletionSite.Read("Remark"));
}
