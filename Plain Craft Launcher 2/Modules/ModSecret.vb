'由于包含加解密等安全信息，本文件的部分代码已被删除。
'CurseForge API Key 与微软登录 ClientID 的注入入口已迁移至 PCLCS\Modules\ModSecret.cs，
'通过环境变量 PCL_CURSEFORGE_API_KEY 与 PCL_MS_CLIENT_ID 提供。
'其余成员保留为同名转发壳，具体实现位于 PCLCS\Modules\ModSecret.cs。

Friend Module ModSecret

    '标注 PCL 的不同分支，仅用于替换标记
    Public ReadOnly Property VersionBranchMain As String
        Get
            Return PCLCS.Modules.ModSecret.VersionBranchMain
        End Get
    End Property
    '在开源版的注册表与常规版的注册表隔离，以防数据冲突
    Public ReadOnly Property RegFolder As String
        Get
            Return PCLCS.Modules.ModSecret.RegFolder
        End Get
    End Property
    '用于微软登录的 ClientId
    Public Property OAuthClientId As String
        Get
            Return PCLCS.Modules.ModSecret.OAuthClientId
        End Get
        Set(value As String)
            PCLCS.Modules.ModSecret.OAuthClientId = value
        End Set
    End Property
    'CurseForge API Key
    Public Property CurseForgeAPIKey As String
        Get
            Return PCLCS.Modules.ModSecret.CurseForgeAPIKey
        End Get
        Set(value As String)
            PCLCS.Modules.ModSecret.CurseForgeAPIKey = value
        End Set
    End Property
    '用于匿名数据收集的腾讯云日志服务上报 URL，形如 https://{region}.cls.tencentcs.com/track?topic_id={topic_id}
    Public ReadOnly Property ClsBaseUrl As String
        Get
            Return PCLCS.Modules.ModSecret.ClsBaseUrl
        End Get
    End Property

