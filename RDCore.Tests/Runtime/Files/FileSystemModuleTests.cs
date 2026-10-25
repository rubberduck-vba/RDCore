using RDCore.SDK.Runtime.Abstract.Execution;
using System.IO.Abstractions.TestingHelpers;

namespace RDCore.Tests.Runtime.Files;

/// <summary>
/// <strong>MS-VBAL §6.1.2.5</strong> the FileSystem module, and <c>GetAttr</c>: what MS-VBA gives and raises, recorded in Excel (oracle\s25 grid, grid-b), against
/// a fake file system. Paths are rooted because the platform's CI runs on Linux.
/// </summary>
[TestClass]
public sealed class FileSystemModuleTests
{
    private const string Root = "/ws";

    // a file system holding the files given, as "name=content" under Root, whose current directory is Root; P x prints x, and what the body prints is the
    // printed values, each followed by |.
    private static (string Printed, MockFileSystem FileSystem) Run(string files, string body)
    {
        var fileSystem = new MockFileSystem(new Dictionary<string, MockFileData>(), currentDirectory: Root);
        fileSystem.Directory.CreateDirectory(Root);
        foreach (var file in files.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var (name, content) = (file.Split('=')[0], file.Split('=').ElementAtOrDefault(1) ?? string.Empty);
            if (name.EndsWith('/'))
            {
                fileSystem.Directory.CreateDirectory($"{Root}/{name}");
                continue;
            }

            fileSystem.AddFile($"{Root}/{name}", new MockFileData(content));
        }

        var output = new RuntimeOutputBuffer();
        var lines = body.Split(" ;; ").Select(line => line.StartsWith("P ") ? "Debug.Print " + line[2..] : line).Prepend("On Error Resume Next");
        var (_, outcome) = RuntimeSourceHarness.Run(fileSystem, [], output, standardLibrary: true, [.. lines]);
        Assert.AreNotEqual(RDCore.Runtime.Execution.RuntimeExecutionOutcomeKind.Error, outcome.Kind, outcome.ErrorInfo?.Verbose);
        return (string.Concat(output.Lines.Select(line => line.Trim() + "|")), fileSystem);
    }

    private static void AssertPrinted(string expected, string files, string body) => Assert.AreEqual(expected, Run(files, body).Printed);

    // C01, C02: the current directory, the drive's own; a letter that is no drive, a drive that is no letter.
    [TestMethod]
    public void CurDir_IsTheCurrentDirectory()
    {
        var (printed, fileSystem) = Run("", "P CurDir ;; P TypeName(CurDir) ;; P TypeName(CurDir$) ;; P (CurDir(\"\") = CurDir) ;; Err.Clear ;; P CurDir(\"Q\") ;; P Err.Number ;; Err.Clear ;; P CurDir(\"1\") ;; P Err.Number");

        Assert.AreEqual($"{fileSystem.Directory.GetCurrentDirectory()}|String|String|True|68|5|", printed);
    }

    // D01-D07, D11, D12: the names that match, one by one; then a zero-length String; then error 5.
    [TestMethod]
    [DataRow("b.txt,a.txt,c.log", "P Dir(\"/ws/*.txt\") ;; P Dir() ;; P \"[\" & Dir() & \"]\" ;; Err.Clear ;; P Dir() ;; P Err.Number", "a.txt|b.txt|[]|5|", DisplayName = "D01 one by one, then none, then error 5")]
    [DataRow("", "Err.Clear ;; P Dir() ;; P Err.Number", "5|", DisplayName = "no pattern to go on with")]
    [DataRow("", "P \"[\" & Dir(\"/ws/nope.txt\") & \"]\" ;; Err.Clear ;; P \"[\" & Dir(\"/ws/nodir/*.*\") & \"]\" ;; P Err.Number", "[]|[]|0|", DisplayName = "D02 what matches nothing")]
    [DataRow("a.txt,sub/", "P Dir(\"/ws/*\", vbDirectory) ;; P Dir() ;; P Dir() ;; P Dir() ;; P \"[\" & Dir() & \"]\"", ".|..|a.txt|sub|[]|", DisplayName = "D03 directories, with vbDirectory")]
    [DataRow("a.txt,sub/", "P Dir(\"/ws/\") ;; P \"[\" & Dir() & \"]\" ;; P Dir(\"/ws/sub\", vbDirectory) ;; P \"[\" & Dir(\"/ws/sub\") & \"]\"", "a.txt|[]|sub|[]|", DisplayName = "D04 what is in a directory")]
    [DataRow("a.txt", "P Dir(\"/ws/A.TXT\") ;; P Dir(\"/ws/?.txt\") ;; P TypeName(Dir(\"/ws/a.txt\"))", "a.txt|a.txt|String|", DisplayName = "D05 whatever the case, ? a character")]
    [DataRow("B.TXT,a.txt,_c.txt,10.txt,9.txt", "P Dir(\"/ws/*.txt\") ;; P Dir() ;; P Dir() ;; P Dir() ;; P Dir()", "10.txt|9.txt|a.txt|B.TXT|_c.txt|", DisplayName = "D11 in the order of their upper-case spelling")]
    // the name matches whatever its case; the directory is found as the file system finds it, which on Linux is by its case - so this spells it as it is.
    [DataRow("sub/,sub/x.txt", "P Dir(\"/ws/sub/\") ;; P Dir(\"/ws/sub/*.*\") ;; P Dir(\"/ws/sub/X.TXT\")", "x.txt|x.txt|x.txt|", DisplayName = "D12 in a directory")]
    public void Dir_IsTheNamesThatMatch(string files, string body, string expected) => AssertPrinted(expected, files, body);

