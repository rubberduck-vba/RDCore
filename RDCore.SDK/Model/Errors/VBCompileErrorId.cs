using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Semantics.Flags;
namespace RDCore.SDK.Model.Errors;

/// <summary>
/// Formally encodes a range of integer values for all <see cref="VBSyntaxErrorInfo"/> and <see cref="VBCompileErrorInfo"/> errors.
/// </summary>
/// <remarks>
/// 👉 The members of this <c>enum</c> are divided into sections.<br/>
/// <list type="bullet">
/// <item><strong>0 to 1</strong>: Generic system or fallback defaults.</item>
/// <item><strong>2 to 41</strong>: Reserved</item>
/// <item><strong>42 to 999</strong>: Reserved for <strong>RD-VBA</strong> formalized syntax errors (<see cref="VBSyntaxErrorInfo"/>).</item>
/// <item><strong>1000 to 7999</strong>: Reserved</item>
/// <item><strong>8000 to 9299</strong>: Reserved for future <strong>RD-VBA</strong> extensibility.</item>
/// <item><strong>9300+</strong>: Formalized MS-VBA compilation errors. <strong>This list is currently vastly incomplete.</strong></item>
/// </list>
/// ℹ️ <c>93</c> is for <c>1993</c>, the year MS-VBA came into existence.
/// </remarks>
public enum VBCompileErrorId
{
    #region [0,1]: system fallbacks/defaults
    /// <summary>
    /// A fallback error ID for a compilation error.
    /// </summary>
    /// <remarks>
    /// ⚠️ This value may not reliably be interpreted as a valid compilation error ID.
    /// </remarks>
    UnspecifiedCompileError = 0,
    /// <summary>
    /// A generic "Syntax error" compile-time error.<br/>
    /// <a href="https://learn.microsoft.com/office/vba/language/reference/user-interface-help/syntax-error">learn.microsoft.com</a>
    /// </summary>
    /// <remarks>
    /// 👉 <c>RDCore.Parsing</c> <em>should</em> be giving us <em>much</em> more detailed syntax errors that this.
    /// </remarks>
    SyntaxError = 1,
    #endregion

    #region [42..999]: parser errors (VBSyntaxErrorInfo)

    /****************************************************************************************************
     * 💯 This section is intended to grow as RDCore.Parsing starts assembling the SDK-defined AST nodes.
     *     👉 THANK YOU for taking the time to write XML documentation for anything you add here.
    /****************************************************************************************************/

    #endregion

    #region [8000..9299]: RD-VBA reserved for future extensibility
    /// <summary>
    /// An <c>Option Strict</c> <em>directive</em> or <em>annotation</em> is enforcing stricter semantics that block a number of <em>semantic flags</em> at compile-time:
    /// <list type="bullet">
    /// <item><see cref="ConversionSemanticFlags.Implicit"/></item>
    /// <item><see cref="ConversionSemanticFlags.ObjectOperand"/></item>
    /// <item><see cref="ConversionSemanticFlags.Narrowing"/></item>
    /// <item><see cref="MemberAccessOperationSemanticFlags.LateBound"/></item>
    /// </list>
    /// </summary>
    /// <remarks>
    /// ℹ️ <strong>This is a <em>specified error code</em>, not (yet) a language feature.</strong><br/>
    /// The <strong>VB.NET</strong> <c>Option Strict</c> <em>statement</em> (RD-VBA: "directive") is documented as follows:<br/>
    /// 🎯 Restricts implicit data type conversions to only widening conversions, disallows late binding, and disallows implicit typing that results in an Object type.<br/>
    /// <a href="https://learn.microsoft.com/dotnet/visual-basic/language-reference/statements/option-strict-statement">learn.microsoft.com</a>
    /// </remarks>
    ForbiddenWithOptionStrict = 8000,

    /****************************************************************************************************
     * 💯 This section is intended to grow as RDCore.Parsing starts assembling the SDK-defined AST nodes.
     *     👉 THANK YOU for taking the time to write XML documentation for anything you add here.
    /****************************************************************************************************/
    #endregion

