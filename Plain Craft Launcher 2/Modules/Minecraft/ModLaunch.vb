Public Module ModLaunch

#Region "开始"

    '当前启动选项，字段已迁移至 PCLCS.Modules.PclLaunch，此处保留转发属性
    Public Property CurrentLaunchOptions As McLaunchOptions
        Get
            Return PclLaunch.CurrentLaunchOptions
        End Get
        Set(value As McLaunchOptions)
            PclLaunch.CurrentLaunchOptions = value
        End Set
    End Property
    '逻辑已迁移至 PCLCS.Modules.PclLaunchUi.McLaunchStart
    ''' <summary>
    ''' 尝试启动 Minecraft。必须在 UI 线程调用。
    ''' 返回是否实际开始了启动（如果没有，则一定弹出了错误提示）。
    ''' </summary>
    Public Function McLaunchStart(Optional Options As McLaunchOptions = Nothing) As Boolean
        Return PclLaunchUi.McLaunchStart(Options)
    End Function

    '逻辑已迁移至 PCLCS.Modules.PclLaunch.McLaunchLog
    ''' <summary>
    ''' 记录启动日志。
    ''' </summary>
    Public Sub McLaunchLog(Text As String, Optional Level As LogLevel = LogLevel.Info)
        PclLaunch.McLaunchLog(Text, Level)
    End Sub

    '启动状态切换
    Public McLaunchLoader As New LoaderTask(Of McLaunchOptions, Object)("Loader Launch", AddressOf McLaunchStart) With {.OnStateChanged = AddressOf McLaunchState}
    Public McLaunchLoaderReal As LoaderCombo(Of Object)
    '启动进程，字段已迁移至 PCLCS.Modules.PclLaunch，此处保留转发属性
    Public Property McLaunchProcess As Process
        Get
            Return PclLaunch.McLaunchProcess
        End Get
        Set(value As Process)
            PclLaunch.McLaunchProcess = value
        End Set
    End Property
    Public McLaunchWatcher As Watcher
    '逻辑已迁移至 PCLCS.Modules.PclLaunchUi.McLaunchState
    Private Sub McLaunchState(Loader As LoaderTask(Of McLaunchOptions, Object))
        PclLaunchUi.McLaunchState(Loader)
    End Sub
    '字段已迁移至 PCLCS.Modules.PclLaunch，此处保留转发属性
    ''' <summary>
    ''' 指定启动中断时的提示文本。若不为 Nothing 则会显示为绿色。
    ''' </summary>
    Private Property CanceledHint As String
        Get
            Return PclLaunch.CanceledHint
        End Get
        Set(value As String)
            PclLaunch.CanceledHint = value
        End Set
    End Property

    '实际的启动方法
    Private Sub McLaunchStart(Loader As LoaderTask(Of McLaunchOptions, Object))
        '开始动画
        RunInUiWait(AddressOf FrmLaunchLeft.PageChangeToLaunching)
        '预检测（预检测的错误将直接抛出）
        Try
            McLaunchPrecheck()
            McLaunchLog("预检测已通过")
        Catch ex As Exception
            If Not ex.IsCanceled Then Hint(ex.Message, HintType.Red)
            Throw
        End Try
        Dim IsSavingBatch As Boolean = CurrentLaunchOptions?.SaveBatch IsNot Nothing

        '正式加载
        Try
            '构造主加载器
            Dim Loaders As New List(Of LoaderBase) From {
                New LoaderTask(Of Integer, Integer)("获取 Java", AddressOf McLaunchJava) With {.ProgressWeight = 4, .Block = False},
                McLoginLoader, '.ProgressWeight = 15, .Block = False
                New LoaderCombo(Of String)("补全文件", DlClientFix(McInstanceSelected, False, True)) With {.ProgressWeight = 15, .Show = False},
                New LoaderTask(Of String, Integer)("获取启动参数", AddressOf McLaunchArgumentMain) With {.ProgressWeight = 2},
                New LoaderTask(Of Integer, Integer)("解压文件", AddressOf McLaunchNatives) With {.ProgressWeight = 2},
                New LoaderTask(Of Integer, Integer)("预启动处理", AddressOf McLaunchPrerun) With {.ProgressWeight = 1},
                New LoaderTask(Of Integer, Integer)("执行自定义命令", AddressOf McLaunchCustom) With {.ProgressWeight = 1},
                New LoaderTask(Of Integer, Process)("启动进程", AddressOf McLaunchRun) With {.ProgressWeight = 2},
                New LoaderTask(Of Process, Integer)("等待游戏窗口出现", AddressOf McLaunchWait) With {.ProgressWeight = 1},
                New LoaderTask(Of Integer, Integer)("结束处理", AddressOf McLaunchEnd) With {.ProgressWeight = 1}
            }
            '内存优化
            Select Case Settings.Get(Of Integer)("VersionRamOptimize", Instance:=McInstanceSelected)
                Case 0 '全局
                    If Settings.Get(Of Boolean)("LaunchArgumentRam") Then '使用全局设置
                        CType(Loaders(2), LoaderCombo(Of String)).Block = False
                        Loaders.Insert(3, New LoaderTask(Of Integer, Integer)("内存优化", AddressOf McLaunchMemoryOptimize) With {.ProgressWeight = 30})
                    End If
                Case 1 '开启
                    CType(Loaders(2), LoaderCombo(Of String)).Block = False
                    Loaders.Insert(3, New LoaderTask(Of Integer, Integer)("内存优化", AddressOf McLaunchMemoryOptimize) With {.ProgressWeight = 30})
                Case 2 '关闭
            End Select
            Dim LaunchLoader As New LoaderCombo(Of Object)("Minecraft 启动", Loaders) With {.Show = False}
            If McLoginLoader.State = LoadState.Finished Then McLoginLoader.State = LoadState.Waiting '要求重启登录主加载器，它会自行决定是否启动副加载器
            '等待加载器执行并更新 UI
            McLaunchLoaderReal = LaunchLoader
            CanceledHint = Nothing
            LaunchLoader.Start()
            '任务栏进度条
            LoaderTaskbarAdd(LaunchLoader)
            Do While LaunchLoader.State = LoadState.Loading
                FrmLaunchLeft.Dispatcher.Invoke(AddressOf FrmLaunchLeft.LaunchingRefresh)
                Thread.Sleep(100)
            Loop
            FrmLaunchLeft.Dispatcher.Invoke(AddressOf FrmLaunchLeft.LaunchingRefresh)
            '成功与失败处理
            Select Case LaunchLoader.State
                Case LoadState.Finished
                    Hint(McInstanceSelected.Name & " 启动成功！", HintType.Green)
                    '上报
                    If Not IsSavingBatch Then
                        Telemetry("Minecraft 启动成功",
                              "Version", McInstanceSelected.VersionDisplayName,
                              "LoginType", Settings.Get(Of McLoginType)("LoginType").ToString)
                    End If
                Case LoadState.Canceled
                    If CanceledHint Is Nothing Then
                        Hint(If(IsSavingBatch, "已取消导出启动脚本！", "已取消启动！"), HintType.Blue)
                    Else
                        Hint(CanceledHint, HintType.Green)
                    End If
                Case LoadState.Failed
                    Throw LaunchLoader.Error
                Case Else
                    Throw New Exception("错误的状态改变：" & LaunchLoader.State.ToString())
            End Select
        Catch ex As Exception
            ex.ThrowIfCanceled()
            Dim CurrentEx = ex
