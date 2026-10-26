using Microsoft.Win32;
using RDCore.SDK.Runtime.Libraries;
using System.Collections.Immutable;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using VarEnum = System.Runtime.InteropServices.VarEnum;

namespace RDCore.CLI.Libraries;

/// <summary>
/// What exporting a type library made.
/// </summary>
/// <param name="Description">The description of the library.</param>
/// <param name="Dependencies">The other libraries the types of this one are declared as, loaded, by name: they are described to be exported with it.</param>
/// <param name="Skipped">What the library declares that a description cannot hold, said once each.</param>
internal sealed record TypeLibraryExport(LibraryDescription Description, IReadOnlyDictionary<string, ITypeLib> Dependencies, IReadOnlyList<string> Skipped);

/// <summary>
/// Describes a type library the way VBA sees it (<see cref="LibraryDescription"/>): the object browser's view, and not the library's own.
/// </summary>
/// <remarks>
/// <para>
/// A type library declares coclasses, the interfaces they implement, the interfaces they raise events through, and everything an interface inherits; VBA shows a
/// class with the members of its default interface and the events of its default source interface, under the name of the coclass. An interface that no coclass
/// has as its default is a class of its own, under its own name; an event interface is not a class. A type that is named by an interface is named by the coclass
/// whose default interface it is (<c>_Worksheet</c> is <c>Worksheet</c>), as the object browser names it.
/// </para>
/// <para>
/// What a description cannot hold is left out, and said: records, unions and modules. A member that the library marks restricted is not one VBA can call and is
/// not described. The output is the same for the same library: nothing in it depends on when or where it was made, so that a change to a description is a change
/// to the library.
/// </para>
/// </remarks>
internal sealed class TypeLibraryExporter(bool includeDescriptions = true)
{
    private static readonly Guid IUnknown = new("00000000-0000-0000-C000-000000000046");
    private static readonly Guid IDispatch = new("00020400-0000-0000-C000-000000000046");

    private readonly Dictionary<string, ITypeLib> _dependencies = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _skipped = [];
    private readonly Dictionary<string, Dictionary<Guid, string>> _classOfInterface = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The name of the standard library, whose types a description names and does not describe: the platform provides it.
    /// </summary>
    private const string StandardLibrary = "VBA";

    /// <summary>
    /// Describes a library.
    /// </summary>
    /// <param name="loaded">The library.</param>
    /// <param name="exportedBy">What to say made the description.</param>
    public TypeLibraryExport Export(LoadedTypeLibrary loaded, string exportedBy)
    {
        var library = loaded.Library;
        CurrentLibrary = loaded.Name;
        var attributes = Attributes(library);
        var classOfInterface = ClassOfInterface(loaded.Name, library);

        var enums = new List<EnumDescription>();
        var classes = new List<ClassDescription>();
        var sources = SourceInterfaces(library);

        for (var i = 0; i < library.GetTypeInfoCount(); i++)
        {
            library.GetTypeInfo(i, out var info);
            var attr = Attr(info);
            var name = Name(info);
            var flags = (TYPEFLAGS)attr.wTypeFlags;

            switch (attr.typekind)
            {
                case TYPEKIND.TKIND_ENUM:
                    enums.Add(DescribeEnum(info, attr, name, flags));
                    break;

                case TYPEKIND.TKIND_COCLASS:
                    if (!flags.HasFlag(TYPEFLAGS.TYPEFLAG_FRESTRICTED))
                    {
                        classes.Add(DescribeCoclass(loaded, info, attr, name, flags));
                    }

                    break;

                case TYPEKIND.TKIND_INTERFACE or TYPEKIND.TKIND_DISPATCH:
                    // an interface that no coclass has as its default is a class of its own; one that is only raised events through is not. IUnknown, which stdole
                    // declares hidden, is one: what every object is (`As stdole.IUnknown`). IDispatch is restricted, and no type name.
                    if (!classOfInterface.ContainsKey(attr.guid) && !sources.Contains(attr.guid) && !flags.HasFlag(TYPEFLAGS.TYPEFLAG_FRESTRICTED)
                        && attr.guid != IDispatch)
                    {
                        classes.Add(new ClassDescription
                        {
                            Name = name,
                            Description = Doc(info, -1),
                            IsHidden = flags.HasFlag(TYPEFLAGS.TYPEFLAG_FHIDDEN),
                            Members = Ordered(MembersOf(loaded, info, [])),
                        });
                    }

                    break;

                case TYPEKIND.TKIND_RECORD or TYPEKIND.TKIND_UNION or TYPEKIND.TKIND_MODULE:
                    _skipped.Add($"{attr.typekind.ToString()[6..].ToLowerInvariant()} {name}");
                    break;
            }
        }

        library.GetDocumentation(-1, out _, out var libraryDoc, out _, out _);
        var description = new LibraryDescription
        {
            Name = loaded.Name,
            Description = includeDescriptions ? Clean(libraryDoc) : null,
            Origin = new LibraryOrigin
            {
                Guid = attributes.guid.ToString("B").ToUpperInvariant(),
                Version = $"{attributes.wMajorVerNum}.{attributes.wMinorVerNum}",
                File = loaded.File is null ? null : Path.GetFileName(loaded.File),
                ExportedBy = exportedBy,
            },
            DependsOn = [.. _dependencies.Keys.Order(StringComparer.OrdinalIgnoreCase)],
            Enums = [.. enums],
            Classes = [.. classes],
        };

        return new TypeLibraryExport(description, _dependencies, [.. _skipped.Distinct()]);
    }

