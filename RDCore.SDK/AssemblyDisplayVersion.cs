using System.Reflection;

namespace RDCore.SDK;

/// <summary>
/// Resolves the version an RDCore application reports about itself, e.g. in the LSP <c>serverInfo</c> and <c>clientInfo</c>.
/// </summary>
/// <remarks>
/// The build stamps the full version into <see cref="AssemblyInformationalVersionAttribute"/>, prerelease suffix and
/// <c>+&lt;commit&gt;</c> build metadata included (e.g. <c>0.1.0-rc.1+2062f98…</c>), whereas <see cref="AssemblyName.Version"/>
/// only carries the numeric <c>0.1.0.0</c>.<br/>
/// ⚠️ This is for display only: anything that compares versions must keep using <see cref="AssemblyName.Version"/>,
/// because <see cref="Version"/> cannot parse a prerelease suffix or build metadata.
/// </remarks>
public static class AssemblyDisplayVersion
{
    /// <summary>
    /// The version reported when an assembly has neither an informational nor a numeric version.
    /// </summary>
    public const string Unknown = "0.0.0";

    /// <summary>
    /// Gets the display version of the specified assembly.
    /// </summary>
    /// <param name="assembly">The assembly to describe, e.g. the result of <see cref="Assembly.GetEntryAssembly"/>; may be <c>null</c>.</param>
    /// <returns>
    /// The assembly's <see cref="AssemblyInformationalVersionAttribute.InformationalVersion"/> as-is when present and not blank;
    /// otherwise its <see cref="AssemblyName.Version"/> as <c>major.minor.build</c>; otherwise <see cref="Unknown"/>.
    /// </returns>
    public static string Get(Assembly? assembly)
        => Get(assembly?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion, assembly?.GetName().Version);

    internal static string Get(string? informationalVersion, Version? version)
    {
        if (!string.IsNullOrWhiteSpace(informationalVersion))
        {
            return informationalVersion;
        }

        // ToString(3) throws for a two-part version (Build is -1), so pad it rather than trust the caller.
        return version is null
            ? Unknown
            : new Version(version.Major, version.Minor, Math.Max(version.Build, 0)).ToString();
    }
}
