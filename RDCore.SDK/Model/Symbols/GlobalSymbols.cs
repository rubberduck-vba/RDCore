using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Server.ProtocolExtensions;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using RDCore.SDK.Model.Symbols.Operators;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Meta;

namespace RDCore.SDK.Model.Symbols;

public static class GlobalSymbols
{
    public static class StaticSymbols
    {
        public static readonly StaticSymbol True = new(Tokens.True, SymbolKindExt.Constant, VBBooleanType.TypeInfo);
        public static readonly StaticSymbol False = new(Tokens.False, SymbolKindExt.Constant, VBBooleanType.TypeInfo);

        public static readonly StaticSymbol Empty = new(Tokens.Empty, SymbolKindExt.Constant, VBEmptyType.TypeInfo);
        public static readonly StaticSymbol Nothing = new(Tokens.Nothing, SymbolKindExt.Constant, VBObjectType.TypeInfo);
        public static readonly StaticSymbol Null = new(Tokens.Null, SymbolKindExt.Constant, VBNullType.TypeInfo);
        public static readonly StaticSymbol VBNullString = new(Tokens.vbNullString, SymbolKindExt.Constant, VBStringType.TypeInfo);
        public static readonly StaticSymbol VBEmptyString = new("String.Empty", SymbolKindExt.Constant, VBStringType.TypeInfo);

        public static readonly StaticSymbol VBVoid = new("(void)", SymbolKindExt.Ignored, VBVoidType.TypeInfo);
        public static readonly StaticSymbol VBUnknown = new("Unkown", SymbolKindExt.Ignored, VBVoidType.TypeInfo);
        public static readonly StaticSymbol TypeDesc = new("TypeDesc", SymbolKindExt.TypeDescriptor, VBTypeDesc.TypeInfo);
        public static readonly StaticSymbol MissingValue = new("Missing", SymbolKindExt.Variable, VBMissingType.TypeInfo);

        public static readonly StaticSymbol EmptyFixedSizeArray = new("Array.Empty", SymbolKindExt.Array, VBResizableArrayType.TypeInfo);
        public static readonly StaticSymbol EmptyResizableArray = new("Array().Empty", SymbolKindExt.Array, VBResizableArrayType.TypeInfo);
        public static readonly StaticSymbol EmptyResizableByteArray = new("Array().Byte", SymbolKindExt.Array, VBResizableByteArrayType.TypeInfo);

        public static readonly StaticSymbol VBAStdLib = new("VBA", SymbolKindExt.Project, VBLibraryProjectType.TypeInfo);
    }

    /// <summary>
    /// Defines a number of static (unallocated) symbols used internally for semantic purposes.
    /// </summary>
    public static class ExtensionSymbols
    {
        public static readonly StaticSymbol VBByteZeroValue = new("Byte.0", SymbolKindExt.Constant, VBByteType.TypeInfo);
        public static readonly StaticSymbol VBByteMinValue = new("Byte.Min", SymbolKindExt.Constant, VBByteType.TypeInfo);
        public static readonly StaticSymbol VBByteMaxValue = new("Byte.Max", SymbolKindExt.Constant, VBByteType.TypeInfo);

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
        public static readonly UnaryOperatorSymbol UnaryNegation = Operators.OfType<UnaryNegationOperatorSymbol>().Single();
        public static readonly UnaryOperatorSymbol UnaryPlus = Operators.OfType<UnaryPlusOperatorSymbol>().Single();
        public static readonly UnaryOperatorSymbol BitwiseNot = Operators.OfType<BitwiseNotOperatorSymbol>().Single();
        //public static readonly UnaryOperatorSymbol Parentheses = Operators.OfType<ParenthesizedExpressionOperatorSymbol>().Single();