    private ClassDescription DescribeCoclass(LoadedTypeLibrary loaded, ITypeInfo info, TYPEATTR attr, string name, TYPEFLAGS flags)
    {
        ITypeInfo? defaultInterface = null;
        ITypeInfo? defaultSource = null;
        for (var i = 0; i < attr.cImplTypes; i++)
        {
            info.GetImplTypeFlags(i, out var implFlags);
            info.GetRefTypeOfImplType(i, out var href);
            info.GetRefTypeInfo(href, out var implemented);

            if (implFlags.HasFlag(IMPLTYPEFLAGS.IMPLTYPEFLAG_FSOURCE))
            {
                if (defaultSource is null || implFlags.HasFlag(IMPLTYPEFLAGS.IMPLTYPEFLAG_FDEFAULT))
                {
                    defaultSource = implemented;
                }
            }
            else if (defaultInterface is null || implFlags.HasFlag(IMPLTYPEFLAGS.IMPLTYPEFLAG_FDEFAULT))
            {
                defaultInterface = implemented;
            }
        }

        // the default interface is a type name of its own, which the class's members are declared by: IReturnBoolean of ReturnBoolean. One the class names itself
        // after, or that is IDispatch - a class that declares no interface of its own - is no other name.
        var interfaceName = defaultInterface is null ? null : Name(defaultInterface);
        var namesItsInterface = interfaceName is not null && !string.Equals(interfaceName, name, StringComparison.OrdinalIgnoreCase)
            && Attr(defaultInterface!).guid is var interfaceId && interfaceId != IUnknown && interfaceId != IDispatch;

        var creatable = flags.HasFlag(TYPEFLAGS.TYPEFLAG_FCANCREATE);
        return new ClassDescription
        {
            Name = name,
            Description = Doc(info, -1),
            ProgId = creatable ? ProgIdOf(attr.guid) : null,
            IsCreatable = creatable,
            IsHidden = flags.HasFlag(TYPEFLAGS.TYPEFLAG_FHIDDEN),
            IsApplication = flags.HasFlag(TYPEFLAGS.TYPEFLAG_FAPPOBJECT),
            DefaultInterface = namesItsInterface ? interfaceName : null,
            IsDefaultInterfaceHidden = namesItsInterface && ((TYPEFLAGS)Attr(defaultInterface!).wTypeFlags).HasFlag(TYPEFLAGS.TYPEFLAG_FHIDDEN),
            Members = defaultInterface is null ? [] : Ordered(MembersOf(loaded, defaultInterface, [])),
            Events = defaultSource is null ? [] : [.. EventsOf(loaded, defaultSource)],
        };
    }

