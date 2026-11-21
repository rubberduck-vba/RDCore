using RDCore.SDK.Runtime.Libraries;
using static RDCore.Tests.Runtime.Libraries.LibraryFixtures;

namespace RDCore.Tests.Runtime.Libraries;

/// <summary>
/// The file a library is described in: plain JSON, read by people in a review as much as by the platform.
/// </summary>
[TestClass]
public sealed class LibraryJsonTests
{
    [TestMethod]
    public void ADescription_SurvivesBeingWrittenAndRead()
    {
        var read = LibraryJson.Read(LibraryJson.Write(Widgets));

        Assert.AreEqual(Widgets.Name, read.Name);
        Assert.AreEqual(Widgets.Classes.Length, read.Classes.Length);
        Assert.AreEqual("Part", read.Classes[0].Members[0].Type);
        Assert.AreEqual(MemberKind.PropertyLet, read.Classes[0].Members[2].Kind);
        Assert.AreEqual("10", read.Classes[0].Members[3].Parameters[1].DefaultValue);
        Assert.IsTrue(read.Classes[0].Members[4].Type is "String()");
        Assert.AreEqual(1, read.Enums[0].Members[1].Value);
        Assert.AreEqual("tests", read.Origin?.ExportedBy);
    }

    [TestMethod]
    public void WhatIsNotStated_IsNotWritten_AndEnumerationsAreWordsNotNumbers()
    {
        var text = LibraryJson.Write(Widgets);

        StringAssert.Contains(text, "\"propertyGet\"");
        Assert.DoesNotContain("\"isHidden\": false", text);
        Assert.DoesNotContain("\"isOptional\": false", text);
        StringAssert.Contains(text, "\"name\": \"Widgets\"");
    }

    [TestMethod]
    public void AFileEditedByHand_MayHaveCommentsAndTrailingCommas_AndNamesInAnyCase()
    {
        var read = LibraryJson.Read("""
            {
              // a library
              "Name": "Tiny",
              "classes": [ { "name": "Thing", "isCreatable": true, }, ],
            }
            """);

        Assert.AreEqual("Tiny", read.Name);
        Assert.IsTrue(read.Classes.Single().IsCreatable);
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("not json")]
    [DataRow("{ }")]
    [DataRow("{ \"name\": \"  \" }")]
    public void ADescriptionThatIsNotOne_IsRefused(string text)
        => Assert.Throws<InvalidDataException>(() => LibraryJson.Read(text));

    [TestMethod]
    [DataRow("""{ "name": "Twice", "classes": [ { "name": "Same" }, { "name": "same" } ] }""", DisplayName = "a class declared twice, in another case")]
    [DataRow("""{ "name": "Twice", "enums": [ { "name": "Same" }, { "name": "SAME" } ] }""", DisplayName = "an enumeration declared twice")]
    [DataRow("""{ "name": "Twice", "classes": [ { "name": "Same" } ], "enums": [ { "name": "Same" } ] }""", DisplayName = "a class and an enumeration of one name")]
    public void ALibraryThatDeclaresANameTwice_IsRefused_NotQuietlyCollapsed(string text)
    {
        var exception = Assert.Throws<InvalidDataException>(() => LibraryJson.Read(text));

        StringAssert.Contains(exception.Message, "more than once");
    }

    [TestMethod]
    public void AFormatThisDoesNotKnow_IsRefused_NotMisread()
    {
        var future = LibraryJson.CurrentFormatVersion + 1;

        var exception = Assert.Throws<InvalidDataException>(() => LibraryJson.Read($$"""{ "formatVersion": {{future}}, "name": "Future" }"""));

        StringAssert.Contains(exception.Message, $"version {future}");
    }
}
