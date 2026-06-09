using Microsoft.Win32;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.Operators;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Complex;
using RDCore.SDK.Model.Types.Meta;
using RDCore.SDK.Runtime;
using RDCore.SDK.Runtime.Abstract;
using RDCore.SDK.Server.ProtocolExtensions;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;

namespace RDCore.SDK.Model.Symbols;

public class StaticSymbolsProvider : IStaticSymbolsProvider 
{
    private readonly Dictionary<string, StaticSymbol> _registry;

    public StaticSymbolsProvider(IEnumerable<StaticSymbol> symbols)
    {
        _registry = symbols.ToDictionary(symbol => symbol.Name);
    }

    protected void Load(IStaticSymbolsProvider provider)
    {
        foreach (var symbol in provider.GetAll())
        {
            _registry[symbol.Name] = symbol;
        }
    }

    public IEnumerable<StaticSymbol> GetAll() 
        => _registry.Values;

    public bool TryGetByName(string name, [MaybeNullWhen(false), NotNullWhen(true)] out StaticSymbol symbol)
        => _registry.TryGetValue(name, out symbol);
}

public sealed class GlobalSymbols : StaticSymbolsProvider
{
    public GlobalSymbols(IEnumerable<IStaticSymbolsProvider> providers) : base([])
    {
        foreach (var provider in providers)
        {
            Load(provider);
        }
    }

    /// <summary>
    /// Represents a symbol that is unbound and unresolved.
    /// </summary>
    /// <remarks>
    /// The data type of this symbol is <see cref="VBUnknownType"/>.
    /// </remarks>
    public static readonly StaticSymbol UnresolvedSymbol = new(nameof(UnresolvedSymbol), SymbolKindExt.Ignored, VBUnknownType.TypeInfo);

    /// <summary>
    /// An immutable array containing all <c>OperatorSymbol</c> implementations.
    /// </summary>
    /// <remarks>
    /// Alphabetical order for searchability.
    /// </remarks>
    public static readonly ImmutableArray<StaticSymbol> Operators =
    [
        new BinaryArithmeticAdditionOperatorSymbol(),
        new BinaryBitwiseAndOperatorSymbol(),
        new BinaryBitwiseEqvOperatorSymbol(),
        new BinaryBitwiseImpOperatorSymbol(),
        new UnaryBitwiseNotOperatorSymbol(),
        new BinaryBitwiseOrOperatorSymbol(),
        new BinaryBitwiseXOrOperatorSymbol(),
        new BinaryStringConcatOperatorSymbol(),
        new BinaryArithmeticDivisionOperatorSymbol(),
        new BinaryCompareEqOperatorSymbol(),
        new BinaryArithmeticExponentOperatorSymbol(),
        new BinaryCompareGtOperatorSymbol(),
        new BinaryCompareGtEqOperatorSymbol(),
        new BinaryCompareNeqOperatorSymbol(),
        new BinaryArithmeticIntegerDivisionOperatorSymbol(),
        new BinaryCompareIsOperatorSymbol(),
        new BinaryCompareLtOperatorSymbol(),
        new BinaryCompareLtEqOperatorSymbol(),
        new BinaryCompareLikeOperatorSymbol(),
        new BinaryMemberAccessOperatorSymbol(),
        new BinaryArithmeticModuloOperatorSymbol(),
        new BinaryArithmeticMultiplicationOperatorSymbol(),
        new BinaryArithmeticSubtractionOperatorSymbol(),
        new BinaryLetCoercionOperatorSymbol(),
        new UnaryArithmeticNegationOperatorSymbol(),
        new UnaryArithmeticAdditionOperatorSymbol(),
    ];


    public static class StaticSymbols
    {
        public static readonly StaticSymbol True = new(Tokens.True, SymbolKindExt.Constant, VBBooleanType.TypeInfo);
        public static readonly StaticSymbol False = new(Tokens.False, SymbolKindExt.Constant, VBBooleanType.TypeInfo);

        public static readonly StaticSymbol Empty = new(Tokens.Empty, SymbolKindExt.Constant, VBEmptyType.TypeInfo);
        public static readonly StaticSymbol Nothing = new(Tokens.Nothing, SymbolKindExt.Constant, VBObjectType.TypeInfo);
        public static readonly StaticSymbol Null = new(Tokens.Null, SymbolKindExt.Constant, VBNullType.TypeInfo);
        public static readonly StaticSymbol VBNullString = new(Tokens.vbNullString, SymbolKindExt.Constant, VBStringType.TypeInfo);
        public static readonly StaticSymbol VBEmptyString = new("String.Empty", SymbolKindExt.Constant, VBStringType.TypeInfo);

