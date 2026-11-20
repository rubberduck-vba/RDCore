# 5.3.1.1 Procedure Scope

This section corresponds to [**MS-VBAL §5.3.1.1** Procedure Scope](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/d1bd9698-ff9d-4945-9518-ad6009e5544d).

## Static Semantics

|Access level|Declared in|Accessible from|
|---|---|---|
|`Public`, or no modifier|Any module|Every module of the project, and the projects that reference it where the module is accessible to them ([**RD-VBAL §2.2.3.2**](rd-vbal.2.2.3.projectfile.md#references-to-projects)).|
|`Private`|Any module|Its own module.|
|`Friend`|A class module only|Every module of the class's own project. Code of a referencing project does not see the member: completion does not offer it, and an early-bound access of it is refused as an access of a `Private` member is.|

A `Friend` procedure of a standard module is [VBC09384](../diagnostics/vbc09384.md) _Only valid in object module_.

A property whose `Property Get` is `Public` and whose `Property Let` or `Property Set` is `Friend` is read-only to a referencing project, as it is with a
`Private` `Property Let` or `Property Set`. Its code reads the property, and an assignment of it is refused as the
[**RD-VBAL §5.4.3.8** Let Statement](rd-vbal.5.4.3.8.let-statement.md) and [**RD-VBAL §5.4.3.9** Set Statement](rd-vbal.5.4.3.9.set-statement.md)
refuse an assignment of a read-only property.

## Runtime Semantics

A late-bound access (the object is an `Object` or a `Variant`), and `CallByName`, reach the members that an early-bound access would reach from the
same code ([**RD-VBAL §5.6.3** Member Resolution](rd-vbal.5.6.3.member-resolution.md)):

|Code of|Late-bound access of a `Friend` member|
|---|---|
|The class's own project|Runs.|
|A referencing project|Run-time error 438, as for a name the class does not declare. An assignment of a property whose `Property Get` is `Public` raises error 451 for a `Let` and 450 for a `Set`, as for a read-only property.|

## Implementation

|Type or member|Role|
|---|---|
|`ProjectVisibility`|Which members of a class the code of a project sees, for completion, early binding and late binding.|
|`ReferencedProjectSymbols`|The symbols a referenced project gives the projects that reference it, without its `Friend` members.|
|`PublicMemberSemantics`|Raises VBC09384.|
