Attribute VB_Name = "Calls"
Option Explicit
' Calls: to a procedure of the workspace, to the members of an object, and to the standard library.

Private Function Add(ByVal A As Long, ByVal B As Long) As Long
    Add = A + B
End Function

Public Sub BenchProcedureCalls()
    Dim I As Long
    Dim S As Long
    For I = 1 To 100000
        S = Add(I, 1)
    Next
End Sub

Private Sub Increment(ByRef N As Long)
    N = N + 1
End Sub

' a variable of the caller passed by reference, and assigned through the parameter.
Public Sub BenchByRefCalls()
    Dim I As Long
    Dim S As Long
    For I = 1 To 100000
        Increment S
    Next
End Sub

Public Sub BenchClassMemberCalls()
    Dim C As Counter
    Set C = New Counter
    Dim I As Long
    For I = 1 To 100000
        C.Increment
    Next
    Dim Total As Long
    Total = C.Value
End Sub

Public Sub BenchInterfaceMemberCalls()
    Dim C As ICounter
    Set C = New InterfaceCounter
    Dim I As Long
    For I = 1 To 100000
        C.Increment
    Next
    Dim Total As Long
    Total = C.Value
End Sub

Public Sub BenchStandardLibraryCalls()
    Dim I As Long
    Dim N As Long
    For I = 1 To 100000
        N = Len(CStr(I)) + CLng(I)
    Next
End Sub

' a factory method off a class's default instance: a new object on every call, given its state by the factory.
Public Sub BenchFactoryCalls()
    Dim G As Gadget
    Dim I As Long
    For I = 1 To 100000
        Set G = Gadget.Create(I)
    Next
End Sub

' a member called on the default instance itself.
Public Sub BenchDefaultInstanceCalls()
    Dim I As Long
    Dim S As Long
    For I = 1 To 100000
        S = Gadget.Twice(I)
    Next
End Sub

' a member that calls a private member of its own class by its bare name, which is a call on the current instance.
Public Sub BenchSiblingCalls()
    Dim G As Gadget
    Set G = Gadget.Create(3)
    Dim I As Long
    Dim S As Long
    For I = 1 To 100000
        S = G.Scaled(2)
    Next
End Sub