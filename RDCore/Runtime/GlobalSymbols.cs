using RDCore.Parsing.Model;
using RDCore.Parsing.Model.Expressions.Operators;
using RDCore.Parsing.Model.Symbols;
using RDCore.Parsing.Model.Types;
using RDCore.Server.ProtocolExtensions;
using System.Collections.Concurrent;
using System.Collections.Immutable;

namespace RDCore.Runtime;

internal static class GlobalSymbols
{
    // constants
    public static readonly StaticSymbol Empty = new(Tokens.Empty, SymbolKindExt.Constant, VBEmptyType.TypeInfo);
    public static readonly StaticSymbol Nothing = new(Tokens.Nothing, SymbolKindExt.Constant, VBObjectType.TypeInfo);
    public static readonly StaticSymbol Null = new(Tokens.Null, SymbolKindExt.Constant, VBNullType.TypeInfo);

    public static readonly StaticSymbol True = new(Tokens.True, SymbolKindExt.Constant, VBBooleanType.TypeInfo);
    public static readonly StaticSymbol False = new(Tokens.False, SymbolKindExt.Constant, VBBooleanType.TypeInfo);

    public static readonly ImmutableArray<OperatorSymbol> Operators =
    [
        new AdditionOperatorSymbol(),
        new SubtractionOperatorSymbol(),
        new MultiplicationOperatorSymbol(),
        new DivisionOperatorSymbol(),
        new IntegerDivisionOperatorSymbol(),
        new ExponentiationOperatorSymbol(),
        new ModuloOperatorSymbol(),
        //new ParenthesizedExpressionOperatorSymbol(),
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

    //public static readonly UnaryOperatorSymbol Parentheses = Operators.OfType<ParenthesizedExpressionOperatorSymbol>().Single();
    public static readonly UnaryOperatorSymbol UnaryNegation = Operators.OfType<UnaryNegationOperatorSymbol>().Single();
    //public static readonly UnaryOperatorSymbol UnaryPlus = Operators.OfType<UnaryPlusOperatorSymbol>().Single();
    public static readonly UnaryOperatorSymbol BitwiseNot = Operators.OfType<BitwiseNotOperatorSymbol>().Single();

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