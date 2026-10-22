Attribute VB_Name = "Statements"
Option Explicit
' The statements that are not Let assignments of a variable: what each evaluates its target, its selector or its items with.

Private Type Pair
    X As Long
    Y As Long
End Type

Public Sub BenchSetAssignment()
    Dim C As Counter
    Set C = New Counter
    Dim D As Counter
    Dim I As Long
    For I = 1 To 100000
        Set D = C
    Next
End Sub

Public Sub BenchMemberAssignment()
    Dim P As Point
    Set P = New Point
    Dim I As Long
    For I = 1 To 100000
        P.X = I
    Next
End Sub

Public Sub BenchRecordFieldAssignment()
    Dim R As Pair
    Dim I As Long
    For I = 1 To 100000
        R.X = I
    Next
End Sub

Public Sub BenchSelectCase()
    Dim I As Long
    Dim S As Long
    For I = 1 To 100000
        Select Case I Mod 4
            Case 0
                S = S + 1
            Case 1, 2
                S = S + 2
            Case Else
                S = S + 3
        End Select
    Next
End Sub

Public Sub BenchMidStatement()
    Dim S As String
    S = "RD-VBA"
    Dim I As Long
    For I = 1 To 100000
        Mid$(S, 1, 1) = "r"
    Next
End Sub
