using System.Globalization;
using System.Text;
using RegionalSettings = RDCore.SDK.Runtime.RegionalSettings;

namespace RDCore.SDK.Runtime.Abstract.Execution;

/// <summary>
/// An immutable description of the host environment an execution session runs in — the facts VBA
/// semantics depend on that are neither in the source nor derivable from it.
/// </summary>
/// <remarks>
/// Supplied by the environment host (from <c>appsettings.json</c>, overridable by the host). Design-
/// time and tests use <see cref="RuntimeEnvironmentProfile.Default"/>.
/// </remarks>
public interface IRuntimeEnvironmentProfile
{
    /// <summary>
    /// <c>true</c> for a 64-bit environment. Determines <c>LongPtr</c> width and the value of the
    /// <c>#If Win64</c> / <c>#If VBA7</c> pre-compiler directives.
    /// </summary>
    bool Is64Bit { get; }

    /// <summary>
    /// The environment locale identifier (LCID). <c>0</c> means the invariant locale.
    /// </summary>
    int Lcid { get; }

    /// <summary>
    /// The <see cref="CultureInfo"/> for <see cref="Lcid"/> — drives <c>Option Compare Text</c>,
    /// <c>Like</c>, case conversion, and <c>Format$</c> given a format.
    /// </summary>
    /// <remarks>
    /// 👉 Not how a value is Let-coerced to or from a <c>String</c>: a number or a date is written and read by <see cref="Regional"/>, which is the
    /// platform's and not the culture's, so that what <c>CStr</c> writes, <c>CDbl</c> and <c>CDate</c> read back.
    /// </remarks>
    CultureInfo Culture { get; }

    /// <summary>
    /// 🎯 The regional settings of Let-coercion to and from <c>String</c> (<strong>MS-VBAL §5.5.1.2.4</strong>): a number or a date is written by
    /// them, and a <c>String</c> read by them as a number or a date. The decimal, digit grouping and currency symbols, the Short Date and Long Time
    /// formats, and the window two-digit years are in; en-US unless the platform's configuration says otherwise.
    /// </summary>
    /// <remarks>
    /// One source for both directions, as MS-VBA's is the machine's regional settings: a value written as text reads back as the same value.
    /// </remarks>
    RegionalSettings Regional => RegionalSettings.Default;

    /// <summary>
    /// The ANSI code page: what a character is as a single byte or two outside a <c>String</c>'s own data —
    /// the text of a file, and the non-Unicode behaviour of <c>Chr</c> / <c>Asc</c>.
    /// </summary>
    /// <remarks>
    /// 👉 Not <c>Byte()</c> ↔ <c>String</c>, which reinterprets the string's own UTF-16 data
    /// (<strong>MS-VBAL §5.5.1.2.6</strong>) and needs no code page at all.
    /// </remarks>
    int AnsiCodePage { get; }

    /// <summary>
    /// The <see cref="Encoding"/> of <see cref="AnsiCodePage"/>: the one encoding the session's files and the
    /// standard library's character codes share, so that the two cannot disagree about what a byte is.
    /// </summary>
    Encoding AnsiEncoding { get; }

    /// <summary>
    /// <c>true</c> when the host supports the <c>Option Compare Database</c> directive (Microsoft Access).
    /// </summary>
    bool SupportsOptionCompareDatabase { get; }

    /// <summary>
    /// The comparison mode <c>Option Compare Database</c> stands for in this environment: <see cref="Model.Symbols.OptionCompare.Text"/>
    /// or <see cref="Model.Symbols.OptionCompare.Binary"/>.
    /// </summary>
    /// <remarks>
    /// 👉 MS-VBAL leaves <c>Option Compare Database</c> unspecified (§5.2.1.1); a module that declares it compares strings as this says.
    /// </remarks>
    Model.Symbols.OptionCompare DatabaseCompare { get; }

    /// <summary>
    /// 🎯 What <c>Erl</c> counts as the line a run-time error was raised at. <c>DocumentLine</c> unless a
    /// workspace asks for MS-VBA's own behaviour.
    /// </summary>
    VBErlLineNumbering ErlLineNumbering { get; }

    /// <summary>
    /// The language the code is written in: what the environment is a dialect of, and so which symbols and statements exist at all.
    /// </summary>
    /// <remarks>
    /// 👉 RD-VBA unless the workspace says otherwise. It is not derivable from the source, which is why it is a fact of the environment.
    /// </remarks>
    Workspace.SupportedLanguage Language { get; }

    /// <summary>
    /// 🎯 Whether a <c>Declare</c>'d library import may actually be called. <c>true</c> unless an
    /// administrator said otherwise.
    /// </summary>
    bool AllowDllImports { get; }

    /// <summary>
    /// 🎯 Whether a program may create and call the objects of a referenced library - a host application's object model among them (<c>Excel</c>,
    /// <c>Word</c>, <c>Scripting</c>). <c>true</c> unless an administrator said otherwise.
    /// </summary>
    /// <remarks>
    /// Its own switch, apart from <see cref="AllowDllImports"/>: automating a spreadsheet and calling an arbitrary export are not the same risk, and one
    /// switch for both would be of no use to anyone who needs one and not the other.
    /// </remarks>
    bool AllowAutomation { get; }

    /// <summary>
    /// 🎯 What runs the code of a program: the interpreter, or IL compiled from it where every instruction can be. <see cref="VBExecutionEngine.Interpreter"/>
    /// unless said otherwise.
    /// </summary>
    VBExecutionEngine ExecutionEngine { get; }

    /// <summary>
    /// 🎯 Whether a program is built to be debugged or to be run. <see cref="VBBuildConfiguration.Debug"/> unless said otherwise.
    /// </summary>
    /// <remarks>
    /// 👉 It is the default of the built-in <c>DEBUG</c> conditional-compilation constant, which a project <c>#Const</c> or a command-line define still overrides;
    /// what a program leaves out follows <c>DEBUG</c> (<see cref="RuntimeSessionExtensions.IsDebugBuild"/>).
    /// </remarks>
    VBBuildConfiguration Configuration { get; }

    /// <summary>
    /// 🎯 Whether the legacy settings members (<c>SaveSetting</c>, <c>GetSetting</c>, <c>GetAllSettings</c>, <c>DeleteSetting</c>) use the <em>Windows Registry</em>, as
    /// MS-VBA does. <c>false</c> unless the workspace asks for it: they use the <em>workspace application settings</em>.
    /// </summary>
    /// <remarks>
    /// 👉 Only Windows has a registry. Elsewhere the workspace application settings are used whatever this says (<strong>RD-VBAL §2.1.1</strong>).
    /// </remarks>
    bool LegacyRegistrySettings { get; }
}
