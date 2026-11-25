using RDCore.External.Automation;
using RDCore.Runtime.Execution.External.Automation;

namespace RDCore.Tests.Runtime.Execution;

/// <summary>
/// The classes and members of late-bound objects, which a program names at run time: as many are kept as the bound allows, and a name past it still
/// reaches its class or its member, made for the call.
/// </summary>
[TestClass]
public sealed class LateBindingTests
{
    // CallByName gives a member's name as data: past the bound, a member is made for each call, and the process grows no further.
    [TestMethod]
    public void AMemberPastTheBound_IsMadeForEachCall_AndIsTheMemberItsNameReaches()
    {
        var classModule = LateBinding.ClassNamed("Scripting.Dictionary");
        for (var index = 0; index < LateBinding.Capacity; index++)
        {
            _ = LateBinding.MemberOf(classModule, $"Filler{index}x{Guid.NewGuid():N}", AutomationInvocation.Get);
        }

        var name = $"Past{Guid.NewGuid():N}";
        var first = LateBinding.MemberOf(classModule, name, AutomationInvocation.Get);
        var second = LateBinding.MemberOf(classModule, name, AutomationInvocation.Get);

        Assert.AreNotSame(first, second, "a name past the bound is not kept");
        Assert.AreEqual(name, second.Name);
        Assert.IsTrue(LateBinding.IsLateBoundMember(second));
    }

    // an object held with no class of its own is known by its class being Object's: that class is the one, however many class names servers give.
    [TestMethod]
    public void TheObjectClass_IsTheOne_PastTheBound()
    {
        for (var index = 0; index < LateBinding.Capacity; index++)
        {
            _ = LateBinding.ClassNamed($"Server.Filler{index}x{Guid.NewGuid():N}");
        }

        var held = LateBinding.ClassNamed(null);
        var past = $"Server.Past{Guid.NewGuid():N}";

        Assert.AreSame(held, LateBinding.ClassNamed(null));
        Assert.AreSame(held, LateBinding.ClassNamed("Object"));
        Assert.AreNotSame(LateBinding.ClassNamed(past), LateBinding.ClassNamed(past), "a class name past the bound is not kept");
        Assert.IsTrue(LateBinding.IsLateBound(LateBinding.ClassNamed(past)));
    }
}