        public static readonly StaticSymbol VBVoid = new("void", SymbolKindExt.Ignored, VBVoidType.TypeInfo);
        public static readonly StaticSymbol VBUnknown = new("unkown", SymbolKindExt.Ignored, VBVoidType.TypeInfo);
        public static readonly StaticSymbol TypeDesc = new("TypeDesc", SymbolKindExt.TypeDescriptor, VBTypeDesc.TypeInfo);
        public static readonly StaticSymbol MissingValue = new("Missing", SymbolKindExt.Variable, VBMissingType.TypeInfo);

        public static readonly StaticSymbol EmptyFixedSizeArray = new("Array.Empty", SymbolKindExt.Array, VBResizableArrayType.TypeInfo);
        public static readonly StaticSymbol EmptyResizableArray = new("Array().Empty", SymbolKindExt.Array, VBResizableArrayType.TypeInfo);
        public static readonly StaticSymbol EmptyResizableByteArray = new("Array().Byte", SymbolKindExt.Array, VBResizableByteArrayType.TypeInfo);

        public static readonly StaticSymbol DefBoolDirective = new("def.bool", SymbolKindExt.Directive, VBBooleanType.TypeInfo);
        public static readonly StaticSymbol DefByteDirective = new("def.byte", SymbolKindExt.Directive, VBByteType.TypeInfo);
        public static readonly StaticSymbol DefIntDirective = new("def.int16", SymbolKindExt.Directive, VBIntegerType.TypeInfo);
        public static readonly StaticSymbol DefLngDirective = new("def.int32", SymbolKindExt.Directive, VBLongType.TypeInfo);
        public static readonly StaticSymbol DefLngLngDirective = new("def.int64", SymbolKindExt.Directive, VBLongLongType.TypeInfo);
        public static readonly StaticSymbol DefSngDirective = new("def.float", SymbolKindExt.Directive, VBSingleType.TypeInfo);
        public static readonly StaticSymbol DefDblDirective = new("def.double", SymbolKindExt.Directive, VBDoubleType.TypeInfo);
        public static readonly StaticSymbol DefStrDirective = new("def.string", SymbolKindExt.Directive, VBStringType.TypeInfo);
        public static readonly StaticSymbol DefObjDirective = new("def.obj", SymbolKindExt.Directive, VBObjectType.TypeInfo);
        public static readonly StaticSymbol DefVarDirective = new("def.variant", SymbolKindExt.Directive, VBVariantType.TypeInfo);
        public static readonly StaticSymbol OptionExplicitDirective = new("option.explicit", SymbolKindExt.Directive, VBVoidType.TypeInfo);
        public static readonly StaticSymbol OptionBase0Directive = new("option.base.0", SymbolKindExt.Directive, VBVoidType.TypeInfo);
        public static readonly StaticSymbol OptionBase1Directive = new("option.base.1", SymbolKindExt.Directive, VBVoidType.TypeInfo);
        public static readonly StaticSymbol OptionPrivateModuleDirective = new("option.private", SymbolKindExt.Directive, VBVoidType.TypeInfo);
        public static readonly StaticSymbol OptionCompareBinaryDirective = new("option.compare.bin", SymbolKindExt.Directive, VBVoidType.TypeInfo);
        public static readonly StaticSymbol OptionCompareTextDirective = new("option.compare.text", SymbolKindExt.Directive, VBVoidType.TypeInfo);
        public static readonly StaticSymbol OptionCompareDatabaseDirective = new("option.compare.db", SymbolKindExt.Directive, VBVoidType.TypeInfo);
        public static readonly StaticSymbol OptionStrictDirective = new("option.strict", SymbolKindExt.Directive, VBVoidType.TypeInfo);

        public static readonly StaticSymbol SubStmtProc = new("sub.proc", SymbolKindExt.Procedure, VBVoidType.TypeInfo);
        public static readonly StaticSymbol FuncStmtProc = new("func.proc", SymbolKindExt.Function, VBVariantType.TypeInfo);
        public static readonly StaticSymbol PropGetStmtProc = new("prop-get.proc", SymbolKindExt.Property, VBVariantType.TypeInfo);
        public static readonly StaticSymbol PropLetStmtProc = new("prop-let.proc", SymbolKindExt.Property, VBVoidType.TypeInfo);
        public static readonly StaticSymbol PropSetStmtProc = new("prop-set.proc", SymbolKindExt.Property, VBVoidType.TypeInfo);
    }

