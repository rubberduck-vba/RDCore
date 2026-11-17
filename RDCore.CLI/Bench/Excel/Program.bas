Attribute VB_Name = "Program"
Option Explicit

' <Main>
Public Sub Main()
    Dim Xl As Object
    Set Xl = CreateObject("Excel.Application")
    Xl.Visible = False
    Xl.DisplayAlerts = False
    Debug.Print RunExcelCases(Xl, CLng(Command$))
    Xl.Quit
End Sub
' </Main>
