using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace RDCore.CLI.App.Bench;

/// <summary>
/// What <c>rdc bench-msvba</c> imports into Excel: the corpus as MS-VBA reads it, the Excel automation cases, and the harness module that times them.
/// </summary>
internal static partial class MsVbaHarness
{
    /// <summary>The name of the harness module.</summary>
    public const string ModuleName = "XlBench";

    /// <summary>The prefix of the names of the Excel automation files embedded in <c>rdc</c>: <c>ExcelCases.bas</c> and <c>Program.bas</c>.</summary>
    public const string ExcelResourcePrefix = "Bench/Excel/";

    /// <summary>The module of the Excel automation cases, the same source on both sides.</summary>
    public const string ExcelCasesFile = "ExcelCases.bas";

    /// <summary>The RD-VBA program that runs the Excel automation cases; MS-VBA runs them from the harness instead.</summary>
    public const string ExcelProgramFile = "Program.bas";

    // MS-VBA rejects Local as the name of a variable ("Syntax error"), which MS-VBAL does not reserve and RD-VBA accepts: the MS-VBA copy names it LocalCount.
    [GeneratedRegex(@"\bLocal\b", RegexOptions.CultureInvariant)]
    private static partial Regex Local();

    /// <summary>
    /// A module of the corpus as it is imported into Excel: its lines ended with CR LF, and <c>Local</c>, which MS-VBA rejects as a name, named <c>LocalCount</c>.
    /// </summary>
    /// <param name="source">The module's source, as the corpus has it.</param>
    public static string Importable(string source) => Crlf(Local().Replace(source, "LocalCount"));

