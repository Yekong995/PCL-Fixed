Imports MeloongCore
Imports System.Runtime.CompilerServices

Public Module ModMinecraft

#Region "文件夹"

    ''' <summary>
    ''' 当前的 Minecraft 文件夹路径，以 \ 结尾。
    ''' </summary>
    Public Property McFolderSelected As String
        Get
            If _McFolderSelected Is Nothing Then SetMcFolder(Settings.Get(Of String)("LaunchFolderSelect")) '不能使用 McFolderSelected 触发 Set，这会被视为为 Get 设置返回值
            Return _McFolderSelected
        End Get
        Set(Value As String)
            SetMcFolder(Value)
        End Set
    End Property
    Private Sub SetMcFolder(Value As String) '等同 McFolderSelected.Set
        If PathUtils.IsSame(_McFolderSelected, Value.Replace("$", Paths.Base)) Then Return
        _McFolderSelected = Value.Replace("$", Paths.Base)
        _McFolderSelected = PathUtils.AddSlashSuffix(PathUtils.ForCompare(_McFolderSelected))
        Settings.Set("LaunchFolderSelect", Value.Replace(Paths.Base, "$"))
        Logger.Info($"当前选择的 Minecraft 文件夹：{_McFolderSelected}")
    End Sub
    Private _McFolderSelected As String
    ''' <summary>
    ''' 当前的 Minecraft 文件夹列表。
    ''' </summary>
    Public McFolderList As New List(Of McFolder)

    ''' <summary>
    ''' 加载 Minecraft 文件夹列表。
    ''' </summary>
    Public McFolderListLoader As New LoaderTask(Of Integer, Integer)("Minecraft Folder List", AddressOf McFolderListLoadSub, Priority:=ThreadPriority.AboveNormal)
    Private Sub McFolderListLoadSub()
        Try
            '初始化
            Dim CacheMcFolderList = New List(Of McFolder)

#Region "读取默认（Original）文件夹，即当前、官启文件夹，可能没有结果"

            '扫描当前文件夹
            Try
                If DirectoryUtils.Exists(Paths.Base & "versions\") Then CacheMcFolderList.Add(New McFolder With {.Name = "当前文件夹", .Location = Paths.Base, .Type = McFolder.Types.Vanilla})
                For Each Folder As DirectoryInfo In DirectoryUtils.GetInfo(Paths.Base).GetDirectories
                    If DirectoryUtils.Exists(Folder.FullName & "versions\") OrElse Folder.Name = ".minecraft" Then
                        CacheMcFolderList.Add(New McFolder With {.Name = "当前文件夹", .Location = PathUtils.AddSlashSuffix(PathUtils.ForCompare(Folder.FullName)), .Type = McFolder.Types.Vanilla})
                    End If
                Next
            Catch ex As Exception
                Logger.Warn(ex, "扫描 PCL 所在文件夹中是否有 MC 文件夹失败")
            End Try

            '扫描官启文件夹
            Dim MojangPath As String = Paths.AppData & ".minecraft\"
            If (Not CacheMcFolderList.Any OrElse Not PathUtils.IsSame(MojangPath, CacheMcFolderList(0).Location)) AndAlso '当前文件夹不是官启文件夹
                DirectoryUtils.Exists(MojangPath & "versions\") Then '具有权限且存在 versions 文件夹
                CacheMcFolderList.Add(New McFolder With {.Name = "官方启动器文件夹", .Location = MojangPath, .Type = McFolder.Types.Vanilla})
            End If

#End Region

#Region "读取自定义（Custom）文件夹，可能没有结果"

            '格式：TMZ 12>C://xxx/xx/|Test>D://xxx/xx/|名称>路径
            For Each Folder As String In Settings.Get(Of String)("LaunchFolders").ToString.Split("|"c, True)
                If Folder = "" Then Continue For
                If Not Folder.Contains(">") OrElse Not Folder.EndsWithF("\") Then
                    Hint("无效的 Minecraft 文件夹：" & Folder, HintType.Red)
                    Continue For
                End If
                Dim FolderName As String = Folder.BeforeFirst(">"), FolderPath As String = Folder.AfterFirst(">")
                Try
                    CheckPermissionWithException(FolderPath)
                    '若已有该文件夹，则直接重命名；没有则添加
                    Dim Renamed As Boolean = False
                    For Each OriginalFolder As McFolder In CacheMcFolderList
                        If PathUtils.IsSame(OriginalFolder.Location, FolderPath) Then
                            OriginalFolder.Name = FolderName
                            OriginalFolder.Type = McFolder.Types.RenamedVanilla
                            Renamed = True
                        End If
                    Next
                    If Not Renamed Then CacheMcFolderList.Add(New McFolder With {.Name = FolderName, .Location = PathUtils.AddSlashSuffix(PathUtils.ForCompare(FolderPath)), .Type = McFolder.Types.Custom})
                Catch ex As Exception
                    MyMsgBox("失效的 Minecraft 文件夹：" & vbCrLf & FolderPath & vbCrLf & vbCrLf & ex.GetDisplay(False), "Minecraft 文件夹失效", IsWarn:=True)
                    Logger.Warn(ex, $"无法访问 Minecraft 文件夹 {FolderPath}")
                End Try
            Next

            '将自定义文件夹情况同步到设置
            Dim NewSetup As New List(Of String)
            For Each Folder As McFolder In CacheMcFolderList
                If Not Folder.Type = McFolder.Types.Vanilla Then NewSetup.Add(Folder.Name & ">" & Folder.Location)
            Next
            If Not NewSetup.Any() Then NewSetup.Add("") '防止 0 元素 Join 返回 Nothing
            Settings.Set("LaunchFolders", NewSetup.Join("|"c))

#End Region

            '若没有可用文件夹，则创建 .minecraft
            If Not CacheMcFolderList.Any() Then
                DirectoryUtils.Create(Paths.Base & ".minecraft\versions\")
                CacheMcFolderList.Add(New McFolder With {.Name = "当前文件夹", .Location = Paths.Base & ".minecraft\", .Type = McFolder.Types.Vanilla})
            End If

            For Each Folder As McFolder In CacheMcFolderList
                McFolderLauncherProfilesJsonCreate(Folder.Location)
            Next
            If Settings.Get(Of Boolean)("SystemDebugDelay") Then Thread.Sleep(RandomInteger(200, 2000))

            '回设
            McFolderList = CacheMcFolderList

        Catch ex As Exception
            Logger.Error(ex, "加载 Minecraft 文件夹列表失败")
        End Try
    End Sub

    ''' <summary>
    ''' 为 Minecraft 文件夹创建 launcher_profiles.json 文件。
    ''' </summary>
    Public Sub McFolderLauncherProfilesJsonCreate(Folder As String)
        PclMc.McFolderLauncherProfilesJsonCreate(Folder)
    End Sub

