using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Runtime.Abstract.Execution;

namespace RDCore.SDK.Runtime;

/// <summary>
/// What an <see cref="IRuntimeSession"/> says about the code that is executing in it.
/// </summary>
public static class RuntimeSessionExtensions
{
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
    {
        var declared = session.CallStack.Current?.Directives.Compare ?? OptionCompare.Binary;

        return declared != OptionCompare.Database
            ? declared
            : session.Environment.DatabaseCompare == OptionCompare.Binary ? OptionCompare.Binary : OptionCompare.Text;
    }

    /// <summary>
    /// How the relational operators compare <c>String</c> values at the point of the session's execution (<strong>MS-VBAL §5.6.9.5</strong>):
    /// the <see cref="CurrentCompareMode"/>, collated by the regional settings of the session's environment.
    /// </summary>
    /// <param name="session">The session the code is executing in.</param>
    public static StringComparisonRules CurrentStringComparison(this IRuntimeSession session)
        => new(session.CurrentCompareMode(), session.Environment.Culture);
}