    // D06: a hidden file only with vbHidden.
    [TestMethod]
    public void Dir_OfAHiddenFile_IsOnlyWithvbHidden()
        => AssertPrinted("[]|a.txt|", "a.txt", "SetAttr \"/ws/a.txt\", vbHidden ;; P \"[\" & Dir(\"/ws/*.txt\") & \"]\" ;; P Dir(\"/ws/*.txt\", vbHidden)");

    // E01-E05: EOF of Input once nothing is left; always for Output and Append; for Binary and Random once a Get could not read a whole record.
    [TestMethod]
    [DataRow("a.txt=l1\r\nl2", "Dim s As String ;; Open \"/ws/a.txt\" For Input As #1 ;; P EOF(1) ;; Line Input #1, s ;; P EOF(1) ;; Line Input #1, s ;; P EOF(1)", "False|False|True|", DisplayName = "E01 Input")]
    [DataRow("", "Open \"/ws/o.txt\" For Output As #1 ;; P EOF(1) ;; Close #1 ;; Open \"/ws/o.txt\" For Append As #1 ;; P EOF(1)", "True|True|", DisplayName = "E02 Output, Append")]
    [DataRow("a.txt=ab", "Dim b As Byte ;; Open \"/ws/a.txt\" For Binary As #1 ;; P EOF(1) ;; Get #1, , b ;; Get #1, , b ;; P EOF(1) ;; Get #1, , b ;; P EOF(1)", "False|False|True|", DisplayName = "E03 Binary")]
    [DataRow("", "Dim l As Long ;; Open \"/ws/r.dat\" For Random As #1 Len = 4 ;; P EOF(1) ;; Put #1, 1, 7& ;; Get #1, 1, l ;; P EOF(1) ;; Get #1, 2, l ;; P EOF(1)", "False|False|True|", DisplayName = "E04 Random")]
    [DataRow("e.txt", "Err.Clear ;; P EOF(9) ;; P Err.Number ;; Open \"/ws/e.txt\" For Input As #1 ;; P EOF(1)", "52|True|", DisplayName = "E05 not open; an empty file")]
    public void EOF_IsTheEndOfTheFile(string files, string body, string expected) => AssertPrinted(expected, files, body);

    // A01, A02: the mode; only ReturnType 1.
    [TestMethod]
    public void FileAttr_IsTheMode()
        => AssertPrinted("1|1|5|2|8|32|4|52|", "a.txt",
            "Open \"/ws/a.txt\" For Input As #1 ;; P FileAttr(1) ;; P FileAttr(1, 1) ;; Err.Clear ;; P FileAttr(1, 2) ;; P Err.Number ;; Close #1"
            + " ;; Open \"/ws/o.txt\" For Output As #1 ;; P FileAttr(1) ;; Close #1 ;; Open \"/ws/o.txt\" For Append As #1 ;; P FileAttr(1) ;; Close #1"
            + " ;; Open \"/ws/o.txt\" For Binary As #1 ;; P FileAttr(1) ;; Close #1 ;; Open \"/ws/o.txt\" For Random As #1 ;; P FileAttr(1) ;; Close #1"
            + " ;; Err.Clear ;; P FileAttr(9) ;; P Err.Number");