#End Region

#Region "版本处理"

    Private _McInstanceSelected As McInstance
    ''' <summary>
    ''' 当前的 Minecraft 版本。
    ''' </summary>
    Public Property McInstanceSelected As McInstance
        Get
            Return _McInstanceSelected
        End Get
        Set(value As McInstance)
            Static _McInstanceSelectedLast As Object = 0 '为 0 以保证与 Nothing 不相同，使得 UI 显示可以正常初始化
            If ReferenceEquals(_McInstanceSelectedLast, value) Then Return
            _McInstanceSelected = value '由于有可能是 Nothing，导致无法初始化，才得这样弄一圈
            _McInstanceSelectedLast = value
            '更新 MC 文件夹所选取的 Minecraft 版本
            Logger.Info($"当前选择的 Minecraft 版本：{If(value?.PathVersion, "无")}")
            WriteIni(McFolderSelected & "PCL.ini", "Version", If(value?.Name, ""))
            If value Is Nothing Then Return
            '重置缓存的下载文件夹
            PageDownloadResourceDetail.CachedFolder.Clear()
            '统一通行证重判
            If AniControlEnabled = 0 AndAlso
               Settings.Get(Of String)("VersionServerNide", Instance:=value) <> Settings.Get(Of String)("CacheNideServer") AndAlso
               Settings.Get(Of Integer)("VersionServerLogin", Instance:=value) = 3 Then
                Settings.Set("CacheNideAccess", "")
                Logger.Info("服务器改变，要求重新登录统一通行证")
            End If
            If Settings.Get(Of Integer)("VersionServerLogin", Instance:=value) = 3 Then
                Settings.Set("CacheNideServer", Settings.Get(Of String)("VersionServerNide", Instance:=value))
            End If
            'Authlib-Injector 重判
            If AniControlEnabled = 0 AndAlso
               Settings.Get(Of String)("VersionServerAuthServer", Instance:=value) <> Settings.Get(Of String)("CacheAuthServerServer") AndAlso
               Settings.Get(Of Integer)("VersionServerLogin", Instance:=value) = 4 Then
                Settings.Set("CacheAuthAccess", "")
                Logger.Info("服务器改变，要求重新登录 Authlib-Injector")
            End If
            If Settings.Get(Of Integer)("VersionServerLogin", Instance:=value) = 4 Then
                Settings.Set("CacheAuthServerServer", Settings.Get(Of String)("VersionServerAuthServer", Instance:=value))
                Settings.Set("CacheAuthServerName", Settings.Get(Of String)("VersionServerAuthName", Instance:=value))
                Settings.Set("CacheAuthServerRegister", Settings.Get(Of String)("VersionServerAuthRegister", Instance:=value))
            End If
        End Set
    End Property

    ''' <summary>
    ''' 生成版本的列表项。
    ''' 由于依赖 VB UI 类型（MyListItem、MyVirtualizingElement、MyColor），此成员保留在 VB 侧，以扩展方法形式提供，调用点无需改动。
    ''' </summary>
    <Extension()>
    Public Function ToListItem(Instance As McInstance, Optional ContentHandler As Action(Of MyListItem, EventArgs) = Nothing) As MyVirtualizingElement(Of MyListItem)
        Return New MyVirtualizingElement(Of MyListItem)(
        Function()
            Dim NewItem As New MyListItem With {
                .Info = Instance.Info, .Height = 42, .Tag = Instance, .SnapsToDevicePixels = True, .Type = MyListItem.CheckType.Clickable,
                .ContentHandler = ContentHandler
            }
            '标题
            NewItem.Inlines.Clear()
            NewItem.Inlines.Add(New Run(Instance.Name))
            If ReadIni(Instance.PathVersion & "PCL\Setup.ini", "CustomInfo") <> "" Then '如果版本设置了自定义描述，在标题后面以淡灰色显示其版本号
                NewItem.Inlines.Add(New Run("  |  " & Instance.VersionDisplayName()) With {.Foreground = New MyColor(215, 215, 215), .FontSize = 12})
            End If
            'Logo
            Try
                If Instance.Logo.EndsWithF("PCL\Logo.png") Then
                    NewItem.Logo = Instance.PathVersion & "PCL\Logo.png" '修复老版本中，存储的自定义 Logo 使用完整路径，导致移动后无法加载的 Bug
                Else
                    NewItem.Logo = Instance.Logo
                End If
            Catch ex As Exception
                Logger.Error(ex, "加载版本图标失败", LogBehavior.Toast)
                NewItem.Logo = "pack://application:,,,/images/Blocks/RedstoneBlock.png"
            End Try
            Return NewItem
        End Function) With {.Height = 42}
    End Function

    ''' <summary>
    ''' 根据版本名获取对应的愚人节版本描述。非愚人节版本会返回空字符串。
    ''' </summary>
    Public Function GetMcFoolName(Name As String) As String
        Return PclMc.GetMcFoolName(Name)
    End Function

    ''' <summary>
    ''' 当前按卡片分类的所有版本列表。
    ''' </summary>
    Public Property McInstanceList As Dictionary(Of McInstanceCardType, List(Of McInstance))
        Get
            Return PclMc.McInstanceList
        End Get
        Set(value As Dictionary(Of McInstanceCardType, List(Of McInstance)))
            PclMc.McInstanceList = value
        End Set
    End Property

#End Region

#Region "版本列表加载"

    ''' <summary>
    ''' 是否要求本次加载强制刷新版本列表。
    ''' </summary>
    Public Property McInstanceListForceRefresh As Boolean
        Get
            Return PclMc.McInstanceListForceRefresh
        End Get
        Set(value As Boolean)
            PclMc.McInstanceListForceRefresh = value
        End Set
    End Property

    ''' <summary>
    ''' 加载 Minecraft 文件夹的版本列表。
    ''' </summary>
    Public McInstanceListLoader As New LoaderTask(Of String, Integer)("Minecraft Instance List", AddressOf InitMcInstanceList) With {.ReloadTimeout = 1}
    ''' <summary>
    ''' 加载 Minecraft 文件夹的版本列表。（已迁移至 PclMc.InitMcInstanceList）
    ''' </summary>
    Private Sub InitMcInstanceList(Loader As LoaderTask(Of String, Integer))
        PclMc.InitMcInstanceList(Loader)
    End Sub

