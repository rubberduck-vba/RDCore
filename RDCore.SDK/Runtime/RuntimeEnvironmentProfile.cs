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
public sealed record class RuntimeEnvironmentProfile(
    bool Is64Bit,
    int Lcid,
    int AnsiCodePage,
    bool SupportsOptionCompareDatabase) : IRuntimeEnvironmentProfile
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
        => new(options.Is64Bit, options.Lcid, options.AnsiCodePage, options.SupportsOptionCompareDatabase);

    /// <inheritdoc/>
    public CultureInfo Culture => Lcid == 0 ? CultureInfo.InvariantCulture : CultureInfo.GetCultureInfo(Lcid);
}