    private EnumDescription DescribeEnum(ITypeInfo info, TYPEATTR attr, string name, TYPEFLAGS flags)
    {
        var members = new List<EnumMemberDescription>();
        for (var i = 0; i < attr.cVars; i++)
        {
            info.GetVarDesc(i, out var pointer);
            try
            {
                var variable = Marshal.PtrToStructure<VARDESC>(pointer);
                var value = variable.desc.lpvarValue == IntPtr.Zero ? null : Marshal.GetObjectForNativeVariant(variable.desc.lpvarValue);
                var varFlags = (VARFLAGS)variable.wVarFlags;
                if (varFlags.HasFlag(VARFLAGS.VARFLAG_FRESTRICTED))
                {
                    continue;
                }

                members.Add(new EnumMemberDescription
                {
                    Name = NameOf(info, variable.memid),
                    Value = value is null ? 0 : Convert.ToInt64(value, CultureInfo.InvariantCulture),
                    Description = Doc(info, variable.memid),
                    IsHidden = varFlags.HasFlag(VARFLAGS.VARFLAG_FHIDDEN),
                });
            }
            finally
            {
                info.ReleaseVarDesc(pointer);
            }
        }

        return new EnumDescription { Name = name, Description = Doc(info, -1), IsHidden = flags.HasFlag(TYPEFLAGS.TYPEFLAG_FHIDDEN), Members = [.. members] };
    }

    // the members of an interface and of every interface it inherits, but for what every automation object has.
    private IEnumerable<MemberDescription> MembersOf(LoadedTypeLibrary loaded, ITypeInfo info, HashSet<Guid> visited)
    {
        var attr = Attr(info);
        if (attr.guid == IUnknown || attr.guid == IDispatch || !visited.Add(attr.guid))
        {
            yield break;
        }

        for (var i = 0; i < attr.cFuncs; i++)
        {
            info.GetFuncDesc(i, out var pointer);
            MemberDescription? member;
            try
            {
                member = Member(loaded, info, Marshal.PtrToStructure<FUNCDESC>(pointer));
            }
            finally
            {
                info.ReleaseFuncDesc(pointer);
            }

            if (member is not null)
            {
                yield return member;
            }
        }

        // the properties of a dispinterface are declared as variables.
        for (var i = 0; i < attr.cVars; i++)
        {
            info.GetVarDesc(i, out var pointer);
            VARDESC variable;
            try
            {
                variable = Marshal.PtrToStructure<VARDESC>(pointer);
            }
            finally
            {
                info.ReleaseVarDesc(pointer);
            }

            foreach (var property in DispatchProperty(loaded, info, variable))
            {
                yield return property;
            }
        }

        for (var i = 0; i < attr.cImplTypes; i++)
        {
            info.GetRefTypeOfImplType(i, out var href);
            info.GetRefTypeInfo(href, out var baseInfo);
            foreach (var inherited in MembersOf(loaded, baseInfo, visited))
            {
                yield return inherited;
            }
        }
    }

    private IEnumerable<MemberDescription> DispatchProperty(LoadedTypeLibrary loaded, ITypeInfo info, VARDESC variable)
    {
        var varFlags = (VARFLAGS)variable.wVarFlags;
        if (variable.varkind != VARKIND.VAR_DISPATCH || varFlags.HasFlag(VARFLAGS.VARFLAG_FRESTRICTED))
        {
            yield break;
        }

        var name = NameOf(info, variable.memid);
        var type = TypeNameOf(variable.elemdescVar.tdesc, info);
        var hidden = varFlags.HasFlag(VARFLAGS.VARFLAG_FHIDDEN) || name.StartsWith('_');
        var description = Doc(info, variable.memid);

        yield return new MemberDescription
        {
            Name = name, Kind = MemberKind.PropertyGet, Type = type, DispId = SpecialId(variable.memid), Description = description, IsHidden = hidden,
        };

        if (!varFlags.HasFlag(VARFLAGS.VARFLAG_FREADONLY))
        {
            yield return new MemberDescription
            {
                Name = name,
                Kind = IsObject(variable.elemdescVar.tdesc, info) ? MemberKind.PropertySet : MemberKind.PropertyLet,
                Description = description,
                IsHidden = hidden,
                Parameters = [new ParameterDescription { Name = "Value", Type = type, IsByVal = true }],
            };
        }
    }

