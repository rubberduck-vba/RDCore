using RDCore.SDK.Runtime.Abstract.Execution;

namespace RDCore.Tests.Runtime.StdLib;

/// <summary>
/// <strong>RD-VBAL §6.2</strong> VBScript Regular Expressions 5.5 - <c>RegExp</c>, <c>MatchCollection</c>, <c>Match</c>, <c>SubMatches</c> - as MS-VBA
/// has them, recorded in Excel (oracle\s26: grid-re, grid-re2, grid-re3, grid-re4). Each case is the probe as it ran there, and what it printed.
/// </summary>
[TestClass]
[TestCategory("RD-VBAL 6.2 VBScript Regular Expressions")]
public sealed class RegExpTests
{
    // P x prints x; what the body prints is the printed values, each followed by |.
    private static string Run(string body)
    {
        var output = new RuntimeOutputBuffer();
        var lines = body.Split(" ;; ").Select(line => line.StartsWith("P ") ? "Debug.Print " + line[2..] : line).Prepend("On Error Resume Next");
        var (_, outcome) = RuntimeSourceHarness.Run(null, [], output, standardLibrary: true, [.. lines]);
        Assert.AreNotEqual(RDCore.Runtime.Execution.RuntimeExecutionOutcomeKind.Error, outcome.Kind, outcome.ErrorInfo?.Verbose);
        return string.Concat(output.Lines.Select(line => line.Trim() + "|"));
    }

    // the same, in a workspace's module: what declares a variable of a class of the standard library (Dim r As RegExp).
    private static async Task<string> RunInWorkspace(string body)
    {
        var lines = body.Split(" ;; ").Select(line => line.StartsWith("P ") ? "Debug.Print " + line[2..] : line);
        var output = await Cli.ModuleWorkspace.RunAsync([], $"Attribute VB_Name = \"Program\"\r\nPublic Sub Main()\r\nOn Error Resume Next\r\n{string.Join("\r\n", lines)}\r\nEnd Sub\r\n");
        return string.Concat(output.Select(line => line.Trim() + "|"));
    }

    private const string New = """Dim r As Object ;; Set r = CreateObject("VBScript.RegExp") ;; """;

