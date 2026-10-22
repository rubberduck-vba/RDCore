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