VERSION 1.0 CLASS
BEGIN
  MultiUse = -1  'True
END
Attribute VB_Name = "Gadget"
Attribute VB_PredeclaredId = True
Option Explicit
' A factory method off a stateless default instance, the pattern object-oriented VBA is built on: the object it makes is given its state through a Friend
' setter that only the factory uses, and a member guards against being called on the default instance by calling a private member of the class.

Private Type TGadget
    Size As Long
End Type

Private This As TGadget

Public Function Create(ByVal Size As Long) As Gadget
    Dim Result As Gadget
    Set Result = New Gadget
    Result.Size = Size
    Set Create = Result
End Function

Public Property Get Size() As Long
    Size = This.Size
End Property

Friend Property Let Size(ByVal Value As Long)
    This.Size = Value
End Property

Public Function Scaled(ByVal Factor As Long) As Long
    GuardInstance
    Scaled = This.Size * Factor
End Function

Public Function Twice(ByVal N As Long) As Long
    Twice = 2 * N
End Function

Private Sub GuardInstance()
    If Me Is Gadget Then Err.Raise 5
End Sub