    [TestMethod]
    [DataRow(New + """P TypeName(r.Global) & "=" & r.Global ;; P r.IgnoreCase ;; P r.MultiLine ;; P TypeName(r.Pattern) & "=[" & r.Pattern & "]" ;; P TypeName(r)""",
        "Boolean=False|False|False|String=[]|RegExp|", DisplayName = "R01 a new RegExp")]
    [DataRow(New + """Dim m As Object ;; r.Pattern = "(\d+)-(\d+)?" ;; Set m = r.Execute("ab 12- 34-56") ;; P TypeName(m) ;; P m.Count ;; P TypeName(m(0)) ;; P m(0).FirstIndex ;; P m(0).Length ;; P m(0).Value ;; P m(0) ;; P TypeName(m(0).SubMatches) ;; P m(0).SubMatches.Count ;; P TypeName(m(0).SubMatches(0)) & "=" & m(0).SubMatches(0) ;; P TypeName(m(0).SubMatches(1))""",
        "MatchCollection|1|Match|3|3|12-|12-|SubMatches|2|String=12|Empty|", DisplayName = "R02 the first match")]
    [DataRow(New + """Dim m As Object ;; r.Pattern = "(\d+)-(\d+)?" ;; r.Global = True ;; Set m = r.Execute("ab 12- 34-56") ;; P m.Count ;; P m(1).FirstIndex ;; P m(1).Value ;; P m(1).SubMatches(1) ;; P m.Item(1).Value ;; P m(1).SubMatches.Item(0)""",
        "2|7|34-56|56|34-56|34|", DisplayName = "R03 every match")]
    [DataRow(New + """Dim m As Object ;; r.Pattern = "(a)" ;; Set m = r.Execute("a") ;; Err.Clear ;; P TypeName(m(5)) ;; P Err.Number & ":" & Err.Description ;; Err.Clear ;; P TypeName(m(-1)) ;; P Err.Number ;; Err.Clear ;; P m("0").Value ;; P Err.Number ;; Err.Clear ;; P m(0).SubMatches(9) ;; P Err.Number & ":" & Err.Description ;; Err.Clear ;; P m(0.6).Value ;; P Err.Number""",
        "5:Invalid procedure call or argument|5|a|0|5:Invalid procedure call or argument|5|", DisplayName = "R04 positions that are not there")]
    [DataRow(New + """Dim x As Variant ;; r.Pattern = "(\w)(\d)" ;; r.Global = True ;; For Each x In r.Execute("a1 b2") ;; P x.Value ;; Next ;; For Each x In r.Execute("c3")(0).SubMatches ;; P TypeName(x) & "=" & x ;; Next""",
        "a1|b2|String=c|String=3|", DisplayName = "R05 For Each")]
    [DataRow(New + """P r.Test("x") ;; P r.Test("") ;; r.Pattern = "^a" ;; P r.Test("ba") ;; P r.Test("Ab") ;; r.IgnoreCase = True ;; P r.Test("Ab") ;; P r.Execute("").Count ;; r.Pattern = "" ;; P r.Execute("ab").Count ;; P r.Execute("ab")(0).Length""",
        "True|True|False|False|True|0|1|0|", DisplayName = "R06 Test, and the empty pattern")]
    [DataRow(New + """r.Pattern = "(\w+)@(\w+)" ;; P r.Replace("a@b c@d", "$2#$1") ;; r.Global = True ;; P r.Replace("a@b c@d", "$2#$1") ;; P r.Replace("a@b", "[$&]") ;; P r.Replace("a@b", "[$$]") ;; P r.Replace("x a@b y", "[$`]") ;; P r.Replace("x a@b y", "[$']") ;; P r.Replace("a@b", "[$0]") ;; P r.Replace("a@b", "[$9]") ;; P r.Replace("a@b", "[$1x]") ;; P r.Replace("a@b", "[$10]") ;; P r.Replace("a@b", "[$]")""",
        "b#a c@d|b#a d#c|[a@b]|[$]|x [x ] y|x [ y] y|[$0]|[$9]|[ax]|[a0]|[$]|", DisplayName = "R07 what a replacement says")]
    [DataRow(New + """r.Pattern = "a" ;; Err.Clear ;; P r.Replace("bab", 5) ;; P Err.Number ;; Err.Clear ;; P r.Replace("bab", Empty) ;; P Err.Number ;; Err.Clear ;; P r.Replace("bab", Null) ;; P Err.Number & ":" & Err.Description ;; Err.Clear ;; P r.Replace("bab", True) ;; P Err.Number ;; Err.Clear ;; P r.Replace("bab", 1.5) ;; P Err.Number""",
        "b5b|0|bb|0|13:Type mismatch|b-1b|0|b1.5b|0|", DisplayName = "R08 replacements that are not text")]
    [DataRow(New + """r.Pattern = "^b" ;; P r.Test("a" & vbCrLf & "b") ;; r.MultiLine = True ;; P r.Test("a" & vbCrLf & "b") ;; P r.Test("a" & vbCr & "b") ;; P r.Test("a" & vbLf & "b") ;; r.Pattern = "a$" ;; P r.Test("a" & vbCrLf & "b") ;; P r.Test("a" & vbCr & "b") ;; P r.Test("a" & vbLf & "b") ;; r.MultiLine = False ;; P r.Test("a" & vbLf) ;; P r.Test("a" & vbCrLf)""",
        "False|True|True|True|True|True|True|False|False|", DisplayName = "R11 lines")]
    [DataRow(New + """r.Pattern = "a.b" ;; P r.Test("a" & vbCr & "b") ;; P r.Test("a" & vbLf & "b") ;; P r.Test("a" & ChrW(8232) & "b") ;; P r.Test("a" & ChrW(133) & "b") ;; r.Pattern = "a\sb" ;; P r.Test("a" & ChrW(160) & "b") ;; P r.Test("a" & ChrW(8195) & "b") ;; P r.Test("a" & ChrW(65279) & "b")""",
        "True|False|True|True|False|False|False|", DisplayName = "R12 any character, and spaces")]
    [DataRow(New + """Dim m As Object, i As Long ;; r.Pattern = "x*" ;; r.Global = True ;; Set m = r.Execute("axb") ;; P m.Count ;; For i = 0 To m.Count - 1 ;; P m(i).FirstIndex & "," & m(i).Length ;; Next ;; P r.Replace("axb", "-") ;; r.Pattern = "" ;; P r.Replace("ab", "-")""",
        "4|0,0|1,1|2,0|3,0|-a--b-|-a-b-|", DisplayName = "R13 empty matches")]
    [DataRow(New + """r.Pattern = "(a)\1" ;; P r.Test("aa") ;; r.IgnoreCase = True ;; P r.Test("aA") ;; r.IgnoreCase = False ;; r.Pattern = "\1(a)" ;; P r.Test("a") ;; r.Pattern = "\101" ;; P r.Test("A") ;; r.Pattern = "(a)\10" ;; P r.Test("a" & Chr(8)) ;; P r.Test("aa0")""",
        "True|True|False|True|False|True|", DisplayName = "R15 backreferences and octal escapes")]
    [DataRow(New + """Dim m As Object ;; r.Pattern = "(a)|(b)" ;; Set m = r.Execute("b") ;; P TypeName(m(0).SubMatches(0)) ;; P IsEmpty(m(0).SubMatches(0)) ;; P TypeName(m(0).SubMatches(1)) ;; r.Pattern = "(a*)b" ;; P TypeName(r.Execute("b")(0).SubMatches(0)) & "=[" & r.Execute("b")(0).SubMatches(0) & "]" ;; r.Pattern = "(a)?b" ;; P TypeName(r.Execute("b")(0).SubMatches(0))""",
        "Empty|True|String|String=[]|Empty|", DisplayName = "R16 groups that took no part")]
    [DataRow(New + """Dim m As Object ;; r.Pattern = "z" ;; Set m = r.Execute("abc") ;; P m.Count ;; Err.Clear ;; P TypeName(m(0)) ;; P Err.Number ;; r.Pattern = "(b)" ;; r.Global = True ;; Set m = r.Execute("abcb") ;; r.Pattern = "c" ;; P m.Count ;; P m(1).FirstIndex ;; P m(1).SubMatches(0)""",
        "0|5|2|3|b|", DisplayName = "R18 the matches are what was found when they were")]
    [DataRow(New + """r.Pattern = "a+?" ;; P r.Execute("aaa")(0).Value ;; r.Pattern = "a(?=b)" ;; P r.Execute("ab")(0).Value ;; r.Pattern = "a(?!b)" ;; P r.Execute("abac")(0).FirstIndex ;; r.Pattern = "^\w+$" ;; P r.Test("a_1") ;; r.Pattern = "\S\D\W" ;; P r.Test("a ") & r.Test("aa!") ;; r.Pattern = "[^\d]+" ;; P r.Execute("12ab3")(0).Value ;; r.Pattern = "a{2,}" ;; P r.Execute("baaaa")(0).Length""",
        "a|a|2|True|FalseTrue|ab|4|", DisplayName = "R20 lazy, lookahead, classes")]
    [DataRow(New + """r.Pattern = "(a)(b)(c)(d)(e)(f)(g)(h)(i)(j)" ;; P r.Replace("abcdefghij", "$10|$11|$1") ;; r.Pattern = "(\d)" ;; r.Global = True ;; P r.Replace("a1b2", "<$1>") ;; P r.Replace("a1b2", "\n$1") ;; P r.Replace("a1", "$")""",
        "j|a1|a|a<1>b<2>|a\\n1b\\n2|a$|", DisplayName = "R21 two-digit groups")]
    [DataRow(New + """r.Pattern = "[A-Z]" ;; r.IgnoreCase = True ;; P r.Test("q") ;; r.Pattern = "^[^a]$" ;; P r.Test("A") ;; r.Pattern = "k" ;; P r.Test(ChrW(8490)) ;; r.Pattern = "ß" ;; P r.Test("SS") ;; r.Pattern = "i" ;; P r.Test(ChrW(304)) ;; r.IgnoreCase = False ;; r.Pattern = "^.$" ;; P r.Test(ChrW(55357) & ChrW(56832))""",
        "True|False|False|False|True|False|", DisplayName = "R22 case")]
    [DataRow(New + """r.Pattern = "^$" ;; r.MultiLine = True ;; r.Global = True ;; P r.Execute("a" & vbCrLf & vbCrLf & "b").Count ;; P r.Execute("a" & vbLf & vbLf & "b").Count ;; r.Pattern = "$" ;; P r.Execute("a" & vbCrLf & "b").Count""",
        "3|1|3|", DisplayName = "R23 a carriage return and a line feed each end a line")]
    [DataRow(New + """r.Pattern = "a|ab" ;; P r.Execute("ab")(0).Value ;; r.Pattern = "(a|ab)(c|bcd)(d*)" ;; P r.Execute("abcd")(0).Value ;; r.Pattern = "(a*)*b" ;; P r.Test("aaab") ;; r.Pattern = "(z)((a+)?(b+)?(c))*" ;; P r.Execute("zaacbbbcac")(0).SubMatches(2) & "/" & r.Execute("zaacbbbcac")(0).SubMatches(3)""",
        "a|abcd|True|a/bbb|", DisplayName = "R24 backtracking, and a group inside a repetition")]
    public void AsMSVBA(string body, string printed) => Assert.AreEqual(printed, Run(body));

