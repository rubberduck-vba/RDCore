namespace RDCore.SDK.Model.Symbols.Abstract
{
    /// <summary>
    /// An interface to help select <c>Property</c> members.
    /// </summary>
    /// <remarks>
    /// Property members can be <c>Get</c> (read), <c>Let</c> (value write), or <c>Set</c> (reference write); property members sharing a name, together define how a property can be used.
    /// </remarks>
    public interface IVBPropertyMemberSymbol
    { 
        /// <summary>
        /// The <c>Name</c> of the property symbol.
        /// </summary>
        string Name { get; }
    }
}