    // T01, L01, L03, L11: what does not exist is 53, a pattern 52, a directory's length 0; an open file's is what the file system has.
    [TestMethod]
    [DataRow("a.txt=hello", "P TypeName(FileDateTime(\"/ws/a.txt\")) ;; Err.Clear ;; P FileDateTime(\"/ws/nope.txt\") ;; P Err.Number", "Date|53|", DisplayName = "T01 FileDateTime")]
    [DataRow("a.txt=hello", "P FileLen(\"/ws/a.txt\") ;; P TypeName(FileLen(\"/ws/a.txt\")) ;; Err.Clear ;; P FileLen(\"/ws/nope.txt\") ;; P Err.Number ;; P FileLen(\"/ws\")", "5|Long|53|0|", DisplayName = "L01 FileLen")]
    [DataRow("a.txt=x", "Err.Clear ;; P FileLen(\"/ws/*.txt\") ;; P Err.Number", "52|", DisplayName = "L03 a pattern")]
    [DataRow("a.txt=hello", "Open \"/ws/a.txt\" For Output As #1 ;; Print #1, \"longer\"; ;; P FileLen(\"/ws/a.txt\") ;; P LOF(1) ;; Close #1 ;; Open \"/ws/a.txt\" For Append As #1 ;; Print #1, \"more\"; ;; P FileLen(\"/ws/a.txt\") ;; P LOF(1)", "0|6|6|10|", DisplayName = "L02, L11 an open file")]
    public void FileLenAndFileDateTime_AreTheFileSystems(string files, string body, string expected) => AssertPrinted(expected, files, body);

    // O01-O05, Q01-Q04: LOF, Loc and Seek by mode.
    [TestMethod]
    [DataRow("a.txt=abcdef", "Dim b As Byte ;; Open \"/ws/a.txt\" For Binary As #1 ;; P Loc(1) ;; P Seek(1) ;; Get #1, , b ;; P Loc(1) ;; P Seek(1) ;; Get #1, 4, b ;; P Loc(1) ;; P Seek(1) ;; P LOF(1)", "0|1|1|2|4|5|6|", DisplayName = "O02 Binary")]
    [DataRow("", "Dim l As Long ;; Open \"/ws/r.dat\" For Random As #1 Len = 4 ;; P Loc(1) ;; P Seek(1) ;; Put #1, , 1& ;; Put #1, , 2& ;; P Loc(1) ;; P Seek(1) ;; Get #1, 1, l ;; P Loc(1) ;; P Seek(1) ;; P LOF(1)", "0|1|2|3|1|2|8|", DisplayName = "O03 Random")]
    [DataRow("", "Dim i As Long ;; Open \"/ws/o.txt\" For Output As #1 ;; P Loc(1) ;; For i = 1 To 127: Print #1, \"x\"; : Next ;; P Seek(1) ;; P Loc(1) ;; Print #1, \"x\"; ;; P Seek(1) ;; P Loc(1) ;; For i = 1 To 172: Print #1, \"x\"; : Next ;; P Loc(1) ;; P LOF(1)", "0|128|0|129|1|2|300|", DisplayName = "Q02, O04 Output")]
    [DataRow("a.txt=line1\r\nline2", "Dim s As String ;; Open \"/ws/a.txt\" For Input As #1 ;; P LOF(1) ;; P Loc(1) ;; P Seek(1) ;; Line Input #1, s ;; P Loc(1) ;; P Seek(1)", "12|1|1|1|8|", DisplayName = "O01 Input")]
    [DataRow("", "Err.Clear ;; P LOF(9) ;; P Err.Number ;; Err.Clear ;; P Loc(9) ;; P Err.Number ;; Err.Clear ;; P Seek(9) ;; P Err.Number", "52|52|52|", DisplayName = "O05 not open")]
    public void LocAndSeek_AreThePosition(string files, string body, string expected) => AssertPrinted(expected, files, body);

    // Q01: an Input file's Loc is the 128-byte blocks read, the first one read when it is opened.
    [TestMethod]
    public void TheLocOfAnInputFile_IsTheBlocksRead()
        => AssertPrinted("440|1|133|2|287|3|441|4|True|", "a.txt=" + string.Concat(Enumerable.Repeat("123456789\r\n", 40)),
            "Dim s As String, i As Long ;; Open \"/ws/a.txt\" For Input As #1 ;; P LOF(1) ;; P Loc(1) ;; For i = 1 To 12: Line Input #1, s: Next ;; P Seek(1) ;; P Loc(1)"
            + " ;; For i = 1 To 14: Line Input #1, s: Next ;; P Seek(1) ;; P Loc(1) ;; For i = 1 To 14: Line Input #1, s: Next ;; P Seek(1) ;; P Loc(1) ;; P EOF(1)");

