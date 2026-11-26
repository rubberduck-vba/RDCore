using RDCore.SDK.Runtime.Libraries;
using RDCore.Tests.Cli;
using System.IO.Abstractions;

namespace RDCore.Tests.Runtime.Scripting;

/// <summary>
/// The Scripting library's <c>FileSystemObject</c>, as scrrun.dll has it (measured over COM and in VBScript on Windows): made by <c>New</c> and by
/// <c>CreateObject</c>, its path functions, the text streams it reads and writes, the files and folders it gives, copies, moves and deletes, and the errors
/// it raises.
/// </summary>
/// <remarks>
/// A program's files are the machine's: each test's are in a folder of its own, <c>r</c>, which is deleted after it, and every path is built from it with
/// <c>BuildPath</c>, in the separator of the platform the tests run on.
/// </remarks>
[TestClass]
public sealed class ScriptingFileSystemTests
{
    private static ModuleWorkspace.WorkspaceLibraries Referenced => new(
        ["Scripting"], new DirectoryLibrarySource(new FileSystem(), RDCore.Tests.Runtime.Libraries.ShippedLibrariesTests.Symbols),
        RDCore.External.Automation.UnavailableAutomationServer.Instance);

    private static ModuleWorkspace.WorkspaceLibraries Unreferenced => Referenced with { References = [] };

    private const string Made = "Set fso = CreateObject(\"Scripting.FileSystemObject\")";

