namespace RDCore.SDK.Model.AST.Declarations;

/// <summary>
/// Describes the type of module an AST is for.
/// </summary>
/// <remarks>
/// What decides it is <see cref="Workspace.ModuleKinds.Of(Workspace.RDCoreModule?, string?)"/>: the project's word for a document module, and the module's
/// own <c>VERSION</c> header otherwise.
/// </remarks>
public enum ModuleType
{
    /// <summary>
    /// Root node is for a standard (procedural) module.
    /// </summary>
    StdModule,
    /// <summary>
    /// Root node is for a class module.
    /// </summary>
    ClassModule,
    /// <summary>
    /// Root node is for a document module: the code of a host's document - an Excel worksheet or workbook, a Word document, an Access form or report - which is
    /// an extension module of the extensible module the host has for it (<strong>MS-VBAL §4.2.1</strong>). It is a class module, with the attributes a document
    /// module has (<see cref="Workspace.ModuleKinds.DirectivesOf"/>), analyzed and never run: RD-VBA hosts no document.
    /// </summary>
    DocumentModule,
}
