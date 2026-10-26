using System.Collections.Frozen;

namespace RDCore.SDK.Model;

/// <summary>
/// What a name has to be for code to declare it (<strong>MS-VBAL §3.3.5</strong>), and why a text is not one.
/// </summary>
public enum VBIdentifierValidity
{
    /// <summary>The text is a name code can declare.</summary>
    Valid,

    /// <summary>The text is empty.</summary>
    Empty,

    /// <summary>
    /// The text is not an identifier: it does not begin with a letter, or it has something other than letters, digits and underscores
    /// (<strong>MS-VBAL §3.3.5</strong> <c>Latin-identifier</c>).
    /// </summary>
    NotAnIdentifier,

    /// <summary>The text is a keyword: a <c>reserved-identifier</c> of the language (<strong>MS-VBAL §3.3.5.2</strong>), which no declaration can be named.</summary>
    Reserved,
}

/// <summary>
/// The rules of the names code declares (<strong>MS-VBAL §3.3.5</strong>): what tooling asks of a name it writes into code, a rename's new name first.
/// </summary>
/// <remarks>
/// A name is a <c>Latin-identifier</c> - a letter, then letters, digits and underscores - that is not a <c>reserved-identifier</c> (§3.3.5.2). The letters of other
/// scripts are accepted where a Latin letter is: an implementation MAY support the identifier forms of a code page (§3.3.5.1), and MS-VBA does - <c>Données</c> is
/// a name. Names are the same in any case.
/// </remarks>
public static class VBIdentifier
{
    // MS-VBAL §3.3.5.2, every category of reserved-identifier: statement-keyword, rem-keyword, marker-keyword, operator-identifier, reserved-name,
    // special-form, reserved-type-identifier, literal-identifier, reserved-for-implementation-use and future-reserved. The specification's own list runs two of
    // the implementation's names together ("VB_Invoke_PropertyPutRefVB_MemberFlags"): they are the two names VB_Invoke_PropertyPutRef and VB_MemberFlags.
    private static readonly FrozenSet<string> _reserved = new[]
    {
        "Call", "Case", "Close", "Const", "Declare", "DefBool", "DefByte", "DefCur", "DefDate", "DefDbl", "DefInt", "DefLng", "DefLngLng", "DefLngPtr", "DefObj",
        "DefSng", "DefStr", "DefVar", "Dim", "Do", "Else", "ElseIf", "End", "EndIf", "Enum", "Erase", "Event", "Exit", "For", "Friend", "Function", "Get", "Global",
        "GoSub", "GoTo", "If", "Implements", "Input", "Let", "Lock", "Loop", "LSet", "Next", "On", "Open", "Option", "Print", "Private", "Public", "Put",
        "RaiseEvent", "ReDim", "Resume", "Return", "RSet", "Seek", "Select", "Set", "Static", "Stop", "Sub", "Type", "Unlock", "Wend", "While", "With", "Write",
        "Rem",
        "Any", "As", "ByRef", "ByVal", "Each", "In", "New", "Shared", "Until", "WithEvents", "Optional", "ParamArray", "Preserve", "Spc", "Tab", "Then", "To",
        "AddressOf", "And", "Eqv", "Imp", "Is", "Like", "Mod", "Not", "Or", "TypeOf", "Xor",
        "Abs", "CBool", "CByte", "CCur", "CDate", "CDbl", "CDec", "CInt", "CLng", "CLngLng", "CLngPtr", "CSng", "CStr", "CVar", "CVErr", "Date", "Debug",
        "DoEvents", "Fix", "Int", "Len", "LenB", "Me", "PSet", "Scale", "Sgn", "String",
        "Array", "Circle", "InputB", "LBound", "UBound",
        "Boolean", "Byte", "Currency", "Double", "Integer", "Long", "LongLong", "LongPtr", "Single", "Variant",
        "True", "False", "Nothing", "Empty", "Null",
        "Attribute", "LINEINPUT", "VB_Base", "VB_Control", "VB_Creatable", "VB_Customizable", "VB_Description", "VB_Exposed", "VB_Ext_KEY", "VB_GlobalNameSpace",
        "VB_HelpID", "VB_Invoke_Func", "VB_Invoke_Property", "VB_Invoke_PropertyPut", "VB_Invoke_PropertyPutRef", "VB_MemberFlags", "VB_Name", "VB_PredeclaredId",
        "VB_ProcData", "VB_TemplateDerived", "VB_UserMemId", "VB_VarDescription", "VB_VarHelpID", "VB_VarMemberFlags", "VB_VarProcData", "VB_VarUserMemId",
        "CDecl", "Decimal", "DefDec",
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Whether <paramref name="name"/> is a keyword of the language: a <c>reserved-identifier</c> (<strong>MS-VBAL §3.3.5.2</strong>).
    /// </summary>
    /// <param name="name">A name, without a type-declaration character.</param>
    public static bool IsReserved(string name) => _reserved.Contains(name);

    /// <summary>
    /// Whether code can declare <paramref name="name"/>, and why not when it cannot.
    /// </summary>
    /// <param name="name">The name, without a type-declaration character.</param>
    public static VBIdentifierValidity Validate(string name)
    {
        if (name.Length == 0)
        {
            return VBIdentifierValidity.Empty;
        }

        if (!char.IsLetter(name[0]) || !name.All(character => char.IsLetterOrDigit(character) || character == '_'))
        {
            return VBIdentifierValidity.NotAnIdentifier;
        }

        return IsReserved(name) ? VBIdentifierValidity.Reserved : VBIdentifierValidity.Valid;
    }
}
