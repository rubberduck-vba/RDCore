using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Runtime.Abstract.Execution;

namespace RDCore.SDK.Runtime;

/// <summary>
/// What an <see cref="IRuntimeSession"/> says about the code that is executing in it.
/// </summary>
public static class RuntimeSessionExtensions
{
    /// <summary>
    /// The name of the conditional compilation constant that says whether this is a debug build.
    /// </summary>
    /// <remarks>
    /// RDCore's own, not MS-VBAL's — VBA has no such constant. It is a conditional compilation
    /// constant rather than a setting of its own so that <c>#If DEBUG Then</c> and "does this
    /// <c>Debug.Print</c> run" cannot disagree, and so that the precedence a project already has over
    /// its constants (a <c>--define</c> beats the <c>.rdproj</c>, which beats the built-in) applies to
    /// it unchanged.
    /// </remarks>
    public const string DebugConstantName = "DEBUG";

    /// <summary>
    /// Whether this session is running a debug build — whether <c>Stop</c> breaks, and whether a release build's
    /// choice to drop <c>Debug.Print</c> and <c>Debug.Assert</c> (<see cref="Abstract.Execution.IRuntimeEnvironmentProfile.ReleaseDropsDebugStatements"/>) applies.
    /// </summary>
    /// <remarks>
    /// False only when <see cref="DebugConstantName"/> is defined <em>and</em> zero. A session
    /// composed without the constant at all is a debug build: keeping the statements is the
    /// recoverable answer, and silently dropping every <c>Debug.Print</c> because a symbol provider
    /// was missing is not.
    /// </remarks>
    /// <param name="session">The session the code is executing in.</param>
    public static bool IsDebugBuild(this IRuntimeSession session)
        => !session.Symbols.TryResolveConditionalConstant(DebugConstantName, GlobalSymbols.UnresolvedSymbol, out var symbol)
            || symbol is not PrecompilerConstantSymbol constant
            || !IsZero(constant.Value);

    private static bool IsZero(Model.Values.Abstract.VBTypedValue value)
        => value.Handle.Value.BoxedValue switch
        {
            byte number => number == 0,
            short number => number == 0,
            int number => number == 0,
            long number => number == 0,
            double number => number == 0,
            _ => false,
        };

    /// <summary>
    /// The mode the relational operators compare <c>String</c> values in, at the point of the session's execution
    /// (<strong>MS-VBAL §5.2.1.1</strong>): the comparison mode of the module declaring the procedure of the current call frame,
    /// <see cref="OptionCompare.Binary"/> if nothing is executing.
    /// </summary>
    /// <remarks>
    /// 👉 Never <see cref="OptionCompare.Database"/>: a module that declares <c>Option Compare Database</c> compares as the platform
    /// says (<see cref="IRuntimeEnvironmentProfile.DatabaseCompare"/>).
    /// </remarks>
    /// <param name="session">The session the code is executing in.</param>
    public static OptionCompare CurrentCompareMode(this IRuntimeSession session)
        => Resolve(session, session.CallStack.Current?.Directives.Compare ?? OptionCompare.Binary);

    /// <summary>
    /// How the relational operators compare <c>String</c> values at the point of the session's execution (<strong>MS-VBAL §5.6.9.5</strong>):
    /// the <see cref="CurrentCompareMode"/>, collated by the regional settings of the session's environment.
    /// </summary>
    /// <param name="session">The session the code is executing in.</param>
    public static StringComparisonRules CurrentStringComparison(this IRuntimeSession session)
        => session.StringComparisonIn(session.CurrentCompareMode());

    /// <summary>
    /// How <c>String</c> values compare in <paramref name="mode"/>, collated by the regional settings of the session's environment: what a
    /// library member that is told which comparison to make compares with.
    /// </summary>
    /// <remarks>
    /// 👉 <see cref="OptionCompare.Database"/> is resolved the way a module's own is (<see cref="IRuntimeEnvironmentProfile.DatabaseCompare"/>).
    /// </remarks>
    /// <param name="session">The session the code is executing in.</param>
    /// <param name="mode">The comparison mode.</param>
    public static StringComparisonRules StringComparisonIn(this IRuntimeSession session, OptionCompare mode)
        => new(Resolve(session, mode), session.Environment.Culture);

    // Option Compare Database compares as the environment says it does; the other two are what they say.
    private static OptionCompare Resolve(IRuntimeSession session, OptionCompare mode)
        => mode != OptionCompare.Database
            ? mode
            : session.Environment.DatabaseCompare == OptionCompare.Binary ? OptionCompare.Binary : OptionCompare.Text;
}