#Region "网络鉴权"

    Friend Function SecretCdnSign(UrlWithMark As String) As String
        Return PCLCS.Modules.ModSecret.SecretCdnSign(UrlWithMark)
    End Function
    ''' <summary>
    ''' 设置 Headers 的 UA、Referer。
    ''' </summary>
    Friend Sub SecretHeadersSign(Url As String, ByRef Req As HttpRequestMessage, Optional SimulateBrowserHeaders As Boolean = False)
        PCLCS.Modules.ModSecret.SecretHeadersSign(Url, Req, SimulateBrowserHeaders)
    End Sub

#End Region

#Region "主题"

    Public Property Color1 As MyColor
        Get
            Return PCLCS.Modules.ModSecret.Color1
        End Get
        Set(value As MyColor)
            PCLCS.Modules.ModSecret.Color1 = value
        End Set
    End Property
    Public Property Color2 As MyColor
        Get
            Return PCLCS.Modules.ModSecret.Color2
        End Get
        Set(value As MyColor)
            PCLCS.Modules.ModSecret.Color2 = value
        End Set
    End Property
    Public Property Color3 As MyColor
        Get
            Return PCLCS.Modules.ModSecret.Color3
        End Get
        Set(value As MyColor)
            PCLCS.Modules.ModSecret.Color3 = value
        End Set
    End Property
    Public Property Color4 As MyColor
        Get
            Return PCLCS.Modules.ModSecret.Color4
        End Get
        Set(value As MyColor)
            PCLCS.Modules.ModSecret.Color4 = value
        End Set
    End Property
    Public Property Color5 As MyColor
        Get
            Return PCLCS.Modules.ModSecret.Color5
        End Get
        Set(value As MyColor)
            PCLCS.Modules.ModSecret.Color5 = value
        End Set
    End Property
    Public Property Color6 As MyColor
        Get
            Return PCLCS.Modules.ModSecret.Color6
        End Get
        Set(value As MyColor)
            PCLCS.Modules.ModSecret.Color6 = value
        End Set
    End Property
    Public Property Color7 As MyColor
        Get
            Return PCLCS.Modules.ModSecret.Color7
        End Get
        Set(value As MyColor)
            PCLCS.Modules.ModSecret.Color7 = value
        End Set
    End Property
    Public Property Color8 As MyColor
        Get
            Return PCLCS.Modules.ModSecret.Color8
        End Get
        Set(value As MyColor)
            PCLCS.Modules.ModSecret.Color8 = value
        End Set
    End Property
    Public Property ColorBg0 As MyColor
        Get
            Return PCLCS.Modules.ModSecret.ColorBg0
        End Get
        Set(value As MyColor)
            PCLCS.Modules.ModSecret.ColorBg0 = value
        End Set
    End Property
    Public Property ColorBg1 As MyColor
        Get
            Return PCLCS.Modules.ModSecret.ColorBg1
        End Get
        Set(value As MyColor)
            PCLCS.Modules.ModSecret.ColorBg1 = value
        End Set
    End Property
    Public Property ColorGray1 As MyColor
        Get
            Return PCLCS.Modules.ModSecret.ColorGray1
        End Get
        Set(value As MyColor)
            PCLCS.Modules.ModSecret.ColorGray1 = value
        End Set
    End Property
    Public Property ColorGray2 As MyColor
        Get
            Return PCLCS.Modules.ModSecret.ColorGray2
        End Get
        Set(value As MyColor)
            PCLCS.Modules.ModSecret.ColorGray2 = value
        End Set
    End Property
    Public Property ColorGray3 As MyColor
        Get
            Return PCLCS.Modules.ModSecret.ColorGray3
        End Get
        Set(value As MyColor)
            PCLCS.Modules.ModSecret.ColorGray3 = value
        End Set
    End Property
    Public Property ColorGray4 As MyColor
        Get
            Return PCLCS.Modules.ModSecret.ColorGray4
        End Get
        Set(value As MyColor)
            PCLCS.Modules.ModSecret.ColorGray4 = value
        End Set
    End Property
    Public Property ColorGray5 As MyColor
        Get
            Return PCLCS.Modules.ModSecret.ColorGray5
        End Get
        Set(value As MyColor)
            PCLCS.Modules.ModSecret.ColorGray5 = value
        End Set
    End Property
    Public Property ColorGray6 As MyColor
        Get
            Return PCLCS.Modules.ModSecret.ColorGray6
        End Get
        Set(value As MyColor)
            PCLCS.Modules.ModSecret.ColorGray6 = value
        End Set
    End Property
    Public Property ColorGray7 As MyColor
        Get
            Return PCLCS.Modules.ModSecret.ColorGray7
        End Get
        Set(value As MyColor)
            PCLCS.Modules.ModSecret.ColorGray7 = value
        End Set
    End Property
    Public Property ColorGray8 As MyColor
        Get
            Return PCLCS.Modules.ModSecret.ColorGray8
        End Get
        Set(value As MyColor)
            PCLCS.Modules.ModSecret.ColorGray8 = value
        End Set
    End Property
    Public Property ColorSemiTransparent As MyColor
        Get
            Return PCLCS.Modules.ModSecret.ColorSemiTransparent
        End Get
        Set(value As MyColor)
            PCLCS.Modules.ModSecret.ColorSemiTransparent = value
        End Set
    End Property

    Public Property ThemeNow As Integer
        Get
            Return PCLCS.Modules.ModSecret.ThemeNow
        End Get
        Set(value As Integer)
            PCLCS.Modules.ModSecret.ThemeNow = value
        End Set
    End Property
    Public Property ColorHue As Integer
        Get
            Return PCLCS.Modules.ModSecret.ColorHue
        End Get
        Set(value As Integer)
            PCLCS.Modules.ModSecret.ColorHue = value
        End Set
    End Property
    Public Property ColorSat As Integer
        Get
            Return PCLCS.Modules.ModSecret.ColorSat
        End Get
        Set(value As Integer)
            PCLCS.Modules.ModSecret.ColorSat = value
        End Set
    End Property
    Public Property ColorLightAdjust As Integer
        Get
            Return PCLCS.Modules.ModSecret.ColorLightAdjust
        End Get
        Set(value As Integer)
            PCLCS.Modules.ModSecret.ColorLightAdjust = value
        End Set
    End Property
    Public Property ColorHueTopbarDelta As OneOf(Of Integer, Integer())
        Get
            Return PCLCS.Modules.ModSecret.ColorHueTopbarDelta
        End Get
        Set(value As OneOf(Of Integer, Integer()))
            PCLCS.Modules.ModSecret.ColorHueTopbarDelta = value
        End Set
    End Property
    Public Property ThemeDontClick As Integer
        Get
            Return PCLCS.Modules.ModSecret.ThemeDontClick
        End Get
        Set(value As Integer)
            PCLCS.Modules.ModSecret.ThemeDontClick = value
        End Set
    End Property

    Public Sub ThemeRefresh(Optional NewTheme As Integer = -1)
        PCLCS.Modules.ModSecret.ThemeRefresh(NewTheme)
    End Sub
    Public Sub ThemeRefreshMain()
        RunInUi(
        Sub()
            If Not FrmMain.IsLoaded Then Return
            '顶部条背景
            Dim Brush = New LinearGradientBrush With {.EndPoint = New Point(1, 0), .StartPoint = New Point(0, 0)}
            Dim Deltas = ColorHueTopbarDelta.Switch(Function(d) New Integer() {-d, 0, d}, Function(d) d)
            Brush.GradientStops.Add(New GradientStop With {.Offset = 0, .Color = New MyColor().FromHSL2(ColorHue + Deltas(0), ColorSat, 48 + ColorLightAdjust)})
            Brush.GradientStops.Add(New GradientStop With {.Offset = 0.5, .Color = New MyColor().FromHSL2(ColorHue + Deltas(1), ColorSat, 54 + ColorLightAdjust)})
            Brush.GradientStops.Add(New GradientStop With {.Offset = 1, .Color = New MyColor().FromHSL2(ColorHue + Deltas(2), ColorSat, 48 + ColorLightAdjust)})
            FrmMain.PanTitle.Background = Brush
            FrmMain.PanTitle.Background.Freeze()
            '主页面背景
            If Settings.Get(Of Boolean)("UiBackgroundColorful") Then
                Brush = New LinearGradientBrush With {.EndPoint = New Point(0.1, 1), .StartPoint = New Point(0.9, 0)}
                Brush.GradientStops.Add(New GradientStop With {.Offset = -0.1, .Color = New MyColor().FromHSL2(ColorHue - 15, ColorSat * 0.8, 91)})
                Brush.GradientStops.Add(New GradientStop With {.Offset = 0.4, .Color = New MyColor().FromHSL2(ColorHue, ColorSat * 0.8, 91)})
                Brush.GradientStops.Add(New GradientStop With {.Offset = 1.1, .Color = New MyColor().FromHSL2(ColorHue + 15, ColorSat * 0.8, 91)})
                FrmMain.PanForm.Background = Brush
            Else
                FrmMain.PanForm.Background = New MyColor(245, 245, 245)
            End If
            FrmMain.PanForm.Background.Freeze()
        End Sub)
    End Sub
    Friend Sub ThemeCheckAll(EffectSetup As Boolean)
        PCLCS.Modules.ModSecret.ThemeCheckAll(EffectSetup)
    End Sub
    Friend Function ThemeCheckOne(Id As Integer) As Boolean
        Return PCLCS.Modules.ModSecret.ThemeCheckOne(Id)
    End Function
    Friend Function ThemeUnlock(Id As Integer, Optional ShowDoubleHint As Boolean = True, Optional UnlockHint As String = Nothing) As Boolean
        Return PCLCS.Modules.ModSecret.ThemeUnlock(Id, ShowDoubleHint, UnlockHint)
    End Function

#End Region

#Region "更新"

    Friend Sub UpdateCheckByButton()
        PCLCS.Modules.ModSecret.UpdateCheckByButton()
    End Sub

    Public Property IsUpdateWaitingRestart As Boolean
        Get
            Return PCLCS.Modules.ModSecret.IsUpdateWaitingRestart
        End Get
        Set(value As Boolean)
            PCLCS.Modules.ModSecret.IsUpdateWaitingRestart = value
        End Set
    End Property
    Public Sub UpdateRestart(TriggerRestartAndByEnd As Boolean)
        PCLCS.Modules.ModSecret.UpdateRestart(TriggerRestartAndByEnd)
    End Sub
    Public Sub UpdateReplace(ProcessId As Integer, OldFileName As String, NewFileName As String, TriggerRestart As Boolean)
        PCLCS.Modules.ModSecret.UpdateReplace(ProcessId, OldFileName, NewFileName, TriggerRestart)
    End Sub

    ''' <summary>
    ''' 确保 PathTemp/Latest.exe 是最新正式版的 PCL，它会被用于整合包打包。
    ''' 如果不是，则下载一个。
    ''' </summary>
    Friend Sub DownloadLatestPCL(Optional LoaderToSyncProgress As LoaderBase = Nothing)
        PCLCS.Modules.ModSecret.DownloadLatestPCL(LoaderToSyncProgress)
    End Sub

#End Region

#Region "联网配置"

    ''' <summary>
    ''' 联网获取的配置信息。
    ''' 若获取失败或仍在获取中，可能为 Nothing。
    ''' </summary>
    Public Property ServerConfig As JObject
        Get
            Return PCLCS.Modules.ModSecret.ServerConfig
        End Get
        Set(value As JObject)
            PCLCS.Modules.ModSecret.ServerConfig = value
        End Set
    End Property

    Public Property ServerLoader As LoaderTask(Of Integer, Integer)
        Get
            Return PCLCS.Modules.ModSecret.ServerLoader
        End Get
        Set(value As LoaderTask(Of Integer, Integer))
            PCLCS.Modules.ModSecret.ServerLoader = value
        End Set
    End Property

#End Region

#Region "赞助等级"

    Public ReadOnly Property CurrentRank As DonationRank
        Get
            Return PCLCS.Modules.ModSecret.CurrentRank
        End Get
    End Property

    Public Sub InputPotatoCode(IsUpdating As Boolean)
        PCLCS.Modules.ModSecret.InputPotatoCode(IsUpdating)
    End Sub
    Friend Sub GeneratePotatoCode()
        PCLCS.Modules.ModSecret.GeneratePotatoCode()
    End Sub

    ''' <summary>
    ''' 获取设备识别码。
    ''' </summary>
    Friend Function GetIdentify() As String
        Return PCLCS.Modules.ModSecret.GetIdentify()
    End Function

#End Region

End Module
