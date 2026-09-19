namespace RDCore.SDK.Model.Symbols;

/// <summary>
/// The comparison mode of a module: how the relational operators compare <c>String</c> values (<strong>MS-VBAL §5.2.1.1</strong>,
/// <strong>§5.6.9.5</strong>).
/// </summary>
public enum OptionCompare
{
    /// <summary>
    /// <c>Option Compare Binary</c>: the strings are compared by the code of each character, so that <c>"a"</c> and <c>"A"</c> differ.
    /// This is the comparison mode of a module that declares no <c>Option Compare</c> directive.
    /// </summary>
    Binary = 0,

    /// <summary>
    /// <c>Option Compare Text</c>: the strings are compared regardless of case, according to the regional settings of the environment.
    /// </summary>
    Text = 1,

    /// <summary>
    /// <c>Option Compare Database</c>: unspecified by MS-VBAL, known to be supported by Microsoft Access.
    /// </summary>
    /// <remarks>
    /// 👉 What the strings are compared by is a setting of the platform (<c>IRuntimeEnvironmentProfile.DatabaseCompare</c>), which is
    /// either <see cref="Binary"/> or <see cref="Text"/>; an operation is never evaluated in <c>Database</c> mode.
    /// </remarks>
    Database = 2,
}
