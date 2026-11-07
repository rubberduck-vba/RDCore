#:property PublishAot=false
#:property BuiltInComInteropSupport=true
// Makes Widgets.tlb, the export-library fixture of a library declared in terms of a library no machine has, on Windows:
//
//     dotnet run RDCore.Tests/Cli/Data/MakeWidgetsTlb.cs -- RDCore.Tests/Cli/Data
//
// Widgets {0D7C2B6E-5E39-4C47-9D0B-1A6F0E5C3B01} 1.0 declares a creatable class Widget: its default interface _Widget has a property Thing of type
// Missing.IThing and a property Count (Long); its events interface _WidgetEvents has an event Changed(Value As Missing.IThing) and an event Ready. Missing
// {0D7C2B6E-5E39-4C47-9D0B-1A6F0E5C3A01} 1.0 is made beside it, in a folder of its own that is deleted after, and never registered: Widgets names it, and
// loading Widgets in any other process finds no such library (TYPE_E_CANTLOADLIBRARY). The test project does not compile this file.
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

var output = Path.GetFullPath(args.Length > 0 ? args[0] : ".");
var scratch = Path.Combine(Path.GetTempPath(), "MakeWidgetsTlb-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(scratch);
try
{
    Native.Check(Native.LoadTypeLib("stdole2.tlb", out var stdole));
    var dispatchId = new Guid("00020400-0000-0000-C000-000000000046");
    stdole.GetTypeInfoOfGuid(ref dispatchId, out var dispatch);

    var missingPath = Path.Combine(scratch, "Missing.tlb");
    var missing = Build.Library(missingPath, "Missing", new Guid("0D7C2B6E-5E39-4C47-9D0B-1A6F0E5C3A01"));
    Build.Dispinterface(missing, "IThing", new Guid("0D7C2B6E-5E39-4C47-9D0B-1A6F0E5C3A02"), dispatch).LayOut();
    missing.SaveAllChanges();

    Native.Check(Native.LoadTypeLibEx(missingPath, 2, out var loadedMissing));
    var thingId = new Guid("0D7C2B6E-5E39-4C47-9D0B-1A6F0E5C3A02");
    loadedMissing.GetTypeInfoOfGuid(ref thingId, out var thing);

    var widgets = Build.Library(Path.Combine(output, "Widgets.tlb"), "Widgets", new Guid("0D7C2B6E-5E39-4C47-9D0B-1A6F0E5C3B01"));

    var widget = Build.Dispinterface(widgets, "_Widget", new Guid("0D7C2B6E-5E39-4C47-9D0B-1A6F0E5C3B02"), dispatch);
    widget.AddRefTypeInfo(thing, out var thingOfWidget);
    Build.Function(widget, 0, 1, "Thing", [], Build.PointerTo(thingOfWidget), INVOKEKIND.INVOKE_PROPERTYGET);
    Build.Function(widget, 1, 2, "Count", [], new TYPEDESC { vt = (short)VarEnum.VT_I4 }, INVOKEKIND.INVOKE_PROPERTYGET);
    widget.LayOut();

    var events = Build.Dispinterface(widgets, "_WidgetEvents", new Guid("0D7C2B6E-5E39-4C47-9D0B-1A6F0E5C3B03"), dispatch);
    events.AddRefTypeInfo(thing, out var thingOfEvents);
    Build.Function(events, 0, 1, "Changed", [("Value", Build.PointerTo(thingOfEvents))], new TYPEDESC { vt = (short)VarEnum.VT_VOID }, INVOKEKIND.INVOKE_FUNC);
    Build.Function(events, 1, 2, "Ready", [], new TYPEDESC { vt = (short)VarEnum.VT_VOID }, INVOKEKIND.INVOKE_FUNC);
    events.LayOut();

    widgets.CreateTypeInfo("Widget", TYPEKIND.TKIND_COCLASS, out var coclass);
    var classId = new Guid("0D7C2B6E-5E39-4C47-9D0B-1A6F0E5C3B04");
    coclass.SetGuid(ref classId);
    coclass.SetTypeFlags((uint)TYPEFLAGS.TYPEFLAG_FCANCREATE);
    coclass.AddRefTypeInfo((ITypeInfo)widget, out var widgetReference);
    coclass.AddImplType(0, widgetReference);
    coclass.SetImplTypeFlags(0, IMPLTYPEFLAGS.IMPLTYPEFLAG_FDEFAULT);
    coclass.AddRefTypeInfo((ITypeInfo)events, out var eventsReference);
    coclass.AddImplType(1, eventsReference);
    coclass.SetImplTypeFlags(1, IMPLTYPEFLAGS.IMPLTYPEFLAG_FDEFAULT | IMPLTYPEFLAGS.IMPLTYPEFLAG_FSOURCE);
    coclass.LayOut();
    widgets.SaveAllChanges();

    Console.WriteLine(Path.Combine(output, "Widgets.tlb"));
}
finally
{
    try { Directory.Delete(scratch, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
}

static class Native
{
    [DllImport("oleaut32.dll", CharSet = CharSet.Unicode)] public static extern int LoadTypeLib(string file, out ITypeLib library);
    [DllImport("oleaut32.dll", CharSet = CharSet.Unicode)] public static extern int LoadTypeLibEx(string file, int regKind, out ITypeLib library);
    [DllImport("oleaut32.dll", CharSet = CharSet.Unicode)] public static extern int CreateTypeLib2(SYSKIND kind, string file, out ICreateTypeLib library);

    public static void Check(int result) => Marshal.ThrowExceptionForHR(result);
}

static class Build
{
    public static ICreateTypeLib Library(string path, string name, Guid id)
    {
        Native.Check(Native.CreateTypeLib2(SYSKIND.SYS_WIN64, path, out var library));
        library.SetName(name);
        library.SetGuid(ref id);
        library.SetVersion(1, 0);
        return library;
    }

    public static ICreateTypeInfo Dispinterface(ICreateTypeLib library, string name, Guid id, ITypeInfo dispatch)
    {
        library.CreateTypeInfo(name, TYPEKIND.TKIND_DISPATCH, out var info);
        info.SetGuid(ref id);
        info.AddRefTypeInfo(dispatch, out var reference);
        info.AddImplType(0, reference);
        return info;
    }

    public static TYPEDESC PointerTo(int reference)
    {
        var pointee = Marshal.AllocHGlobal(Marshal.SizeOf<TYPEDESC>());
        Marshal.StructureToPtr(new TYPEDESC { vt = (short)VarEnum.VT_USERDEFINED, lpValue = reference }, pointee, false);
        return new TYPEDESC { vt = (short)VarEnum.VT_PTR, lpValue = pointee };
    }

    public static void Function(ICreateTypeInfo info, int index, int memberId, string name, (string Name, TYPEDESC Type)[] parameters, TYPEDESC returns, INVOKEKIND kind)
    {
        var size = Marshal.SizeOf<ELEMDESC>();
        var elements = parameters.Length == 0 ? IntPtr.Zero : Marshal.AllocHGlobal(size * parameters.Length);
        for (var i = 0; i < parameters.Length; i++)
        {
            var element = new ELEMDESC { tdesc = parameters[i].Type };
            element.desc.paramdesc.wParamFlags = PARAMFLAG.PARAMFLAG_FIN;
            Marshal.StructureToPtr(element, IntPtr.Add(elements, i * size), false);
        }

        var function = new FUNCDESC
        {
            memid = memberId, funckind = FUNCKIND.FUNC_DISPATCH, invkind = kind, callconv = CALLCONV.CC_STDCALL,
            cParams = (short)parameters.Length, lprgelemdescParam = elements, elemdescFunc = new ELEMDESC { tdesc = returns },
        };
        info.AddFuncDesc(index, ref function);
        info.SetFuncAndParamNames(index, [name, .. parameters.Select(parameter => parameter.Name)], parameters.Length + 1);
    }
}

[ComImport, Guid("00020406-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface ICreateTypeLib
{
    void CreateTypeInfo([MarshalAs(UnmanagedType.LPWStr)] string name, TYPEKIND kind, out ICreateTypeInfo info);
    void SetName([MarshalAs(UnmanagedType.LPWStr)] string name);
    void SetVersion(short major, short minor);
    void SetGuid(ref Guid id);
    void SetDocString([MarshalAs(UnmanagedType.LPWStr)] string doc);
    void SetHelpFileName([MarshalAs(UnmanagedType.LPWStr)] string file);
    void SetHelpContext(int context);
    void SetLcid(int lcid);
    void SetLibFlags(uint flags);
    void SaveAllChanges();
}

// the methods in the order of their vtable: those after LayOut are not called, and are left out.
[ComImport, Guid("00020405-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface ICreateTypeInfo
{
    void SetGuid(ref Guid id);
    void SetTypeFlags(uint flags);
    void SetDocString([MarshalAs(UnmanagedType.LPWStr)] string doc);
    void SetHelpContext(int context);
    void SetVersion(short major, short minor);
    void AddRefTypeInfo(ITypeInfo info, out int reference);
    void AddFuncDesc(int index, ref FUNCDESC function);
    void AddImplType(int index, int reference);
    void SetImplTypeFlags(int index, IMPLTYPEFLAGS flags);
    void SetAlignment(short alignment);
    void SetSchema([MarshalAs(UnmanagedType.LPWStr)] string schema);
    void AddVarDesc(int index, ref VARDESC variable);
    void SetFuncAndParamNames(int index, [MarshalAs(UnmanagedType.LPArray, ArraySubType = UnmanagedType.LPWStr)] string[] names, int count);
    void SetVarName(int index, [MarshalAs(UnmanagedType.LPWStr)] string name);
    void SetTypeDescAlias(ref TYPEDESC alias);
    void DefineFuncAsDllEntry(int index, [MarshalAs(UnmanagedType.LPWStr)] string dll, [MarshalAs(UnmanagedType.LPWStr)] string entry);
    void SetFuncDocString(int index, [MarshalAs(UnmanagedType.LPWStr)] string doc);
    void SetVarDocString(int index, [MarshalAs(UnmanagedType.LPWStr)] string doc);
    void SetFuncHelpContext(int index, int context);
    void SetVarHelpContext(int index, int context);
    void SetMops(int index, [MarshalAs(UnmanagedType.BStr)] string mops);
    void SetTypeIdldesc(IntPtr idl);
    void LayOut();
}
