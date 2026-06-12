namespace RDCore.SDK.Model.Errors
{
    /// <summary>
    /// Formally encodes a range of integer values for all <see cref="VBSyntaxErrorInfo"/> and <see cref="VBCompileErrorInfo"/> errors.
    /// </summary>
    public enum VBCompileErrorId
    {
        /***
         * [1-999]: Reserved for RD-VBA formalized syntax errors (VBSyntaxErrorInfo)
         */

        /// <summary>
        /// A generic "Syntax error" compile-time error.
        /// </summary>
        /// <remarks>
        /// Ideally the parser gives us <em>much</em> more detailed syntax errors that this.
        /// </remarks>
        SyntaxError = 1,

        /***
         * [8000-8999]: Reserved for future RD-VBA extensibility
         */

        ForbiddenWithOptionStrict = 8000,

        /***
         * [9300+]: Formalized MS-VBA compilation error messages (VBCompileErrorInfo).
         * LORE: 93 is for 1993, the year MS-VBA came into existence.
         */

        AmbiguousName = 9301,
        VariableNotDefined = 9302,
        DuplicateDeclaration = 9303,
        InvalidUseOfObject = 9304,
        InvalidParamArrayUse = 9305,
        InvalidReDim = 9306,
        ExpectedArray = 9307,
        ExpectedIdentifier = 9308,
        LabelNotDefined = 9309,
        TypeMismatch = 9310,
        UserDefinedTypeNotDefined = 9311,
        ExitDoNotWithinDoLoop = 9312,
        ExitForNotWithinForNext = 9313,
        ExitFunctionNotAllowedInSubOrProperty = 9314,
        ExitPropertyNotAllowedInSubOrFunction = 9315,
        MethodOrDataMemberNotFound = 9316,


    }
}
