namespace RDCore.Runtime.Execution.Memory;

/// <summary>
/// What a copy of a record as bytes (<see cref="VBUserDefinedTypeImage.Copy"/>, <c>LSet</c>, <strong>MS-VBAL §5.4.3.6</strong>) did: copied it, or wrote
/// nothing because an object field could not be given what the bytes say.
/// </summary>
public enum VBRecordCopyOutcome
{
    /// <summary>
    /// The record was copied.
    /// </summary>
    Copied,

    /// <summary>
    /// The bytes an object field is read from are an address no object of the session has: <c>Object required</c> (424). Nothing was written.
    /// </summary>
    NoObjectAtAddress,

    /// <summary>
    /// An object field of a class type would be given an object of a class that is not that class and does not implement it, as <c>Set</c> would refuse:
    /// <c>Type mismatch</c> (13). Nothing was written.
    /// </summary>
    ObjectOfAnotherClass,
}
