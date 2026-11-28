namespace RDCore.Runtime.Execution.Hosting.Json;

// what a JSON object given to an object of a class of the workspace raises, where the default ignores: a key that names no member the object can give a
// value to (438; 450 for a method, or a property that takes arguments besides its value), a member no key names (449), and a key that names a read-only
// property (451). The default raises none of them, and is what a program invoked by name is given its arguments by.
internal readonly record struct JsonMemberRules(bool RaiseUnknown, bool RaiseMissing, bool RaiseReadOnly);
