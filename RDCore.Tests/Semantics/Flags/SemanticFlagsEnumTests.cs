using RDCore.SDK.Semantics.Flags;
using System.Reflection;

namespace RDCore.Tests.Semantics.Flags;

/// <summary>
/// The shape every semantic flags enum must have for its values to mean anything: it is a <c>[Flags]</c> enum, each member
/// is its own bit (a zero-valued member can never be set, and a shared bit is two facts that cannot be told apart), and its
/// <c>All</c> member, where it has one, really is all of them. These went wrong more than once, silently.
/// </summary>
[TestClass]
public sealed class SemanticFlagsEnumTests
{
    private static readonly Type[] FlagsEnums =
    [
        .. typeof(ConversionSemanticFlags).Assembly.GetTypes()
            .Where(type => type.IsEnum && type.Name.EndsWith("Flags", StringComparison.Ordinal) && type.Namespace!.StartsWith("RDCore.SDK.Semantics", StringComparison.Ordinal))
            .OrderBy(type => type.Name),
    ];

    public static IEnumerable<object[]> Enums() => FlagsEnums.Select(type => new object[] { type });

    // an enum member that is a named combination of others (All, or a documented alias) is not a fact of its own.
    private static bool IsCombination(long value) => value != 0 && (value & (value - 1)) != 0;

    private static IEnumerable<(string Name, long Value)> Members(Type type)
        => Enum.GetNames(type).Select(name => (name, Convert.ToInt64(Enum.Parse(type, name))));

    [TestMethod]
    public void TheFlagsEnums_AreFound()
        => Assert.IsGreaterThan(8, FlagsEnums.Length, "the reflection over the SDK found (almost) nothing");

    [TestMethod]
    [DynamicData(nameof(Enums))]
    public void IsAFlagsEnum(Type type)
        => Assert.IsTrue(type.GetCustomAttribute<FlagsAttribute>() is not null, $"{type.Name} is not marked [Flags]");

    [TestMethod]
    [DynamicData(nameof(Enums))]
    public void NoMemberButNoneIsZero_ForAZeroFlagCanNeverBeSet(Type type)
    {
        var zeros = Members(type).Where(member => member.Value == 0 && member.Name != "None").Select(member => member.Name).ToArray();

        Assert.IsEmpty(zeros, $"{type.Name}: {string.Join(", ", zeros)} is 0");
    }

    [TestMethod]
    [DynamicData(nameof(Enums))]
    public void EachSingleBitMember_HasABitOfItsOwn(Type type)
    {
        var shared = Members(type)
            .Where(member => member.Value != 0 && !IsCombination(member.Value))
            .GroupBy(member => member.Value)
            .Where(group => group.Count() > 1)
            .Select(group => string.Join(" = ", group.Select(member => member.Name)))
            .ToArray();

        Assert.IsEmpty(shared, $"{type.Name}: {string.Join("; ", shared)}");
    }

    [TestMethod]
    [DynamicData(nameof(Enums))]
    public void All_WhereThereIsOne_IsEveryOtherMember(Type type)
    {
        var members = Members(type).ToArray();
        if (members.All(member => member.Name != "All"))
        {
            return;
        }

        var everything = members.Where(member => member.Name != "All").Aggregate(0L, (all, member) => all | member.Value);

        Assert.AreEqual(everything, members.Single(member => member.Name == "All").Value, $"{type.Name}.All");
    }
}
