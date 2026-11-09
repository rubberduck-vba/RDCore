using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Runtime.Abstract.Execution;

namespace RDCore.Runtime.LanguageCore.Static;

// MS-VBAL §5.6.4
internal enum TypeNameClass
{
    Type,

    ProceduralModule,

    LibraryModule,

    Project,

    Unresolved,
}

// MS-VBAL §5.6.16.7, §5.6.10
internal static class TypeNameClassifier
{
    public static (TypeNameClass Class, Symbol? Symbol) Classify(string? qualifier, string typeName, Uri handle, ISymbolResolver resolver)
    {
        if (qualifier is null && IntrinsicVBTypes.IsIntrinsic(typeName))
        {
            return (TypeNameClass.Type, null);
        }

        var result = VBProjectSymbol.ResolveQualifiedType(resolver, qualifier, typeName, handle);
        return result switch
        {
            { IsResolved: false } => (TypeNameClass.Unresolved, null),
            { Symbol: VBProjectSymbol project } => (TypeNameClass.Project, project),
            { Symbol: VBStandardModuleSymbol module } when module.GetProperty(SymbolProperties.Library) is not null => (TypeNameClass.LibraryModule, module),
            { Symbol: VBStandardModuleSymbol module } => (TypeNameClass.ProceduralModule, module),
            { Symbol: var symbol } => (TypeNameClass.Type, symbol),
        };
    }

    public static (TypeNameClass Class, Symbol? Symbol) Classify(string name, Uri handle, ISymbolResolver resolver)
    {
        var split = name.LastIndexOf('.');
        return split < 0 ? Classify(null, name, handle, resolver) : Classify(name[..split], name[(split + 1)..], handle, resolver);
    }
}