#End Region

#Region "皮肤"

    ''' <summary>
    ''' 要求玩家选择一个皮肤文件，并进行相关校验。
    ''' </summary>
    Public Function McSkinSelect() As McSkinInfo
        Dim FileName As String = Dialogs.SelectFile("选择皮肤文件", False, filter:={({"png", "jpg", "jpeg", "webp"}, "皮肤文件")}).FirstOrDefault()

        '验证有效性
        If String.IsNullOrEmpty(FileName) Then Return New McSkinInfo With {.IsVaild = False}
        Try
            Dim Image As New MyBitmap(FileName)
            If Image.Pic.Width <> 64 OrElse Not (Image.Pic.Height = 32 OrElse Image.Pic.Height = 64) Then
                Hint("皮肤图片大小应为 64x32 像素或 64x64 像素！", HintType.Red)
                Return New McSkinInfo With {.IsVaild = False}
            End If
            Dim FileInfo = FileUtils.GetInfo(FileName)
            If FileInfo.Length > 24 * 1024 Then
                Hint("皮肤文件大小需小于 24 KB，而所选文件大小为 " & Math.Round(FileInfo.Length / 1024, 2) & " KB", HintType.Red)
                Return New McSkinInfo With {.IsVaild = False}
            End If
        Catch ex As Exception
            Logger.Error(ex, "皮肤文件存在错误", LogBehavior.Toast)
            Return New McSkinInfo With {.IsVaild = False}
        End Try

        '获取皮肤种类
        Dim IsSlim As Integer = MyMsgBox("此皮肤为 Steve 模型（粗手臂）还是 Alex 模型（细手臂）？", "选择皮肤种类", "Steve 模型", "Alex 模型", "我不知道", HighLight:=False)
        If IsSlim = 3 Then
            Hint("请在皮肤下载页面确认皮肤种类后再使用此皮肤！")
            Return New McSkinInfo With {.IsVaild = False}
        End If

        Return New McSkinInfo With {.IsVaild = True, .IsSlim = IsSlim = 2, .LocalFile = FileName}
    End Function

    ''' <summary>
    ''' 获取 Uuid 对应的皮肤文件地址，失败将抛出异常。
    ''' </summary>
    Public Function McSkinGetAddress(UUID As String, Type As String) As String
        Return PclMc.McSkinGetAddress(UUID, Type)
    End Function

    ''' <summary>
    ''' 从 Url 下载皮肤，可能会使用缓存。返回本地文件路径，失败将抛出异常。
    ''' </summary>
    Public Function McSkinDownload(Address As String) As String
        Return PclMc.McSkinDownload(Address)
    End Function

    ''' <summary>
    ''' 获取 Uuid 对应的皮肤，返回“Steve”或“Alex”。
    ''' </summary>
    Public Function McSkinSex(Uuid As String) As String
        Return PclMc.McSkinSex(Uuid)
    End Function

