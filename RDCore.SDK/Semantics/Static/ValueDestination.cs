namespace RDCore.SDK.Semantics.Static;

/// <summary>
/// What a place that expects a value expects of an object, at compile time: what <see cref="ObjectValueStaticSemantics"/> checks the object it is given
/// against (<strong>MS-VBAL §5.5.1.1</strong>, as MS-VBA compiles it).
/// </summary>
/// <remarks>
/// Where a value is expected, an object stands for its <em>simple data value</em>, what its default member returns (<strong>MS-VBAL §5.5.1.2.13</strong>).
/// The <c>Nothing</c> literal has none, and neither has an object whose default member takes an argument; what else is checked depends on the place.
/// </remarks>
public enum ValueDestination
{
    /// <summary>
    /// A value of whatever type it turns out to be: the argument of <c>Abs</c>, <c>Int</c> or <c>Fix</c>. An object whose default member returns an object is
    /// read at run time.
    /// </summary>
    AnyValue,

    /// <summary>
    /// A value of a declared type: a variable or a parameter of a value type, an operand, the argument of a conversion function such as <c>CInt</c>. An
    /// object whose default member returns an object is a type mismatch.
    /// </summary>
    TypedValue,

    /// <summary>
    /// "Any valid string expression, or any valid variable name": the argument of <c>Len</c> and <c>LenB</c> (<strong>MS-VBAL §6.1.2.11.1.22</strong>).
    /// </summary>
    /// <remarks>
    /// A <c>String</c>, a <c>Variant</c>, a late-bound object and a record are read as a value; a value of any other declared type must be a variable, and
    /// anything else is <see cref="Model.Errors.VBCompileErrorId.VariableRequired"/>: <c>Len(5)</c>, <c>Len(FnLong())</c>, an object whose default member
    /// returns neither a <c>String</c> nor a <c>Variant</c> (recorded in Excel).
    /// </remarks>
    StringOrVariable,

    /// <summary>
    /// What a pair of parentheses makes of what it encloses (<strong>MS-VBAL §5.6.6</strong>): the simple data value, read once - an object's default
    /// member, of the type it is declared as, whether that is a value's or an object's.
    /// </summary>
    /// <remarks>
    /// An object a default member returns is that object, of its declared class (MS-VBA, recorded in Excel): parentheses around it read its own
    /// default member in turn - <c>((o))</c> of a default member that returns a <c>Collection</c> is the <c>Collection</c>'s <c>Item</c>, which takes an
    /// argument.
    /// </remarks>
    SimpleDataValue,
}
