using RDCore.SDK.Runtime.Abstract.Execution;

namespace RDCore.SDK.Runtime;

/// <summary>
/// The conditional-compilation constants every project starts with, which the environment defines (<strong>MS-VBAL §3.4.1</strong>): what each of them is in
/// an environment.
/// </summary>
/// <remarks>
/// <para>
/// One table for every reader: the environment host defines these in each session it composes, and what the language server makes of a <c>#If</c> - which
/// branch is live, what is analyzed - is what that session's constants say, so nothing reads them anywhere else.
/// </para>
/// <para>
/// Each is <c>1</c> where it is true and <c>0</c> where it is not, as MS-VBA defines its own (recorded in Excel), and not <c>-1</c> as a <c>Boolean</c> stores
/// <c>True</c>. A condition that tests one by itself means what it says, and so does a comparison with <c>0</c> or <c>1</c>; one that reads its bits does not:
/// <c>#If Not Win64</c> is live on 64-bit Windows, because <c>Not 1</c> is <c>-2</c>, and <c>Win64 = True</c> is <c>False</c> there
/// (<strong>MS-VBAL §5.6.9.8</strong>). The analyzers report such a condition.
/// </para>
/// <para>
/// A project <c>#Const</c>, a command-line define and a module's own <c>#Const</c> override any of them.
/// </para>
/// </remarks>
public static class PredefinedConditionalConstants
{
    /// <summary>
    /// The value of a predefined constant that is true: <c>1</c>, as MS-VBA defines <c>Win32</c>, <c>Win64</c>, <c>VBA6</c> and <c>VBA7</c>.
    /// </summary>
    public const short True = 1;

    /// <summary>
    /// The value of a predefined constant that is false.
    /// </summary>
    public const short False = 0;

    private const string Win16 = "Win16";

    private static readonly HashSet<string> NamesOfConstants =
        new([RuntimeSessionExtensions.DebugConstantName, Win16, "Win32", "Win64", "Mac", "Linux", "Linux64", "VBA6", "VBA7"], StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The names of the constants the environment defines, whatever the environment: each is defined, true or false, in every one.
    /// </summary>
    /// <remarks>Names are compared without regard to case, as a <c>#If</c> reads them.</remarks>
    public static IReadOnlySet<string> Names => NamesOfConstants;

    /// <summary>
    /// Whether the predefined constant <paramref name="name"/> is true in some environment: every one but <c>Win16</c>, which is never true.
    /// </summary>
    /// <param name="name">The name of a constant of <see cref="Names"/>.</param>
    /// <returns><see langword="false"/> for <c>Win16</c>, and for a name that is not one of <see cref="Names"/>.</returns>
    public static bool CanBeTrue(string name) => NamesOfConstants.Contains(name) && !string.Equals(name, Win16, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The name and the value of each constant the environment defines, in the order they are defined.
    /// </summary>
    /// <param name="environment">The environment: its operating system, its bitness and its build configuration decide the values.</param>
    /// <returns>
    /// <list type="table">
    /// <listheader><term>Constant</term><description>Is <c>1</c></description></listheader>
    /// <item><term><c>DEBUG</c></term><description>In a debug build (<see cref="IRuntimeEnvironmentProfile.Configuration"/>). RD-VBA's own.</description></item>
    /// <item><term><c>Win16</c></term><description>Never.</description></item>
    /// <item><term><c>Win32</c></term><description>On Windows, 32-bit or 64-bit: the Win32 API is there.</description></item>
    /// <item><term><c>Win64</c></term><description>On 64-bit Windows.</description></item>
    /// <item><term><c>Mac</c></term><description>On macOS.</description></item>
    /// <item><term><c>Linux</c></term><description>On Linux, 32-bit or 64-bit. RD-VBA's own.</description></item>
    /// <item><term><c>Linux64</c></term><description>On 64-bit Linux. RD-VBA's own.</description></item>
    /// <item><term><c>VBA6</c></term><description>Always, as in MS-VBA 7.</description></item>
    /// <item><term><c>VBA7</c></term><description>Always.</description></item>
    /// </list>
    /// </returns>
    public static IReadOnlyList<(string Name, short Value)> Of(IRuntimeEnvironmentProfile environment)
    {
        var os = environment.OperatingSystem;
        return
        [
            (RuntimeSessionExtensions.DebugConstantName, Of(environment.Configuration is not VBBuildConfiguration.Release)),
            (Win16, False),
            ("Win32", Of(os is VBOperatingSystem.Windows)),
            ("Win64", Of(os is VBOperatingSystem.Windows && environment.Is64Bit)),
            ("Mac", Of(os is VBOperatingSystem.MacOS)),
            ("Linux", Of(os is VBOperatingSystem.Linux)),
            ("Linux64", Of(os is VBOperatingSystem.Linux && environment.Is64Bit)),
            ("VBA6", True),
            ("VBA7", True),
        ];
    }

    private static short Of(bool value) => value ? True : False;
}