#End Region

#Region "支持库文件（Library）"

    ''' <summary>
    ''' 检查是否符合 Json 中的 Rules。
    ''' </summary>
    ''' <param name="RuleToken">Json 中的 "rules" 项目。</param>
    Public Function McJsonRuleCheck(RuleToken As JToken) As Boolean
        Return PclMc.McJsonRuleCheck(RuleToken)
    End Function

    ''' <summary>
    ''' 递归获取 Minecraft 某一版本的完整支持库列表。
    ''' </summary>
    Public Function McLibListGet(Instance As McInstance, IncludeMainJar As Boolean) As List(Of McLibEntry)
        Return PclMc.McLibListGet(Instance, IncludeMainJar)
    End Function
    ''' <summary>
    ''' 获取 Minecraft 某一版本忽视继承的支持库列表，即结果中没有继承项。
    ''' </summary>
    Public Function McLibListGetWithJson(JsonObject As JObject, Optional KeepSameNameDifferentVersionResult As Boolean = False, Optional CustomMcFolder As String = Nothing) As List(Of McLibEntry)
        Return PclMc.McLibListGetWithJson(JsonObject, KeepSameNameDifferentVersionResult, CustomMcFolder)
    End Function

    ''' <summary>
    ''' 获取版本所需支持库文件的 NetFile。
    ''' </summary>
    Public Function McLibNetFilesFromInstance(Instance As McInstance) As List(Of NetFile)
        Return PclMc.McLibNetFilesFromInstance(Instance)
    End Function
    ''' <summary>
    ''' 将 McLibToken 列表转换为 NetFile。
    ''' </summary>
    Public Function McLibNetFilesFromTokens(Libs As List(Of McLibEntry), Optional CustomMcFolder As String = Nothing) As List(Of NetFile)
        Return PclMc.McLibNetFilesFromTokens(Libs, CustomMcFolder)
    End Function
    ''' <summary>
    ''' 获取对应的支持库文件地址。
    ''' </summary>
    ''' <param name="Original">原始地址，如 com.mumfrey:liteloader:1.12.2-SNAPSHOT。</param>
    ''' <param name="WithHead">是否包含 Lib 文件夹头部，若不包含，则会类似以 com\xxx\ 开头。</param>
    Public Function McLibGet(Original As String, Optional WithHead As Boolean = True, Optional IgnoreLiteLoader As Boolean = False, Optional CustomMcFolder As String = Nothing) As String
        Return PclMc.McLibGet(Original, WithHead, IgnoreLiteLoader, CustomMcFolder)
    End Function

    ''' <summary>
    ''' 检查设置，是否应当忽略文件检查？
    ''' </summary>
    Public Function ShouldIgnoreFileCheck(Instance As McInstance)
        Return PclMc.ShouldIgnoreFileCheck(Instance)
    End Function