    // what is no pattern is told when it is matched, never when it is assigned: R09 (Pattern assigned, then matched), T01 (matched).
    [TestMethod]
    [DataRow(New + """Dim q As Variant ;; For Each q In Array("(", "a{2,1}", "[z-a]", "\", "*a", "a**", "(?<=a)b", "(?<!a)b", "(?<n>a)", "(?i)a", "a++", "\p{L}", "(?#c)a", "a{,2}", "[[:alpha:]]", "(?:a)", "(?=a)", "(?!a)", "a*?", "\cA", "\x41", "\0", "[\d]", "\Z", "\A", "a{2}", "\k<n>", "(a)\2") ;; Err.Clear ;; r.Pattern = q ;; P q & "=" & Err.Number ;; Err.Clear ;; r.Test "a" ;; P Err.Number ;; Next""",
        """(=0|5020|a{2,1}=0|5017|[z-a]=0|5021|\=0|5017|*a=0|5018|a**=0|5018|(?<=a)b=0|5017|(?<!a)b=0|5017|(?<n>a)=0|5017|(?i)a=0|5017|a++=0|5018|\p{L}=0|0|(?#c)a=0|5017|a{,2}=0|0|[[:alpha:]]=0|0|(?:a)=0|0|(?=a)=0|0|(?!a)=0|0|a*?=0|0|\cA=0|0|\x41=0|0|\0=0|0|[\d]=0|0|\Z=0|0|\A=0|0|a{2}=0|0|\k<n>=0|0|(a)\2=0|0|""",
        DisplayName = "R09")]
    [DataRow(New + """Dim q As Variant ;; For Each q In Array(")", "a)", "[a", "]", "}", "a{2", "{2}", "x|*", "(*)", "\p", "a{1,2}{3}", "(?=a)*", "^*", "$+", "\b+", "a{2}?", "a?+", "a{2}*", "[]a", "[^]a", "(?", "(?:", "a{1,", "[\b]", "[a-\d]", "[\d-z]", "\8", "(a)\8", "a{99999}", "\c", "\c1", "\x4", "\u12", "a{3,2}", "[b-a]", "(a", "a|", "|", "()", "(|)", "[-a]", "[a-]", "[\w-z]", "\") ;; Err.Clear ;; r.Pattern = q ;; r.Test "a" ;; P q & "=" & Err.Number ;; Next""",
        """)=5017|a)=5017|[a=5019|]=0|}=0|a{2=0|{2}=5018|x|*=5018|(*)=5018|\p=0|a{1,2}{3}=5018|(?=a)*=0|^*=5018|$+=5018|\b+=5018|a{2}?=0|a?+=5018|a{2}*=5018|[]a=5019|[^]a=5019|(?=5017|(?:=5020|a{1,=0|[\b]=0|[a-\d]=5021|[\d-z]=0|\8=0|(a)\8=0|a{99999}=0|\c=0|\c1=0|\x4=0|\u12=0|a{3,2}=5017|[b-a]=5021|(a=5020|a|=0||=0|()=0|(|)=0|[-a]=0|[a-]=0|[\w-z]=0|\=5017|""",
        DisplayName = "T01")]
    [DataRow(New + """Dim q As Variant ;; For Each q In Array("[a", "(", "*", "[z-a]", "a{2,1}") ;; Err.Clear ;; r.Pattern = q ;; r.Test "a" ;; P Err.Number & ":" & Err.Description ;; Next""",
        "5019:Application-defined or object-defined error|5020:Application-defined or object-defined error|5018:Application-defined or object-defined error|5021:Application-defined or object-defined error|5017:Application-defined or object-defined error|",
        DisplayName = "S02 MS-VBA has no description of its own for them")]
    [DataRow(New + """r.Pattern = "(" ;; Err.Clear ;; r.Test "a" ;; P Err.Number ;; Err.Clear ;; P r.Execute("a").Count ;; P Err.Number ;; Err.Clear ;; P r.Replace("a", "b") ;; P Err.Number""",
        "5020|5020|5020|", DisplayName = "R10 every member that matches")]
    public void WhatIsNoPattern_IsARunTimeErrorOfItsOwn(string body, string printed) => Assert.AreEqual(printed, Run(body));