NextInner:
            If CurrentEx.Message.StartsWithF("$") Then
                '若有以 $ 开头的错误信息，则以此为准显示提示
                MyMsgBox(CurrentEx.Message.TrimStart("$"), If(IsSavingBatch, "导出启动脚本失败", "启动失败"))
                Throw
            ElseIf CurrentEx.InnerException IsNot Nothing Then
                '检查下一级错误
                CurrentEx = CurrentEx.InnerException
                GoTo NextInner
            Else
                '没有特殊处理过的错误信息
                McLaunchLog("错误：" & ex.GetDisplay(True))
                Logger.Error(ex, If(IsSavingBatch, "导出启动脚本失败", "Minecraft 启动失败"), LogBehavior.Alert)
                '上报
                If Not IsSavingBatch Then
                    Telemetry("Minecraft 启动失败",
                              "Version", McInstanceSelected.VersionDisplayName,
                              "LoginType", Settings.Get(Of McLoginType)("LoginType").ToString,
                              "Exception", FilterUserName(FilterAccessToken(ex.GetDisplay(True), "*"), "*"))
                End If
                Throw
            End If
        End Try
    End Sub

#End Region

#Region "内存优化"

    '逻辑已迁移至 PCLCS.Modules.PclLaunchUi.McLaunchMemoryOptimize
    Private Sub McLaunchMemoryOptimize(Loader As LoaderTask(Of Integer, Integer))
        PclLaunchUi.McLaunchMemoryOptimize(Loader)
    End Sub