    // the program run with r the full path of a folder of its own, which is deleted after it, read-only files and all.
    private static async Task<string[]> RunAsync(ModuleWorkspace.WorkspaceLibraries libraries, params string[] lines)
    {
        var folder = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), $"rdcore-fso-{Guid.NewGuid():N}")).FullName;
        try
        {
            return await ModuleWorkspace.RunAsync([], Source([$"r = \"{folder}\"", .. lines]), libraries);
        }
        finally
        {
            foreach (var file in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
            {
                File.SetAttributes(file, FileAttributes.Normal);
            }

            Directory.Delete(folder, recursive: true);
        }
    }

    private static string Source(string[] lines) => string.Join("\r\n",
    [
        "Attribute VB_Name = \"Program\"",
        "Option Explicit",
        "Public Sub Main()",
        "    Dim fso As Object, f As Object, t As Object, v As Variant, r As String, p As String, a As String, b As String, s As String",
        .. lines.Select(line => "    " + line),
        "End Sub",
        "",
    ]);

    // each statement, and the number of the error it raised.
    private static IEnumerable<string> Raised(params string[] statements)
        => statements.SelectMany(statement => new[] { statement, "Debug.Print Err.Number", "Err.Clear" });

    // what the program printed, all of it in the message when it is not what was expected.
    private static void Printed(string[] expected, string[] printed)
        => Assert.IsTrue(expected.SequenceEqual(printed), $"printed [{string.Join(" | ", printed)}], expected [{string.Join(" | ", expected)}]");

    [TestMethod]
    [DataRow(false, "Dim x As Object", "Set x = CreateObject(\"Scripting.FileSystemObject\")")]
    [DataRow(true, "Dim x As Scripting.FileSystemObject", "Set x = New Scripting.FileSystemObject")]
    [DataRow(true, "Dim x As Object", "Set x = New Scripting.FileSystemObject")]
    public async Task AFileSystemObject_MadeWithOrWithoutTheReference_WritesAFileAndReadsItBack(bool referenced, string declared, string made)
        => Printed(["FileSystemObject True False", "8 TextStream File", "one|two"], await RunAsync(referenced ? Referenced : Unreferenced,
        [
            declared,
            made,
            "a = x.BuildPath(r, \"a.txt\")",
            "Debug.Print TypeName(x) & \" \" & x.FolderExists(r) & \" \" & x.FileExists(a)",
            "Set t = x.CreateTextFile(a)",
            "t.WriteLine \"one\"",
            "t.Write \"two\"",
            "t.Close",
            "Debug.Print x.GetFile(a).Size & \" \" & TypeName(t) & \" \" & TypeName(x.GetFile(a))",
            "Set t = x.OpenTextFile(a)",
            "s = t.ReadLine",
            "Debug.Print s & \"|\" & t.ReadAll",
            "t.Close",
        ]));

    // a separator at either side of the join is the one there; there is none after a drive's colon, nor next to nothing.
    [TestMethod]
    public async Task BuildPath_JoinsWithTheSeparatorOfThePlatform_UnlessOneIsThere()
        => Printed([$"a{Path.DirectorySeparatorChar}b", @"a\b", "a/b", "b", "a", "C:b"], await RunAsync(Unreferenced,
        [
            Made,
            "Debug.Print fso.BuildPath(\"a\", \"b\")",
            "Debug.Print fso.BuildPath(\"a\\\", \"b\")",
            "Debug.Print fso.BuildPath(\"a\", \"/b\")",
            "Debug.Print fso.BuildPath(\"\", \"b\")",
            "Debug.Print fso.BuildPath(\"a\", \"\")",
            "Debug.Print fso.BuildPath(\"C:\", \"b\")",
        ]));

    // what scrrun.dll makes of a path, of either separator, without reading the file system; where it has nothing to give, it gives "".
    [TestMethod]
    [DataRow("GetParentFolderName", @"C:\a\b\c.txt", @"C:\a\b")]
    [DataRow("GetParentFolderName", @"C:\a\b\", @"C:\a")]
    [DataRow("GetParentFolderName", @"C:\a", @"C:\")]
    [DataRow("GetParentFolderName", @"C:\", "")]
    [DataRow("GetParentFolderName", "a", "")]
    [DataRow("GetParentFolderName", @"\a", "")]
    [DataRow("GetParentFolderName", "C:/a/b.txt", "C:/a")]
    [DataRow("GetParentFolderName", @"\\srv\share\x\y.txt", @"\\srv\share\x")]
    [DataRow("GetParentFolderName", @"x:y\z", "x:y")]
    [DataRow("GetFileName", @"C:\a\b\c.txt", "c.txt")]
    [DataRow("GetFileName", @"C:\a\b\", "b")]
    [DataRow("GetFileName", @"C:\", "")]
    [DataRow("GetFileName", @"\\srv\share", "")]
    [DataRow("GetBaseName", @"C:\a\b.c.d", "b.c")]
    [DataRow("GetBaseName", @"C:\a\.hidden", "")]
    [DataRow("GetBaseName", @"C:\a\b.", "b")]
    [DataRow("GetBaseName", @"C:\a\..", ".")]
    [DataRow("GetExtensionName", @"C:\a\b.c.d", "d")]
    [DataRow("GetExtensionName", @"C:\a\.hidden", "hidden")]
    [DataRow("GetExtensionName", @"C:\a\b.", "")]
    [DataRow("GetExtensionName", @"C:\a\b", "")]
    [DataRow("GetDriveName", @"C:\a\b\c.txt", "C:")]
    [DataRow("GetDriveName", @"\\srv\share\x\y.txt", @"\\srv\share")]
    [DataRow("GetDriveName", @"a\b", "")]
    public async Task APathFunction_GivesWhatScrrunGivesForThePath(string function, string path, string expected)
        => Printed([$"[{expected}]"], await RunAsync(Unreferenced, [Made, $"Debug.Print \"[\" & fso.{function}(\"{path}\") & \"]\""]));

    // a line feed begins a line, and every other character moves the column on: a carriage return alone too, which ReadLine leaves in the line.
    [TestMethod]
    public async Task ATextStream_CountsItsLinesAndColumns_AndReadsALineToALineFeed()
        => Printed(["5:5", "27", "1:1:one", "2:1:two", "3:1:", "4:1:three<CR>four", "5:1:five", "one<CRLF> False", "3 3 0", "5:6"],
            await RunAsync(Unreferenced,
            [
                Made,
                "a = fso.BuildPath(r, \"a.txt\")",
                "Set t = fso.CreateTextFile(a)",
                "t.WriteLine \"one\"",
                "t.Write \"two\"",
                "t.WriteBlankLines 2",
                "t.Write \"three\" & vbCr & \"four\" & vbLf & \"five\"",
                "Debug.Print t.Line & \":\" & t.Column",
                "t.Close",
                "Debug.Print FileLen(a)",
                "Set t = fso.OpenTextFile(a)",
                "Do Until t.AtEndOfStream",
                "    s = t.Line & \":\" & t.Column",
                "    Debug.Print s & \":\" & Replace(t.ReadLine, vbCr, \"<CR>\")",
                "Loop",
                "t.Close",
                "Set t = fso.OpenTextFile(a, 1)",
                "s = Replace(t.Read(5), vbCrLf, \"<CRLF>\")",
                "Debug.Print s & \" \" & t.AtEndOfLine",
                "t.Skip 2",
                "s = t.Column",
                "t.SkipLine",
                "Debug.Print s & \" \" & t.Line & \" \" & Len(t.Read(0))",
                "t.Close",
                "Set t = fso.OpenTextFile(a, 8)",
                "t.Write \"!\"",
                "Debug.Print t.Line & \":\" & t.Column",
                "t.Close",
            ]));

    // a Unicode file is UTF-16LE after its byte order mark; any other is in the environment's ANSI code page, which StrConv converts with.
    [TestMethod]
    public async Task ATextFile_IsUnicodeOrANSI_AsItIsOpened()
        => Printed(["True True", "True True"], await RunAsync(Unreferenced,
        [
            Made,
            "s = \"a\" & ChrW(233)",
            "a = fso.BuildPath(r, \"u.txt\")",
            "b = fso.BuildPath(r, \"a.txt\")",
            "Set t = fso.CreateTextFile(a, True, True)",
            "t.Write s",
            "t.Close",
            "Set t = fso.CreateTextFile(b)",
            "t.Write s",
            "t.Close",
            "Debug.Print (FileLen(a) = 2 + LenB(s)) & \" \" & (FileLen(b) = LenB(StrConv(s, vbFromUnicode)))",
            "Set t = fso.OpenTextFile(a, 1, False, -1)",
            "v = t.ReadAll",
            "t.Close",
            "Set t = fso.GetFile(b).OpenAsTextStream(1, 0)",
            "Debug.Print (v = s) & \" \" & (t.ReadAll = StrConv(StrConv(s, vbFromUnicode), vbUnicode))",
            "t.Close",
        ]));

    // a relative path is in the current directory, the one CurDir gives; the FileSystemObject and the Open statement act on the same files.
    [TestMethod]
    public async Task AFileSystemObject_SharesTheCurrentDirectoryAndTheFiles_OfTheOpenStatement()
        => Printed(["True True", "from fso", "from open"], await RunAsync(Unreferenced,
        [
            Made,
            "Debug.Print (fso.GetAbsolutePathName(\"rel.txt\") = fso.BuildPath(CurDir, \"rel.txt\")) & \" \" & (fso.GetFolder(\".\").Path = CurDir)",
            "a = fso.BuildPath(r, \"fso.txt\")",
            "Set t = fso.CreateTextFile(a)",
            "t.WriteLine \"from fso\"",
            "t.Close",
            "Open a For Input As #1",
            "Line Input #1, s",
            "Close #1",
            "Debug.Print s",
            "b = fso.BuildPath(r, \"open.txt\")",
            "Open b For Output As #1",
            "Print #1, \"from open\"",
            "Close #1",
            "Set t = fso.OpenTextFile(b)",
            "Debug.Print t.ReadLine",
            "t.Close",
        ]));

    // a folder's files and subfolders, by name and enumerated, in order of their names; a File's and a Folder's default member is its path, and a
    // folder's size is the size of every file in it and under it.
    [TestMethod]
    public async Task AFolder_HasItsFilesAndSubfolders_ByNameAndForEach()
        => Printed(["Folder box 2 1 3 16 False True", "File a.txt 1 True True", "File b.txt 2 True True", "Folder sub 0", "a.txt sub Files Folders"],
            await RunAsync(Unreferenced,
            [
                Made,
                "p = fso.BuildPath(r, \"box\")",
                "Set f = fso.CreateFolder(p)",
                "Set t = fso.CreateTextFile(fso.BuildPath(p, \"b.txt\"))",
                "t.Write \"bb\"",
                "t.Close",
                "Set t = fso.CreateTextFile(fso.BuildPath(p, \"a.txt\"))",
                "t.Write \"a\"",
                "t.Close",
                "f.SubFolders.Add \"sub\"",
                "Debug.Print TypeName(f) & \" \" & f.Name & \" \" & f.Files.Count & \" \" & f.SubFolders.Count & \" \" & f.Size & \" \" & f.Attributes "
                    + "& \" \" & f.IsRootFolder & \" \" & (f.ParentFolder = r)",
                "For Each v In f.Files",
                "    Debug.Print TypeName(v) & \" \" & v.Name & \" \" & v.Size & \" \" & (v.ParentFolder = p) & \" \" & (v = fso.BuildPath(p, v.Name))",
                "Next",
                "For Each v In f.SubFolders",
                "    Debug.Print TypeName(v) & \" \" & v.Name & \" \" & v.Size",
                "Next",
                "Debug.Print f.Files(\"a.txt\").Name & \" \" & f.SubFolders.Item(\"sub\").Name & \" \" & TypeName(f.Files) & \" \" & TypeName(f.SubFolders)",
            ]));

    // a File renamed or moved is where it went; a wildcard in the last part of a path names every file, or folder, it matches there.
    [TestMethod]
    public async Task FilesAndFolders_AreCopiedMovedRenamedAndDeleted()
        => Printed(["d.txt False False True", "sub d.txt", "True False", "False 0 2", "1"], await RunAsync(Unreferenced,
        [
            Made,
            "p = fso.BuildPath(r, \"box\")",
            "fso.CreateFolder p",
            "a = fso.BuildPath(p, \"a.txt\")",
            "Set t = fso.CreateTextFile(a)",
            "t.Write \"x\"",
            "t.Close",
            "fso.CopyFile a, fso.BuildPath(p, \"b.txt\")",
            "fso.MoveFile fso.BuildPath(p, \"b.txt\"), fso.BuildPath(p, \"c.txt\")",
            "Set f = fso.GetFile(fso.BuildPath(p, \"c.txt\"))",
            "f.Name = \"d.txt\"",
            "Debug.Print f.Name & \" \" & fso.FileExists(fso.BuildPath(p, \"b.txt\")) & \" \" & fso.FileExists(fso.BuildPath(p, \"c.txt\")) & \" \" "
                + "& (f.Path = fso.BuildPath(p, \"d.txt\"))",
            "fso.CreateFolder fso.BuildPath(p, \"sub\")",
            "f.Move fso.BuildPath(p, \"sub\") & \"\\\"",
            "Debug.Print f.ParentFolder.Name & \" \" & f.Name",
            "fso.CopyFolder fso.BuildPath(p, \"sub\"), fso.BuildPath(p, \"copy\")",
            "fso.MoveFolder fso.BuildPath(p, \"copy\"), fso.BuildPath(p, \"moved\")",
            "Debug.Print fso.FileExists(fso.BuildPath(fso.BuildPath(p, \"moved\"), \"d.txt\")) & \" \" & fso.FolderExists(fso.BuildPath(p, \"copy\"))",
            "fso.DeleteFile fso.BuildPath(p, \"*.txt\")",
            "Debug.Print fso.FileExists(a) & \" \" & fso.GetFolder(p).Files.Count & \" \" & fso.GetFolder(p).SubFolders.Count",
            "fso.DeleteFolder fso.BuildPath(p, \"s*\")",
            "Debug.Print fso.GetFolder(p).SubFolders.Count",
        ]));

    // a read-only file is deleted only by force (70 otherwise).
    [TestMethod]
    public async Task AReadOnlyFile_IsDeletedOnlyByForce()
        => Printed(["1", "70 True", "0 False"], await RunAsync(Unreferenced,
        [
            Made,
            "a = fso.BuildPath(r, \"a.txt\")",
            "Set t = fso.CreateTextFile(a)",
            "t.Close",
            "Set f = fso.GetFile(a)",
            "f.Attributes = 1",
            "Debug.Print f.Attributes And 1",
            "On Error Resume Next",
            "fso.DeleteFile a",
            "Debug.Print Err.Number & \" \" & fso.FileExists(a)",
            "Err.Clear",
            "fso.DeleteFile a, True",
            "Debug.Print Err.Number & \" \" & fso.FileExists(a)",
        ]));

    // the errors scrrun.dll raises: a file that is not there (53), a folder (76), one that is (58), a bad argument (5); then a File and a Folder that are
    // gone, and an argument to a member that takes none and returns no object to give it to (451).
    [TestMethod]
    public async Task WhatAFileSystemObjectCannotDo_IsTheErrorScrrunRaises()
        => Printed(["53", "76", "58", "53", "53", "5", "58", "76", "53", "76", "58", "58", "58", "53", "53", "76", "5", "5", "53", "76", "451"],
            await RunAsync(Unreferenced,
            [
                Made,
                "p = fso.BuildPath(r, \"box\")",
                "fso.CreateFolder p",
                "a = fso.BuildPath(p, \"a.txt\")",
                "b = fso.BuildPath(p, \"b.txt\")",
                "Set t = fso.CreateTextFile(a)",
                "t.Close",
                "Set t = fso.CreateTextFile(b)",
                "t.Close",
                "fso.GetFolder(p).SubFolders.Add \"s\"",
                "On Error Resume Next",
                .. Raised(
                    "Set f = fso.GetFile(fso.BuildPath(p, \"no.txt\"))",
                    "Set f = fso.GetFolder(fso.BuildPath(p, \"no\"))",
                    "Set t = fso.CreateTextFile(a, False)",
                    "Set t = fso.OpenTextFile(fso.BuildPath(p, \"no.txt\"))",
                    "Set t = fso.OpenTextFile(fso.BuildPath(p, \"no.txt\"), 8)",
                    "Set t = fso.OpenTextFile(a, 3)",
                    "Set f = fso.CreateFolder(p)",
                    "Set f = fso.CreateFolder(fso.BuildPath(fso.BuildPath(p, \"no\"), \"deep\"))",
                    "Set f = fso.GetFolder(p).Files(\"no.txt\")",
                    "Set f = fso.GetFolder(p).SubFolders(\"no\")",
                    "fso.GetFolder(p).SubFolders.Add \"s\"",
                    "fso.CopyFile a, b, False",
                    "fso.MoveFile a, b",
                    "fso.DeleteFile fso.BuildPath(p, \"no.txt\")",
                    "fso.DeleteFile fso.BuildPath(p, \"*.no\")",
                    "fso.DeleteFolder fso.BuildPath(p, \"no\")",
                    "Set f = fso.GetSpecialFolder(3)",
                    "Set f = fso.GetDrive(\"?\")"),
                "Set f = fso.GetFile(a)",
                "Set v = fso.GetFolder(fso.BuildPath(p, \"s\"))",
                "fso.DeleteFile a",
                "fso.DeleteFolder fso.BuildPath(p, \"s\")",
                .. Raised("s = f.Name", "s = v.Name", "s = fso.GetFolder(p).Name(1)"),
            ]));

    // a stream for writing cannot be read, nor one for reading written (54); nothing is read past the end (62); nothing is done once it is closed (91).
    [TestMethod]
    public async Task WhatATextStreamCannotDo_IsTheErrorScrrunRaises()
        => Printed(["54", "54", "5", "91", "91", "62", "62", "62", "54"], await RunAsync(Unreferenced,
        [
            Made,
            "a = fso.BuildPath(r, \"a.txt\")",
            "Set t = fso.CreateTextFile(a)",
            "On Error Resume Next",
            .. Raised("v = t.ReadLine", "v = t.AtEndOfStream", "t.WriteBlankLines -1"),
            "t.Close",
            .. Raised("t.Write \"x\"", "v = t.Line"),
            "Set t = fso.OpenTextFile(a)",
            .. Raised("v = t.ReadAll", "v = t.ReadLine", "t.SkipLine", "t.Write \"x\""),
        ]));
}
