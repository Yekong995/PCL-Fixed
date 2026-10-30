Public Module ModWatcher

    'Watcher 类已迁移至 C#（PCLCS\Modules\Minecraft\PclWatcher.cs），经 Imports PCLCS.Modules 解析。

    '对全体的监视
    Public McWatcherList As New List(Of Watcher)
    Private IsWatcherRunning As Boolean = False
    Public HasRunningMinecraft As Boolean = False
    '由 Private 改为 Friend：需在 ModBase.InitBridges 中注册为 PclUiBridge.WatcherStateChanged
    Friend Sub WatcherStateChanged()
        Dim IsRunning As Boolean = False
        Dim TriggerLauncherShutdown As Boolean = True
        For Each Watcher In McWatcherList
            If Watcher.State = Watcher.MinecraftState.Loading OrElse Watcher.State = Watcher.MinecraftState.Running OrElse
               (Watcher.State = Watcher.MinecraftState.Crashed AndAlso Not Watcher.GameProcess.HasExited) Then
                IsRunning = True
                Exit For
            ElseIf Watcher.State = Watcher.MinecraftState.Crashed OrElse Watcher.State = Watcher.MinecraftState.Canceled Then
                TriggerLauncherShutdown = False
            End If
        Next
        If IsWatcherRunning = IsRunning Then Return
        IsWatcherRunning = IsRunning
        If IsWatcherRunning Then
            MinecraftStart()
        Else
            MinecraftStop(TriggerLauncherShutdown)
        End If
    End Sub
    Private Sub MinecraftStart()
        McLaunchLog("[全局] 出现运行中的 Minecraft")
        HasRunningMinecraft = True
        FrmMain.BtnExtraShutdown.ShowRefresh()
    End Sub
    Private Sub MinecraftStop(TriggerLauncherShutdown As Boolean)
        McLaunchLog("[全局] 已无运行中的 Minecraft")
        HasRunningMinecraft = False
        FrmMain.BtnExtraShutdown.ShowRefresh()
        '音乐播放
        If Settings.Get(Of Boolean)("UiMusicStop") Then
            RunInUi(Sub() If MusicResume() Then Logger.Info("已根据设置，在结束后开始音乐播放"))
        ElseIf Settings.Get(Of Boolean)("UiMusicStart") Then
            RunInUi(Sub() If MusicPause() Then Logger.Info("已根据设置，在结束后暂停音乐播放"))
        End If
        '启动器可见性
        Select Case Settings.Get(Of Integer)("LaunchArgumentVisible")
            Case 2
                '直接关闭
                If TriggerLauncherShutdown Then
                    RunInUi(Sub() FrmMain.EndProgram(False))
                Else
                    RunInUi(Sub() FrmMain.Hidden = False)
                End If
            Case 3
                '恢复
                RunInUi(Sub() FrmMain.Hidden = False)
        End Select
    End Sub

End Module
