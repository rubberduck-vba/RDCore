#pragma warning disable IDE0130 // Namespace does not match folder structure
namespace RDCore.SDK.Model.Types
{
    /// <summary>
    /// The type of a <c>Project</c>, as specified in <strong>MS-VBAL 4.1</strong>.
    /// </summary>
    public enum ProjectTypeId
    {
        /// <summary>
        /// A <em>source project</em> is composed of VBA program code that exists in source code form.
        /// </summary>
        SourceProject,
        /// <summary>
        /// A <em>host project</em> is a <c>LibraryProject</c> that is introduced in a VBA environment by the <em>host application</em>.
        /// </summary>
        HostProject,
        /// <summary>
        /// A <em>library project</em> is a project that defines entities that a <c>SourceProject</c> might define, but can exist in compiled/binary form.
        /// </summary>
        LibraryProject
    }
}