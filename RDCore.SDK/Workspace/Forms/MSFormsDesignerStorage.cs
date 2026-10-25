namespace RDCore.SDK.Workspace.Forms;

/// <summary>
/// Reads the controls of a Microsoft Forms form out of the storage the form persists itself to ([MS-OFORMS] 2.1.2).
/// </summary>
/// <remarks>
/// A parent control (the form, a <c>Frame</c>, a <c>MultiPage</c> and its pages) is a storage whose form stream
/// (<c>f</c>) lists the sites of its embedded controls. An embedded control that is itself a parent has a storage of its
/// own in its parent's, named <c>i</c> and its ID in at least two digits ([MS-OFORMS] 2.1.2.2.2). Only the names, classes
/// and tab order of the controls are read: what a form declares. Their other properties (in the object stream, <c>o</c>)
/// are Microsoft Forms' own business, which hosts the form at run time from this same storage.
/// </remarks>
internal static class MSFormsDesignerStorage
{
    /// <summary>
    /// The Microsoft Forms type library's name, which qualifies the classes of its controls.
    /// </summary>
    public const string Library = "MSForms";

    // FormEmbeddedActiveXControlCached ([MS-OFORMS] 2.4.5): a ClsidCacheIndex below 0x7FFF names a Microsoft Forms control
    private static readonly IReadOnlyDictionary<ushort, string> CachedClasses = new Dictionary<ushort, string>
    {
        [7] = "UserForm",
        [12] = "Image",
        [14] = "Frame",
        [15] = "Control",
        [16] = "SpinButton",
        [17] = "CommandButton",
        [18] = "TabStrip",
        [21] = "Label",
        [23] = "TextBox",
        [24] = "ListBox",
        [25] = "ComboBox",
        [26] = "CheckBox",
        [27] = "OptionButton",
        [28] = "ToggleButton",
        [47] = "ScrollBar",
        [57] = "MultiPage",
    };

    private const ushort InvalidCacheIndex = 0x7FFF;
    private const ushort ClassTableIndex = 0x8000;

    /// <summary>
    /// The controls of the form persisted to <paramref name="file"/>'s root storage.
    /// </summary>
    /// <exception cref="FormDesignerFormatException">The storage, or a form stream in it, cannot be read.</exception>
    public static IReadOnlyList<FormControlDescriptor> ReadControls(CompoundFile file)
        => ReadParent(file, file.Root, parentClass: null, visited: []);

    private static List<FormControlDescriptor> ReadParent(CompoundFile file, CompoundFileEntry storage, string? parentClass, HashSet<int> visited)
    {
        if (!visited.Add(storage.Id))
        {
            throw new FormDesignerFormatException(FormDesignerFormatProblem.CorruptCompoundFile);
        }

        var formStream = file.Find(storage, "f") is { Type: CompoundFileEntryType.Stream } f
            ? MSFormsFormStream.Read(file.Read(f))
            : throw new FormDesignerFormatException(FormDesignerFormatProblem.MissingFormStream);

        // a site's depth places it under the closest preceding site one level up ([MS-OFORMS] 2.2.10.7); a parent control
        // with a storage of its own also has its own controls in that storage
        var roots = new List<FormControlDescriptor>();
        var open = new List<(FormControlDescriptor Control, List<FormControlDescriptor> Children)>();
        foreach (var site in formStream.Sites)
        {
            var @class = ClassOf(site, formStream.ClassTable, parentClass);
            if (parentClass == "MultiPage" && @class.Name == "TabStrip")
            {
                // a MultiPage's own tab strip, which shows its pages' tabs, is a site of its storage, not a control
                // ([MS-OFORMS] 2.1.2.3)
                continue;
            }

            var children = file.Find(storage, $"i{site.Id:00}") is { Type: CompoundFileEntryType.Storage } own
                ? ReadParent(file, own, @class.Name, visited)
                : [];
            var control = new FormControlDescriptor(site.Name, @class, site.TabIndex < 0 ? null : site.TabIndex, Index: null, children);

            if (site.Depth > open.Count)
            {
                throw new FormDesignerFormatException(FormDesignerFormatProblem.InvalidControl);
            }

            open.RemoveRange(site.Depth, open.Count - site.Depth);
            (site.Depth == 0 ? roots : open[^1].Children).Add(control);
            open.Add((control, children));
        }

        return roots;
    }

    private static FormControlClass ClassOf(MSFormsSite site, IReadOnlyList<Guid?> classTable, string? parentClass)
    {
        if (site.ClsidCacheIndex >= ClassTableIndex)
        {
            // an ActiveX control that is not Microsoft Forms' own: the class table names it by its CLSID
            var index = site.ClsidCacheIndex - ClassTableIndex;
            return index < classTable.Count && classTable[index] is { } classId
                ? new FormControlClass(null, null, classId)
                : throw new FormDesignerFormatException(FormDesignerFormatProblem.InvalidControl);
        }

        if (site.ClsidCacheIndex == InvalidCacheIndex)
        {
            throw new FormDesignerFormatException(FormDesignerFormatProblem.InvalidControl);
        }

        if (!CachedClasses.TryGetValue(site.ClsidCacheIndex, out var name))
        {
            throw new FormDesignerFormatException(FormDesignerFormatProblem.InvalidControl);
        }

        // a MultiPage's pages are persisted as forms (FormControl, cache index 7); their class is Page
        return new FormControlClass(Library, name == "UserForm" && parentClass == "MultiPage" ? "Page" : name);
    }
}
