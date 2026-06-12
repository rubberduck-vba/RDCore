namespace RDCore.SDK.Model.Types.Abstract;

/// <summary>
/// Represents any data type mentioned in the <strong>MS-VBAL</strong> language specifications, regardless of semantics.
/// </summary>
/// <param name="Name">The name (token) of the data type.</param>
/// <param name="ManagedType">The underlying managed data type representation.</param>
/// <remarks>
/// This class is to simplify pattern matching by removing generic type parameters; derived types implement a more specialized generic class.
/// </remarks>
public abstract record class VBIntrinsicType(string Name, Type ManagedType) : VBType(ManagedType, Name) { } // TODO delete if it's not used anywhere

/// <summary>
/// Represents any data type mentioned in the <strong>MS-VBAL</strong> language specifications, regardless of semantics.
/// </summary>
/// <typeparam name="T">The managed type (internal representation) associated with this data type.</typeparam>
/// <param name="Name">The name (token) of the data type.</param>
public abstract record class VBIntrinsicType<T>(string Name) : VBIntrinsicType(Name, typeof(T)) { }
