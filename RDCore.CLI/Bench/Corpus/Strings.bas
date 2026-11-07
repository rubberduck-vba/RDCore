Attribute VB_Name = "Strings"
Option Explicit
' String assignment and concatenation.

Public Sub BenchStringAssignment()
    Dim I As Long
    Dim S As String
    For I = 1 To 100000
        S = "RD-VBA"
    Next
End Sub

' each concatenation copies the string so far: the cost grows with its length, as it does in MS-VBA.
Public Sub BenchStringConcatenation()
    Dim I As Long
    Dim S As String
    For I = 1 To 10000
        S = S & "x"
    Next
End Sub

' short Strings joined and compared: what parsing and formatting code does.
Public Sub BenchStringComparison()
    Dim I As Long
    Dim N As Long
    Dim S As String
    For I = 1 To 100000
        S = "RD" & "-VBA"
        If S = "RD-VBA" Then N = N + 1
    Next
End Sub

' a line built of numbers written as text, a separator between each: what a report or an export does, a conversion and two concatenations a value.
Public Sub BenchStringBuilding()
    Dim I As Long
    Dim S As String
    For I = 1 To 5000
        S = S & CStr(I) & ";"
    Next
End Sub

' Strings given to a procedure, by value and by reference: what a helper that checks or edits text is given, a call a pass.
Public Sub BenchStringArguments()
    Dim I As Long
    Dim M As Long
    Dim N As Long
    Dim S As String
    Dim T As String
    S = "RD-VBA"
    For I = 1 To 100000
        M = Matched(S, "RD-VBA")
        N = N + M
        T = "RD"
        Suffixed T
    Next
End Sub

Private Function Matched(ByVal S As String, ByVal Expected As String) As Long
    If S = Expected Then Matched = 1
End Function

Private Sub Suffixed(ByRef T As String)
    T = T & "-VBA"
End Sub