#End Region

#Region "预检测"

    '逻辑已迁移至 PCLCS.Modules.PclLaunchUi.McLaunchPrecheck
    Private Sub McLaunchPrecheck()
        PclLaunchUi.McLaunchPrecheck()
    End Sub

#End Region

#Region "主登录模块"

    '登录方式枚举、登录数据类与登录返回结构已迁移至 PCLCS.Modules.PclLaunch（McLoginType、McLoginData、
    'McLoginServer、McLoginMs、McLoginLegacy、McLoginResult），经 Imports PCLCS.Modules 解析。
    ''' <summary>
    ''' 根据登录信息获取玩家的 MC 用户名。如果无法获取则返回 Nothing。
    ''' </summary>
    Public Function McLoginName() As String
        Return PclLaunch.McLoginName()
    End Function
    '逻辑已迁移至 PCLCS.Modules.PclLaunchUi.McLoginAble
    ''' <summary>
    ''' 当前是否可以进行登录。若不可以则会返回错误原因。
    ''' </summary>
    Public Function McLoginAble() As String
        Return PclLaunchUi.McLoginAble()
    End Function
    '逻辑已迁移至 PCLCS.Modules.PclLaunchUi.McLoginAble
    ''' <summary>
    ''' 登录输入是否可以进行登录。若不可以则会返回错误原因。
    ''' </summary>
    Public Function McLoginAble(LoginData As McLoginData) As String
        Return PclLaunchUi.McLoginAble(LoginData)
    End Function

    '登录主模块加载器
    Public McLoginLoader As New LoaderTask(Of McLoginData, McLoginResult)("登录", AddressOf McLoginStart, AddressOf McLoginInput, ThreadPriority.BelowNormal) With
        {.ReloadTimeout = 1, .ProgressWeight = 15, .Block = False}
    '逻辑已迁移至 PCLCS.Modules.PclLaunchUi.McLoginInput
    Public Function McLoginInput() As McLoginData
        Return PclLaunchUi.McLoginInput()
    End Function
    '逻辑已迁移至 PCLCS.Modules.PclLaunchUi.McLoginStart
    Private Sub McLoginStart(Data As LoaderTask(Of McLoginData, McLoginResult))
        PclLaunchUi.McLoginStart(Data)
    End Sub

#End Region
#Region "分方式登录模块"

    '各个登录方式的主对象与输入构造
    Public McLoginMsLoader As New LoaderTask(Of McLoginMs, McLoginResult)("Loader Login Ms", AddressOf McLoginMsStart) With {.ReloadTimeout = 1}
    Public McLoginLegacyLoader As New LoaderTask(Of McLoginLegacy, McLoginResult)("Loader Login Legacy", AddressOf McLoginLegacyStart)
    Public McLoginNideLoader As New LoaderTask(Of McLoginServer, McLoginResult)("Loader Login Nide", AddressOf McLoginServerStart) With {.ReloadTimeout = 1000 * 60 * 10}
    Public McLoginAuthLoader As New LoaderTask(Of McLoginServer, McLoginResult)("Loader Login Auth", AddressOf McLoginServerStart) With {.ReloadTimeout = 1000 * 60 * 10}

    '逻辑已迁移至 PCLCS.Modules.PclLaunchUi.McLoginMsStart
    Private Sub McLoginMsStart(Data As LoaderTask(Of McLoginMs, McLoginResult))
        PclLaunchUi.McLoginMsStart(Data)
    End Sub
    '主加载函数，逻辑已迁移至 PCLCS.Modules.PclLaunch.McLoginServerStart
    Private Sub McLoginServerStart(Data As LoaderTask(Of McLoginServer, McLoginResult))
        PclLaunch.McLoginServerStart(Data)
    End Sub
    '逻辑已迁移至 PCLCS.Modules.PclLaunch.McLoginLegacyStart
    Private Sub McLoginLegacyStart(Data As LoaderTask(Of McLoginLegacy, McLoginResult))
        PclLaunch.McLoginLegacyStart(Data)
    End Sub
    'McLoginRequestLogin 已迁移至 PCLCS.Modules.PclLaunchUi.McLoginRequestLogin（原为 Friend，仅供 InitBridges 注册，无 VB 调用方，无需转发壳）
    'MsLoginStep1New、MsLoginStep1Refresh、MsLoginStep3、MsLoginStep4、MsLoginStep5、MsLoginStep6 均已迁移至
    'PCLCS.Modules.PclLaunchUi（Private，无 VB 调用方，无需转发壳）
    '微软登录步骤 2：从 OAuth AccessToken 获取 XBLToken，逻辑已迁移至 PCLCS.Modules.PclLaunch.MsLoginStep2
    Private Function MsLoginStep2(AccessToken As String) As String
        Return PclLaunch.MsLoginStep2(AccessToken)
    End Function

    'McLoginLegacyUuidWithCustomSkin 已迁移至 PCLCS.Modules.PclLaunch（Private，无 VB 调用方，无需转发壳）
    '根据用户名返回对应 UUID，逻辑已迁移至 PCLCS.Modules.PclLaunch.McLoginMojangUuid
    Public Function McLoginMojangUuid(Name As String, ThrowOnNotFound As Boolean)
        Return PclLaunch.McLoginMojangUuid(Name, ThrowOnNotFound)
    End Function
    '逻辑已迁移至 PCLCS.Modules.PclLaunch.McLoginLegacyUuid
    Public Function McLoginLegacyUuid(Name As String)
        Return PclLaunch.McLoginLegacyUuid(Name)
    End Function

#End Region

#Region "Java 处理"

    '字段已迁移至 PCLCS.Modules.PclLaunch，此处保留转发属性
    Public Property McLaunchJavaSelected As Java
        Get
            Return PclLaunch.McLaunchJavaSelected
        End Get
        Set(value As Java)
            PclLaunch.McLaunchJavaSelected = value
        End Set
    End Property
    '逻辑已迁移至 PCLCS.Modules.PclLaunch.McLaunchJava
    Private Sub McLaunchJava(Task As LoaderTask(Of Integer, Integer))
        PclLaunch.McLaunchJava(Task)
    End Sub

#End Region

#Region "启动参数"

    'McLaunchArgument、McLaunchEncoding 私有字段已迁移至 PCLCS.Modules.PclLaunch，无 VB 调用方，无需保留
    '主方法，逻辑已迁移至 PCLCS.Modules.PclLaunch.McLaunchArgumentMain
    Private Sub McLaunchArgumentMain(Loader As LoaderTask(Of String, Integer))
        PclLaunch.McLaunchArgumentMain(Loader)
    End Sub

    'McLaunchArgumentsJVM、McLaunchArgumentsGame、McLaunchArgumentsReplace、SplitJavaArguments、DeduplicateJavaArguments
    '均已迁移至 PCLCS.Modules.PclLaunch（Private，无 VB 调用方，无需转发壳）


#End Region

#Region "解压 Natives"

    '逻辑已迁移至 PCLCS.Modules.PclLaunch.McLaunchNatives
    Private Sub McLaunchNatives(Loader As LoaderTask(Of Integer, Integer))
        PclLaunch.McLaunchNatives(Loader)
    End Sub
    ''' <summary>
    ''' 获取 Natives 文件夹路径，以 \ 结尾。
    ''' </summary>
    Public Function GetNativesFolder(Optional Instance As McInstance = Nothing) As String
        Return PclLaunch.GetNativesFolder(Instance)
    End Function

#End Region

#Region "启动与前后处理"

    '逻辑已迁移至 PCLCS.Modules.PclLaunchUi.McLaunchPrerun
    Private Sub McLaunchPrerun()
        PclLaunchUi.McLaunchPrerun()
    End Sub
    '逻辑已迁移至 PCLCS.Modules.PclLaunch.McLaunchCustom
    Private Sub McLaunchCustom(Loader As LoaderTask(Of Integer, Integer))
        PclLaunch.McLaunchCustom(Loader)
    End Sub
    '逻辑已迁移至 PCLCS.Modules.PclLaunch.McLaunchRun
    Private Sub McLaunchRun(Loader As LoaderTask(Of Integer, Process))
        PclLaunch.McLaunchRun(Loader)
    End Sub
    '逻辑已迁移至 PCLCS.Modules.PclLaunchUi.McLaunchWait
    Private Sub McLaunchWait(Loader As LoaderTask(Of Process, Integer))
        PclLaunchUi.McLaunchWait(Loader)
    End Sub
    '逻辑已迁移至 PCLCS.Modules.PclLaunchUi.McLaunchEnd
    Private Sub McLaunchEnd()
        PclLaunchUi.McLaunchEnd()
    End Sub

#End Region

End Module
