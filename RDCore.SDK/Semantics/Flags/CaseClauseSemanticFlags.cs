namespace RDCore.SDK.Semantics.Flags;

/// <summary>
/// The semantic flags that can be attached to a range clause of a <c>Case</c> (<strong>MS-VBAL 5.4.2.10</strong>): which form it takes, and how its value is
/// compared with the selector.
/// </summary>
[Flags]
public enum CaseClauseSemanticFlags
{
    /// <summary>
    /// No flags set.
    /// </summary>
    None = 0,
    /// <summary>
    /// The clause is a single value (<c>Case 5</c>), which MS-VBAL says "is compared with the value of select-expression", without saying how.
    /// </summary>
    ValueForm = 1 << 0,
    /// <summary>
    /// The clause is a comparison (<c>Case Is &gt; 5</c>), for which MS-VBAL states that the expression <c>select-expression &gt; 5</c> is evaluated.
    /// </summary>
    ComparisonForm = 1 << 1,
    /// <summary>
    /// The clause is a range (<c>Case 1 To 10</c>), for which MS-VBAL states that the expression
    /// <c>((select-expression) &gt;= (1)) And ((select-expression) &lt;= (10))</c> is evaluated.
    /// </summary>
    RangeForm = 1 << 2,
    /// <summary>
    /// The value of the clause is converted to the declared type of the selector, and compared with the selector in that type, where the comparison of the two
    /// as an expression has another <em>effective type</em> (<strong>MS-VBAL 5.6.9.5</strong>): the result can differ from the expression's.
    /// </summary>
    /// <remarks>
    /// 👉 What MS-VBA does, and RD-VBA with it (<strong>RD-VBAL 5.4.2.10</strong>): with <c>n</c> a <c>Long</c> of <c>2</c>, <c>Case 1.7</c> matches,
    /// because <c>1.7</c> is the <c>Long</c> <c>2</c>; the expression <c>n = 1.7</c> compares two <c>Double</c> values, and is <c>False</c>.
    /// </remarks>
    ComparedInSelectorType = 1 << 3,
}