    /// <summary>
    /// The source of the harness module: each case called directly (no <c>Application.Run</c>) between two <c>QueryPerformanceCounter</c> reads, and the
    /// Excel automation cases run against a second Excel instance it starts.
    /// </summary>
    /// <remarks>
    /// <c>TimeCorpus(Runs)</c> returns a line a case, <c>Module.Case|us;us;...</c>, or <c>Module.Case|error &lt;number&gt; &lt;description&gt;</c> for a case
    /// that raises. <c>StartTarget</c> starts the second instance and returns its process id, <c>TimeExcel(Runs)</c> runs <c>ExcelCases.RunExcelCases</c>
    /// against it, and <c>StopTarget</c> quits it. <c>HostProcess</c> and <c>Bitness</c> say which process the harness runs in, and whether it is 64-bit.
    /// </remarks>
    /// <param name="cases">The cases, <c>Module.Procedure</c>, in the order <c>rdc bench</c> runs them.</param>
    public static string ModuleText(IReadOnlyList<string> cases)
    {
        var text = new StringBuilder();
        void Line(string line = "") => text.Append(line).Append("\r\n");

        Line($"Attribute VB_Name = \"{ModuleName}\"");
        Line("Option Explicit");
        Line("'  The harness of rdc bench-msvba: each case of the corpus called between two QueryPerformanceCounter reads, every run in microseconds, and the");
        Line("'  Excel automation cases run against a second Excel instance, so that MS-VBA crosses a process boundary as RD-VBA does.");
        Line("Private Declare PtrSafe Function QueryPerformanceCounter Lib \"kernel32\" (ByRef Count As Currency) As Long");
        Line("Private Declare PtrSafe Function QueryPerformanceFrequency Lib \"kernel32\" (ByRef Frequency As Currency) As Long");
        Line("Private Declare PtrSafe Function GetWindowThreadProcessId Lib \"user32\" (ByVal hWnd As LongPtr, ByRef ProcessId As Long) As Long");
        Line("Private Declare PtrSafe Function GetCurrentProcessId Lib \"kernel32\" () As Long");
        Line();
        Line($"Private Const CaseCount As Long = {cases.Count.ToString(CultureInfo.InvariantCulture)}");
        Line("Private Target As Object");
        Line();
        Line("Private Function CaseName(ByVal Index As Long) As String");
        Line("    Select Case Index");
        for (var index = 0; index < cases.Count; index++)
        {
            Line($"        Case {(index + 1).ToString(CultureInfo.InvariantCulture)}: CaseName = \"{cases[index]}\"");
        }

        Line("    End Select");
        Line("End Function");
        Line();
        Line("Private Sub RunCase(ByVal Index As Long)");
        Line("    Select Case Index");
        for (var index = 0; index < cases.Count; index++)
        {
            Line($"        Case {(index + 1).ToString(CultureInfo.InvariantCulture)}: {cases[index]}");
        }

        Line("    End Select");
        Line("End Sub");
        Line();
        Line("'  each case, Runs times, a line each: \"Module.Case|us;us;...\", or \"Module.Case|error <number> <description>\" for a case that raises.");
        Line("Public Function TimeCorpus(ByVal Runs As Long) As String");
        Line("    Dim F As Currency, T0 As Currency, T1 As Currency, I As Long, R As Long, Entry As String, Result As String");
        Line("    QueryPerformanceFrequency F");
        Line("    For I = 1 To CaseCount");
        Line("        Entry = CaseName(I) & \"|\"");
        Line("        On Error GoTo Failed");
        Line("        For R = 1 To Runs");
        Line("            QueryPerformanceCounter T0");
        Line("            RunCase I");
        Line("            QueryPerformanceCounter T1");
        Line("            Entry = Entry & CStr(CLng((T1 - T0) / F * 1000000#)) & \";\"");
        Line("        Next");
        Line("        GoTo Done");
        Line("Failed:");
        Line("        Entry = Entry & \"error \" & Err.Number & \" \" & Err.Description");
        Line("        Resume Done");
        Line("Done:");
        Line("        On Error GoTo 0");
        Line("        Result = Result & Entry & vbLf");
        Line("    Next");
        Line("    TimeCorpus = Result");
        Line("End Function");
        Line();
        Line("Public Function HostProcess() As Long");
        Line("    HostProcess = GetCurrentProcessId()");
        Line("End Function");
        Line();
        Line("Public Function Bitness() As Long");
        Line("#If Win64 Then");
        Line("    Bitness = 64");
        Line("#Else");
        Line("    Bitness = 32");
        Line("#End If");
        Line("End Function");
        Line();
        Line("'  the second Excel instance the Excel automation cases run against: its process id.");
        Line("Public Function StartTarget() As Long");
        Line("    Dim Id As Long");
        Line("    Set Target = CreateObject(\"Excel.Application\")");
        Line("    Target.Visible = False");
        Line("    Target.DisplayAlerts = False");
        Line("    GetWindowThreadProcessId Target.Hwnd, Id");
        Line("    StartTarget = Id");
        Line("End Function");
        Line();
        Line("'  the Excel automation cases, Runs times, against the second instance, as RD-VBA's program runs them.");
        Line("Public Function TimeExcel(ByVal Runs As Long) As String");
        Line("    TimeExcel = ExcelCases.RunExcelCases(Target, Runs)");
        Line("End Function");
        Line();
        Line("Public Sub StopTarget()");
        Line("    If Not Target Is Nothing Then Target.Quit");
        Line("    Set Target = Nothing");
        Line("End Sub");
        return text.ToString();
    }

    private static string Crlf(string text) => text.ReplaceLineEndings("\r\n");
}

/// <summary>
/// The figures of the runs of <c>rdc bench-msvba</c>: what each side's output says of each case, the fastest run kept, in milliseconds.
/// </summary>
internal static partial class BenchFigures
{
    // a case rdc bench ran: its name, its runs, its fastest and its median run, and what the fastest run allocated when the build says so.
    [GeneratedRegex(@"^(?<case>[A-Za-z]\w*\.Bench\w+)\s+\d+\s+(?<min>[0-9.]+) ms\s+[0-9.]+ ms(\s+\d+ KB\s+\d+/\d+/\d+)?\s*$", RegexOptions.CultureInvariant)]
    private static partial Regex RdcCase();

    // a case rdc bench names: a case that failed says why after its runs, and rdc bench --list names each case alone.
    [GeneratedRegex(@"^(?<case>[A-Za-z]\w*\.Bench\w+)(?<rest>\s.*)?$", RegexOptions.CultureInvariant)]
    private static partial Regex RdcName();