    // Q03: an Append file's Seek and Loc count from where the appending began.
    [TestMethod]
    public void TheSeekOfAnAppendFile_CountsFromWhereItBegan()
        => AssertPrinted("1|0|2|0|201|", "a.txt=" + new string('a', 200), "Open \"/ws/a.txt\" For Append As #1 ;; P Seek(1) ;; P Loc(1) ;; Print #1, \"x\"; ;; P Seek(1) ;; P Loc(1) ;; P LOF(1)");

    // H01, H02: ChDir, ChDrive.
    [TestMethod]
    public void ChDir_ChangesTheCurrentDirectory()
    {
        var (printed, fileSystem) = Run("sub/", "ChDir \"/ws/sub\" ;; P CurDir ;; Err.Clear ;; ChDir \"/ws/nope\" ;; P Err.Number ;; Err.Clear ;; ChDir \"\" ;; P Err.Number"
            + " ;; Err.Clear ;; ChDrive \"\" ;; P Err.Number ;; Err.Clear ;; ChDrive \"Q\" ;; P Err.Number");

        Assert.AreEqual($"{fileSystem.Path.GetFullPath("/ws/sub")}|76|76|0|68|", printed);
    }

    // Y01-Y03, Y11: FileCopy replaces the destination; an open source but for Input, an open destination, are 55.
    [TestMethod]
    [DataRow("a.txt=hello,c.txt=hi", "FileCopy \"/ws/a.txt\", \"/ws/b.txt\" ;; P FileLen(\"/ws/b.txt\") ;; FileCopy \"/ws/c.txt\", \"/ws/b.txt\" ;; P FileLen(\"/ws/b.txt\") ;; Err.Clear ;; FileCopy \"/ws/nope.txt\", \"/ws/d.txt\" ;; P Err.Number", "5|2|53|", DisplayName = "Y01 copies, replaces")]
    [DataRow("a.txt=hello", "Open \"/ws/a.txt\" For Input As #1 ;; Err.Clear ;; FileCopy \"/ws/a.txt\", \"/ws/b.txt\" ;; P Err.Number ;; Err.Clear ;; FileCopy \"/ws/b.txt\", \"/ws/a.txt\" ;; P Err.Number", "0|55|", DisplayName = "Y02 an open source for Input, an open destination")]
    [DataRow("a.txt=hello", "Open \"/ws/a.txt\" For Binary Access Read As #1 ;; Err.Clear ;; FileCopy \"/ws/a.txt\", \"/ws/b.txt\" ;; P Err.Number", "55|", DisplayName = "Y11, Y12 an open source in any other mode")]
    [DataRow("a.txt=x,sub/", "Err.Clear ;; FileCopy \"/ws/*.txt\", \"/ws/b.txt\" ;; P Err.Number ;; Err.Clear ;; FileCopy \"/ws/a.txt\", \"/ws/nodir/b.txt\" ;; P Err.Number ;; Err.Clear ;; FileCopy \"/ws/a.txt\", \"/ws/sub\" ;; P Err.Number", "52|76|75|", DisplayName = "Y03 a pattern, no directory, a directory")]
    public void FileCopy_CopiesAFile(string files, string body, string expected) => AssertPrinted(expected, files, body);

    // K01-K03, K11: Kill deletes the files that match, not directories, nor hidden ones.
    [TestMethod]
    [DataRow("a.txt,b.txt,c.log", "Kill \"/ws/*.txt\" ;; P Dir(\"/ws/*.*\") ;; Err.Clear ;; Kill \"/ws/*.txt\" ;; P Err.Number ;; Err.Clear ;; Kill \"/ws/nope.log\" ;; P Err.Number", "c.log|53|53|", DisplayName = "K01 what matches")]
    [DataRow("a.txt,sub/", "Open \"/ws/a.txt\" For Input As #1 ;; Err.Clear ;; Kill \"/ws/a.txt\" ;; P Err.Number ;; Close #1 ;; Err.Clear ;; Kill \"/ws/sub\" ;; P Err.Number ;; Err.Clear ;; Kill \"/ws/nodir/a.txt\" ;; P Err.Number", "55|53|53|", DisplayName = "K02 open, a directory, no directory")]
    [DataRow("a.txt,h.txt", "SetAttr \"/ws/a.txt\", vbReadOnly ;; Err.Clear ;; Kill \"/ws/a.txt\" ;; P Err.Number ;; SetAttr \"/ws/a.txt\", vbNormal ;; SetAttr \"/ws/h.txt\", vbHidden ;; Err.Clear ;; Kill \"/ws/*.txt\" ;; P Err.Number ;; P Dir(\"/ws/*.txt\", vbHidden)", "75|0|h.txt|", DisplayName = "K03 read-only, hidden")]
    [DataRow("a.txt,sub/,sub/x.txt", "Err.Clear ;; Kill \"/ws/*\" ;; P Err.Number ;; P Dir(\"/ws/*\", vbDirectory) ;; P Dir() ;; P Dir() ;; P \"[\" & Dir() & \"]\"", "0|.|..|sub|[]|", DisplayName = "K11 not directories")]
    public void Kill_DeletesFiles(string files, string body, string expected) => AssertPrinted(expected, files, body);

