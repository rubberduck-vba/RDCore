using RDCore.Runtime.Execution.Memory;
using RDCore.SDK.Runtime.Shared;

namespace RDCore.Tests;

[TestClass]
public class AddressMapTests
{
    private static AddressMap<string> MapAt(params int[] addresses)
    {
        var map = new AddressMap<string>();
        foreach (var address in addresses)
        {
            map[new MemoryAddress(address)] = $"at {address}";
        }

        return map;
    }

    [TestMethod]
    [DataRow(new int[0], 0, 100, "", DisplayName = "nothing held")]
    [DataRow(new[] { 8, 16, 24 }, 16, 8, "16", DisplayName = "the first byte is in the range, the byte after it is not")]
    [DataRow(new[] { 8, 16, 24 }, 9, 7, "", DisplayName = "between two")]
    [DataRow(new[] { 24, 8, 16 }, 0, 100, "8,16,24", DisplayName = "all of them, in address order")]
    [DataRow(new[] { 8, 16, 24 }, 25, 100, "", DisplayName = "after the last")]
    [DataRow(new[] { 8, 16, 24 }, 0, 8, "", DisplayName = "before the first")]
    [DataRow(new[] { 8, 16, 24 }, 8, 0, "", DisplayName = "an empty range")]
    [DataRow(new[] { 8, int.MaxValue }, 8, int.MaxValue, "8,2147483647", DisplayName = "a range past the last address")]
    public void In_IsWhatIsHeldInTheRange(int[] held, int start, int size, string expected)
    {
        var map = MapAt(held);

        Assert.AreEqual(expected, string.Join(",", map.In(new MemoryAddress(start), size).Select(address => address.Value)));
    }

    [TestMethod]
    public void In_RemovingWhatWasFound_LeavesTheRest()
    {
        var map = MapAt(8, 16, 24);

        foreach (var address in map.In(new MemoryAddress(8), 9))
        {
            Assert.IsTrue(map.Remove(address));
        }

        Assert.AreEqual(1, map.Count);
        Assert.AreEqual("at 24", map.GetValueOrDefault(new MemoryAddress(24)));
        Assert.AreEqual("", string.Join(",", map.In(new MemoryAddress(0), 24).Select(address => address.Value)));
    }

    [TestMethod]
    public void Set_AnAddressHeldAgain_IsOneEntry()
    {
        var map = MapAt(8, 8);

        Assert.AreEqual(1, map.Count);
        Assert.AreEqual(1, map.In(new MemoryAddress(0), 16).Length);
        Assert.IsTrue(map.Remove(new MemoryAddress(8)));
        Assert.IsFalse(map.Remove(new MemoryAddress(8)));
        Assert.IsEmpty(map.In(new MemoryAddress(0), 16));
    }
}
