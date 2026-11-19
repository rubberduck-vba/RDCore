using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Complex;
using RDCore.SDK.Semantics;
using System.Collections.Immutable;

namespace RDCore.Runtime.LanguageCore.Static;

// MS-VBAL §5.6.8, §5.2.3.1.4: the classes whose objects a module's code creates, with New, or with a variable declared As New, and what is known of creating
// them.
public static class ClassFacts
{
    public static ImmutableArray<ClassFact> Of(SyntaxNode module, IEnumerable<ProcedureSemanticModel> procedures, IEnumerable<Symbol> declared)
    {
        var facts = procedures.SelectMany(procedure => procedure.Expressions).GroupBy(fact => fact.Key).ToDictionary(group => group.Key, group => group.First().Value);
        var created = module.Descendants().OfType<NewExpressionNode>()
            .Select(created => facts.TryGetValue(created.Identity, out var fact) && fact.DeclaredType is VBClassType { Symbol: var symbol } ? symbol : null)
            .Concat(declared.Select(AutoInstantiatedClassOf));

        return [.. created.OfType<VBClassModuleSymbol>()
            .DistinctBy(symbol => symbol.SemanticId)
            .Select(symbol => new ClassFact(symbol.Uri, symbol.Name, symbol.GetProperty(SymbolProperties.Creatable), !symbol.GetProperty(SymbolProperties.Unregistered)))];
    }

    // the class of a variable declared As New, or of an array whose elements are: what its object is created as where code first uses it.
    public static VBClassModuleSymbol? AutoInstantiatedClassOf(Symbol symbol)
        => symbol.GetProperty(SymbolProperties.AutoInstantiated)
            && symbol is ITypedSymbol { ResolvedType: var type }
            && (type is VBArrayType array ? array.ItemType : type) is VBClassType { Symbol: var created }
            ? created
            : null;
}