    // JScript's leniencies: an escape or a brace that means nothing is what it is written as (T02, T03, U01, U02, S04).
    [TestMethod]
    [DataRow(New + """Dim q As Variant, s As Variant, i As Long ;; q = Array("^]$", "^}$", "^a{2$", "^\p$", "^[\b]$", "^\8$", "^\c1$", "^\x4$", "^\u12$", "^\c$", "^[\d-z]$", "^[-a]$", "^a{1,$", "^()a$", "^a|$") ;; s = Array("]", "}", "a{2", "p", Chr(8), "8", "\c1", "x4", "u12", "\c", "-", "-", "a{1,", "a", "") ;; For i = 0 To UBound(q) ;; Err.Clear ;; r.Pattern = q(i) ;; P q(i) & "=" & r.Test(s(i)) & "/" & Err.Number ;; Next""",
        """^]$=True/0|^}$=True/0|^a{2$=True/0|^\p$=True/0|^[\b]$=True/0|^\8$=True/0|^\c1$=False/0|^\x4$=True/0|^\u12$=True/0|^\c$=False/0|^[\d-z]$=True/0|^[-a]$=True/0|^a{1,$=True/0|^()a$=True/0|^a|$=True/0|""",
        DisplayName = "T02")]
    [DataRow(New + """r.Pattern = "^\x4$" ;; P r.Test(Chr(4)) ;; r.Pattern = "^\x4g$" ;; P r.Test("x4g") ;; r.Pattern = "^[\b]$" ;; P r.Test("b") ;; P r.Test("\") ;; r.Pattern = "^[]$" ;; P r.Test("") ;; r.Pattern = "^a[]b$" ;; P r.Test("ab")""",
        "False|True|False|False|", DisplayName = "T03")]
    [DataRow(New + """Dim q As Variant, s As Variant, i As Long ;; q = Array("^\c1$", "^\c1$", "^\c1$", "^\c1$", "^\c$", "^\c$", "^\c$", "^[\c1]$", "^[\c1]$", "^[\c1]$", "^\c_$", "^\c_$", "^\cj$", "^[\cA]$", "^\c1$") ;; s = Array("c1", "\c1", Chr(17), "1", "c", "\", "", "c", "1", "\", Chr(31), "c_", vbLf, Chr(1), Chr(28) & "1") ;; For i = 0 To UBound(q) ;; Err.Clear ;; r.Pattern = q(i) ;; P i & ":" & r.Test(s(i)) & "/" & Err.Number ;; Next""",
        "0:False/0|1:False/0|2:False/0|3:False/0|4:False/0|5:False/0|6:False/0|7:False/0|8:True/0|9:False/0|10:False/0|11:False/0|12:True/0|13:True/0|14:False/0|",
        DisplayName = "U01 \\c and no letter")]
    [DataRow(New + """Dim q As Variant, s As Variant, i As Long ;; q = Array("^a{0}$", "^a{0,0}b$", "^(?:a|b)+$", "^[a-c-e]$", "^[a-c-e]$", "^[\w-]$", "^\B$", "^a\Bb$", "^\-$", "^[^\d]$", "^\x41B$", "^.$") ;; s = Array("", "b", "abba", "-", "d", "-", "", "ab", "-", "a", "AB", vbCr) ;; For i = 0 To UBound(q) ;; Err.Clear ;; r.Pattern = q(i) ;; P i & ":" & r.Test(s(i)) & "/" & Err.Number ;; Next""",
        "0:True/0|1:True/0|2:True/0|3:True/0|4:False/0|5:True/0|6:True/0|7:True/0|8:True/0|9:True/0|10:True/0|11:True/0|", DisplayName = "U02")]
    [DataRow(New + """r.Pattern = "^[a-\d]$" ;; P r.Test("-") & r.Test("5") & r.Test("b") ;; r.Pattern = "^\cJ$" ;; P r.Test(vbLf) ;; r.Pattern = "^\0$" ;; P r.Test(Chr(0)) ;; r.Pattern = "^\08$" ;; P r.Test(Chr(0) & "8") ;; r.Pattern = "^\377$" ;; P r.Test(Chr(255)) ;; r.Pattern = "^\400$" ;; P r.Test(Chr(32) & "0") ;; r.Pattern = "^(a)\1\2$" ;; P r.Test("aa" & Chr(2))""",
        "True|True|True|True|True|True|", DisplayName = "S04 octal escapes")]
    public void WhatMeansNothing_IsWhatItIsWrittenAs(string body, string printed) => Assert.AreEqual(printed, Run(body));

