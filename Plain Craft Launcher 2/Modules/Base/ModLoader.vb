Public Module ModLoader

    '任务栏进度条
    Public LoaderTaskbar As New ConcurrentList(Of LoaderBase)
    Public LoaderTaskbarProgress As Double = 0 '平滑后的进度
    Private LoaderTaskbarProgressLast As Shell.TaskbarItemProgressState = Shell.TaskbarItemProgressState.None

    Public Sub LoaderTaskbarAdd(Of T)(Loader As LoaderCombo(Of T))
        If FrmSpeedLeft IsNot Nothing Then FrmSpeedLeft.TaskRemove(Loader)
        LoaderTaskbar.Add(Loader)
        Logger.Info($"{Loader.Name} 已加入任务列表")
    End Sub
    Public Sub LoaderTaskbarProgressRefresh()
        Try
            Dim NewState As Shell.TaskbarItemProgressState
            Dim NewProgress As Double = LoaderTaskbarProgressGet()
            '若单个任务已中止，或全部任务已完成，则刷新并移除
            For Each Task In LoaderTaskbar
                If LoaderTaskbar.All(Function(l) l.State <> LoadState.Loading) OrElse
                   (Task.State = LoadState.Waiting OrElse Task.State = LoadState.Canceled) Then
                    FrmSpeedLeft?.TaskRefresh(Task)
                    LoaderTaskbar.Remove(Task)
                    Logger.Info($"{Task.Name} 已移出任务列表")
                End If
            Next
            '更新平滑后的进度
            If NewProgress <= 0 OrElse NewProgress >= 1 OrElse LoaderTaskbarProgress > NewProgress Then
                LoaderTaskbarProgress = NewProgress
            Else
                LoaderTaskbarProgress = LoaderTaskbarProgress * 0.9 + NewProgress * 0.1
            End If
            RunInUi(Sub() FrmMain.BtnExtraDownload.Progress = LoaderTaskbarProgress)
            '更新任务栏信息
            If Not LoaderTaskbar.Any() OrElse LoaderTaskbarProgress = 1 Then
                NewState = Shell.TaskbarItemProgressState.None
            ElseIf LoaderTaskbarProgress < 0.015 Then
                NewState = Shell.TaskbarItemProgressState.Indeterminate
            Else
                NewState = Shell.TaskbarItemProgressState.Normal
                FrmMain.TaskbarItemInfo.ProgressValue = LoaderTaskbarProgress
            End If
            If LoaderTaskbarProgressLast <> NewState Then
                LoaderTaskbarProgressLast = NewState
                FrmMain.TaskbarItemInfo.ProgressState = NewState
                FrmMain.BtnExtraDownload.ShowRefresh()
            End If
        Catch ex As Exception
            Logger.Error(ex, "刷新任务栏进度显示失败")
        End Try
    End Sub
    Public Function LoaderTaskbarProgressGet() As Double
        Try
            If Not LoaderTaskbar.Any Then Return 1
            Return LoaderTaskbar.Select(Function(l) l.Progress).Average().Clamp(0, 1)
        Catch ex As Exception
            Logger.Error(ex, "获取任务栏进度出错")
            Return 0.5
        End Try
    End Function

    ''' <summary>
    ''' 执行以文件夹检测作为输入的加载器。加载器需以文件夹路径为输入值。
    ''' 返回是否执行了加载器。
    ''' </summary>
    ''' <param name="ExtraPath">用于检查文件夹修改的额外路径。该路径不会传入加载器。</param>
    Public Function LoaderFolderRun(Loader As LoaderBase, FolderPath As String, Type As LoaderFolderRunType, Optional MaxDepth As Integer = 0, Optional ExtraPath As String = "", Optional WaitForExit As Boolean = False) As Boolean
        Return PclLoader.LoaderFolderRun(Loader, FolderPath, Type, MaxDepth, ExtraPath, WaitForExit)
    End Function

End Module