#End Region

#Region "资源文件（Assets）"

    '获取索引
    ''' <summary>
    ''' 获取某版本资源文件索引的对应 Json 项，详见版本 Json 中的 assetIndex 项。失败会抛出异常。
    ''' </summary>
    Public Function McAssetsGetIndex(Instance As McInstance, Optional ReturnLegacyOnError As Boolean = False, Optional CheckURLEmpty As Boolean = False) As JToken
        Return PclMc.McAssetsGetIndex(Instance, ReturnLegacyOnError, CheckURLEmpty)
    End Function
    ''' <summary>
    ''' 获取某版本资源文件索引名，优先使用 assetIndex，其次使用 assets。失败会返回 legacy。
    ''' </summary>
    Public Function McAssetsGetIndexName(Instance As McInstance) As String
        Return PclMc.McAssetsGetIndexName(Instance)
    End Function


    ''' <summary>
    ''' 获取版本资源文件所对应的 NetFile。
    ''' </summary>
    Public Function McAssetsFixList(Instance As McInstance, CheckHash As Boolean, Optional ByRef ProgressFeed As LoaderBase = Nothing) As List(Of NetFile)
        Return PclMc.McAssetsFixList(Instance, CheckHash, ProgressFeed)
    End Function

#End Region

    ''' <summary>
    ''' 发送 Minecraft 更新提示。
    ''' </summary>
    Public Sub McDownloadClientUpdateHint(VersionName As String, Json As JObject)
        Try

            '获取对应版本
            Dim Version As JToken = Nothing
            For Each Token In Json("versions")
                If Token("id") IsNot Nothing AndAlso Token("id").ToString = VersionName Then
                    Version = Token
                    Exit For
                End If
            Next
            '进行提示
            If Version Is Nothing Then Return
            Dim Time As Date = Version("releaseTime")
            Dim MsgBoxText As String = $"新版本：{VersionName}{vbCrLf}" &
                If((Date.Now - Time).TotalDays > 1, "更新时间：" & Time.ToString, "更新于：" & StringUtils.FormatTimeSpan(Time - Date.Now, False))
            Dim MsgResult = MyMsgBox(MsgBoxText, "Minecraft 更新提示", "确定", "下载", If((Date.Now - Time).TotalHours > 3, "更新日志", ""),
                Button3Action:=Sub() McUpdateLogShow(Version))
            '弹窗结果
            If MsgResult = 2 Then
                '下载
                RunInUi(
                Sub()
                    PageDownloadInstall.VersionWaitingSelect = VersionName
                    FrmMain.PageChange(FormMain.PageType.Download, FormMain.PageSubType.DownloadInstall)
                End Sub)
            End If

        Catch ex As Exception
            Logger.Error(ex, $"Minecraft 更新提示发送失败（{If(VersionName, "Nothing")}）")
        End Try
    End Sub

    ''' <summary>
    ''' 打码字符串中的 AccessToken。
    ''' </summary>
    Public Function FilterAccessToken(Raw As String, FilterChar As Char) As String
        Return PclMc.FilterAccessToken(Raw, FilterChar)
    End Function
    ''' <summary>
    ''' 打码字符串中的 Windows 用户名。
    ''' </summary>
    Public Function FilterUserName(Raw As String, FilterChar As Char) As String
        Return PclMc.FilterUserName(Raw, FilterChar)
    End Function

End Module