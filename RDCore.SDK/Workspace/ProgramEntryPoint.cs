using RDCore.SDK.Model;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;

namespace RDCore.SDK.Workspace;

/// <summary>
/// Where a program of a project starts: the procedure a run of the project runs, when nothing names the module it is in.
/// </summary>
/// <remarks>
/// <para>
/// MS-VBAL says nothing of a program's start: a VBA project is code a host calls into. A project that is a program has one entry point, as a VB6 project's
/// startup object is <c>Sub Main</c>: a <c>Sub</c> named <see cref="Name"/>, in a standard module, that the rest of the project can call - <c>Public</c> or
/// <c>Global</c>, or written with no scope, which is <c>Public</c> (<strong>MS-VBAL §5.3.1.1</strong>) - and that takes no argument, or takes what it is
/// given as one <c>ParamArray</c>: <c>Sub Main()</c>, or <c>Sub Main(ParamArray Args())</c>. A class or a document has no entry point: running one runs no
/// program.
/// </para>
/// <para>
/// The procedure is judged as the runtime session defined it, which is the procedure a run runs: of a declaration whose header is written once in each
/// branch of a <c>#If</c> block, the header of the live branch under the project's constants (<strong>MS-VBAL §3.4</strong>), and of a declaration written
/// only in dead branches, nothing, since it is not defined.
/// </para>
/// </remarks>
public static class ProgramEntryPoint
{
    /// <summary>The name of the procedure a program starts with when nothing names another.</summary>
    public const string Name = BasicProgramText.EntryPointName;

    /// <summary>
    /// Whether a module declares a procedure a program can start with.
    /// </summary>
    /// <param name="module">The module.</param>
    /// <param name="members">The members the runtime session defined for the module.</param>
    /// <param name="name">The name of the procedure; <see cref="Name"/> unless the run names another. VBA's names are compared without regard to case.</param>
    public static bool IsDeclaredBy(VBModuleSymbol module, IEnumerable<VBTypeMemberSymbol> members, string name = Name)
        => module is VBStandardModuleSymbol && members.Any(member => IsEntryPoint(member, name));

    /// <summary>
    /// Whether a member is a procedure a program can start with: a <c>Sub</c> of that name that the project can call, and that takes no argument or one
    /// <c>ParamArray</c>.
    /// </summary>
    /// <remarks>
    /// A <c>Declare Sub</c> is not one: it is a procedure of a library, not of the module's code.
    /// </remarks>
    /// <param name="member">The member, as the runtime session defined it.</param>
    /// <param name="name">The name of the procedure; <see cref="Name"/> unless the run names another. VBA's names are compared without regard to case.</param>
    public static bool IsEntryPoint(VBTypeMemberSymbol member, string name = Name)
        => member is VBProcedureMemberSymbol { Parameters: [] or [ParamArrayParameterSymbol] } and not VBExternalSubMemberSymbol
           && member.AccessModifier is AccessModifier.Implicit or AccessModifier.Public or AccessModifier.Global
           && string.Equals(member.Name, name, StringComparison.OrdinalIgnoreCase);
}