    // M01-M03: MkDir, RmDir.
    [TestMethod]
    [DataRow("a.txt", "MkDir \"/ws/sub\" ;; P GetAttr(\"/ws/sub\") ;; Err.Clear ;; MkDir \"/ws/sub\" ;; P Err.Number ;; Err.Clear ;; MkDir \"/ws/nodir/sub\" ;; P Err.Number ;; Err.Clear ;; MkDir \"/ws/a.txt\" ;; P Err.Number", "16|75|76|75|", DisplayName = "M01 MkDir")]
    [DataRow("a.txt,sub/,sub/x.txt", "Err.Clear ;; RmDir \"/ws/sub\" ;; P Err.Number ;; Kill \"/ws/sub/x.txt\" ;; Err.Clear ;; RmDir \"/ws/sub\" ;; P Err.Number ;; Err.Clear ;; RmDir \"/ws/sub\" ;; P Err.Number ;; Err.Clear ;; RmDir \"/ws/a.txt\" ;; P Err.Number", "75|0|76|76|", DisplayName = "M02 RmDir")]
    [DataRow("sub/,sub/deep/", "Err.Clear ;; RmDir \"/ws/sub\" ;; P Err.Number ;; RmDir \"/ws/sub/deep\" ;; Err.Clear ;; RmDir \"/ws/su*\" ;; P Err.Number", "75|76|", DisplayName = "M03 a directory in it, a pattern")]
    public void MkDirAndRmDir_MakeAndRemoveDirectories(string files, string body, string expected) => AssertPrinted(expected, files, body);

    // G01-G03, G11: SetAttr sets exactly the attributes given, GetAttr reads them.
    [TestMethod]
    [DataRow("a.txt", "SetAttr \"/ws/a.txt\", vbReadOnly ;; P GetAttr(\"/ws/a.txt\") ;; SetAttr \"/ws/a.txt\", vbHidden + vbSystem ;; P GetAttr(\"/ws/a.txt\") ;; SetAttr \"/ws/a.txt\", vbNormal ;; P GetAttr(\"/ws/a.txt\") ;; SetAttr \"/ws/a.txt\", vbArchive ;; P GetAttr(\"/ws/a.txt\")", "1|6|0|32|", DisplayName = "G01 exactly the attributes given")]
    [DataRow("a.txt", "Err.Clear ;; P GetAttr(\"/ws/nope.txt\") ;; P Err.Number ;; Err.Clear ;; SetAttr \"/ws/nope.txt\", vbNormal ;; P Err.Number ;; Err.Clear ;; SetAttr \"/ws/a.txt\", vbDirectory ;; P Err.Number ;; Err.Clear ;; SetAttr \"/ws/a.txt\", 64 ;; P Err.Number", "53|53|5|5|", DisplayName = "G02 what does not exist, what cannot be set")]
    [DataRow("a.txt", "Open \"/ws/a.txt\" For Input As #1 ;; Err.Clear ;; SetAttr \"/ws/a.txt\", vbReadOnly ;; P Err.Number ;; P GetAttr(\"/ws/a.txt\") ;; Close #1 ;; SetAttr \"/ws/a.txt\", vbNormal ;; Err.Clear ;; SetAttr \"/ws/*.txt\", vbNormal ;; P Err.Number ;; Err.Clear ;; P GetAttr(\"/ws/*.txt\") ;; P Err.Number", "0|1|52|52|", DisplayName = "G03 open for Input, a pattern")]
    [DataRow("a.txt", "Open \"/ws/a.txt\" For Binary As #1 ;; Err.Clear ;; SetAttr \"/ws/a.txt\", vbHidden ;; P Err.Number", "55|", DisplayName = "G11 open in any other mode")]
    public void SetAttrAndGetAttr_AreTheAttributes(string files, string body, string expected) => AssertPrinted(expected, files, body);
}
