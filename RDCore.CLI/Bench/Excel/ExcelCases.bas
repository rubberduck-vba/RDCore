Attribute VB_Name = "ExcelCases"
Option Explicit
'  Excel driven through its object model, late bound: a block of cells read into a Variant at once, worked on, and written to another sheet at once;
'  then the same done cell by cell. Every run in microseconds, each phase on its own: "case|phase|us;us;...".
' <Declarations>
Private Declare PtrSafe Function QueryPerformanceCounter Lib "kernel32" (ByRef c As Currency) As Long
Private Declare PtrSafe Function QueryPerformanceFrequency Lib "kernel32" (ByRef f As Currency) As Long

Private Freq As Currency
' </Declarations>

' <Since>
Private Function Since(ByVal T0 As Currency) As Long
    Dim T1 As Currency
    QueryPerformanceCounter T1
    Since = CLng((T1 - T0) / Freq * 1000000#)
End Function
' </Since>

' <Fill>
'  numbers, and a blank where (row + column) Mod 7 = 0.
Private Sub Fill(ByVal Sheet As Object, ByVal Rows As Long, ByVal Cols As Long)
    Dim Data() As Variant, R As Long, C As Long
    ReDim Data(1 To Rows, 1 To Cols)
    For R = 1 To Rows
        For C = 1 To Cols
            If (R + C) Mod 7 <> 0 Then Data(R, C) = R * Cols + C
        Next
    Next
    Sheet.Range(Sheet.Cells(1, 1), Sheet.Cells(Rows, Cols)).Value = Data
End Sub
' </Fill>

' <Block>
Private Function Block(ByVal Source As Object, ByVal Target As Object, ByVal Rows As Long, ByVal Cols As Long, ByVal Runs As Long) As String
    Dim V As Variant, R As Long, C As Long, Run As Long, T0 As Currency, ReadUs As String, PassUs As String, WriteUs As String, TotalUs As String
    Dim Start As Currency, Label As String
    For Run = 1 To Runs
        Target.Cells.ClearContents
        QueryPerformanceCounter Start
        QueryPerformanceCounter T0
        V = Source.Range(Source.Cells(1, 1), Source.Cells(Rows, Cols)).Value
        ReadUs = ReadUs & CStr(Since(T0)) & ";"
        QueryPerformanceCounter T0
        For R = 1 To Rows
            For C = 1 To Cols
                If Not IsEmpty(V(R, C)) Then V(R, C) = V(R, C) * 2
            Next
        Next
        PassUs = PassUs & CStr(Since(T0)) & ";"
        QueryPerformanceCounter T0
        Target.Range(Target.Cells(1, 1), Target.Cells(Rows, Cols)).Value = V
        WriteUs = WriteUs & CStr(Since(T0)) & ";"
        TotalUs = TotalUs & CStr(Since(Start)) & ";"
    Next
    Label = "Excel.Block" & Rows & "x" & Cols
    Block = Label & "|read|" & ReadUs & vbLf & Label & "|pass|" & PassUs & vbLf & Label & "|write|" & WriteUs & vbLf & Label & "|total|" & TotalUs & vbLf
End Function
' </Block>

' <CellByCell>
Private Function CellByCell(ByVal Source As Object, ByVal Target As Object, ByVal Rows As Long, ByVal Cols As Long, ByVal Runs As Long) As String
    Dim X As Variant, R As Long, C As Long, Run As Long, Start As Currency, TotalUs As String
    For Run = 1 To Runs
        Target.Cells.ClearContents
        QueryPerformanceCounter Start
        For R = 1 To Rows
            For C = 1 To Cols
                X = Source.Cells(R, C).Value
                If Not IsEmpty(X) Then X = X * 2
                Target.Cells(R, C).Value = X
            Next
        Next
        TotalUs = TotalUs & CStr(Since(Start)) & ";"
    Next
    CellByCell = "Excel.CellByCell" & Rows & "x" & Cols & "|total|" & TotalUs & vbLf
End Function
' </CellByCell>

' <RunExcelCases>
Public Function RunExcelCases(ByVal Xl As Object, ByVal Runs As Long) As String
    Dim Book As Object, Source As Object, Target As Object, Result As String
    QueryPerformanceFrequency Freq
    Xl.ScreenUpdating = False
    Xl.EnableEvents = False
    Set Book = Xl.Workbooks.Add
    Xl.Calculation = -4135
    Set Source = Book.Worksheets(1)
    Set Target = Book.Worksheets.Add
    Fill Source, 100000, 10
    Result = Result & Block(Source, Target, 1000, 10, Runs)
    Result = Result & CellByCell(Source, Target, 1000, 10, Runs)
    Result = Result & Block(Source, Target, 100000, 10, Runs)
    Book.Close False
    RunExcelCases = Result
End Function
' </RunExcelCases>