    /// <summary>
    /// Defines a number of static (unallocated) symbols used internally for semantic purposes.
    /// </summary>
    public static class ExtensionSymbols
    {
        public static readonly StaticSymbol VBByteZeroValue = new("Byte.0", SymbolKindExt.Constant, VBByteType.TypeInfo);
        public static readonly StaticSymbol VBByteMinValue = new("Byte.Min", SymbolKindExt.Constant, VBByteType.TypeInfo);
        public static readonly StaticSymbol VBByteMaxValue = new("Byte.Max", SymbolKindExt.Constant, VBByteType.TypeInfo);

        public static readonly StaticSymbol VBIntegerNegativeOneValue = new("Integer.-1", SymbolKindExt.Constant, VBIntegerType.TypeInfo);
        public static readonly StaticSymbol VBIntegerZeroValue = new("Integer.0", SymbolKindExt.Constant, VBIntegerType.TypeInfo);
        public static readonly StaticSymbol VBIntegerMinValue = new("Integer.Min", SymbolKindExt.Constant, VBIntegerType.TypeInfo);
        public static readonly StaticSymbol VBIntegerMaxValue = new("Integer.Max", SymbolKindExt.Constant, VBIntegerType.TypeInfo);

        public static readonly StaticSymbol VBLongZeroValue = new("Long.0", SymbolKindExt.Constant, VBLongType.TypeInfo);
        public static readonly StaticSymbol VBLongMinValue = new("Long.Min", SymbolKindExt.Constant, VBLongType.TypeInfo);
        public static readonly StaticSymbol VBLongMaxValue = new("Long.Max", SymbolKindExt.Constant, VBLongType.TypeInfo);

        public static readonly StaticSymbol VBLongLongZeroValue = new("LongLong.0", SymbolKindExt.Constant, VBLongLongType.TypeInfo);
        public static readonly StaticSymbol VBLongLongMinValue = new("LongLong.Min", SymbolKindExt.Constant, VBLongLongType.TypeInfo);
        public static readonly StaticSymbol VBLongLongMaxValue = new("LongLong.Max", SymbolKindExt.Constant, VBLongLongType.TypeInfo);

        public static readonly StaticSymbol VBLongPtrZeroValue = new("LongPtr.0", SymbolKindExt.Constant, VBLongType.TypeInfo);
        public static readonly StaticSymbol VBLongPtrMinValue = new("LongPtr.Min", SymbolKindExt.Constant, VBLongType.TypeInfo);
        public static readonly StaticSymbol VBLongPtrMaxValue = new("LongPtr.Max", SymbolKindExt.Constant, VBLongType.TypeInfo);

        public static readonly StaticSymbol VBLongPtr64ZeroValue = new("LongPtr.x64.0", SymbolKindExt.Constant, VBLongType.TypeInfo);
        public static readonly StaticSymbol VBLongPtr64MinValue = new("LongPtr.x64.Min", SymbolKindExt.Constant, VBLongType.TypeInfo);
        public static readonly StaticSymbol VBLongPtr64MaxValue = new("LongPtr.x64.Max", SymbolKindExt.Constant, VBLongType.TypeInfo);

        public static readonly StaticSymbol VBSingleZeroValue = new("Single.0", SymbolKindExt.Constant, VBSingleType.TypeInfo);
        public static readonly StaticSymbol VBSingleMinValue = new("Single.Min", SymbolKindExt.Constant, VBSingleType.TypeInfo);
        public static readonly StaticSymbol VBSingleMaxValue = new("Single.Max", SymbolKindExt.Constant, VBSingleType.TypeInfo);

        public static readonly StaticSymbol VBDoubleZeroValue = new("Double.0", SymbolKindExt.Constant, VBDoubleType.TypeInfo);
        public static readonly StaticSymbol VBDoubleOneValue = new("Double.1", SymbolKindExt.Constant, VBDoubleType.TypeInfo);
        public static readonly StaticSymbol VBDoubleMinValue = new("Double.Min", SymbolKindExt.Constant, VBDoubleType.TypeInfo);
        public static readonly StaticSymbol VBDoubleMaxValue = new("Double.Max", SymbolKindExt.Constant, VBDoubleType.TypeInfo);

        public static readonly StaticSymbol VBCurrencyZeroValue = new("Currency.0", SymbolKindExt.Constant, VBCurrencyType.TypeInfo);
        public static readonly StaticSymbol VBCurrencyMinValue = new("Currency.Min", SymbolKindExt.Constant, VBCurrencyType.TypeInfo);
        public static readonly StaticSymbol VBCurrencyMaxValue = new("Currency.Max", SymbolKindExt.Constant, VBCurrencyType.TypeInfo);

