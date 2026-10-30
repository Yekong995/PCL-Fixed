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
    ''' <summary>
    ''' 尝试启动 Minecraft。必须在 UI 线程调用。
    ''' 返回是否实际开始了启动（如果没有，则一定弹出了错误提示）。
    ''' </summary>
    Public Function McLaunchStart(Optional Options As McLaunchOptions = Nothing) As Boolean
        CurrentLaunchOptions = If(Options, New McLaunchOptions)
        '预检查
        If Not RunInUi() Then Throw New Exception("McLaunchStart 必须在 UI 线程调用！")
        If McLaunchLoader.State = LoadState.Loading Then
            Hint("已有游戏正在启动中！", HintType.Red)
            Return False
        End If
        '强制切换需要启动的版本
        If CurrentLaunchOptions.Instance IsNot Nothing AndAlso McInstanceSelected <> CurrentLaunchOptions.Instance Then
            McLaunchLog("在启动前切换到版本 " & CurrentLaunchOptions.Instance.Name)
            '检查版本是否存在
            Try
                CurrentLaunchOptions.Instance.GetJsonPath()
            Catch
                Hint($"无法启动 {CurrentLaunchOptions.Instance.Name}：当前 Minecraft 文件夹中尚未安装该版本！", HintType.Red)
                Return False
            End Try
            '检查版本
            CurrentLaunchOptions.Instance.Load()
            If CurrentLaunchOptions.Instance.State = McInstance.McInstanceState.Error Then
                Hint($"无法启动 {CurrentLaunchOptions.Instance.Name}：{CurrentLaunchOptions.Instance.Info}", HintType.Red)
                Return False
            End If
            '切换版本
            McInstanceSelected = CurrentLaunchOptions.Instance
            FrmLaunchLeft.RefreshButtonsUI()
            FrmLaunchLeft.RefreshPage(False, False)
        End If
        If McInstanceSelected IsNot Nothing AndAlso McInstanceLock.IsLocked(McInstanceSelected) Then
            Hint($"版本 {McInstanceSelected.Name} 正在被修改，请稍后再启动！", HintType.Red)
            Return False
        End If
        FrmMain.AprilGiveup()
        '禁止进入版本选择页面（否则就可以在启动中切换 McInstanceSelected 了）
        FrmMain.PageStack = FrmMain.PageStack.Where(Function(p) p.Page <> FormMain.PageType.InstanceSelect).ToList
        '实际启动加载器
        McLaunchLoader.Start(Options, IsForceRestart:=True)
        Return True
    End Function

    ''' <summary>
    ''' 记录启动日志。
    ''' </summary>
    Public Sub McLaunchLog(Text As String, Optional Level As LogLevel = LogLevel.Info)
        Text = FilterUserName(FilterAccessToken(Text, "*"), "*")
        If ModeDebug Then
            RunInUi(
            Sub()
                FrmLaunchRight.LabLog.AppendText($"[{Date.Now:HH':'mm':'ss'.'fff}] {Text}{vbCrLf}")
            End Sub)
        End If
        Logger.Log(Text, Level)
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
    Private Sub McLaunchState(Loader As LoaderTask(Of McLaunchOptions, Object))
        Select Case McLaunchLoader.State
            Case LoadState.Finished, LoadState.Failed, LoadState.Waiting, LoadState.Canceled
                FrmLaunchLeft.PageChangeToLogin()
            Case LoadState.Loading
                '在预检测结束后再触发动画
                FrmLaunchRight.LabLog.Text = ""
        End Select
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

    Private Sub McLaunchMemoryOptimize(Loader As LoaderTask(Of Integer, Integer))
        McLaunchLog("内存优化开始")
        Dim Finished As Boolean = False
        RunInNewThread(
        Sub()
            PageOtherTest.MemoryOptimize(False)
            Finished = True
        End Sub, "Launch Memory Optimize")
        Do While Not Finished AndAlso Not Loader.IsCanceled
            If Loader.Progress < 0.7 Then
                Loader.Progress += 0.007 '10s
            Else
                Loader.Progress += (0.95 - Loader.Progress) * 0.02 '最快 += 0.005
            End If
            Thread.Sleep(100)
        Loop
    End Sub

#End Region

#Region "预检测"

    Private Sub McLaunchPrecheck()
        If Settings.Get(Of Boolean)("SystemDebugDelay") Then Thread.Sleep(RandomInteger(100, 2000))
        '检查路径
        If McInstanceSelected.PathIndie.Contains("!") OrElse McInstanceSelected.PathIndie.Contains(";") Then Throw New Exception("游戏路径中不可包含 ! 或 ;（" & McInstanceSelected.PathIndie & "）")
        If McInstanceSelected.PathVersion.Contains("!") OrElse McInstanceSelected.PathVersion.Contains(";") Then Throw New Exception("游戏路径中不可包含 ! 或 ;（" & McInstanceSelected.PathVersion & "）")
        '检查版本
        If McInstanceSelected Is Nothing Then Throw New Exception("未选择 Minecraft 版本！")
        McInstanceSelected.Load()
        If McInstanceSelected.State = McInstance.McInstanceState.Error Then Throw New Exception("Minecraft 存在问题：" & McInstanceSelected.Info)
        '检查输入信息
        Dim CheckResult As String = ""
        RunInUiWait(Sub() CheckResult = McLoginAble(McLoginInput()))
        If CheckResult <> "" Then Throw New ArgumentException(CheckResult)
        '求赞助
        If BuildType = BuildTypes.Release AndAlso CurrentLaunchOptions?.SaveBatch Is Nothing Then '保存脚本时不提示
            RunInNewThread(
            Sub()
                Select Case Settings.Get(Of Integer)("SystemLaunchCount")
                    Case 10, 20, 40, 60, 80, 100, 120, 150, 200, 250, 300, 350, 400, 500, 600, 700, 800, 900, 1000, 1200, 1400, 1600, 1800, 2000
                        If MyMsgBox("PCL 已经为你启动了 " & Settings.Get(Of Integer)("SystemLaunchCount") & " 次游戏啦！" & vbCrLf &
                                    "如果 PCL 还算好用的话，能不能考虑赞助一下 PCL……" & vbCrLf &
                                    "如果没有大家的支持，PCL 很难在免费、无任何广告的情况下维持数年的更新（磕头）……！",
                                    Settings.Get(Of Integer)("SystemLaunchCount") & " 次启动！", "支持 PCL！", "但是我拒绝") = 1 Then
                            OpenWebsite("https://meloong.com/afd/a/LTCat")
                        End If
                End Select
            End Sub, "Donate")
        End If
        '正版购买提示
        If CurrentLaunchOptions?.SaveBatch Is Nothing AndAlso '保存脚本时不提示
           Not Settings.Get(Of Boolean)("HintBuy") AndAlso Settings.Get(Of McLoginType)("LoginType") <> McLoginType.Ms Then
            If Globalization.CultureInfo.CurrentCulture.Name.StartsWithF("zh") OrElse Globalization.CultureInfo.CurrentUICulture.Name.StartsWithF("zh") Then '中文？
                RunInNewThread(
                Sub()
                    Select Case Settings.Get(Of Integer)("SystemLaunchCount")
                        Case 3, 8, 15, 30, 50, 70, 90, 110, 130, 180, 220, 280, 330, 380, 450, 550, 660, 750, 880, 950, 1100, 1300, 1500, 1700, 1900
                            If MyMsgBox("你已经启动了 " & Settings.Get(Of Integer)("SystemLaunchCount") & " 次 Minecraft 啦！" & vbCrLf &
                                "如果觉得 Minecraft 还不错，可以购买正版支持一下，毕竟开发游戏也真的很不容易……不要一直白嫖啦。" & vbCrLf & vbCrLf &
                                "在登录一次正版账号后，就不会再出现这个提示了！",
                                "考虑一下正版？", "支持正版游戏！", "下次一定") = 1 Then
                                OpenWebsite("https://www.xbox.com/zh-cn/games/store/minecraft-java-bedrock-edition-for-pc/9nxp44l49shj")
                            End If
                    End Select
                End Sub, "Buy Minecraft")
            ElseIf Settings.Get(Of McLoginType)("LoginType") = McLoginType.Legacy Then
                Select Case MyMsgBox("你必须先登录正版账号，才能进行离线登录！", "正版验证", "购买正版", "试玩", "返回",
                    Button1Action:=Sub() OpenWebsite("https://www.xbox.com/zh-cn/games/store/minecraft-java-bedrock-edition-for-pc/9nxp44l49shj"))
                    Case 2
                        Hint("游戏将以试玩模式启动！", HintType.Red)
                        CurrentLaunchOptions.ExtraGameArgs.Add("--demo")
                    Case 3
                        Throw New OperationCanceledException
                End Select
            End If
        End If
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
    ''' <summary>
    ''' 当前是否可以进行登录。若不可以则会返回错误原因。
    ''' </summary>
    Public Function McLoginAble() As String
        Select Case Settings.Get(Of McLoginType)("LoginType")
            Case McLoginType.Ms
                If Settings.Get(Of String)("CacheMsV2OAuthRefresh") = "" Then
                    Return FrmLoginMs.IsVaild()
                Else
                    Return ""
                End If
            Case McLoginType.Legacy
                Return FrmLoginLegacy.IsVaild()
            Case McLoginType.Nide
                If Settings.Get(Of String)("CacheNideAccess") = "" Then
                    Return FrmLoginNide.IsVaild()
                Else
                    Return ""
                End If
            Case McLoginType.Auth
                If Settings.Get(Of String)("CacheAuthAccess") = "" Then
                    Return FrmLoginAuth.IsVaild()
                Else
                    Return ""
                End If
            Case Else
                Return "未知的登录方式"
        End Select
    End Function
    ''' <summary>
    ''' 登录输入是否可以进行登录。若不可以则会返回错误原因。
    ''' </summary>
    Public Function McLoginAble(LoginData As McLoginData) As String
        Select Case LoginData.Type
            Case McLoginType.Ms
                Return PageLoginMs.IsVaild(LoginData)
            Case McLoginType.Legacy
                Return PageLoginLegacy.IsVaild(LoginData)
            Case McLoginType.Nide
                Return PageLoginNide.IsVaild(LoginData)
            Case McLoginType.Auth
                Return PageLoginAuth.IsVaild(LoginData)
            Case Else
                Return "未知的登录方式"
        End Select
    End Function

    '登录主模块加载器
    Public McLoginLoader As New LoaderTask(Of McLoginData, McLoginResult)("登录", AddressOf McLoginStart, AddressOf McLoginInput, ThreadPriority.BelowNormal) With
        {.ReloadTimeout = 1, .ProgressWeight = 15, .Block = False}
    Public Function McLoginInput() As McLoginData
        Dim LoginData As McLoginData = Nothing
        Dim LoginType = Settings.Get(Of McLoginType)("LoginType")
        Try
            Select Case LoginType
                Case McLoginType.Legacy
                    LoginData = PageLoginLegacy.GetLoginData()
                Case McLoginType.Ms
                    If Settings.Get(Of String)("CacheMsV2OAuthRefresh") = "" Then
                        LoginData = PageLoginMs.GetLoginData()
                    Else
                        LoginData = PageLoginMsSkin.GetLoginData()
                    End If
                Case McLoginType.Nide
                    If Settings.Get(Of String)("CacheNideAccess") = "" Then
                        LoginData = PageLoginNide.GetLoginData()
                    Else
                        LoginData = PageLoginNideSkin.GetLoginData()
                    End If
                Case McLoginType.Auth
                    If Settings.Get(Of String)("CacheAuthAccess") = "" Then
                        LoginData = PageLoginAuth.GetLoginData()
                    Else
                        LoginData = PageLoginAuthSkin.GetLoginData()
                    End If
            End Select
            Return LoginData
        Catch ex As Exception
            Throw New Exception($"获取登录输入信息失败（{LoginType}）", ex)
        End Try
    End Function
    Private Sub McLoginStart(Data As LoaderTask(Of McLoginData, McLoginResult))
        McLaunchLog("登录加载已开始")
        '校验登录信息
        Dim CheckResult As String = McLoginAble(Data.Input)
        If Not CheckResult = "" Then Throw New ArgumentException(CheckResult)
        '获取对应加载器
        Dim Loader As LoaderBase = Nothing
        Select Case Data.Input.Type
            Case McLoginType.Ms
                Loader = McLoginMsLoader
            Case McLoginType.Legacy
                Loader = McLoginLegacyLoader
            Case McLoginType.Nide
                Loader = McLoginNideLoader
            Case McLoginType.Auth
                Loader = McLoginAuthLoader
        End Select
        '尝试加载
        Loader.WaitForExit(Data.Input, McLoginLoader, Data.IsForceRestarting)
        Data.Output = CType(Loader, Object).Output
        RunInUi(Sub() FrmLaunchLeft.RefreshPage(True, False)) '刷新自动填充列表
        McLaunchLog("登录加载已结束")
    End Sub

#End Region
#Region "分方式登录模块"

    '各个登录方式的主对象与输入构造
    Public McLoginMsLoader As New LoaderTask(Of McLoginMs, McLoginResult)("Loader Login Ms", AddressOf McLoginMsStart) With {.ReloadTimeout = 1}
    Public McLoginLegacyLoader As New LoaderTask(Of McLoginLegacy, McLoginResult)("Loader Login Legacy", AddressOf McLoginLegacyStart)
    Public McLoginNideLoader As New LoaderTask(Of McLoginServer, McLoginResult)("Loader Login Nide", AddressOf McLoginServerStart) With {.ReloadTimeout = 1000 * 60 * 10}
    Public McLoginAuthLoader As New LoaderTask(Of McLoginServer, McLoginResult)("Loader Login Auth", AddressOf McLoginServerStart) With {.ReloadTimeout = 1000 * 60 * 10}

    '主加载函数，返回所有需要的登录信息
    Private Sub McLoginMsStart(Data As LoaderTask(Of McLoginMs, McLoginResult))
        Dim Input As McLoginMs = Data.Input
        Dim LogUsername As String = Input.UserName
        McLaunchLog("登录方式：正版（" & If(LogUsername = "", "尚未登录", LogUsername) & "）")
        Data.Progress = 0.05
        '检查是否已经登录完成
        Dim ExpiresAt = Settings.Get(Of Long)("CacheMsV2Expires")
        If Not Data.IsForceRestarting AndAlso '不要求强行重启
           ExpiresAt > 0 AndAlso ExpiresAt > ToUnixTimestampUtc(Date.UtcNow) AndAlso 'AccessToken 尚未过期
           Input.UserName = Settings.Get(Of String)("CacheMsV2Name") Then
            Data.Output = New McLoginResult With {
                .Name = Input.UserName, .Type = "Microsoft",
                .AccessToken = Settings.Get(Of String)("CacheMsV2Access"),
                .Uuid = Settings.Get(Of String)("CacheMsV2Uuid"),
                .ClientToken = Settings.Get(Of String)("CacheMsV2Uuid"),
                .ProfileJson = Settings.Get(Of String)("CacheMsV2ProfileJson")
            }
            McLaunchLog("无需登录，AccessToken 尚未过期")
            GoTo SkipLogin
        End If
        '尝试登录
        Dim OAuthTokens As (OAuthAccessToken As String, OAuthRefreshToken As String)
        If Input.OAuthRefreshToken = "" Then
            '无 RefreshToken
Relogin:
            OAuthTokens = MsLoginStep1New(Data)
        Else
            '有 RefreshToken
            OAuthTokens = MsLoginStep1Refresh(Input.OAuthRefreshToken)
            If OAuthTokens.OAuthAccessToken = "Relogin" Then GoTo Relogin '要求重新打开登录网页认证
        End If
        If Data.IsCanceled Then Throw New OperationCanceledException
        Data.Progress = 0.25
        If Data.IsCanceled Then Throw New OperationCanceledException
        Dim OAuthAccessToken As String = OAuthTokens.OAuthAccessToken
        Dim OAuthRefreshToken As String = OAuthTokens.OAuthRefreshToken
        Dim XBLToken As String = MsLoginStep2(OAuthAccessToken)
        Data.Progress = 0.4
        If Data.IsCanceled Then Throw New OperationCanceledException
        Dim Tokens = MsLoginStep3(XBLToken)
        Data.Progress = 0.55
        If Data.IsCanceled Then Throw New OperationCanceledException
        Dim LoginResult = MsLoginStep4(Tokens)
        ExpiresAt = LoginResult.ExpiresAt
        Data.Progress = 0.7
        If Data.IsCanceled Then Throw New OperationCanceledException
        MsLoginStep5(LoginResult.AccessToken)
        Data.Progress = 0.85
        If Data.IsCanceled Then Throw New OperationCanceledException
        Dim Result = MsLoginStep6(LoginResult.AccessToken)
        Data.Progress = 0.98
        '输出登录结果
        Settings.Set("CacheMsV2OAuthRefresh", OAuthRefreshToken)
        Settings.Set("CacheMsV2Access", LoginResult.AccessToken)
        Settings.Set("CacheMsV2Uuid", Result.UUID)
        Settings.Set("CacheMsV2Name", Result.UserName)
        Settings.Set("CacheMsV2ProfileJson", Result.ProfileJson)
        Settings.Set("CacheMsV2Expires", LoginResult.ExpiresAt)
        Dim MsJson As JObject = Settings.Get(Of String)("LoginMsJson").DeserializeJson()
        MsJson.Remove(Input.UserName) '如果更改了玩家名……
        MsJson(Result.UserName) = OAuthRefreshToken
        Settings.Set("LoginMsJson", MsJson.ToString(Newtonsoft.Json.Formatting.None))
        Data.Output = New McLoginResult With {.AccessToken = LoginResult.AccessToken, .Name = Result.UserName, .Uuid = Result.UUID, .Type = "Microsoft", .ClientToken = Result.UUID, .ProfileJson = Result.ProfileJson}
        '结束
SkipLogin:
        McLaunchLog($"微软登录结束，AccessToken 将在 {DateTimeOffset.FromUnixTimeSeconds(ExpiresAt).LocalDateTime} 过期")
        Settings.Set("HintBuy", True) '关闭正版购买提示
        If ThemeUnlock(10, False) Then MyMsgBox("感谢你对正版游戏的支持！" & vbCrLf & "隐藏主题 跳票红 已解锁！", "提示")
    End Sub
    '主加载函数，逻辑已迁移至 PCLCS.Modules.PclLaunch.McLoginServerStart
    Private Sub McLoginServerStart(Data As LoaderTask(Of McLoginServer, McLoginResult))
        PclLaunch.McLoginServerStart(Data)
    End Sub
    '逻辑已迁移至 PCLCS.Modules.PclLaunch.McLoginLegacyStart
    Private Sub McLoginLegacyStart(Data As LoaderTask(Of McLoginLegacy, McLoginResult))
        PclLaunch.McLoginLegacyStart(Data)
    End Sub
    '原为 Private，因需在 ModBase.InitBridges 中注册 PclUiBridge.McLoginRequestLogin 而提升为 Friend
    Friend Function McLoginRequestLogin(ByRef Data As LoaderTask(Of McLoginServer, McLoginResult)) As Boolean
        Try
            Dim NeedRefresh As Boolean = False
            McLaunchLog("登录开始（Login, " & Data.Input.Token & "）")
            Dim RequestData As New JObject(
                New JProperty("agent", New JObject(New JProperty("name", "Minecraft"), New JProperty("version", 1))),
                New JProperty("username", Data.Input.UserName),
                New JProperty("password", Data.Input.Password),
                New JProperty("requestUser", True))
            Dim LoginJson As JObject = NetRequestByClientRetry(
                Data.Input.BaseUrl & "/authenticate", HttpMethod.Post,
                Content:=RequestData.ToString(Newtonsoft.Json.Formatting.None),
                Headers:={{"Accept-Language", "zh-CN"}},
                ContentType:="application/json; charset=utf-8", RequireJson:=True).DeserializeJson()
            '检查登录结果
            If LoginJson("availableProfiles").Count = 0 Then
                If Data.Input.ForceReselectProfile Then Hint("你还没有创建角色，无法更换！", HintType.Red)
                Throw New Exception("$你还没有创建角色，请在创建角色后再试！")
            ElseIf Data.Input.ForceReselectProfile AndAlso LoginJson("availableProfiles").IsSingle Then
                Hint("你的账户中只有一个角色，无法更换！", HintType.Red)
            End If
            Dim SelectedName As String = Nothing
            Dim SelectedId As String = Nothing
            If (LoginJson("selectedProfile") Is Nothing OrElse Data.Input.ForceReselectProfile) AndAlso LoginJson("availableProfiles").Count > 1 Then
                '要求选择档案；优先从缓存读取
                NeedRefresh = True
                Dim CacheId As String = Settings.Get(Of String)("Cache" & Data.Input.Token & "Uuid")
                For Each Profile In LoginJson("availableProfiles")
                    If Profile("id").ToString = CacheId Then
                        SelectedName = Profile("name").ToString
                        SelectedId = Profile("id").ToString
                        McLaunchLog("根据缓存选择的角色：" & SelectedName)
                    End If
                Next
                '缓存无效，要求玩家选择
                If SelectedName Is Nothing Then
                    McLaunchLog("要求玩家选择角色")
                    RunInUiWait(
                    Sub()
                        Dim SelectionControl As New List(Of IMyRadio)
                        Dim SelectionJson As New List(Of JToken)
                        For Each Profile In LoginJson("availableProfiles")
                            SelectionControl.Add(New MyRadioBox With {.Text = Profile("name").ToString})
                            SelectionJson.Add(Profile)
                        Next
                        Dim SelectedIndex As Integer = MyMsgBoxSelect(SelectionControl, "选择使用的角色")
                        SelectedName = SelectionJson(SelectedIndex)("name").ToString
                        SelectedId = SelectionJson(SelectedIndex)("id").ToString
                    End Sub)
                    McLaunchLog("玩家选择的角色：" & SelectedName)
                End If
            Else
                SelectedName = LoginJson("selectedProfile")("name").ToString
                SelectedId = LoginJson("selectedProfile")("id").ToString
            End If
            '将登录结果输出
            Data.Output.AccessToken = LoginJson("accessToken").ToString
            Data.Output.ClientToken = LoginJson("clientToken").ToString
            Data.Output.Name = SelectedName
            Data.Output.Uuid = SelectedId
            Data.Output.Type = Data.Input.Token
            '保存缓存
            Settings.Set("Cache" & Data.Input.Token & "Access", Data.Output.AccessToken)
            Settings.Set("Cache" & Data.Input.Token & "Client", Data.Output.ClientToken)
            Settings.Set("Cache" & Data.Input.Token & "Uuid", Data.Output.Uuid)
            Settings.Set("Cache" & Data.Input.Token & "Name", Data.Output.Name)
            Settings.Set("Cache" & Data.Input.Token & "Username", Data.Input.UserName)
            Settings.Set("Cache" & Data.Input.Token & "Pass", Data.Input.Password)
            McLaunchLog("登录成功（Login, " & Data.Input.Token & "）")
            Return NeedRefresh
        Catch ex As Exception
            Dim AllMessage As String = ex.GetDisplay(False)
            Logger.Info(ex, "登录失败原始错误信息")
            '读取服务器返回的错误
            If TypeOf ex Is HttpRequestCodeException Then
                Dim ErrorMessage As String = Nothing
                Try
                    Dim Response = DirectCast(ex, HttpRequestCodeException).Response
                    If Response.ContainsIgnoreCase("errorMessage") Then ErrorMessage = Response.DeserializeJson()("errorMessage").ToString()
                Catch
                End Try
                If Not String.IsNullOrWhiteSpace(ErrorMessage) Then
                    If ErrorMessage.Contains("密码错误") OrElse ErrorMessage.ContainsIgnoreCase("Incorrect username or password") Then
                        '密码错误，退出登录 (#5090)
                        McLaunchLog("密码错误，退出登录")
                        Select Case Data.Input.Type
                            Case McLoginType.Auth
                                RunInUi(AddressOf PageLoginAuthSkin.ExitLogin)
                            Case McLoginType.Nide
                                RunInUi(AddressOf PageLoginNideSkin.ExitLogin)
                        End Select
                    End If
                    Throw New Exception("$登录失败：" & ErrorMessage)
                End If
            End If
            '通用关键字检测
            If AllMessage.Contains("(403)") Then
                Select Case Data.Input.Type
                    Case McLoginType.Auth
                        Throw New Exception("$登录失败，以下为可能的原因：" & vbCrLf &
                                            " - 输入的账号或密码错误。" & vbCrLf &
                                            " - 登录尝试过于频繁，导致被暂时屏蔽。请不要操作，等待 10 分钟后再试。" & vbCrLf &
                                            " - 只注册了账号，但没有在皮肤站新建角色。")
                    Case McLoginType.Nide
                        Throw New Exception("$登录失败，以下为可能的原因：" & vbCrLf &
                                            " - 输入的账号或密码错误。" & vbCrLf &
                                            " - 密码错误次数过多，导致被暂时屏蔽。请不要操作，等待 10 分钟后再试。" & vbCrLf &
                                            If(Data.Input.UserName.Contains("@"), "", " - 登录账号应为邮箱或统一通行证账号，而非游戏角色 ID。" & vbCrLf) &
                                            " - 只注册了账号，但没有加入对应服务器。")
                End Select
            ElseIf ex.IsBadNetwork() Then
                Throw New Exception("$登录失败：你的网络环境不佳，导致难以连接到海外服务器。" & vbCrLf & "请稍后再试，或使用加速器、VPN 改善网络环境。")
            ElseIf ex.Message.StartsWithF("$") Then
                Throw
            Else
                Throw New Exception("登录失败：" & ex.Message, ex)
            End If
            Return False
        End Try
    End Function

    '微软登录步骤 1，原始登录：获取 DeviceCode 并开启登录网页
    Private Function MsLoginStep1New(Data As LoaderTask(Of McLoginMs, McLoginResult)) As (OAuthAccessToken As String, OAuthRefreshToken As String)
        '参考：https://learn.microsoft.com/zh-cn/entra/identity-platform/v2-oauth2-device-code

        '初始请求
Retry:
        McLaunchLog("开始微软登录步骤 1/6（原始登录）")
        Dim PrepareJson As JObject = NetRequestByClientRetry("https://login.microsoftonline.com/consumers/oauth2/v2.0/devicecode", HttpMethod.Post,
            Content:=$"client_id={OAuthClientId}&tenant=/consumers&scope=XboxLive.signin%20offline_access",
            ContentType:="application/x-www-form-urlencoded", RequireJson:=True).DeserializeJson()
        McLaunchLog("网页登录地址：" & PrepareJson("verification_uri").ToString)

        '弹窗
        Dim Converter As New MyMsgBoxConverter With {.Content = PrepareJson, .ForceWait = True, .Type = MyMsgBoxType.Login}
        WaitingMyMsgBox.Enqueue(Converter)
        While Converter.Result Is Nothing
            Thread.Sleep(100)
        End While
        If TypeOf Converter.Result Is RestartException Then
            If MyMsgBox($"请在登录时选择 {vbLQ}其他登录方法{vbRQ}，然后选择 {vbLQ}使用我的密码{vbRQ}。{vbCrLf}如果没有该选项，请选择 {vbLQ}设置密码{vbRQ}，设置完毕后再登录。",
                "需要使用密码登录", "重新登录", "设置密码", "取消",
                Button2Action:=Sub() OpenWebsite("https://account.live.com/password/Change")) = 1 Then
                GoTo Retry
            Else
                Throw New OperationCanceledException
            End If
        ElseIf TypeOf Converter.Result Is Exception Then
            Throw CType(Converter.Result, Exception)
        Else
            Return (Converter.Result(0), Converter.Result(1))
        End If
    End Function
    '微软登录步骤 1，刷新登录：从 OAuth Code 或 OAuth RefreshToken 获取 {OAuth AccessToken, OAuth RefreshToken}
    Private Function MsLoginStep1Refresh(Code As String) As (OAuthAccessToken As String, OAuthRefreshToken As String)
        McLaunchLog("开始微软登录步骤 1/6（刷新登录）")

        Dim Result As String
        Try
            Result = NetRequestByClientMultiple("https://login.live.com/oauth20_token.srf", HttpMethod.Post,
                Content:=$"client_id={OAuthClientId}&refresh_token={StringUtils.FormUrlEscape(Code)}&grant_type=refresh_token&scope=XboxLive.signin%20offline_access",
                ContentType:="application/x-www-form-urlencoded",
                Headers:={{"Accept-Language", "en-US,en;q=0.5"}, {"X-Requested-With", "XMLHttpRequest"}},
                ThreadCount:=2)
        Catch ex As HttpRequestCodeException
            '修改错误列表时，同时检查 MyMsgLogin.xaml.vb 中的对应代码
            Dim Response = ex.Response
            If Response.ContainsIgnoreCase("must sign in again") OrElse Response.ContainsIgnoreCase("password expired") OrElse
               (Response.Contains("refresh_token") AndAlso Response.ContainsIgnoreCase("is not valid")) OrElse '#269
               Response.ContainsIgnoreCase("expired") Then '#8611
                Return ("Relogin", "")
            ElseIf Response.Contains("Account security interrupt") Then
                MyMsgBox("该账号由于安全问题无法登陆，请前往微软账户页获取更多信息。", "登录失败", "我知道了", IsWarn:=True)
                Throw New OperationCanceledException
            ElseIf Response.Contains("service abuse") Then
                MyMsgBox("非常抱歉，该账号已被微软封禁，无法登录。", "登录失败", "我知道了", IsWarn:=True)
                Throw New OperationCanceledException
            Else
                Throw
            End If
        End Try

        Dim ResultJson As JObject = Result.DeserializeJson()
        Dim AccessToken As String = ResultJson("access_token").ToString
        Dim RefreshToken As String = ResultJson("refresh_token").ToString
        Return (AccessToken, RefreshToken)
    End Function
    '微软登录步骤 2：从 OAuth AccessToken 获取 XBLToken，逻辑已迁移至 PCLCS.Modules.PclLaunch.MsLoginStep2
    Private Function MsLoginStep2(AccessToken As String) As String
        Return PclLaunch.MsLoginStep2(AccessToken)
    End Function
    '微软登录步骤 3：从 XBLToken 获取 {XSTSToken, UHS}
    Private Function MsLoginStep3(XBLToken As String) As (XSTSToken As String, UHS As String)
        McLaunchLog("开始微软登录步骤 3/6")

        Dim Request As String = "{
           ""Properties"": {
               ""SandboxId"": ""RETAIL"",
               ""UserTokens"": [""" & XBLToken & """]
           },
           ""RelyingParty"": ""rp://api.minecraftservices.com/"",
           ""TokenType"": ""JWT""
        }"
        Dim Result As String
        Try
            Result = NetRequestByClientMultiple("https://xsts.auth.xboxlive.com/xsts/authorize", HttpMethod.Post,
                Content:=Request, ContentType:="application/json", RequireJson:=True)
        Catch ex As HttpRequestCodeException
            '参考 https://github.com/PrismarineJS/prismarine-auth/blob/master/src/common/Constants.js
            If ex.Response.Contains("2148916227") Then
                MyMsgBox("该账号似乎已被微软封禁，无法登录。", "登录失败", "我知道了", IsWarn:=True)
                Throw New OperationCanceledException
            ElseIf ex.Response.Contains("2148916233") Then
                If MyMsgBox("你尚未注册 Xbox 账户，请在注册后再登录。", "登录提示", "注册", "取消") = 1 Then
                    OpenWebsite("https://signup.live.com/signup")
                End If
                Throw New OperationCanceledException
            ElseIf ex.Response.Contains("2148916235") Then
                MyMsgBox($"你的网络所在的国家或地区无法登录微软账号。{vbCrLf}请使用加速器或 VPN，然后再试。", "登录失败", "我知道了")
                Throw New OperationCanceledException
            ElseIf ex.Response.Contains("2148916238") Then
                If MyMsgBox("该账号年龄不足，你需要先修改出生日期，然后才能登录。" & vbCrLf &
                            "该账号目前填写的年龄是否在 13 岁以上？", "登录提示", "13 岁以上", "12 岁以下", "我不知道") = 1 Then
                    OpenWebsite("https://account.live.com/editprof.aspx")
                    MyMsgBox("请在打开的网页中修改账号的出生日期（至少改为 18 岁以上）。" & vbCrLf &
                             "在修改成功后等待一分钟，然后再回到 PCL，就可以正常登录了！", "登录提示")
                Else
                    OpenWebsite("https://support.microsoft.com/zh-cn/account-billing/如何更改-microsoft-帐户上的出生日期-837badbc-999e-54d2-2617-d19206b9540a")
                    MyMsgBox("请根据打开的网页的说明，修改账号的出生日期（至少改为 18 岁以上）。" & vbCrLf &
                             "在修改成功后等待一分钟，然后再回到 PCL，就可以正常登录了！", "登录提示")
                End If
                Throw New OperationCanceledException
            Else
                Throw
            End If
        End Try

        Dim ResultJson As JObject = Result.DeserializeJson()
        Return (
            ResultJson("Token").ToString(), 'XSTSToken
            ResultJson("DisplayClaims")("xui")(0)("uhs").ToString() 'UHS
        )
    End Function
    '微软登录步骤 4：从 {XSTSToken, UHS} 获取 {Minecraft AccessToken, 过期于 (Unix 时间戳)}
    Private Function MsLoginStep4(Tokens As (XSTSToken As String, UHS As String)) As (AccessToken As String, ExpiresAt As Integer)
        McLaunchLog("开始微软登录步骤 4/6")

        Dim Request As String = New JObject(New JProperty("identityToken", $"XBL3.0 x={Tokens.UHS};{Tokens.XSTSToken}")).ToString(0)
        Dim Result As String
        Try
            Result = NetRequestByClientRetry("https://api.minecraftservices.com/authentication/login_with_xbox", HttpMethod.Post,
                Content:=Request, ContentType:="application/json", RequireJson:=True)
        Catch ex As HttpRequestCodeException
            If ex.StatusCode = 429 Then
                Logger.Warn(ex, "微软登录第 4 步汇报 429")
                Throw New Exception("$登录尝试太过频繁，请等待几分钟后再试！")
            ElseIf ex.StatusCode = HttpStatusCode.Forbidden Then
                Logger.Warn(ex, "微软登录第 4 步汇报 403")
                Throw New Exception("$当前 IP 的登录尝试异常。" & vbCrLf & "如果你使用了 VPN 或加速器，请把它们关掉或更换节点后再试！")
            ElseIf ex.StatusCode = HttpStatusCode.ServiceUnavailable Then
                Logger.Warn(ex, "微软登录第 4 步汇报 503")
                Throw New Exception("$Mojang 服务器出现问题，请稍后再试。" & vbCrLf & "你的网络是正常的，PCL 也是正常的，是 Mojang 出问题了……")
            ElseIf ex.Response?.Contains("ACCOUNT_SUSPENDED") Then '#8655
                MyMsgBox("该账号似乎已被微软封禁，无法登录。", "登录失败", "我知道了", IsWarn:=True)
                Throw New OperationCanceledException
            Else
                Throw
            End If
        End Try

        Dim ResultJson As JObject = Result.DeserializeJson()
        Return (
            ResultJson("access_token").ToString,
            ResultJson("expires_in").ToObject(Of Integer) + GetUnixTimestampUtc() - 3600) '提前 1 小时视作过期
    End Function
    '微软登录步骤 5：验证微软账号是否持有 MC，这也会刷新 XGP
    Private Sub MsLoginStep5(AccessToken As String)
        McLaunchLog("开始微软登录步骤 5/6")

        Dim Result As String = NetRequestByClientMultiple("https://api.minecraftservices.com/entitlements/mcstore",
            ContentType:="application/json", ThreadCount:=2, Headers:={{"Authorization", "Bearer " & AccessToken}}, RequireJson:=True)
        Try
            Dim ResultJson As JObject = Result.DeserializeJson()
            If Not (ResultJson.ContainsKey("items") AndAlso ResultJson("items").Any) Then
                Select Case MyMsgBox("你尚未购买正版 Minecraft，或者 Xbox Game Pass 已到期。", "登录失败", "购买 Minecraft", "取消")
                    Case 1
                        OpenWebsite("https://www.xbox.com/zh-cn/games/store/minecraft-java-bedrock-edition-for-pc/9nxp44l49shj")
                End Select
                Throw New OperationCanceledException
            End If
        Catch ex As Exception
            Logger.Warn(ex, $"微软登录第 5 步异常：{Result}")
            Throw
        End Try
    End Sub
    '微软登录步骤 6：从 Minecraft AccessToken 获取 {UUID, UserName, ProfileJson}
    Private Function MsLoginStep6(AccessToken As String) As (UUID As String, UserName As String, ProfileJson As String)
        McLaunchLog("开始微软登录步骤 6/6")

        Dim Result As String
        Try
            Result = NetRequestByClientMultiple("https://api.minecraftservices.com/minecraft/profile",
                ContentType:="application/json", ThreadCount:=2, Headers:={{"Authorization", "Bearer " & AccessToken}}, RequireJson:=True)
        Catch ex As HttpRequestCodeException
            Select Case ex.StatusCode
                Case 429
                    Logger.Warn(ex, "微软登录第 6 步汇报 429")
                    Throw New Exception("$登录尝试太过频繁，请等待几分钟后再试！")
                Case HttpStatusCode.NotFound
                    Logger.Warn(ex, "微软登录第 6 步汇报 404")
                    RunInNewThread(
                    Sub()
                        Select Case MyMsgBox("请先创建 Minecraft 玩家档案，然后再重新登录。", "登录失败", "创建档案", "取消")
                            Case 1
                                OpenWebsite("https://www.minecraft.net/zh-hans/msaprofile/mygames/editprofile")
                        End Select
                    End Sub, "Login Failed: Create Profile")
                    Throw New OperationCanceledException
                Case Else
                    Throw
            End Select
        End Try
        Dim ResultJson As JObject = Result.DeserializeJson()
        Dim UUID As String = ResultJson("id").ToString
        Dim UserName As String = ResultJson("name").ToString
        Return (UUID, UserName, Result)
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

    Private Sub McLaunchPrerun()

        '使用高性能显卡
        If Settings.Get(Of Boolean)("LaunchAdvanceGraphicCard") Then
            Try
                SetGPUPreference(McLaunchJavaSelected.JavaExePath)
                SetGPUPreference(PathExe)
            Catch ex As Exception
                If WindowsUtils.HasAdminRole() Then
                    Logger.Warn(ex, "直接调整显卡设置失败")
                Else
                    Logger.Warn(ex, "直接调整显卡设置失败，将以管理员权限重启 PCL 再次尝试")
                    Try
                        If RunAsAdmin($"--gpu ""{McLaunchJavaSelected.JavaExePath}""") = ProcessReturnValues.TaskDone Then
                            McLaunchLog("以管理员权限重启 PCL 并调整显卡设置成功")
                        Else
                            Throw New Exception("调整过程中出现异常")
                        End If
                    Catch exx As Exception
                        Logger.Error(exx, "调整显卡设置失败，Minecraft 可能会使用默认显卡运行", LogBehavior.Toast)
                    End Try
                End If
            End Try
        End If

        '更新 launcher_profiles.json
        Try
            '确保可用
            If Not McLoginLoader.Output.Type = "Microsoft" Then Exit Try
            McFolderLauncherProfilesJsonCreate(McFolderSelected)
            '构建需要替换的 Json 对象
            Dim ReplaceJsonString As String = "
            {
              ""authenticationDatabase"": {
                ""00000111112222233333444445555566"": {
                  ""username"": """ & McLoginLoader.Output.Name.Replace("""", "-") & """,
                  ""profiles"": {
                    ""66666555554444433333222221111100"": {
                        ""displayName"": """ & McLoginLoader.Output.Name & """
                    }
                  }
                }
              },
              ""clientToken"": """ & McLoginLoader.Output.ClientToken & """,
              ""selectedUser"": {
                ""account"": ""00000111112222233333444445555566"", 
                ""profile"": ""66666555554444433333222221111100""
              }
            }"
            Dim ReplaceJson As JObject = (ReplaceJsonString).DeserializeJson()
            '更新文件
            Dim Profiles As JObject = FileUtils.ReadAsJson(McFolderSelected & "launcher_profiles.json")
            Profiles.Merge(ReplaceJson)
            FileUtils.Write(McFolderSelected & "launcher_profiles.json", Profiles.ToString, encoding:=Encoding.GetEncoding("GB18030"))
            McLaunchLog("已更新 launcher_profiles.json")
        Catch ex As Exception
            Logger.Warn(ex, "更新 launcher_profiles.json 失败，将在删除文件后重试")
            Try
                FileUtils.Delete(McFolderSelected & "launcher_profiles.json")
                McFolderLauncherProfilesJsonCreate(McFolderSelected)
                '构建需要替换的 Json 对象
                Dim ReplaceJsonString As String = "
                    {
                      ""authenticationDatabase"": {
                        ""00000111112222233333444445555566"": {
                          ""username"": """ & McLoginLoader.Output.Name.Replace("""", "-") & """,
                          ""profiles"": {
                            ""66666555554444433333222221111100"": {
                                ""displayName"": """ & McLoginLoader.Output.Name & """
                            }
                          }
                        }
                      },
                      ""clientToken"": """ & McLoginLoader.Output.ClientToken & """,
                      ""selectedUser"": {
                        ""account"": ""00000111112222233333444445555566"", 
                        ""profile"": ""66666555554444433333222221111100""
                      }
                    }"
                Dim ReplaceJson As JObject = ReplaceJsonString.DeserializeJson()
                '更新文件
                Dim Profiles As JObject = FileUtils.ReadAsJson(McFolderSelected & "launcher_profiles.json")
                Profiles.Merge(ReplaceJson)
                FileUtils.Write(McFolderSelected & "launcher_profiles.json", Profiles.ToString, encoding:=Encoding.GetEncoding("GB18030"))
                McLaunchLog("已在删除后更新 launcher_profiles.json")
            Catch exx As Exception
                Logger.Error(exx, "更新 launcher_profiles.json 失败")
            End Try
        End Try

        '更新 options.txt
        Dim SetupFileAddress As String = McInstanceSelected.PathIndie & "options.txt"
        If Not FileUtils.Exists(SetupFileAddress) Then
            'Yosbr Mod 兼容（#2385）：https://www.curseforge.com/minecraft/mc-mods/yosbr
            Dim YosbrFileAddress As String = McInstanceSelected.PathIndie & "config\yosbr\options.txt"
            If FileUtils.Exists(YosbrFileAddress) Then
                McLaunchLog("将修改 Yosbr Mod 中的 options.txt")
                SetupFileAddress = YosbrFileAddress
                WriteIni(SetupFileAddress, "lang", "none") '忽略默认语言
            End If
        End If
        Try
            '语言
            '1.0-     ：没有语言选项
            '1.1 ~ 5  ：zh_CN 时正常，zh_cn 时崩溃（最后两位字母必须大写，否则将会 NPE 崩溃）
            '1.6 ~ 10 ：zh_CN 时正常，zh_cn 时自动切换为英文
            '1.11 ~ 12：zh_cn 时正常，zh_CN 时虽然显示了中文但语言设置会错误地显示选择英文
            '1.13+    ：zh_cn 时正常，zh_CN 时自动切换为英文
            Dim CurrentLang As String = ReadIni(SetupFileAddress, "lang", "none")
            Dim RequiredLang As String = If(CurrentLang = "none" OrElse Not DirectoryUtils.Exists(McInstanceSelected.PathIndie & "saves"), '#3844，整合包可能已经自带了 options.txt
                If(Settings.Get(Of Boolean)("ToolHelpChinese"), "zh_cn", "en_us"), CurrentLang.Lower)
            If Not McInstanceSelected.Version.Valid OrElse McInstanceSelected.Version.Vanilla.Major <= 10 Then
                '将最后两位改为大写，前面的部分保留
                RequiredLang = RequiredLang.Substring(0, RequiredLang.Length - 2) & RequiredLang.Substring(RequiredLang.Length - 2).Upper
            End If
            If CurrentLang = RequiredLang Then
                McLaunchLog($"需要的语言为 {RequiredLang}，当前语言为 {CurrentLang}，无需修改")
            Else
                WriteIni(SetupFileAddress, "lang", "-") '触发缓存更改，避免删除后重新下载残留缓存
                WriteIni(SetupFileAddress, "lang", RequiredLang)
                McLaunchLog($"已将语言从 {CurrentLang} 修改为 {RequiredLang}")
            End If
            ''如果是初次设置，一并修改 forceUnicodeFont
            'If Settings.Get(Of Boolean)("ToolHelpChinese") AndAlso (CurrentLang = "none" OrElse Not DirectoryUtils.Exists(McInstanceSelected.PathIndie & "saves")) Then
            '    WriteIni(SetupFileAddress, "forceUnicodeFont", "true")
            '    McLaunchLog("已开启 forceUnicodeFont")
            'End If
            '窗口
            Select Case Settings.Get(Of Integer)("LaunchArgumentWindowType")
                Case 0 '全屏
                    WriteIni(SetupFileAddress, "fullscreen", "true")
                Case 1 '默认
                Case Else '其他
                    WriteIni(SetupFileAddress, "fullscreen", "false")
            End Select
        Catch ex As Exception
            Logger.Error(ex, "更新 options.txt 失败", LogBehavior.Toast)
        End Try

        '离线皮肤 Alex 警告
        If McInstanceSelected.Version.Vanilla.Major <= 7 AndAlso McInstanceSelected.Version.Vanilla.Major >= 2 AndAlso '1.2 ~ 1.7
               McLoginLoader.Input.Type = McLoginType.Legacy AndAlso '离线登录
               (Settings.Get(Of Integer)("LaunchSkinType") = 2 OrElse '强制 Alex
               (Settings.Get(Of Integer)("LaunchSkinType") = 4 AndAlso Settings.Get(Of Boolean)("LaunchSkinSlim"))) Then '或选用 Alex 皮肤
            Hint("此 Minecraft 版本尚不支持 Alex 皮肤，你的皮肤可能会显示为 Steve！", HintType.Red)
        End If

        '离线皮肤资源包
        Try
            Dim ZipFileAddress As String = McInstanceSelected.PathIndie & "resourcepacks\PCL2 Skin.zip"
            Dim NewTypeSetup As Boolean = McInstanceSelected.Version.Vanilla.Major >= 13 OrElse McInstanceSelected.Version.Vanilla.Major < 6
            If McLoginLoader.Input.Type = McLoginType.Legacy AndAlso Settings.Get(Of Integer)("LaunchSkinType") = 4 AndAlso FileUtils.Exists(Paths.AppDataThenName & "CustomSkin.png") Then
                Dim MetaFileAddress As String = PathTemp & "pack.mcmeta"
                Dim PackPicAddress As String = PathTemp & "pack.png"
                If McInstanceSelected.Version.Vanilla.Major < 6 Then
                    McLaunchLog("Minecraft 版本过老，尚不支持自定义离线皮肤")
                    GoTo IgnoreCustomSkin
                End If
                Dim PackVersionJToken As JToken = McInstanceSelected.JsonVersion?("pack_version")
                If TypeOf PackVersionJToken Is JObject Then PackVersionJToken = DirectCast(PackVersionJToken, JObject)("resource") '从 1.17 开始，可以从 pack_version.resource 获取资源包版本
                Dim PackVersion As Integer = Val(PackVersionJToken?.ToString)
                If PackVersion <= 0 Then '没有对应字段的老版本 MC，或兜底
                    Select Case McInstanceSelected.Version.Vanilla.Major
                        Case 6, 7, 8
                            PackVersion = 1
                        Case 9, 10
                            PackVersion = 2
                        Case 11, 12
                            PackVersion = 3
                        Case 13, 14
                            PackVersion = 4
                        Case 15
                            PackVersion = 5
                        Case 16
                            PackVersion = If(McInstanceSelected.Version.Vanilla.Build <= 1, 5, 6)
                        Case 17
                            PackVersion = 7
                        Case 18
                            PackVersion = 8
                        Case Else '旧版快照可能没有可用的资源包格式声明
                            PackVersion = 17
                    End Select
                End If
                McLaunchLog($"正在构建自定义皮肤资源包，版本号 {PackVersion}，MC 版本 {McInstanceSelected.Version.Vanilla}")
                '准备文件
                Dim Bit As New MyBitmap(PathImage & "Heads/Logo.png")
                Bit.Save(PackPicAddress)
                FileUtils.Write(MetaFileAddress, "{""pack"":{""pack_format"":" & PackVersion & ",""description"":""PCL 自定义离线皮肤资源包""}}")
                Dim Skin As New MyBitmap(Paths.AppDataThenName & "CustomSkin.png")
                If (McInstanceSelected.Version.Vanilla.Major = 6 OrElse McInstanceSelected.Version.Vanilla.Major = 7) AndAlso Skin.Pic.Height = 64 Then
                    McLaunchLog("该 Minecraft 版本不支持双层皮肤，已进行裁剪")
                    Skin = Skin.Clip(0, 0, 64, 32)
                End If
                Skin.Save(Paths.Base & "PCL\CustomSkin_Cliped.png")
                '构建压缩文件
                Dim FilesToCompress As New Dictionary(Of String, String) From {
                    {"pack.mcmeta", MetaFileAddress}, {"pack.png", PackPicAddress}}
                If McInstanceSelected.Version.Vanilla >= New Version(19, 0, 3) Then
                    '1.19.3+ 使用复杂版本的替换
                    For Each SkinName In {"alex", "ari", "efe", "kai", "makena", "noor", "steve", "sunny", "zuri"}
                        FilesToCompress.Add(
                                $"assets/minecraft/textures/entity/player/{If(Settings.Get(Of Boolean)("LaunchSkinSlim"), "slim", "wide")}/{SkinName}.png",
                                Paths.Base & "PCL\CustomSkin_Cliped.png")
                    Next
                Else
                    FilesToCompress.Add(
                            $"assets/minecraft/textures/entity/{If(Settings.Get(Of Boolean)("LaunchSkinSlim"), "alex.png", "steve.png")}",
                            Paths.Base & "PCL\CustomSkin_Cliped.png")
                End If
                FileUtils.CreateZipFromFiles(ZipFileAddress, FilesToCompress)
                FileUtils.Delete(Paths.Base & "PCL\CustomSkin_Cliped.png")
                '更改设置文件
                IniClearCache(SetupFileAddress)
                Dim EnabledResourcePack As String = ReadIni(SetupFileAddress, "resourcePacks", "[]").TrimStart("[").TrimEnd("]")
                If NewTypeSetup Then
                    If EnabledResourcePack = "" Then EnabledResourcePack = """vanilla"""
                    Dim EnabledResourcePacks As New List(Of String)(EnabledResourcePack.Split(","))
                    Dim NewResourcePacks As New List(Of String)
                    For Each Res In EnabledResourcePacks
                        If Res <> """file/PCL2 Skin.zip""" AndAlso Res <> "" Then NewResourcePacks.Add(Res)
                    Next
                    NewResourcePacks.Add("""file/PCL2 Skin.zip""")
                    Dim Result As String = "[" & NewResourcePacks.Join(","c) & "]"
                    WriteIni(SetupFileAddress, "resourcePacks", Result)
                Else
                    Dim EnabledResourcePacks As New List(Of String)(EnabledResourcePack.Split(","))
                    Dim NewResourcePacks As New List(Of String)
                    For Each Res In EnabledResourcePacks
                        If Res <> """PCL2 Skin.zip""" AndAlso Res <> "" Then NewResourcePacks.Add(Res)
                    Next
                    NewResourcePacks.Add("""PCL2 Skin.zip""")
                    Dim Result As String = "[" & NewResourcePacks.Join(","c) & "]"
                    WriteIni(SetupFileAddress, "resourcePacks", Result)
                End If
IgnoreCustomSkin:
            ElseIf FileUtils.Exists(ZipFileAddress) Then
                McLaunchLog("正在清空自定义皮肤资源包")
                '删除压缩文件
                FileUtils.Delete(ZipFileAddress)
                '更改设置文件
                IniClearCache(SetupFileAddress)
                Dim EnabledResourcePack As String = ReadIni(SetupFileAddress, "resourcePacks", "[]").TrimStart("[").TrimEnd("]")
                If NewTypeSetup Then
                    If EnabledResourcePack = "" Then EnabledResourcePack = """vanilla"""
                    Dim EnabledResourcePacks As New List(Of String)(EnabledResourcePack.Split(","))
                    EnabledResourcePacks.Remove("""file/PCL2 Skin.zip""")
                    Dim Result As String = "[" & EnabledResourcePacks.Join(","c) & "]"
                    WriteIni(SetupFileAddress, "resourcePacks", Result)
                Else
                    Dim EnabledResourcePacks As New List(Of String)(EnabledResourcePack.Split(","))
                    EnabledResourcePacks.Remove("""PCL2 Skin.zip""")
                    Dim Result As String = "[" & EnabledResourcePacks.Join(","c) & "]"
                    WriteIni(SetupFileAddress, "resourcePacks", Result)
                End If
            End If
        Catch ex As Exception
            Logger.Error(ex, "离线皮肤资源包设置失败", LogBehavior.Toast)
        End Try

    End Sub
    '逻辑已迁移至 PCLCS.Modules.PclLaunch.McLaunchCustom
    Private Sub McLaunchCustom(Loader As LoaderTask(Of Integer, Integer))
        PclLaunch.McLaunchCustom(Loader)
    End Sub
    '逻辑已迁移至 PCLCS.Modules.PclLaunch.McLaunchRun
    Private Sub McLaunchRun(Loader As LoaderTask(Of Integer, Process))
        PclLaunch.McLaunchRun(Loader)
    End Sub
    Private Sub McLaunchWait(Loader As LoaderTask(Of Process, Integer))

        '输出信息
        McLaunchLog("")
        McLaunchLog("~ 基础参数 ~")
        McLaunchLog("PCL 版本：" & VersionDisplay & " (" & VersionCode & ")")
        McLaunchLog($"游戏版本：{McInstanceSelected.VersionDisplayName}（{McInstanceSelected.Version.Vanilla}，Drop {McInstanceSelected.Version.Drop}{If(McInstanceSelected.Version.Reliable, "", "，无法完全确定")}）")
        McLaunchLog("资源版本：" & McAssetsGetIndexName(McInstanceSelected))
        McLaunchLog("版本继承：" & If(McInstanceSelected.InheritName = "", "无", McInstanceSelected.InheritName))
        McLaunchLog("分配的内存：" & PageInstanceSetup.GetRam(McInstanceSelected) & " GB（" & Math.Round(PageInstanceSetup.GetRam(McInstanceSelected) * 1024) & " MB）")
        McLaunchLog("MC 文件夹：" & McFolderSelected)
        McLaunchLog("版本文件夹：" & McInstanceSelected.PathVersion)
        McLaunchLog("版本隔离：" & (McInstanceSelected.PathIndie = McInstanceSelected.PathVersion))
        McLaunchLog("HMCL 格式：" & McInstanceSelected.IsHmclFormatJson)
        McLaunchLog("Java 信息：" & If(McLaunchJavaSelected IsNot Nothing, McLaunchJavaSelected.ToString, "无可用 Java"))
        McLaunchLog("Natives 文件夹：" & GetNativesFolder())
        McLaunchLog("")
        McLaunchLog("~ 登录参数 ~")
        McLaunchLog("玩家用户名：" & McLoginLoader.Output.Name)
        McLaunchLog("AccessToken：" & McLoginLoader.Output.AccessToken)
        McLaunchLog("ClientToken：" & McLoginLoader.Output.ClientToken)
        McLaunchLog("UUID：" & McLoginLoader.Output.Uuid)
        McLaunchLog("登录方式：" & McLoginLoader.Output.Type)
        McLaunchLog("")

        '获取窗口标题
        Dim WindowTitle As String = Settings.Get(Of String)("VersionArgumentTitle", Instance:=McInstanceSelected)
        If WindowTitle = "" Then WindowTitle = Settings.Get(Of String)("LaunchArgumentTitle")
        WindowTitle = ArgumentReplace(WindowTitle, ReplaceTime:=False)

        '初始化等待
        Dim Watcher As New Watcher(Loader, McInstanceSelected, WindowTitle)
        McLaunchWatcher = Watcher

        '等待
        Do While Watcher.State = Watcher.MinecraftState.Loading
            Thread.Sleep(100)
        Loop
        If Watcher.State = Watcher.MinecraftState.Crashed Then
            Throw New OperationCanceledException
        End If

    End Sub
    Private Sub McLaunchEnd()
        McLaunchLog("开始启动结束处理")

        '暂停或开始音乐播放
        If Settings.Get(Of Boolean)("UiMusicStop") Then
            RunInUi(Sub() If MusicPause() Then Logger.Info("已根据设置，在启动后暂停音乐播放"))
        ElseIf Settings.Get(Of Boolean)("UiMusicStart") Then
            RunInUi(Sub() If MusicResume() Then Logger.Info("已根据设置，在启动后开始音乐播放"))
        End If

        '启动器可见性
        McLaunchLog("启动器可见性：" & Settings.Get(Of Integer)("LaunchArgumentVisible"))
        Select Case Settings.Get(Of Integer)("LaunchArgumentVisible")
            Case 0
                '直接关闭
                McLaunchLog("已根据设置，在启动后关闭启动器")
                RunInUi(Sub() FrmMain.EndProgram(False))
            Case 2, 3
                '隐藏
                McLaunchLog("已根据设置，在启动后隐藏启动器")
                RunInUi(Sub() FrmMain.Hidden = True)
            Case 4
                '最小化
                McLaunchLog("已根据设置，在启动后最小化启动器")
                RunInUi(Sub() FrmMain.WindowState = WindowState.Minimized)
            Case 5
                '啥都不干
        End Select

        '启动计数
        Settings.Set("SystemLaunchCount", Settings.Get(Of Integer)("SystemLaunchCount") + 1)

    End Sub

#End Region

End Module