    // a run of the harness or of ExcelCases: "Module.Case|us;us;..." or "Excel.Case|phase|us;us;...".
    [GeneratedRegex(@"^(?<case>[A-Za-z]\w*\.\w+)(\|(?<phase>[a-z]+))?\|(?<runs>\d+(;\d+)*;?)$", RegexOptions.CultureInvariant)]
    private static partial Regex HarnessRuns();

    /// <summary>
    /// The cases <c>rdc bench --list</c> names, in its order.
    /// </summary>
    /// <param name="output">What <c>rdc bench --list</c> wrote.</param>
    public static IReadOnlyList<string> CaseList(string output) =>
        [.. Lines(output).Select(line => RdcName().Match(line)).Where(match => match.Success && match.Groups["rest"].Value.Trim().Length == 0).Select(match => match.Groups["case"].Value)];

    /// <summary>
    /// Keeps the fastest run of each case of a report of <c>rdc bench</c>.
    /// </summary>
    /// <param name="output">What <c>rdc bench</c> wrote.</param>
    /// <param name="fastest">The fastest run of each case so far, in milliseconds; a faster run replaces it.</param>
    /// <param name="problems">Told each case that failed, as the report says it.</param>
    public static void ReadRdcBench(string output, IDictionary<string, double> fastest, ICollection<string> problems)
    {
        foreach (var line in Lines(output))
        {
            if (RdcCase().Match(line) is { Success: true } match)
            {
                Keep(fastest, match.Groups["case"].Value, double.Parse(match.Groups["min"].Value, CultureInfo.InvariantCulture));
            }
            else if (RdcName().Match(line) is { Success: true } named && named.Groups["rest"].Value.Trim().Length > 0)
            {
                problems.Add(line.Trim());
            }
        }
    }

    /// <summary>
    /// Keeps the fastest run of each case of what the harness or <c>ExcelCases</c> returned: <c>Module.Case|us;us;...</c> for a case of the corpus,
    /// <c>Excel.Case|phase|us;us;...</c> for a phase of an Excel automation case, named <c>Excel.Case.Phase</c>.
    /// </summary>
    /// <param name="output">What the harness returned, a line a case or a phase.</param>
    /// <param name="fastest">The fastest run of each case so far, in milliseconds; a faster run replaces it.</param>
    /// <param name="problems">Told each line that is not a case's runs: a case that raised says so.</param>
    public static void ReadHarness(string output, IDictionary<string, double> fastest, ICollection<string> problems)
    {
        foreach (var line in Lines(output))
        {
            if (HarnessRuns().Match(line) is not { Success: true } match)
            {
                problems.Add(line);
                continue;
            }

            var phase = match.Groups["phase"].Value;
            var name = phase.Length == 0 ? match.Groups["case"].Value : $"{match.Groups["case"].Value}.{char.ToUpperInvariant(phase[0])}{phase[1..]}";
            var microseconds = match.Groups["runs"].Value.TrimEnd(';').Split(';').Min(run => long.Parse(run, CultureInfo.InvariantCulture));
            Keep(fastest, name, microseconds / 1000.0);
        }
    }

    private static void Keep(IDictionary<string, double> fastest, string name, double milliseconds)
    {
        if (!fastest.TryGetValue(name, out var kept) || milliseconds < kept)
        {
            fastest[name] = milliseconds;
        }
    }

    private static IEnumerable<string> Lines(string output) =>
        output.ReplaceLineEndings("\n").Split('\n').Select(line => line.TrimEnd()).Where(line => line.Length > 0);
}

/// <summary>
/// The stamp of a reference run: what was run, when, and on what.
/// </summary>
/// <param name="Commit">The commit <c>rdc</c> was built from.</param>
/// <param name="Date">The day of the run, <c>yyyy-MM-dd</c>.</param>
/// <param name="Excel">The Excel that ran MS-VBA: its version, build and bitness.</param>
/// <param name="Os">The operating system and its architecture.</param>
/// <param name="Cores">The machine's logical processors.</param>
/// <param name="Method">How the rounds were run (<see cref="ReferenceData.Method"/>).</param>
internal sealed record class ReferenceStamp(string Commit, string Date, string Excel, string Os, int Cores, string Method);

