using RDCore.CLI.Libraries;
using RDCore.SDK.Runtime.Libraries;

namespace RDCore.Tests.Cli;

/// <summary>
/// <c>rdc export-library</c> reads a type library of the machine it runs on, and describes it as a library description (<see cref="LibraryDescription"/>).
/// These run against the Scripting runtime, which every Windows machine has.
/// </summary>
[TestClass]
[TestCategory("RD-VBAL §2.3.1.2 References")]
public sealed class ExportLibraryTests
{
    private static readonly string ScriptingFile = Path.Combine(Environment.SystemDirectory, "scrrun.dll");

    private static LibraryDescription Export(string reference = "")
    {
        var loaded = TypeLibraryLocator.Load(string.IsNullOrEmpty(reference) ? ScriptingFile : reference);
        Assert.IsNotNull(loaded, "the machine has the library");
        return new TypeLibraryExporter().Export(loaded, "tests").Description;
    }

    [TestMethod]
    [OSCondition(OperatingSystems.Windows)]
    public void ALibrary_IsDescribedByTheNameItGivesItself_NotByItsFile()
    {
        var description = Export();

        Assert.AreEqual("Scripting", description.Name);
        Assert.AreEqual("scrrun.dll", description.Origin?.File, "the file, and not the folder it is in on this machine");
        Assert.IsEmpty(description.DependsOn);
    }

    // a file can hold more than one library, each a resource of its own number, and a reference names the one it means after the file: the VB6 runtime's
    // MSVBVM60.DLL\3 is VBRUN, as a .vbp references it, where MSVBVM60.DLL alone is its first, VBA.
    [TestMethod]
    [OSCondition(OperatingSystems.Windows)]
    public void ALibraryThatIsNotTheFirstOfItsFile_IsFoundByTheNumberOfItsResource()
    {
        var runtime = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.SystemX86), "MSVBVM60.DLL");

        var description = Export(runtime + @"\3");

        Assert.AreEqual("VBRUN", description.Name);
        Assert.AreEqual(@"MSVBVM60.DLL\3", description.Origin?.File, "the file and the number of its resource, and not the folder it is in on this machine");
        Assert.AreEqual("VBA", TypeLibraryLocator.Load(runtime)?.Name, "the file alone is its first library");
    }

    [TestMethod]
    [OSCondition(OperatingSystems.Windows)]
    public void ALibraryIsFoundByItsName_AsWellAsByItsFile()
    {
        var byName = TypeLibraryLocator.Load("Scripting");

        Assert.IsNotNull(byName);
        Assert.AreEqual("Scripting", byName.Name);
    }

    [TestMethod]
    [OSCondition(OperatingSystems.Windows)]
    public void ALibraryThatTheMachineDoesNotHave_IsNotFound()
        => Assert.IsNull(TypeLibraryLocator.Load("NoSuchLibraryOnAnyMachine"));

    [TestMethod]
    [OSCondition(OperatingSystems.Windows)]
    public void TheClassesOfALibrary_AreThoseVbaShows_UnderTheNamesOfTheirCoclasses()
    {
        var description = Export();

        var dictionary = description.Classes.Single(declared => declared.Name == "Dictionary");
        Assert.IsTrue(dictionary.IsCreatable);
        Assert.AreEqual("Scripting.Dictionary", dictionary.ProgId);
        Assert.IsTrue(description.Classes.Any(declared => declared.Name == "FileSystemObject"));
        Assert.IsFalse(description.Classes.Any(declared => declared.Name.StartsWith('_') && declared.Name != "_"), "an interface is shown as its coclass");
    }

    [TestMethod]
    [OSCondition(OperatingSystems.Windows)]
    public void TheMembersOfAClass_AreTheOnesOfItsDefaultInterface_AsTheKindsTheyAre()
    {
        var dictionary = Export().Classes.Single(declared => declared.Name == "Dictionary");

        var exists = dictionary.Members.Single(member => member.Name == "Exists");
        Assert.AreEqual(MemberKind.Method, exists.Kind);
        Assert.AreEqual("Boolean", exists.Type);
        Assert.AreEqual("Key", exists.Parameters.Single().Name);

        var count = dictionary.Members.Single(member => member.Name == "Count");
        Assert.AreEqual(MemberKind.PropertyGet, count.Kind);
        Assert.AreEqual("Long", count.Type);

        var accessors = dictionary.Members.Where(member => member.Name == "Item").Select(member => member.Kind).ToArray();
        CollectionAssert.AreEqual(new[] { MemberKind.PropertyGet, MemberKind.PropertyLet, MemberKind.PropertySet }, accessors, "Get, Let, Set, whichever the library lists them in");
        Assert.AreEqual("Value", dictionary.Members.First(member => member.Name == "Item" && member.Kind == MemberKind.PropertyLet).Parameters[^1].Name);
    }

    [TestMethod]
    [OSCondition(OperatingSystems.Windows)]
    public void TheEnumeratorOfACollection_IsDescribed_ThoughTheLibraryMarksItRestricted()
    {
        var files = Export().Classes.Single(declared => declared.Name == "Files");

        var enumerator = files.Members.Single(member => member.DispId == -4);
        Assert.IsTrue(enumerator.IsHidden);
        Assert.AreEqual("Object", enumerator.Type);
    }

    [TestMethod]
    [OSCondition(OperatingSystems.Windows)]
    public void AnEnumeration_HasTheValuesTheLibraryGivesItsMembers()
    {
        var iomode = Export().Enums.Single(declared => declared.Name == "IOMode");

        CollectionAssert.AreEqual(
            new[] { ("ForReading", 1L), ("ForWriting", 2L), ("ForAppending", 8L) },
            iomode.Members.Select(member => (member.Name, member.Value)).ToArray());
    }

    [TestMethod]
    [OSCondition(OperatingSystems.Windows)]
    public void AnOptionalParameter_HasTheDefaultTheLibraryGivesIt_AndAnEnumerationIsOfItsType()
    {
        var open = Export().Classes.Single(declared => declared.Name == "FileSystemObject").Members.Single(member => member.Name == "OpenTextFile");

        var mode = open.Parameters.Single(parameter => parameter.Name == "IOMode");
        Assert.AreEqual("IOMode", mode.Type);
        Assert.IsTrue(mode.IsOptional);
        Assert.AreEqual("1", mode.DefaultValue);
        Assert.IsTrue(open.Parameters.Single(parameter => parameter.Name == "FileName").IsByVal);
    }

    [TestMethod]
    [OSCondition(OperatingSystems.Windows)]
    public void WhatIsDescribed_IsTheSameEveryTime_AndSurvivesBeingWrittenAndRead()
    {
        var first = LibraryJson.Write(Export());
        var second = LibraryJson.Write(Export());

        Assert.AreEqual(first, second, "nothing in a description depends on when it was made");
        Assert.AreEqual("Scripting", LibraryJson.Read(first).Name);
    }
}
