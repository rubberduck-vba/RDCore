using Microsoft.Extensions.Options;
using RDCore.Parsing;
using RDCore.SDK.Server.Configuration;

namespace RDCore.Tests;

// a VBA or VB6 file declares modules in blocks only in a workspace that allows statements outside procedures (SdkWorkspaceOptions.FileLevelStatements):
// the options and the parser of such a workspace, for a test of blocks in a .bas or a .cls. A script (.vbs) has blocks in any workspace.
internal static class FileLevelStatementsOn
{
    public static SdkWorkspaceOptions Options() => new() { FileLevelStatements = true };

    public static ModuleParser Parser() => new(workspaceOptions: Microsoft.Extensions.Options.Options.Create(Options()));
}