    private MemberDescription? Member(LoadedTypeLibrary loaded, ITypeInfo info, FUNCDESC function)
    {
        var flags = (FUNCFLAGS)function.wFuncFlags;

        // a member the library marks restricted is not one VBA can call, but for the two that VBA itself calls for the language: the default member, and the
        // enumerator `For Each` asks a collection for, which every collection marks restricted. Both are described, and hidden.
        var restricted = flags.HasFlag(FUNCFLAGS.FUNCFLAG_FRESTRICTED);
        if ((restricted && function.memid is not (0 or -4)) || flags.HasFlag(FUNCFLAGS.FUNCFLAG_FSOURCE))
        {
            return null;
        }

        var names = new string[function.cParams + 1];
        info.GetNames(function.memid, names, names.Length, out var count);
        var name = names[0];

        var parameters = new List<ParameterDescription>();
        ELEMDESC? retval = null;
        var size = Marshal.SizeOf<ELEMDESC>();
        for (var i = 0; i < function.cParams; i++)
        {
            var element = Marshal.PtrToStructure<ELEMDESC>(IntPtr.Add(function.lprgelemdescParam, i * size));
            var paramFlags = element.desc.paramdesc.wParamFlags;

            if (paramFlags.HasFlag(PARAMFLAG.PARAMFLAG_FLCID))
            {
                continue;
            }

            if (paramFlags.HasFlag(PARAMFLAG.PARAMFLAG_FRETVAL))
            {
                retval = element;
                continue;
            }

            var type = ParameterType(element.tdesc, info, out var byRef);
            var optional = paramFlags.HasFlag(PARAMFLAG.PARAMFLAG_FOPT);
            parameters.Add(new ParameterDescription
            {
                Name = i + 1 < count && !string.IsNullOrWhiteSpace(names[i + 1])
                    ? names[i + 1]
                    // the value an assignment gives a property is the last parameter of its accessor, and a library does not always name it.
                    : function.invkind is INVOKEKIND.INVOKE_PROPERTYPUT or INVOKEKIND.INVOKE_PROPERTYPUTREF && i == function.cParams - 1 ? "Value" : $"arg{i + 1}",
                Type = type,
                IsByVal = !byRef,
                IsOptional = optional,
                DefaultValue = paramFlags.HasFlag(PARAMFLAG.PARAMFLAG_FHASDEFAULT) ? DefaultOf(element.desc.paramdesc.lpVarValue) : null,
                IsParamArray = function.cParamsOpt == -1 && i == function.cParams - 1,
            });
        }

        string? returns = null;
        if (function.invkind is INVOKEKIND.INVOKE_FUNC or INVOKEKIND.INVOKE_PROPERTYGET)
        {
            var returned = function.elemdescFunc.tdesc.vt == (short)VarEnum.VT_HRESULT ? retval?.tdesc : function.elemdescFunc.tdesc;
            returns = returned is { } typeDesc && typeDesc.vt != (short)VarEnum.VT_VOID ? ParameterType(typeDesc, info, out _) : null;
        }

        return new MemberDescription
        {
            Name = name,
            Kind = function.invkind switch
            {
                INVOKEKIND.INVOKE_PROPERTYGET => MemberKind.PropertyGet,
                INVOKEKIND.INVOKE_PROPERTYPUT => MemberKind.PropertyLet,
                INVOKEKIND.INVOKE_PROPERTYPUTREF => MemberKind.PropertySet,
                _ => MemberKind.Method,
            },
            Type = returns,
            DispId = SpecialId(function.memid),
            Description = Doc(info, function.memid),
            IsHidden = restricted || flags.HasFlag(FUNCFLAGS.FUNCFLAG_FHIDDEN) || name.StartsWith('_'),
            Parameters = [.. parameters],
        };
    }

