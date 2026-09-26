using RDCore.SDK.Runtime.Abstract.Execution;
using System.Globalization;

namespace RDCore.SDK.Runtime;

/// <summary>
/// The default <see cref="IRuntimeEnvironmentProfile"/> implementation.
/// </summary>
/// <param name="Is64Bit">Whether the environment is 64-bit.</param>
/// <param name="Lcid">The environment LCID; <c>0</c> for the invariant locale.</param>
/// <param name="AnsiCodePage">The ANSI code page for <c>Byte()</c> ↔ <c>String</c>.</param>
/// <param name="SupportsOptionCompareDatabase">Whether <c>Option Compare Database</c> is supported.</param>
/// <param name="DatabaseCompare">The comparison mode <c>Option Compare Database</c> stands for; <c>Text</c> unless said otherwise.</param>
/// <param name="ErlLineNumbering">What <c>Erl</c> counts as a line; the document line unless said otherwise.</param>
/// <param name="AllowDllImports">Whether a <c>Declare</c>'d library import may be called; <c>true</c> unless said otherwise.</param>
public sealed record class RuntimeEnvironmentProfile(
    bool Is64Bit,
    int Lcid,
    int AnsiCodePage,
    bool SupportsOptionCompareDatabase,
    Model.Symbols.OptionCompare DatabaseCompare = Model.Symbols.OptionCompare.Text,
    Abstract.Execution.VBErlLineNumbering ErlLineNumbering = Abstract.Execution.VBErlLineNumbering.DocumentLine,
    bool AllowDllImports = true) : IRuntimeEnvironmentProfile
{
    /// <summary>
    /// A 64-bit, current-culture, Windows-1252, no-<c>Option Compare Database</c> profile for
    /// design-time and tests.
    /// </summary>
    public static IRuntimeEnvironmentProfile Default { get; } = new RuntimeEnvironmentProfile(
        Is64Bit: true,
        Lcid: 0,
        AnsiCodePage: 1252,
        SupportsOptionCompareDatabase: false);

    /// <summary>Builds a profile from bound <c>appsettings.json</c> options.</summary>
    public static RuntimeEnvironmentProfile From(Server.Configuration.SdkEnvironmentOptions options)
        => new(options.Is64Bit, options.Lcid, options.AnsiCodePage, options.SupportsOptionCompareDatabase, options.DatabaseCompare,
            options.ErlLineNumbering, options.AllowDllImports);

    /// <inheritdoc/>
    public CultureInfo Culture => Lcid == 0 ? CultureInfo.InvariantCulture : CultureInfo.GetCultureInfo(Lcid);
}
