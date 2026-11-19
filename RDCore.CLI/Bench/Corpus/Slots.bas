Attribute VB_Name = "Slots"
Option Explicit
' What stage 1 of the IR lowering plan moves into frame slots: an activation's parameters and locals, a module's variables, and the
' payloads of Currency and Boolean values.

' <Declarations>
Private Const Iterations As Long = 100000

Private Total As Long
' </Declarations>

' <BenchWideActivations>
' six typed parameters and four locals, for each of which an activation reserves storage.
Private Function Mix(ByVal A As Long, ByVal B As Long, ByVal C As Double, ByVal D As Integer, ByVal E As Boolean, ByVal F As Currency) As Long
    Dim W As Long
    Dim X As Double
    Dim Y As Integer
    Dim Z As Currency
    W = A + B
    X = C
    Y = D
    Z = F
    Mix = W
End Function

Public Sub BenchWideActivations()
    Dim I As Long
    Dim S As Long
    For I = 1 To Iterations
        S = Mix(I, 1, 2.5, 3, True, 4)
    Next
End Sub
' </BenchWideActivations>

' <BenchModuleVariableLoop>
' a variable of the module, read and written each time round.
Public Sub BenchModuleVariableLoop()
    Dim I As Long
    Total = 0
    For I = 1 To Iterations
        Total = Total + 1
    Next
End Sub
' </BenchModuleVariableLoop>

' <BenchLocalVariableLoop>
' the loop of BenchModuleVariableLoop over a local: what a module variable is measured against.
Public Sub BenchLocalVariableLoop()
    Dim I As Long
    Dim Local As Long
    For I = 1 To Iterations
        Local = Local + 1
    Next
End Sub
' </BenchLocalVariableLoop>

' <BenchStaticVariableLoop>
' the same loop over a Static local, which lives as long as the module does.
Public Sub BenchStaticVariableLoop()
    Dim I As Long
    Static Kept As Long
    Kept = 0
    For I = 1 To Iterations
        Kept = Kept + 1
    Next
End Sub
' </BenchStaticVariableLoop>

' <BenchCurrencySum>
' a Currency sum, whose payload is a scaled integer.
Public Sub BenchCurrencySum()
    Dim I As Long
    Dim S As Currency
    For I = 1 To Iterations
        S = S + 1.25@
    Next
End Sub
' </BenchCurrencySum>

' <BenchBooleanFlag>
' a Boolean flag flipped and tested each time round.
Public Sub BenchBooleanFlag()
    Dim I As Long
    Dim B As Boolean
    Dim N As Long
    For I = 1 To Iterations
        B = Not B
        If B Then N = N + 1
    Next
End Sub
' </BenchBooleanFlag>