    // the accessors of a property are described together, as Get, Let, Set, whichever order the library declares them in: a property is read before it is
    // assigned, and a reader that takes the first of the members of a name must find its getter.
    private static ImmutableArray<MemberDescription> Ordered(IEnumerable<MemberDescription> members)
        => [.. members.GroupBy(member => member.Name, StringComparer.OrdinalIgnoreCase).SelectMany(group => group.OrderBy(member => member.Kind))];

    private IEnumerable<EventDescription> EventsOf(LoadedTypeLibrary loaded, ITypeInfo source)
    {
        var attr = Attr(source);
        for (var i = 0; i < attr.cFuncs; i++)
        {
            source.GetFuncDesc(i, out var pointer);
            try
            {
                var function = Marshal.PtrToStructure<FUNCDESC>(pointer);
                if (((FUNCFLAGS)function.wFuncFlags).HasFlag(FUNCFLAGS.FUNCFLAG_FRESTRICTED))
                {
                    continue;
                }

                var names = new string[function.cParams + 1];
                source.GetNames(function.memid, names, names.Length, out var count);
                var parameters = new List<ParameterDescription>();
                var size = Marshal.SizeOf<ELEMDESC>();
                for (var p = 0; p < function.cParams; p++)
                {
                    var element = Marshal.PtrToStructure<ELEMDESC>(IntPtr.Add(function.lprgelemdescParam, p * size));
                    var type = ParameterType(element.tdesc, source, out var byRef);
                    parameters.Add(new ParameterDescription
                    {
                        Name = p + 1 < count && !string.IsNullOrWhiteSpace(names[p + 1]) ? names[p + 1] : $"arg{p + 1}",
                        Type = type,
                        IsByVal = !byRef,
                    });
                }

                yield return new EventDescription { Name = names[0], Description = Doc(source, function.memid), Parameters = [.. parameters] };
            }
            finally
            {
                source.ReleaseFuncDesc(pointer);
            }
        }
    }

    // ---- types ----

    // a parameter is passed by reference when it is a pointer to something that is not an object: a pointer to an interface is an object, and a pointer to one
    // is a reference to it.
    private string ParameterType(TYPEDESC type, ITypeInfo context, out bool byRef)
    {
        byRef = false;
        if (type.vt != (short)VarEnum.VT_PTR)
        {
            return TypeNameOf(type, context);
        }

        var inner = Dereference(type);
        if (inner.vt == (short)VarEnum.VT_PTR)
        {
            byRef = true;
            inner = Dereference(inner);
        }
        else if (inner.vt != (short)VarEnum.VT_USERDEFINED || !IsObject(inner, context))
        {
            byRef = true;
        }

        return TypeNameOf(inner, context);
    }

    private string TypeNameOf(TYPEDESC type, ITypeInfo context)
    {
        switch ((VarEnum)type.vt)
        {
            case VarEnum.VT_PTR:
                var pointee = Dereference(type);
                return pointee.vt == (short)VarEnum.VT_VOID ? "LongPtr" : TypeNameOf(pointee, context);
            case VarEnum.VT_SAFEARRAY:
                return TypeNameOf(Dereference(type), context) + "()";
            case VarEnum.VT_CARRAY:
                return TypeNameOf(Marshal.PtrToStructure<TYPEDESC>(type.lpValue), context) + "()";
            case VarEnum.VT_USERDEFINED:
                context.GetRefTypeInfo((int)type.lpValue, out var referenced);
                return NameOfType(referenced);
            case VarEnum.VT_I2 or VarEnum.VT_UI2: return "Integer";
            case VarEnum.VT_I4 or VarEnum.VT_UI4 or VarEnum.VT_INT or VarEnum.VT_UINT or VarEnum.VT_ERROR or VarEnum.VT_HRESULT: return "Long";
            case VarEnum.VT_I8 or VarEnum.VT_UI8: return "LongLong";
            case VarEnum.VT_I1 or VarEnum.VT_UI1: return "Byte";
            case VarEnum.VT_R4: return "Single";
            case VarEnum.VT_R8: return "Double";
            case VarEnum.VT_CY: return "Currency";
            case VarEnum.VT_DATE: return "Date";
            case VarEnum.VT_BSTR or VarEnum.VT_LPSTR or VarEnum.VT_LPWSTR: return "String";
            case VarEnum.VT_BOOL: return "Boolean";
            case VarEnum.VT_DECIMAL: return "Decimal";
            case VarEnum.VT_DISPATCH or VarEnum.VT_UNKNOWN: return "Object";
            default: return "Variant";
        }
    }

