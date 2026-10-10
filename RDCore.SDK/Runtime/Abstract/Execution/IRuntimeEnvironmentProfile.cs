using System.Globalization;

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
    /// <c>Like</c>, <c>Format$</c>, and number / date let-coercion to and from <c>String</c>.
    /// </summary>
    CultureInfo Culture { get; }

    /// <summary>
    /// The ANSI code page for <c>Byte()</c> ↔ <c>String</c> conversions and the non-Unicode behaviour
    /// of <c>Chr</c> / <c>Asc</c>.
    /// </summary>
    int AnsiCodePage { get; }

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
}
