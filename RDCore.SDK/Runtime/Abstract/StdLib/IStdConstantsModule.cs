namespace RDCore.SDK.Runtime.Abstract.StdLib;

/// <summary>
/// <strong>MS-VBAL 6.1.2.2 Constants Module</strong>
/// </summary>
/// <remarks>
/// Formalizes the public interface of the <c>Constants</c> module.<br/>
/// Defines a series of non-printable characters, making this a bit awkward.<br/>
/// 👉 Environment host should expose all members of this module as <c>Const</c> members.
/// </remarks>
public interface IStdConstantsModule
{
    /// <summary>
    /// The <c>Chr$</c> integer value represented by the <c>vbNullChar</c> constant.
    /// </summary>
    const int VBNullCharChr = 0;
    /// <summary>
    /// The <c>Chr$</c> integer value represented by the <c>vbBack</c> constant.
    /// </summary>
    const int VBBackChr = 8;
    /// <summary>
    /// The <c>Chr$</c> integer value represented by the <c>vbTab</c> constant.
    /// </summary>
    const int VBTabChr = 9;
    /// <summary>
    /// The <c>Chr$</c> integer value represented by the <c>vbLf</c> constant.
    /// </summary>
    const int VBLfChr = 10;
    /// <summary>
    /// The <c>Chr$</c> integer value represented by the <c>vbVerticalTab</c> constant.
    /// </summary>
    const int VBVerticalTabChr = 11;
    /// <summary>
    /// The <c>Chr$</c> integer value represented by the <c>vbFormFeed</c> constant.
    /// </summary>
    const int VBFormFeedChr = 12;
    /// <summary>
    /// The <c>Chr$</c> integer value represented by the <c>vbCr</c> constant.
    /// </summary>
    const int VBCrChr = 13;
    /// <summary>
    /// The <c>Chr$</c> integer value represented by the <c>vbCrLf</c> constant.
    /// </summary>
    const int VBCrLfChr = 10 + 13;

    /// <summary>
    /// The integer value represented by the <c>vbObjectError</c> constant.
    /// </summary>
    const int VBObjectError = -2147221504;

    /// <summary>
    /// An implementation-defined <c>string</c> value representing a <em>new line</em>.
    /// </summary>
    string VBNewLine => Environment.NewLine;
    /// <summary>
    /// An implementation-defined <c>string</c> value representing a <em>null string pointer</em>.
    /// </summary>
    string VBNullString => null!;
}