/// <summary>
/// The fastest run of a case on each side, in milliseconds.
/// </summary>
/// <param name="MsVba">MS-VBA's.</param>
/// <param name="RdVba">RD-VBA's.</param>
internal readonly record struct ReferenceFigure(double MsVba, double RdVba);

/// <summary>
/// The reference data the CI bench job reads (<c>RDCore.CLI/Bench/Reference/msvba.json</c>): the stamp of the run, then each case's fastest run on each side,
/// the corpus under <c>cases</c> and the Excel automation cases under <c>excel</c>.
/// </summary>
internal static class ReferenceData
{
    /// <summary>
    /// Whether MS-VBA runs first in a round: in odd rounds; RD-VBA runs first in even ones, so neither side always runs on a machine the other has warmed.
    /// </summary>
    /// <param name="round">The round, from 1.</param>
    public static bool MsVbaFirst(int round) => round % 2 == 1;

    /// <summary>
    /// How the rounds are run, as the stamp says it: the rounds, which side runs first, the runs a case, and how the Excel automation cases are run.
    /// </summary>
    /// <remarks>The text is data of the reference file, in English whatever the culture.</remarks>
    /// <param name="rounds">The rounds of the corpus.</param>
    /// <param name="excelRounds">The first rounds that run the Excel automation cases as well; none when 0.</param>
    /// <param name="runs">The runs of a case of the corpus, a round.</param>
    /// <param name="excelRuns">The runs of an Excel automation case, a round.</param>
    public static string Method(int rounds, int excelRounds, int runs, int excelRuns)
    {
        var text = new StringBuilder(string.Create(CultureInfo.InvariantCulture, $"alternated rounds on one machine: {rounds} for the corpus"));
        if (excelRounds > 0)
        {
            text.Append(excelRounds == rounds ? ", each with Excel automation" : $", the first {excelRounds} with Excel automation");
        }

        text.Append(rounds == 1 ? " (MS-VBA first)" : " (MS-VBA first in odd rounds, RD-VBA first in even rounds)");
        text.Append(CultureInfo.InvariantCulture, $"; {runs} runs a case");
        if (excelRounds > 0)
        {
            text.Append(CultureInfo.InvariantCulture, $" ({excelRuns} for Excel automation)");
        }

        text.Append(", fastest run");
        if (excelRounds > 0)
        {
            text.Append("; MS-VBA runs the Excel automation cases from a second Excel instance, as RD-VBA runs them out of process, so both cross a process boundary");
        }

        return text.ToString();
    }

    /// <summary>
    /// The reference file, as JSON indented by two spaces.
    /// </summary>
    /// <param name="stamp">What was run, when, and on what.</param>
    /// <param name="cases">The cases of the corpus, in the order they ran.</param>
    /// <param name="excel">The phases of the Excel automation cases, in the order they ran.</param>
    public static string ToJson(ReferenceStamp stamp, IEnumerable<KeyValuePair<string, ReferenceFigure>> cases, IEnumerable<KeyValuePair<string, ReferenceFigure>> excel)
    {
        using var stream = new MemoryStream();
        using (var json = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            json.WriteStartObject();
            json.WriteStartObject("reference");
            json.WriteString("commit", stamp.Commit);
            json.WriteString("date", stamp.Date);
            json.WriteString("excel", stamp.Excel);
            json.WriteString("os", stamp.Os);
            json.WriteNumber("cores", stamp.Cores);
            json.WriteString("method", stamp.Method);
            json.WriteEndObject();
            foreach (var (section, figures) in new[] { ("cases", cases), ("excel", excel) })
            {
                json.WriteStartObject(section);
                foreach (var (name, figure) in figures)
                {
                    json.WriteStartObject(name);
                    json.WriteNumber("msvba", Math.Round(figure.MsVba, 3));
                    json.WriteNumber("rdvba", Math.Round(figure.RdVba, 3));
                    json.WriteEndObject();
                }

                json.WriteEndObject();
            }

            json.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray()).ReplaceLineEndings("\n") + "\n";
    }
}