    // \s is ASCII, and \b is a boundary of ASCII word characters (S05); a line ends at U+2028 and U+2029 too, not at U+0085 (S07).
    [TestMethod]
    [DataRow(New + """r.Pattern = "^\s$" ;; P r.Test(vbTab) & r.Test(Chr(11)) & r.Test(Chr(12)) & r.Test(ChrW(8232)) & r.Test(ChrW(12288)) & r.Test(ChrW(6158)) ;; r.Pattern = "\bb" ;; P r.Test(ChrW(233) & "b") ;; r.Pattern = "^\W$" ;; P r.Test(ChrW(233))""",
        "TrueTrueTrueFalseFalseFalse|True|True|", DisplayName = "S05")]
    [DataRow(New + """r.MultiLine = True ;; r.Pattern = "^b" ;; P r.Test("a" & ChrW(8232) & "b") & r.Test("a" & ChrW(8233) & "b") & r.Test("a" & ChrW(133) & "b") ;; r.Pattern = "a$" ;; P r.Test("a" & ChrW(8232)) ;; r.Pattern = "^I$" ;; r.MultiLine = False ;; P r.Test(ChrW(304))""",
        "TrueTrueFalse|True|False|", DisplayName = "S07")]
    public void ClassesAndLines_AreJScripts(string body, string printed) => Assert.AreEqual(printed, Run(body));

