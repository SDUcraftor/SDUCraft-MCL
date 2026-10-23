Imports System
Imports System.Collections.Generic
Imports System.IO

Module IsolationTests
    Sub Main()
        Dim Fixture = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "fixtures")
        Dim SharedRoot = Path.Combine(Fixture, "minecraft") & Path.DirectorySeparatorChar
        Dim InstanceA As New McInstance(Path.Combine(SharedRoot, "versions", "Pack-A") & Path.DirectorySeparatorChar)
        Dim InstanceB As New McInstance(Path.Combine(SharedRoot, "versions", "Pack-B") & Path.DirectorySeparatorChar)
        Dim Cases As Integer = 0
        For Each GlobalMode In New Integer() {0, 1, 2, 3, 4}
            Settings.Reset()
            Settings.Values("LaunchArgumentIndieV2") = GlobalMode
            McFolderSelected = SharedRoot
            Settings.Values(InstanceA.PathVersion & "VersionArgumentIndieV2") = False
            Settings.Values(InstanceA.PathVersion & "VersionArgumentIndie") = 2
            Assert(InstanceA.PathIndie = InstanceA.PathVersion, "Global/instance opt-out changed active directory")
            Assert(Settings.Get(Of Boolean)("VersionArgumentIndieV2", InstanceA), "Legacy instance flag was not enabled")
            Assert(Settings.Get(Of Integer)("VersionArgumentIndie", InstanceA) = 1, "Legacy compatibility flag was not enabled")
            Dim Writes = Settings.Writes
            Assert(InstanceA.PathIndie = InstanceA.PathVersion AndAlso Settings.Writes = Writes, "Repeated reads rewrote settings")
            Cases += 1
        Next
        Console.WriteLine("PASS: all five global modes and explicit instance opt-out use isolated paths; migration is idempotent")

        Settings.Reset()
        Assert(InstanceA.PathIndie = InstanceA.PathVersion, "Fresh instance was not isolated")
        Assert(InstanceB.PathIndie = InstanceB.PathVersion, "Second instance was not isolated")
        Assert(InstanceA.PathIndie <> InstanceB.PathIndie, "Instances share game data directory")
        Cases += 1
        Console.WriteLine("PASS: fresh instances have distinct game directories")

        Settings.Reset()
        Settings.IgnoreWrites = True
        Settings.Values(InstanceA.PathVersion & "VersionArgumentIndieV2") = False
        Settings.Values(InstanceA.PathVersion & "VersionArgumentIndie") = 2
        Assert(InstanceA.PathIndie = InstanceA.PathVersion, "Failed persistence restored shared game directory")
        Cases += 1
        Console.WriteLine("PASS: persistence failure cannot turn off runtime isolation")

        Settings.Reset()
        Directory.CreateDirectory(Path.Combine(SharedRoot, "mods"))
        Directory.CreateDirectory(Path.Combine(SharedRoot, "saves", "ExistingWorld"))
        File.WriteAllText(Path.Combine(SharedRoot, "mods", "old.jar"), "old shared mod")
        File.WriteAllText(Path.Combine(SharedRoot, "saves", "ExistingWorld", "level.dat"), "old shared world")
        Dim Active = InstanceA.PathIndie
        Assert(Active = InstanceA.PathVersion, "Shared data caused isolation opt-out")
        Assert(File.ReadAllText(Path.Combine(SharedRoot, "mods", "old.jar")) = "old shared mod", "Shared mod was changed")
        Assert(File.ReadAllText(Path.Combine(SharedRoot, "saves", "ExistingWorld", "level.dat")) = "old shared world", "Shared world was changed")
        Cases += 1
        Console.WriteLine("PASS: existing shared mods and saves remain intact")
        Console.WriteLine("PASS: " & Cases.ToString() & " production isolation scenarios; no registry or real game directories used")
    End Sub

    Public McFolderSelected As String

    Private Sub Assert(Condition As Boolean, Message As String)
        If Not Condition Then Throw New Exception(Message)
    End Sub
End Module

Partial Public Class McInstance
    Public ReadOnly PathVersion As String
    Public Sub New(DirectoryPath As String)
        PathVersion = DirectoryPath
    End Sub
End Class

Public Class Settings
    Public Shared ReadOnly Values As New Dictionary(Of String, Object)
    Public Shared Writes As Integer
    Public Shared IgnoreWrites As Boolean

    Public Shared Sub Reset()
        Values.Clear()
        Writes = 0
        IgnoreWrites = False
    End Sub

    Public Shared Function [Get](Of T)(Key As String, Optional Instance As McInstance = Nothing) As T
        Dim FullKey = If(Instance Is Nothing, "", Instance.PathVersion) & Key
        If Values.ContainsKey(FullKey) Then Return DirectCast(Values(FullKey), T)
        If GetType(T) Is GetType(Boolean) Then Return DirectCast(CObj(False), T)
        If GetType(T) Is GetType(Integer) Then Return DirectCast(CObj(-1), T)
        Throw New Exception("Unexpected settings type")
    End Function

    Public Shared Sub [Set](Key As String, Value As Object, Optional Instance As McInstance = Nothing)
        Writes += 1
        If Not IgnoreWrites Then Values(If(Instance Is Nothing, "", Instance.PathVersion) & Key) = Value
    End Sub
End Class
