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
/// Each is <c>True</c> (<c>-1</c>) or <c>False</c> (<c>0</c>), never another number, so that <c>#If Not Win64</c> and <c>#If Win64 = True</c> mean what
/// they say. A project <c>#Const</c>, a command-line define and a module's own <c>#Const</c> override any of them.
/// </para>
/// </remarks>
public static class PredefinedConditionalConstants
{
    /// <summary>
    /// The value of a constant that is <c>True</c>: <c>-1</c>, all bits set, as a <c>Boolean</c> stores it.
    /// </summary>
    public const short True = -1;

    /// <summary>
    /// The value of a constant that is <c>False</c>.
    /// </summary>
    public const short False = 0;

    /// <summary>
    /// The name and the value of each constant the environment defines, in the order they are defined.
    /// </summary>
    /// <param name="environment">The environment: its operating system, its bitness and its build configuration decide the values.</param>
    /// <returns>
    /// <list type="table">
    /// <listheader><term>Constant</term><description>Is <c>True</c></description></listheader>
    /// <item><term><c>DEBUG</c></term><description>In a debug build (<see cref="IRuntimeEnvironmentProfile.Configuration"/>). RD-VBA's own.</description></item>
    /// <item><term><c>Win16</c></term><description>Never.</description></item>
    /// <item><term><c>Win32</c></term><description>On Windows, 32-bit or 64-bit: the Win32 API is there.</description></item>
    /// <item><term><c>Win64</c></term><description>On 64-bit Windows.</description></item>
    /// <item><term><c>Mac</c></term><description>On macOS.</description></item>
    /// <item><term><c>Linux</c></term><description>On Linux, 32-bit or 64-bit. RD-VBA's own.</description></item>
    /// <item><term><c>Linux64</c></term><description>On 64-bit Linux. RD-VBA's own.</description></item>
    /// <item><term><c>VBA6</c></term><description>Never.</description></item>
    /// <item><term><c>VBA7</c></term><description>Always.</description></item>
    /// </list>
    /// </returns>
    public static IReadOnlyList<(string Name, short Value)> Of(IRuntimeEnvironmentProfile environment)
    {
        var os = environment.OperatingSystem;
        return
        [
            (RuntimeSessionExtensions.DebugConstantName, Of(environment.Configuration is not VBBuildConfiguration.Release)),
            ("Win16", False),
            ("Win32", Of(os is VBOperatingSystem.Windows)),
            ("Win64", Of(os is VBOperatingSystem.Windows && environment.Is64Bit)),
            ("Mac", Of(os is VBOperatingSystem.MacOS)),
            ("Linux", Of(os is VBOperatingSystem.Linux)),
            ("Linux64", Of(os is VBOperatingSystem.Linux && environment.Is64Bit)),
            ("VBA6", False),
            ("VBA7", True),
        ];
    }

    private static short Of(bool value) => value ? True : False;
}
