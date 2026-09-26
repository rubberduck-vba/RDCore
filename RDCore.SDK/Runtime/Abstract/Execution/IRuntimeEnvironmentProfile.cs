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
    /// 🎯 Whether a <c>Declare</c>'d library import may actually be called. <c>true</c> unless an
    /// administrator said otherwise.
    /// </summary>
    bool AllowDllImports { get; }
}
