Imports System.Threading

''' <summary>改版说明按当前 Windows 用户持久记忆，不随程序重启或升级重复显示。</summary>
Friend Module SduStartupNotice
    Private ShownThisSession As Integer

    Public Sub ShowOnce()
        If VersionBranchMain <> "OpenSource" OrElse Interlocked.CompareExchange(ShownThisSession, 1, 0) <> 0 Then Return
        Try
            If Settings.Get(Of Boolean)("SduIntroShown") Then Return
            MyMsgBox("欢迎使用 SDUcraft 改版启动器！" & vbCrLf & vbCrLf &
                     "本版本基于 Plain Craft Launcher (PCL) 开源代码修改，由 SDUcraft 社区维护，并非 PCL 官方版本。" & vbCrLf & vbCrLf &
                     "• 使用 SDUcraft 皮肤站账号登录。" & vbCrLf &
                     "• 可在首页及下载页获取、安装 SDUcraft 整合包。" & vbCrLf &
                     "• 访问校园服务前，请连接山东大学校园网。" & vbCrLf & vbCrLf &
                     "皮肤站：https://skin.sducraft.cn/",
                     "SDUcraft 改版说明", "我知道了")
            Settings.Set("SduIntroShown", True)
        Catch ex As Exception
            Logger.Warn(ex, "读取或记录 SDUcraft 改版说明状态失败")
        End Try
    End Sub
End Module
