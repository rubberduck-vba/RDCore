namespace RDCore.SDK.Runtime.Abstract.Execution;

/// <summary>
/// The operating system an environment runs on: which of the built-in <c>Win32</c>, <c>Win64</c>, <c>Mac</c>, <c>Linux</c> and <c>Linux64</c>
/// conditional-compilation constants is <c>1</c>.
/// </summary>
/// <remarks>
/// Each of those constants is <c>1</c> only on its own operating system, and defined <c>0</c> on every other, so that <c>#If Linux Then</c> says
/// the same thing wherever it is compiled. A project <c>#Const</c> or a command-line define still overrides any of them.
/// </remarks>
public enum VBOperatingSystem
{
    /// <summary>
    /// Windows: <c>Win32</c> is <c>1</c>, and <c>Win64</c> too in a 64-bit environment.
    /// </summary>
    /// <remarks>
    /// The default: what MS-VBA runs on, and what a profile that does not say is.
    /// </remarks>
    Windows = 0,

    /// <summary>
    /// macOS: <c>Mac</c> is <c>1</c>.
    /// </summary>
    MacOS = 1,

    /// <summary>
    /// Linux: <c>Linux</c> is <c>1</c>, and <c>Linux64</c> too in a 64-bit environment.
    /// </summary>
    /// <remarks>
    /// Both are RD-VBA additions: MS-VBA does not run on Linux, and has neither.
    /// </remarks>
    Linux = 2,

    /// <summary>
    /// Any other operating system: every operating-system constant is <c>0</c>.
    /// </summary>
    Other = 3,
}
