using RDCore.SDK.Runtime.Shared;
namespace RDCore.Runtime.Execution.Memory;

internal class SessionMemoryBlockSizeComparer : IComparer<SessionMemoryBlock>
{
    public int Compare(SessionMemoryBlock x, SessionMemoryBlock y) => x.Size.CompareTo(y.Size);
}