    // a letter matches another when the two are the same in lower case - not the Kelvin, Ohm or Angstrom sign, which are not their letters (S06, T04).
    [TestMethod]
    [DataRow(New + """Dim q As Variant ;; r.IgnoreCase = True ;; For Each q In Array(ChrW(305), ChrW(304), ChrW(65313), ChrW(201), ChrW(233), ChrW(8490), ChrW(383), ChrW(352)) ;; r.Pattern = "^i$" ;; P AscW(q) & ":" & r.Test(q) ;; r.Pattern = "^a$" ;; P r.Test(q) ;; r.Pattern = "^e$" ;; P r.Test(q) ;; r.Pattern = "^k$" ;; P r.Test(q) ;; r.Pattern = "^s$" ;; P r.Test(q) ;; Next""",
        "305:False|False|False|False|False|304:True|False|False|False|False|-223:False|False|False|False|False|201:False|False|False|False|False|233:False|False|False|False|False|8490:False|False|False|False|False|383:False|False|False|False|False|352:False|False|False|False|False|",
        DisplayName = "S06 ASCII letters and their look-alikes")]
    // S06's last probe, with each character's other case written out: MS-VBA's UCase and LCase leave U+017F and U+212A as they are, and RD-VBA's do not.
    [DataRow(New + """Dim q As Variant, i As Long ;; r.IgnoreCase = True ;; q = Array(201, 233, 233, 201, 352, 353, 65313, 65345, 8490, 8490, 383, 383) ;; For i = 0 To UBound(q) Step 2 ;; r.Pattern = "^" & ChrW(q(i)) & "$" ;; P r.Test(ChrW(q(i + 1))) ;; Next""",
        "True|True|True|True|True|True|", DisplayName = "S06 a letter and its other case")]
    [DataRow(New + """Dim q As Variant ;; r.IgnoreCase = True ;; For Each q In Array(ChrW(8486), ChrW(8491), ChrW(969), ChrW(229), ChrW(8490)) ;; r.Pattern = "^" & ChrW(969) & "$" ;; P AscW(q) & ":" & r.Test(q) ;; r.Pattern = "^" & ChrW(229) & "$" ;; P r.Test(q) ;; r.Pattern = "^" & q & "$" ;; P r.Test(ChrW(969)) & r.Test(ChrW(229)) & r.Test(ChrW(107)) ;; Next ;; r.Pattern = "^[a-z]$" ;; P r.Test(ChrW(304)) & r.Test(ChrW(8490)) ;; r.Pattern = "^[" & ChrW(224) & "-" & ChrW(254) & "]$" ;; P r.Test(ChrW(192)) & r.Test(ChrW(215))""",
        "8486:False|False|FalseFalseFalse|8491:False|False|FalseFalseFalse|969:True|False|TrueFalseFalse|229:False|True|FalseTrueFalse|8490:False|False|FalseFalseFalse|TrueFalse|TrueFalse|",
        DisplayName = "T04")]
    public void IgnoringCase_IsComparingInLowerCase(string body, string printed) => Assert.AreEqual(printed, Run(body));