    private static TYPEDESC Dereference(TYPEDESC type) => Marshal.PtrToStructure<TYPEDESC>(type.lpValue);

    private bool IsObject(TYPEDESC type, ITypeInfo context)
    {
        if (type.vt == (short)VarEnum.VT_PTR)
        {
            return IsObject(Dereference(type), context);
        }

        if (type.vt == (short)VarEnum.VT_DISPATCH || type.vt == (short)VarEnum.VT_UNKNOWN)
        {
            return true;
        }

        if (type.vt != (short)VarEnum.VT_USERDEFINED)
        {
            return false;
        }

        context.GetRefTypeInfo((int)type.lpValue, out var referenced);
        var attr = Attr(referenced);
        return attr.typekind switch
        {
            TYPEKIND.TKIND_INTERFACE or TYPEKIND.TKIND_DISPATCH or TYPEKIND.TKIND_COCLASS => true,
            TYPEKIND.TKIND_ALIAS => IsObject(attr.tdescAlias, referenced),
            _ => false,
        };
    }

    // a type is named by its coclass when an interface is its default, qualified by its library when it is another's.
    private string NameOfType(ITypeInfo type)
    {
        var attr = Attr(type);
        if (attr.typekind == TYPEKIND.TKIND_ALIAS)
        {
            return TypeNameOf(attr.tdescAlias, type);
        }

        if (attr.typekind is TYPEKIND.TKIND_UNION or TYPEKIND.TKIND_MODULE)
        {
            return "Variant";
        }

        type.GetContainingTypeLib(out var container, out _);
        container.GetDocumentation(-1, out var library, out _, out _, out _);

        var name = attr.typekind is TYPEKIND.TKIND_INTERFACE or TYPEKIND.TKIND_DISPATCH
            && ClassOfInterface(library, container).TryGetValue(attr.guid, out var coclass)
                ? coclass
                : Name(type);

        if (string.Equals(library, CurrentLibrary, StringComparison.OrdinalIgnoreCase))
        {
            return name;
        }

        if (!string.Equals(library, StandardLibrary, StringComparison.OrdinalIgnoreCase))
        {
            _dependencies.TryAdd(library, container);
        }

        return $"{library}.{name}";
    }

    private string CurrentLibrary { get; set; } = string.Empty;

    // ---- helpers ----

    private Dictionary<Guid, string> ClassOfInterface(string libraryName, ITypeLib library)
    {
        if (_classOfInterface.TryGetValue(libraryName, out var known))
        {
            return known;
        }

        var map = new Dictionary<Guid, string>();
        for (var i = 0; i < library.GetTypeInfoCount(); i++)
        {
            library.GetTypeInfo(i, out var info);
            var attr = Attr(info);
            if (attr.typekind != TYPEKIND.TKIND_COCLASS)
            {
                continue;
            }

            ITypeInfo? chosen = null;
            for (var j = 0; j < attr.cImplTypes; j++)
            {
                info.GetImplTypeFlags(j, out var flags);
                if (flags.HasFlag(IMPLTYPEFLAGS.IMPLTYPEFLAG_FSOURCE))
                {
                    continue;
                }

                info.GetRefTypeOfImplType(j, out var href);
                info.GetRefTypeInfo(href, out var implemented);
                if (chosen is null || flags.HasFlag(IMPLTYPEFLAGS.IMPLTYPEFLAG_FDEFAULT))
                {
                    chosen = implemented;
                }
            }

            if (chosen is not null)
            {
                map.TryAdd(Attr(chosen).guid, Name(info));
            }
        }

        _classOfInterface[libraryName] = map;
        return map;
    }

