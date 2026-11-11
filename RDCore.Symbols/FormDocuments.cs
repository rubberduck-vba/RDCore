using RDCore.SDK.Workspace.Forms;
using System.IO.Abstractions;

namespace RDCore.Symbols;

// what a form module's designer declares, as every way of loading a workspace reads it: from the module's text and the companion file (a UserForm's .frx) its text
// names, beside it. The parser reads text alone; the designer is set on its parse by whatever loads the workspace, the language server and rdc alike.
public static class FormDocuments
{
    // the content of the companion file of the module at modulePath, which its text names; empty for a module that names none, and when the file is missing or
    // cannot be read, which problem then says.
    public static ReadOnlyMemory<byte> CompanionOf(IFile file, IPath path, string text, string modulePath, out string? problem)
    {
        problem = null;
        try
        {
            if (FormDesigner.CompanionOf(text) is { } reference
                && path.Combine(path.GetDirectoryName(modulePath) ?? string.Empty, reference.FileName) is var companionPath
                && file.Exists(companionPath))
            {
                return file.ReadAllBytes(companionPath);
            }
        }
        catch (FormDesignerFormatException exception)
        {
            problem = $"the designer block cannot be read: {exception.Message}";
        }
        catch (IOException exception)
        {
            problem = $"the companion file could not be read: {exception.Message}";
        }

        return default;
    }

    // what the module declares as a form, from its text and its companion file; null for a module that is not a form, and for a form whose designer data cannot
    // be read, which then declares no controls and problem says why.
    public static FormDescriptor? DesignerOf(string text, ReadOnlyMemory<byte> companion, out string? problem)
    {
        problem = null;
        try
        {
            return FormDesigner.Read(text, companion);
        }
        catch (FormDesignerFormatException exception)
        {
            problem = exception.Message;
            return null;
        }
    }
}
