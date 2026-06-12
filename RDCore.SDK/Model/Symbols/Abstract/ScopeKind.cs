namespace RDCore.SDK.Model.Symbols.Abstract
{
    /// <summary>
    /// Defines the scope of a memory allocation.
    /// </summary>
    public enum ScopeKind
    {
        /// <summary>
        /// A pseudo-scope for pseudo-symbols that aren't allocated in memory, like <c>VBVoidValue</c>.
        /// </summary>
        Unallocated,
        /// <summary>
        /// <c>StaticSymbol</c> and symbols obtained from referenced libraries, mostly. Lives in the globals heap.
        /// </summary>
        Global,
        /// <summary>
        /// Procedure level, scoped to the local stack frame.
        /// </summary>
        Local,
        /// <summary>
        /// Module level, lives in the workspace statics heap.
        /// </summary>
        Module,
        /// <summary>
        /// Instance level, lives in the object heap.
        /// </summary>
        Instance,
        /// <summary>
        /// Allocated externally, lives out of process at a known address.
        /// </summary>
        External,
    }
}