        public static readonly StaticSymbol VBDecimalZeroValue = new("Decimal.0", SymbolKindExt.Constant, VBDecimalType.TypeInfo);
        public static readonly StaticSymbol VBDecimalMinValue = new("Decimal.Min", SymbolKindExt.Constant, VBDecimalType.TypeInfo);
        public static readonly StaticSymbol VBDecimalMaxValue = new("Decimal.Max", SymbolKindExt.Constant, VBDecimalType.TypeInfo);

        public static readonly StaticSymbol VBDateZeroValue = new("Date.0", SymbolKindExt.Constant, VBDateType.TypeInfo);
        public static readonly StaticSymbol VBDateMinValue = new("Date.Min", SymbolKindExt.Constant, VBDateType.TypeInfo);
        public static readonly StaticSymbol VBDateMaxValue = new("Date.Max", SymbolKindExt.Constant, VBDateType.TypeInfo);

        public static readonly StaticSymbol VBErrorZeroValue = new("Error.0", SymbolKindExt.Constant, VBDateType.TypeInfo);
        public static readonly StaticSymbol VBErrorMinValue = new("Error.Min", SymbolKindExt.Constant, VBDateType.TypeInfo);
        public static readonly StaticSymbol VBErrorMaxValue = new("Error.Max", SymbolKindExt.Constant, VBDateType.TypeInfo);
    }


    public static class OperatorSymbols
    {
        public static readonly StaticSymbol ConcatOp = Operators.OfType<BinaryStringConcatOperatorSymbol>().Single();

        public static readonly StaticSymbol UnaryNegationOp = Operators.OfType<UnaryArithmeticNegationOperatorSymbol>().Single();
        public static readonly StaticSymbol UnaryAdditionOp = Operators.OfType<UnaryArithmeticAdditionOperatorSymbol>().Single();
        public static readonly StaticSymbol AdditionOp = Operators.OfType<BinaryArithmeticAdditionOperatorSymbol>().Single();
        public static readonly StaticSymbol SubtractionOp = Operators.OfType<BinaryArithmeticSubtractionOperatorSymbol>().Single();
        public static readonly StaticSymbol MultiplicationOp = Operators.OfType<BinaryArithmeticMultiplicationOperatorSymbol>().Single();
        public static readonly StaticSymbol DivisionOp = Operators.OfType<BinaryArithmeticDivisionOperatorSymbol>().Single();
        public static readonly StaticSymbol IntegerDivisionOp = Operators.OfType<BinaryArithmeticIntegerDivisionOperatorSymbol>().Single();
        public static readonly StaticSymbol ExponentiationOp = Operators.OfType<BinaryArithmeticExponentOperatorSymbol>().Single();
        public static readonly StaticSymbol ModuloOp = Operators.OfType<BinaryArithmeticModuloOperatorSymbol>().Single();

        public static readonly StaticSymbol BitwiseNotOp = Operators.OfType<UnaryBitwiseNotOperatorSymbol>().Single();
        public static readonly StaticSymbol BitwiseAndOp = Operators.OfType<BinaryBitwiseAndOperatorSymbol>().Single();
        public static readonly StaticSymbol BitwiseOrOp = Operators.OfType<BinaryBitwiseOrOperatorSymbol>().Single();
        public static readonly StaticSymbol BitwiseXOrOp = Operators.OfType<BinaryBitwiseXOrOperatorSymbol>().Single();
        public static readonly StaticSymbol BitwiseImpOp = Operators.OfType<BinaryBitwiseImpOperatorSymbol>().Single();
        public static readonly StaticSymbol BitwiseEqvOp = Operators.OfType<BinaryBitwiseEqvOperatorSymbol>().Single();

        public static readonly StaticSymbol CompareEqOp = Operators.OfType<BinaryCompareEqOperatorSymbol>().Single();
        public static readonly StaticSymbol CompareNeqOp = Operators.OfType<BinaryCompareNeqOperatorSymbol>().Single();
        public static readonly StaticSymbol CompareLtOp = Operators.OfType<BinaryCompareLtOperatorSymbol>().Single();
        public static readonly StaticSymbol CompareGtOp = Operators.OfType<BinaryCompareGtOperatorSymbol>().Single();
        public static readonly StaticSymbol CompareLtEqOp = Operators.OfType<BinaryCompareLtEqOperatorSymbol>().Single();
        public static readonly StaticSymbol CompareGtEqOp = Operators.OfType<BinaryCompareGtEqOperatorSymbol>().Single();
        public static readonly StaticSymbol CompareLikeOp = Operators.OfType<BinaryCompareLikeOperatorSymbol>().Single();
        public static readonly StaticSymbol CompareIsOp = Operators.OfType<BinaryCompareIsOperatorSymbol>().Single();

        public static readonly StaticSymbol ExplicitCoercionOp = Operators.OfType<BinaryLetCoercionOperatorSymbol>().Single();
    }
}