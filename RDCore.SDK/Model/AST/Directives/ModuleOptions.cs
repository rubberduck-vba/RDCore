namespace RDCore.SDK.Model.AST.Directives
{
    [Flags]
    /// <summary>
    /// <strong>MS-VBAL 5.2.1 Option Directives</strong>
    /// </summary>
    public enum ModuleOptions
    {
        /// <summary>
        /// Indicates that the <c>Option Compare Binary</c> module option is specified.
        /// </summary>
        /// <remarks>
        /// <strong>MS-VBAL 5.2.1.1</strong> Option Compare Directive.
        /// </remarks>
        OptionCompareBinary = 1 << 1,
        /// <summary>
        /// Indicates that the <c>Option Compare Text</c> module option is specified.
        /// </summary>
        /// <remarks>
        /// <strong>MS-VBAL 5.2.1.1</strong> Option Compare Directive.
        /// </remarks>
        OptionCompareText = 1 << 2,
        /// <summary>
        /// Indicates that the <c>Option Compare Database</c> module option is specified.
        /// </summary>
        /// <remarks>
        /// <strong>MS-VBAL 5.2.1.1</strong> Option Compare Directive. This value is unspecified, but is known to be supported in <em>Microsoft Access</em>.
        /// </remarks>
        OptionCompareDatabase = 1 << 3,
        /// <summary>
        /// Indicates that the <c>Option Base 0</c> module option is specified.
        /// </summary>
        /// <remarks>
        /// <strong>MS-VBAL 5.2.1.2</strong> Option Base Directive.
        /// </remarks>
        OptionBase0 = 1 << 4,
        /// <summary>
        /// Indicates that the <c>Option Base 1</c> module option is specified.
        /// </summary>
        /// <remarks>
        /// <strong>MS-VBAL 5.2.1.2</strong> Option Base Directive.
        /// </remarks>
        OptionBase1 = 1 << 5,
        /// <summary>
        /// Indicates that the <c>Option Explicit</c> module option is specified.
        /// </summary>
        /// <remarks>
        /// <strong>MS-VBAL 5.2.1.3</strong> Option Explicit Directive.
        /// </remarks>
        OptionExplicit = 1 << 6,
        /// <summary>
        /// Indicates that the <c>Option Private Module</c> module option is specified.
        /// </summary>
        /// <remarks>
        /// <strong>MS-VBAL 5.2.1.4</strong> Option Private Directive.
        /// </remarks>
        OptionPrivateModule = 1 << 7,
        /// <summary>
        /// Indicates that the <c>Option Strict</c> module option is specified.
        /// </summary>
        /// <remarks>
        /// This RD-VBA directive is reserved for future use.
        /// </remarks>
        OptionStrict = 1 << 31,
    }
}
