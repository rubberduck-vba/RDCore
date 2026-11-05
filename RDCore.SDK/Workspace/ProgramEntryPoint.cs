using RDCore.SDK.Model;
using RDCore.SDK.Model.AST.Declarations;

namespace RDCore.SDK.Workspace;

/// <summary>
/// Where a program of a project starts: the procedure a run of the project runs, when nothing names the module it is in.
/// </summary>
/// <remarks>
/// MS-VBAL says nothing of a program's start: a VBA project is code a host calls into. A project that is a program has one entry point, as a VB6 project's
/// startup object is <c>Sub Main</c>: a <c>Sub</c> named <see cref="Name"/>, in a standard module, that the rest of the project can call - <c>Public</c> or
/// <c>Global</c>, or written with no scope, which is <c>Public</c> (<strong>MS-VBAL §5.3.1.1</strong>) - and that takes no argument, or takes what it is
/// given as one <c>ParamArray</c>: <c>Sub Main()</c>, or <c>Sub Main(ParamArray Args())</c>. A class or a document has no entry point: running one runs no
/// program. A procedure whose header is written once in each branch of a <c>#If</c> block is an entry point when one of its headers is.
/// </remarks>
public static class ProgramEntryPoint
{
    /// <summary>The name of the procedure a program starts with when nothing names another.</summary>
    public const string Name = BasicProgramText.EntryPointName;

    /// <summary>
    /// Whether a module declares a procedure a program can start with.
    /// </summary>
    /// <param name="kind">The kind of the module (<see cref="ModuleKinds.Of(RDCoreModule?, string?)"/>).</param>
    /// <param name="module">The module's syntax tree.</param>
    /// <param name="name">The name of the procedure; <see cref="Name"/> unless the run names another. VBA's names are compared without regard to case.</param>
    public static bool IsDeclaredBy(ModuleType kind, ModuleNode module, string name = Name)
        => kind is ModuleType.StdModule
           && module.Children.OfType<MemberDeclarationNode>().SelectMany(member => member.WithEachHeader()).Any(header => IsEntryPoint(header, name));

    private static bool IsEntryPoint(MemberDeclarationNode header, string name)
        => header.MemberKind is MemberKind.Procedure
           && header.AccessModifier is AccessModifier.Implicit or AccessModifier.Public or AccessModifier.Global
           && string.Equals(header.Name, name, StringComparison.OrdinalIgnoreCase)
           && header.HeaderChildren.OfType<ParameterDeclarationNode>().ToArray() is [] or [{ IsParamArray: true }];
}
