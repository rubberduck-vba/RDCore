using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Values.Intrinsic;

namespace RDCore.SDK.Runtime.Abstract.StdLib;

/// <summary>
/// <strong>MS-VBAL 6.1.2.2 Constants Module</strong>
/// </summary>
/// <remarks>
/// Formalizes the public interface of the <c>Constants</c> module.<br/>
/// Defines a series of non-printable characters, making this a bit awkward.<br/>
/// 👉 Each field is one of the module's <c>Const</c> members, declared as the value it is:
/// <see cref="RDCore.SDK.Runtime.StdLib.StdLibSymbolReader"/> reads it as a <c>Public Const</c> of that value's type,
/// carrying the value, so a use site folds it like any other constant. Nothing implements this interface: a constant is
/// substituted where it is used, never called.
/// </remarks>
[StdLibModule]
public interface IStdConstantsModule
{
    /// <summary>
    /// <c>vbBack</c>: <c>VBA.Strings.Chr$(8)</c>.
    /// </summary>
    public static readonly VBStringValue VBBack = new("\b");

    /// <summary>
    /// <c>vbCr</c>: <c>VBA.Strings.Chr$(13)</c>.
    /// </summary>
    public static readonly VBStringValue VBCr = new("\r");

    /// <summary>
    /// <c>vbCrLf</c>: <c>VBA.Strings.Chr$(13) + VBA.Strings.Chr$(10)</c>.
    /// </summary>
    public static readonly VBStringValue VBCrLf = new("\r\n");

    /// <summary>
    /// <c>vbFormFeed</c>: <c>VBA.Strings.Chr$(12)</c>.
    /// </summary>
    public static readonly VBStringValue VBFormFeed = new("\f");

    /// <summary>
    /// <c>vbLf</c>: <c>VBA.Strings.Chr$(10)</c>.
    /// </summary>
    public static readonly VBStringValue VBLf = new("\n");

    /// <summary>
    /// <c>vbNewLine</c>: "An implementation-defined <c>String</c> value representing a new line".
    /// </summary>
    /// <remarks>
    /// 👉 CrLf rather than the host's own line separator, as <see cref="VBStackTrace"/> joins its lines: a VBA
    /// <c>String</c>'s line break is CrLf whatever platform the environment host runs on.
    /// </remarks>
    public static readonly VBStringValue VBNewLine = new("\r\n");

    /// <summary>
    /// <c>vbNullChar</c>: <c>VBA.Strings.Chr$(0)</c>.
    /// </summary>
    public static readonly VBStringValue VBNullChar = new("\0");

    /// <summary>
    /// <c>vbTab</c>: <c>VBA.Strings.Chr$(9)</c>.
    /// </summary>
    public static readonly VBStringValue VBTab = new("\t");

    /// <summary>
    /// <c>vbVerticalTab</c>: <c>VBA.Strings.Chr$(11)</c>.
    /// </summary>
    public static readonly VBStringValue VBVerticalTab = new("\v");

    /// <summary>
    /// <c>vbNullString</c>: "An implementation-defined <c>String</c> value representing a null string pointer".
    /// </summary>
    /// <remarks>
    /// <see cref="VBStringValue.VBNullString"/>: a <c>String</c> bound to a null pointer rather than to a zero-length
    /// string, which compares equal to <c>""</c> and occupies no string storage.
    /// </remarks>
    public static readonly VBStringValue VBNullString = VBStringValue.VBNullString;

    /// <summary>
    /// <c>vbObjectError</c>: <c>-2147221504</c>.
    /// </summary>
    /// <remarks>
    /// A <c>Long</c>, which is the type of that literal: it is beyond the range of an <c>Integer</c>.
    /// </remarks>
    public static readonly VBLongValue VBObjectError = new(-2147221504);
}