    #region [9300..] MS-VBA compilation errors, formalized.
    /***
     * [9300+]: Formalized MS-VBA compilation error messages (VBCompileErrorInfo).
     * LORE: 93 is for 1993, the year MS-VBA came into existence.
     */
    /// <summary>
    /// An <em>identifier</em> conflicts with another identifier or requires qualification.<br/>
    /// <a href="https://learn.microsoft.com/office/vba/language/reference/user-interface-help/ambiguous-name-detected">learn.microsoft.com</a>
    /// </summary>
    AmbiguousName = 9301,
    /// <summary>
    /// You use the <c>Option Explicit</c> <em>directive</em> to protect your <em>modules</em> from having undeclared <em>variables</em> and to eliminate the possibility of inadvertently creating new variables when typographical errors occur.<br/>
    /// <a href="https://learn.microsoft.com/office/vba/language/reference/user-interface-help/variable-not-defined">learn.microsoft.com</a>
    /// </summary>
    VariableNotDefined = 9302,
    /// <summary>
    /// The specified name is already used at this level of <em>scope</em>.<br/>
    /// <a href="https://learn.microsoft.com/office/vba/language/reference/user-interface-help/duplicate-declaration-in-current-scope">learn.microsoft.com</a>
    /// </summary>
    DuplicateDeclaration = 9303,
    /// <summary>
    /// You tried to use an object in an incorrect way.<br/>
    /// <a href="https://learn.microsoft.com/office/vba/language/reference/user-interface-help/invalid-use-of-object">learn.microsoft.com</a>
    /// </summary>
    InvalidUseOfObject = 9304,
    /// <summary>
    /// The <em>parameter</em> defined as a <c>ParamArray</c> is used incorrectly in the <em>procedure</em>.<br/>
    /// <a href="https://learn.microsoft.com/office/vba/language/reference/user-interface-help/invalid-paramarray-use">learn.microsoft.com</a>
    /// </summary>
    InvalidParamArrayUse = 9305,
    /// <summary>
    /// Not every <see cref="VBArrayValue"/> can be redimensioned.<br/>
    /// <a href="https://learn.microsoft.com/office/vba/language/reference/user-interface-help/invalid-redim">learn.microsoft.com</a>
    /// </summary>
    InvalidReDim = 9306,
    /// <summary>
    /// A <em>variable</em> name with a <em>subscript</em> indicates the variale is a <see cref="VBArrayValue"/>.<br/>
    /// <a href="https://learn.microsoft.com/office/vba/language/reference/user-interface-help/expected-array">learn.microsoft.com</a>
    /// </summary>
    ExpectedArray = 9307,
    /// <summary>
    /// Syntax specifies an identifier name in this context, but none was supplied.<br/>
    /// </summary>
    /// <remarks>
    /// ℹ️ Unable to find an official <c>learn.microsoft.com</c> documentation link for this error.
    /// </remarks>
    ExpectedIdentifier = 9308,
    /// <summary>
    /// A <em>line label</em> or <em>line number</em> is referred to (for example in a <c>GoTo</c> statement), but doesn't occur within the <em>scope</em> of the reference.<br/>
    /// <a href="https://learn.microsoft.com/office/vba/language/reference/user-interface-help/label-not-defined">learn.microsoft.com</a>
    /// </summary>
    LabelNotDefined = 9309,
    /// <summary>
    /// The <em>variable</em> or <em>property</em> is of a <em>data type</em> that cannot be <em>let-coerced</em> (implicitly converted) to the specified <em>destination type</em>.<br/>
    /// <a href="https://learn.microsoft.com/office/vba/language/reference/user-interface-help/type-mismatch-error-13">learn.microsoft.com</a>
    /// </summary>
    /// <remarks>
    /// ℹ️ The <c>learn.microsoft.com</c> documentation documents this error as both a <em>compile-time</em> and a <see cref="VBRuntimeErrorId.TypeMismatch"/> error.
    /// Since both compile-time and run-time semantics are closely related, this does make sense.
    /// </remarks>
    TypeMismatch = 9310,
    /// <summary>
    /// The data type of a <em>variable</em> or <em>parameter</em> declaration is of an unknown, inaccessible, or otherwise invalid data type.<br/>
    /// <a href="https://learn.microsoft.com/office/vba/language/how-to/user-defined-type-not-defined">learn.microsoft.com</a>
    /// </summary>
    /// <remarks>
    /// 👉 This error usually has absolutely nothing to do with any <em>user-defined type</em> (UDT) declaration. The term "user-defined" here seems to be rather standing for <em>workspace-defined</em>.
    /// </remarks>
    UserDefinedTypeNotDefined = 9311,
    /// <summary>
    /// <c>Exit Do</c> is only valid within a <c>Do...Loop</c> statement.<br/>
    /// <a href="https://learn.microsoft.com/office/vba/language/reference/user-interface-help/exit-do-not-within-doloop">learn.microsoft.com</a>
    /// </summary>
    ExitDoNotWithinDoLoop = 9312,
    /// <summary>
    /// <c>Exit For</c> is only valid with a <c>For...Next</c> statement.<br/>
    /// <a href="https://learn.microsoft.com/office/vba/language/reference/user-interface-help/exit-for-not-within-fornext">learn.microsoft.com</a>
    /// </summary>
    ExitForNotWithinForNext = 9313,
    /// <summary>
    /// <c>Exit</c> statement must match the <em>kind</em> of procedure in which it occurs.<br/>
    /// <a href="https://learn.microsoft.com/office/vba/language/reference/user-interface-help/exit-function-not-allowed-in-sub-or-property">learn.microsoft.com</a>
    /// </summary>
    /// <remarks>
    /// 👉 Here "Property" must be read as <c>Property Let</c> or <c>Property Set</c>, because in MS-VBA <c>Exit Function</c> can legally exist inside a <c>Property Get</c> procedure (VBIDE accepts and corrects it)
    /// </remarks>
    ExitFunctionNotAllowedInSubOrProperty = 9314,
    /// <summary>
    /// <c>Exit</c> statement must match the <em>kind</em> of procedure in which it occurs.<br/>
    /// <a href="https://learn.microsoft.com/office/vba/language/reference/user-interface-help/exit-sub-not-allowed-in-function-or-property">learn.microsoft.com</a>
    /// </summary>
    ExitPropertyNotAllowedInSubOrFunction = 9315,
    /// <summary>
    /// The <em>collection</em>, <em>object</em>, or <em>user-defined type</em> (UDT) doesn't contain the referenced <em>member</em>.<br/>
    /// <a href="https://learn.microsoft.com/office/vba/language/reference/user-interface-help/method-or-data-member-not-found-error-461">learn.microsoft.com</a>
    /// </summary>
    /// <remarks>
    /// ℹ️ The <c>learn.microsoft.com</c> documentation documents this error as both a <em>compile-time</em> and a <see cref="VBRuntimeErrorId.MethodOrDataMemberNotFound"/> error.<br/>
    /// This works only because the documentation includes run-time <em>collection item retrieval</em> - otherwise this error would be purely about a failed member binding.
    /// </remarks>
    MethodOrDataMemberNotFound = 9316,


    /***********************************************************************************************
     * 💯 This section is intended to grow to cover all possible MS-VBA compile-time error messages 
     *     👉 THANK YOU for taking the time to write XML documentation for anything you add here.
    /***********************************************************************************************/
    #endregion
}
