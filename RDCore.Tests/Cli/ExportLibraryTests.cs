using NSubstitute;
using RDCore.CLI.App.Commands;
using RDCore.CLI.Libraries;
using RDCore.CLI.ConsoleIO;
using RDCore.SDK.Runtime.Libraries;
using System.IO.Abstractions.TestingHelpers;
using System.Runtime.Versioning;

namespace RDCore.Tests.Cli;

/// <summary>
/// <c>rdc export-library</c> reads a type library of the machine it runs on, and describes it as a library description (<see cref="LibraryDescription"/>).
/// These run against the Scripting runtime, which every Windows machine has.
/// </summary>
[TestClass]
[TestCategory("RD-VBAL §2.3.1.2 References")]
[SupportedOSPlatform("windows")]
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

    // a library is registered under the locale it is for, and the VB6 runtime's VBRUN is registered for English (9) only, with no neutral (0) registration.
    [TestMethod]
    [OSCondition(OperatingSystems.Windows)]
    [DataRow("{EA544A21-C82D-11D1-A3E4-00A0C90AEA82}", DisplayName = "by its identifier")]
    [DataRow("VBRUN", DisplayName = "by its name")]
    public void ALibraryRegisteredForALocale_IsFound_WithItsFile(string reference)
    {
        var loaded = TypeLibraryLocator.Load(reference);

        Assert.IsNotNull(loaded, "the machine registers VBRUN for one locale");
        Assert.AreEqual("VBRUN", loaded.Name);
        Assert.AreEqual(@"MSVBVM60.DLL\3", loaded.File is null ? null : TypeLibraryLocator.FileNameOf(loaded.File), ignoreCase: true, "the file it is registered in");
    }

    [TestMethod]
    [OSCondition(OperatingSystems.Windows)]
    public void ALibraryThatTheMachineDoesNotHave_IsNotFound()
        => Assert.IsNull(TypeLibraryLocator.Load("NoSuchLibraryOnAnyMachine"));

    // Data\Widgets.tlb declares a class Widget whose default interface has a property Thing, and whose events interface an event Changed(Value), both of the
    // type IThing of a library (Missing, {0D7C2B6E-5E39-4C47-9D0B-1A6F0E5C3A01}) that no machine has; Count and Ready are of no other library.
    [TestMethod]
    [OSCondition(OperatingSystems.Windows)]
    public void WhatIsDeclaredInTermsOfALibraryTheMachineDoesNotHave_IsLeftOut_AndTheRestDescribed()
    {
        var export = new TypeLibraryExporter().Export(
            TypeLibraryLocator.Load(Path.Combine(AppContext.BaseDirectory, "Cli", "Data", "Widgets.tlb")) ?? throw new AssertFailedException("the fixture loads"), "tests");

        var widget = export.Description.Classes.Single(declared => declared.Name == "Widget");
        CollectionAssert.AreEqual(new[] { "Count" }, widget.Members.Select(member => member.Name).ToArray());
        CollectionAssert.AreEqual(new[] { "Ready" }, widget.Events.Select(declared => declared.Name).ToArray());
        CollectionAssert.AreEquivalent(new[] { "member _Widget.Thing", "event _WidgetEvents.Changed" }, export.Skipped.ToArray());
        Assert.IsEmpty(export.Description.DependsOn, "a library the machine does not have is not one it can be described with");
    }

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

    // VB6.OLB flags none of its optional parameters: it counts them (FUNCDESC.cParamsOpt), and they are the last ones.
    [TestMethod]
    [OSCondition(OperatingSystems.Windows)]
    public void TheParametersALibraryCountsAsOptional_AreOptional()
    {
        var file = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Microsoft Visual Studio", "VB98", "VB6.OLB");
        if (!File.Exists(file))
        {
            Assert.Inconclusive("Visual Basic 6.0 is not installed on this machine.");
        }

        var form = Export(file).Classes.Single(declared => declared.Name == "Form");
        var show = form.Members.Single(member => member.Name == "Show");
        var move = form.Members.Single(member => member.Name == "Move");

        Assert.IsTrue(show.Parameters.All(parameter => parameter.IsOptional), "Show([Modal], [OwnerForm])");
        CollectionAssert.AreEqual(new[] { false, true, true, true }, move.Parameters.Select(parameter => parameter.IsOptional).ToArray(), "Move(Left, [Top], [Width], [Height])");
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

    // the type library a library is linked with, which is the one it was built against: a machine can have several of a name.
    [TestMethod]
    [OSCondition(OperatingSystems.Windows)]
    public void ADependency_IsDescribedAsTheTypeLibraryTheLibraryIsLinkedWith()
    {
        var runtime = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.SystemX86), "MSVBVM60.DLL");

        var description = Export(runtime + @"\3");

        CollectionAssert.Contains(description.DependsOn.ToArray(), new LibraryIdentity("stdole", new Guid("00020430-0000-0000-C000-000000000046"), 2, 0));
    }

    private static readonly LibraryDescription OtherScripting = new()
    {
        Name = "Scripting",
        Origin = new LibraryOrigin { Guid = "{0D7C2B6E-5E39-4C47-9D0B-1A6F0E5C3AFF}", Version = "1.0" },
        Classes = [new ClassDescription { Name = "Other" }],
    };

    // the files of the folder before the export, each "<file>=<what it describes>": "other" is another type library of the name, "own" the one exported.
    [TestMethod]
    [OSCondition(OperatingSystems.Windows)]
    [DataRow("", @"c:\out\Scripting.json", DisplayName = "the folder has none of the name")]
    [DataRow("Scripting.json=other", @"c:\out\Scripting.scrrun.json", DisplayName = "the folder has another library of the name")]
    [DataRow("Scripting.json=other;Scripting.scrrun.json=other", @"c:\out\Scripting.scrrun.v1_0.json", DisplayName = "and another of its file's name")]
    [DataRow("Scripting.json=other;Scripting.scrrun.json=other;Scripting.scrrun.v1_0.json=other", @"c:\out\Scripting.scrrun.v1_0.2.json", DisplayName = "and another of its version")]
    [DataRow("Scripting.json=other;Scripting.scrrun.json=own", @"c:\out\Scripting.scrrun.json", DisplayName = "and its own, which is written again")]
    public async Task ALibrary_IsWrittenAsTheLibraryOfItsName_UnlessTheFolderHasAnotherOfTheName_AndNeverOverAnother(string before, string expected)
    {
        var files = new MockFileSystem();
        files.AddDirectory(@"c:\out");
        var others = new List<string>();
        foreach (var (file, what) in before.Split(';', StringSplitOptions.RemoveEmptyEntries).Select(entry => entry.Split('=')).Select(parts => (parts[0], parts[1])))
        {
            var path = Path.Combine(@"c:\out", file);
            files.AddFile(path, new MockFileData(LibraryJson.Write(what == "own" ? Export() : OtherScripting)));
            if (what == "other")
            {
                others.Add(path);
            }
        }

        var exit = await new ExportLibraryCommand(Substitute.For<IConsoleMessageWriter>(), files).ExecuteAsync([ScriptingFile, "-o", @"c:\out"], CancellationToken.None);

        Assert.AreEqual(0, exit);
        Assert.AreEqual("{420B2830-E718-11CF-893D-00A0C9054228}", LibraryJson.Read(files.File.ReadAllText(expected)).Origin?.Guid);
        foreach (var other in others)
        {
            Assert.AreEqual(OtherScripting.Origin?.Guid, LibraryJson.Read(files.File.ReadAllText(other)).Origin?.Guid, $"{other} is left as it is");
        }

        Assert.HasCount(others.Count + 1, files.Directory.GetFiles(@"c:\out"), "one file of the library exported");
    }

    // stdole2.tlb's StdFunctions: two functions, LoadPicture's optional parameters with the defaults the library gives them.
    [TestMethod]
    [OSCondition(OperatingSystems.Windows)]
    public void AModulesFunctions_AreItsProcedures()
    {
        var export = new TypeLibraryExporter().Export(TypeLibraryLocator.Load(Path.Combine(Environment.SystemDirectory, "stdole2.tlb"))!, "tests");

        var module = export.Description.Modules.Single();
        Assert.AreEqual("StdFunctions", module.Name);
        CollectionAssert.AreEqual(new[] { "LoadPicture", "SavePicture" }, module.Members.Select(member => member.Name).ToArray());
        var load = module.Members[0];
        Assert.AreEqual("StdPicture", load.Type);
        CollectionAssert.AreEqual(
            new[] { ("filename", "Variant", (string?)null), ("widthDesired", "Long", "0"), ("heightDesired", "Long", "0"), ("flags", "LoadPictureConstants", "0") },
            load.Parameters.Select(parameter => (parameter.Name, parameter.Type, parameter.DefaultValue)).ToArray());
        Assert.IsTrue(load.Parameters.All(parameter => parameter.IsOptional && parameter.IsByVal));
        Assert.IsNull(module.Members[1].Type, "SavePicture is a Sub");
        Assert.IsFalse(export.Skipped.Any(skipped => skipped.Contains("StdFunctions")));
    }

    // VBRUN's RecordsetTypeConstants: a module of constants alone.
    [TestMethod]
    [OSCondition(OperatingSystems.Windows)]
    public void AModulesConstants_AreDescribed_WithTheirTypesAndValues()
    {
        var runtime = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.SystemX86), "MSVBVM60.DLL");

        var module = Export(runtime + @"\3").Modules.Single(declared => declared.Name == "RecordsetTypeConstants");

        Assert.IsEmpty(module.Members);
        CollectionAssert.AreEqual(
            new[] { ("vbRSTypeTable", "Integer", "0"), ("vbRSTypeDynaset", "Integer", "1"), ("vbRSTypeSnapShot", "Integer", "2") },
            module.Constants.Select(constant => (constant.Name, constant.Type, constant.Value)).ToArray());
    }

    [TestMethod]
    [OSCondition(OperatingSystems.Windows)]
    public async Task ALibraryExportedAgain_IsWrittenOverItsOwnDescription()
    {
        var files = new MockFileSystem();
        files.AddDirectory(@"c:\out");
        var command = new ExportLibraryCommand(Substitute.For<IConsoleMessageWriter>(), files);

        await command.ExecuteAsync([ScriptingFile, "-o", @"c:\out"], CancellationToken.None);
        await command.ExecuteAsync([ScriptingFile, "-o", @"c:\out"], CancellationToken.None);

        CollectionAssert.AreEqual(new[] { @"c:\out\Scripting.json" }, files.Directory.GetFiles(@"c:\out"));
    }
}