    // T05, with a reference in MS-VBA: an argument is coerced to the member's type before the call, so Null is 94 - except a replacement, a Variant.
    [TestMethod]
    public async Task AnArgument_IsCoercedToTheTypeOfItsParameter()
        => Assert.AreEqual("94|94|94|94|13|94|RegExp|MatchCollection|5|", await RunInWorkspace(
            """Dim r As New RegExp ;; Err.Clear ;; r.Pattern = Null ;; P Err.Number ;; Err.Clear ;; P r.Test(Null) ;; P Err.Number ;; Err.Clear ;; P r.Execute(Null).Count ;; P Err.Number ;; Err.Clear ;; P r.Replace(Null, "x") ;; P Err.Number ;; Err.Clear ;; P r.Replace("a", Null) ;; P Err.Number ;; Err.Clear ;; r.Global = Null ;; P Err.Number ;; P TypeName(r) ;; Dim m As MatchCollection ;; Set m = r.Execute("a") ;; P TypeName(m) ;; Err.Clear ;; P m(3).Value ;; P Err.Number"""));

    // R19.
    [TestMethod]
    public async Task AnArgumentOfAnotherType_IsLetCoerced()
        => Assert.AreEqual("12|True|13|True|0|", await RunInWorkspace(
            """Dim r As New RegExp ;; r.Pattern = 12 ;; P r.Pattern ;; P r.Test(12) ;; Err.Clear ;; r.Global = "yes" ;; P Err.Number ;; r.Global = 2 ;; P r.Global ;; P r.Execute(Empty).Count"""));

