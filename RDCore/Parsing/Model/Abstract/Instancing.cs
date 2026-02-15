namespace RDCore.Parsing.Model.Abstract;

internal enum Instancing
{
    Private = 1,                    //Not exposed via COM.
    PublicNotCreatable = 2,         //TYPEFLAG_FCANCREATE not set.
    SingleUse = 3,                  //TYPEFLAGS.TYPEFLAG_FAPPOBJECT
    GlobalSingleUse = 4,
    MultiUse = 5,
    GlobalMultiUse = 6
}