    // the interfaces that coclasses raise events through: they are not classes.
    private static HashSet<Guid> SourceInterfaces(ITypeLib library)
    {
        var sources = new HashSet<Guid>();
        for (var i = 0; i < library.GetTypeInfoCount(); i++)
        {
            library.GetTypeInfo(i, out var info);
            var attr = Attr(info);
            if (attr.typekind != TYPEKIND.TKIND_COCLASS)
            {
                continue;
            }

            for (var j = 0; j < attr.cImplTypes; j++)
            {
                info.GetImplTypeFlags(j, out var flags);
                if (flags.HasFlag(IMPLTYPEFLAGS.IMPLTYPEFLAG_FSOURCE))
                {
                    info.GetRefTypeOfImplType(j, out var href);
                    info.GetRefTypeInfo(href, out var source);
                    sources.Add(Attr(source).guid);
                }
            }
        }

        return sources;
    }

    private static TYPEATTR Attr(ITypeInfo info)
    {
        info.GetTypeAttr(out var pointer);
        try
        {
            return Marshal.PtrToStructure<TYPEATTR>(pointer);
        }
        finally
        {
            info.ReleaseTypeAttr(pointer);
        }
    }

    private static TYPELIBATTR Attributes(ITypeLib library)
    {
        library.GetLibAttr(out var pointer);
        try
        {
            return Marshal.PtrToStructure<TYPELIBATTR>(pointer);
        }
        finally
        {
            library.ReleaseTLibAttr(pointer);
        }
    }

    private static string Name(ITypeInfo info)
    {
        info.GetDocumentation(-1, out var name, out _, out _, out _);
        return name;
    }

    private static string NameOf(ITypeInfo info, int memid)
    {
        var names = new string[1];
        info.GetNames(memid, names, 1, out _);
        return names[0];
    }

    private string? Doc(ITypeInfo info, int memid)
    {
        if (!includeDescriptions)
        {
            return null;
        }

        info.GetDocumentation(memid, out _, out var doc, out _, out _);
        return Clean(doc);
    }

    private static string? Clean(string? text)
    {
        var cleaned = text?.Replace("\r\n", " ").Replace('\n', ' ').Replace('\r', ' ').Trim();
        return string.IsNullOrEmpty(cleaned) ? null : cleaned;
    }

    // 0 marks the default member of a class and -4 its enumerator; no other id is of use to anything that reads a description.
    private static int? SpecialId(int memid) => memid is 0 or -4 ? memid : null;

    // the default of an optional parameter, as the literal VBA would write; PARAMDESCEX is a size and then a VARIANT.
    private static string? DefaultOf(IntPtr parameterDescription)
    {
        if (parameterDescription == IntPtr.Zero)
        {
            return null;
        }

        var value = Marshal.GetObjectForNativeVariant(IntPtr.Add(parameterDescription, 8));
        return value switch
        {
            null or DBNull => null,
            bool flag => flag ? "True" : "False",
            string text => "\"" + text.Replace("\"", "\"\"") + "\"",
            IConvertible number => number.ToString(CultureInfo.InvariantCulture),
            _ => null,
        };
    }

    private static string? ProgIdOf(Guid clsid)
    {
        using var root = RegistryKey.OpenBaseKey(RegistryHive.ClassesRoot, RegistryView.Registry64);
        using var key = root.OpenSubKey($@"CLSID\{clsid:B}");
        if (key is null)
        {
            return null;
        }

        using var independent = key.OpenSubKey("VersionIndependentProgID");
        using var versioned = key.OpenSubKey("ProgID");
        return (independent?.GetValue(null) ?? versioned?.GetValue(null)) as string;
    }
}