        public static readonly BinaryOperatorSymbol Addition = Operators.OfType<AdditionOperatorSymbol>().Single();
        public static readonly BinaryOperatorSymbol Subtraction = Operators.OfType<SubtractionOperatorSymbol>().Single();
        public static readonly BinaryOperatorSymbol Multiplication = Operators.OfType<MultiplicationOperatorSymbol>().Single();
        public static readonly BinaryOperatorSymbol Division = Operators.OfType<DivisionOperatorSymbol>().Single();
        public static readonly BinaryOperatorSymbol IntegerDivision = Operators.OfType<IntegerDivisionOperatorSymbol>().Single();
        public static readonly BinaryOperatorSymbol Exponentiation = Operators.OfType<ExponentiationOperatorSymbol>().Single();
        public static readonly BinaryOperatorSymbol Modulo = Operators.OfType<ModuloOperatorSymbol>().Single();
        public static readonly BinaryOperatorSymbol BitwiseAnd = Operators.OfType<BitwiseAndOperatorSymbol>().Single();
        public static readonly BinaryOperatorSymbol BitwiseOr = Operators.OfType<BitwiseOrOperatorSymbol>().Single();
        public static readonly BinaryOperatorSymbol BitwiseXOr = Operators.OfType<BitwiseXOrOperatorSymbol>().Single();
        public static readonly BinaryOperatorSymbol BitwiseImp = Operators.OfType<BitwiseImpOperatorSymbol>().Single();
        public static readonly BinaryOperatorSymbol BitwiseEqv = Operators.OfType<BitwiseEqvOperatorSymbol>().Single();
        public static readonly BinaryOperatorSymbol Equality = Operators.OfType<EqualityOperatorSymbol>().Single();
        public static readonly BinaryOperatorSymbol Inequality = Operators.OfType<InequalityOperatorSymbol>().Single();
        public static readonly BinaryOperatorSymbol LessThan = Operators.OfType<LessThanOperatorSymbol>().Single();
        public static readonly BinaryOperatorSymbol GreaterThan = Operators.OfType<GreaterThanOperatorSymbol>().Single();
        public static readonly BinaryOperatorSymbol LessThanOrEqual = Operators.OfType<LessThanOrEqualOperatorSymbol>().Single();
        public static readonly BinaryOperatorSymbol GreaterThanOrEqual = Operators.OfType<GreaterThanOrEqualOperatorSymbol>().Single();
        public static readonly BinaryOperatorSymbol Like = Operators.OfType<LikeOperatorSymbol>().Single();

        public static readonly BinaryOperatorSymbol IsRefEquals = Operators.OfType<IsRefEqOperatorSymbol>().Single();
        //public static readonly BinaryOperatorSymbol MemberAccess = Operators.OfType<MemberAccessOperatorSymbol>().Single();
        public static readonly BinaryOperatorSymbol Concat = Operators.OfType<ConcatOperatorSymbol>().Single();
    }

    public static readonly ImmutableArray<OperatorSymbol> Operators =
    [
        new AdditionOperatorSymbol(),
        new SubtractionOperatorSymbol(),
        new MultiplicationOperatorSymbol(),
        new DivisionOperatorSymbol(),
        new IntegerDivisionOperatorSymbol(),
        new ExponentiationOperatorSymbol(),
        new ModuloOperatorSymbol(),
        new UnaryNegationOperatorSymbol(),
        //new UnaryPlusOperatorSymbol(),
        new BitwiseNotOperatorSymbol(),
        new BitwiseAndOperatorSymbol(),
        new BitwiseOrOperatorSymbol(),
        new BitwiseXOrOperatorSymbol(),
        new BitwiseImpOperatorSymbol(),
        new BitwiseEqvOperatorSymbol(),
        new EqualityOperatorSymbol(),
        new InequalityOperatorSymbol(),
        new LessThanOperatorSymbol(),
        new GreaterThanOperatorSymbol(),
        new LessThanOrEqualOperatorSymbol(),
        new GreaterThanOrEqualOperatorSymbol(),
        new IsRefEqOperatorSymbol(),
        new LikeOperatorSymbol(),
        new ConcatOperatorSymbol(),

        //new MemberAccessOperatorSymbol(),
    ];


    public static void Initialize(ConcurrentDictionary<Uri, Symbol> index)
    {
        if (!index.IsEmpty)
        {
            throw new InvalidOperationException("The specified index dictionary is already initialized; index is expected to be empty.");
        }

        foreach (var op in Operators)
        {
            index[op.Uri] = op;
        }
    }
}