'Analysis / design back end of the optimizer. The optimizer (ETABS_Class: repair steps, constraints, penalty) works on the results of an
'evaluator: ETABS (reference, also the final check) or the own FEM solver (FemCore, no ETABS during the optimization).

'Steel design result of one design group: the governing D/C ratio of its members and the AISC 341 checks
Public Class GroupDesign
    Public MaxRatio As Double       'largest governing ratio (PMM or shear) of the members, as reported by the design code (before the D/C limit)
    Public Items As Integer         'members of the group with a result
    Public ErrorCount As Integer    'members with a design error (not seismically compact, Lb/ry, ...)
    Public BCRatio As Double        'AISC 341 strong column - weak beam: largest beam/column capacity ratio (0 = not available)
End Class

'Horizontal displacement of a joint in one output case / combination step (model length unit)
Public Structure DispRow
    Public Point As String
    Public CaseName As String
    Public U1 As Double
    Public U2 As Double
End Structure

Public Interface IStructureEvaluator
    ReadOnly Property Name As String
    'Sections of the design variables (indices of the section catalog) to the structure
    Function AssignSections(ByVal Sect_Ind() As Integer) As Integer
    'Analysis of the structure; a design whose analysis cannot be completed sets AnalysisFailed of the owner (not an error)
    Function Analyze() As Integer
    'Joint displacements of the drift cases / combinations
    Function ReadDisplacements(ByVal Rows As List(Of DispRow)) As Integer
    'Steel design of the groups (key: group name)
    Function ReadSteelDesign(ByVal Result As Dictionary(Of String, GroupDesign)) As Integer
End Interface