    // late-bound, MS-VBA's IDispatch converts an argument to the type of its parameter, and Null is a type mismatch (R19, T06): early-bound it is 94.
    [TestMethod]
    public void LateBound_NullIsATypeMismatch()
        => Assert.AreEqual("13|13|", Run(New + """Err.Clear ;; P r.Test(Null) ;; P Err.Number ;; Err.Clear ;; r.Global = Null ;; P Err.Number"""));

    // R17, where the probe's P read its argument's value with CStr: a RegExp has no default member, and the default member of a collection takes an argument.
    [TestMethod]
    public void AnObjectWithNoDefaultMemberToRead_HasNoValue()
        => Assert.AreEqual("438|450|450|", Run(New +
            """Dim m As Object ;; r.Pattern = "a" ;; Set m = r.Execute("a") ;; Err.Clear ;; P CStr(r) ;; P Err.Number ;; Err.Clear ;; P CStr(m) ;; P Err.Number ;; Err.Clear ;; P CStr(m(0).SubMatches) ;; P Err.Number"""));

    // R17: late-bound, MS-VBA's IDispatch reports 450 for a property a library's object cannot assign, where a workspace's class raises 451.
    [TestMethod]
    public void AReadOnlyProperty_AssignedLateBound_IsError450()
        => Assert.AreEqual("450|450|450|", Run(New +
            """Dim m As Object ;; r.Pattern = "a" ;; Set m = r.Execute("a") ;; Err.Clear ;; m(0).Value = "x" ;; P Err.Number ;; Err.Clear ;; m.Count = 3 ;; P Err.Number ;; Err.Clear ;; m(0).FirstIndex = 3 ;; P Err.Number"""));

    [TestMethod]
    public void AMatch_IsTheSameObjectEveryTimeItIsAskedFor()
        => Assert.AreEqual("True|True|False|", Run(New +
            """Dim m As Object ;; r.Pattern = "a" ;; r.Global = True ;; Set m = r.Execute("aa") ;; P m(0) Is m(0) ;; P m(0).SubMatches Is m(0).SubMatches ;; P m(0) Is m(1)"""));

    [TestMethod]
    public async Task ARegExpIsCreatedWithNew_WithNoReference()
        => Assert.AreEqual("RegExp|True|", await RunInWorkspace(
            """Dim r As RegExp ;; Set r = New RegExp ;; r.Pattern = "^\d+$" ;; P TypeName(r) ;; P r.Test("123")"""));
}
