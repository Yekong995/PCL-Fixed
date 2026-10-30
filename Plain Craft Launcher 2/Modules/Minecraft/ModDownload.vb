Public Module ModDownload

#Region "DlClient* | Minecraft 客户端"

    ''' <summary>
    ''' 返回某 Minecraft 版本对应的原版主 Jar 文件的下载信息，要求对应依赖版本已存在。
    ''' 失败则抛出异常，不需要下载则返回 Nothing。
    ''' </summary>
    Public Function DlClientJarGet(Instance As McInstance, ReturnNothingOnFileUseable As Boolean) As NetFile
        '获取底层继承版本
        Try
            Do While Not String.IsNullOrEmpty(Instance.InheritName)
                Instance = New McInstance(Instance.InheritName)
            Loop
        Catch ex As Exception
            Logger.Warn(ex, "获取底层继承版本失败")
        End Try
        '检查 Json 是否标准
        If Instance.JsonObject("downloads") Is Nothing OrElse Instance.JsonObject("downloads")("client") Is Nothing OrElse Instance.JsonObject("downloads")("client")("url") Is Nothing Then
            Throw New Exception("底层版本 " & Instance.Name & " 中无 jar 文件下载信息")
        End If
        '检查文件
        Dim Checker As New FileChecker With {.MinSize = 1024, .ActualSize = If(Instance.JsonObject("downloads")("client")("size"), -1), .Hash = Instance.JsonObject("downloads")("client")("sha1")}
        If ReturnNothingOnFileUseable AndAlso Checker.Check(Instance.PathVersion & Instance.Name & ".jar") Is Nothing Then Return Nothing '通过校验
        '返回下载信息
        Dim JarUrl As String = Instance.JsonObject("downloads")("client")("url")
        Return New NetFile(DlSourceLauncherOrMetaGet(JarUrl), Instance.PathVersion & Instance.Name & ".jar", Checker)
    End Function

    ''' <summary>
    ''' 返回某 Minecraft 版本对应的原版主 AssetIndex 文件的下载信息，要求对应依赖版本已存在。
    ''' 若未找到，则会返回 Legacy 资源文件或 Nothing。
    ''' </summary>
    Public Function DlClientAssetIndexGet(Instance As McInstance) As NetFile
        '获取底层继承版本
        Do While Not String.IsNullOrEmpty(Instance.InheritName)
            Instance = New McInstance(Instance.InheritName)
        Loop
        '获取信息
        Dim IndexInfo = McAssetsGetIndex(Instance, True, True)
        Dim IndexAddress As String = McFolderSelected & "assets\indexes\" & IndexInfo("id").ToString & ".json"
        Logger.Info($"版本 {Instance.Name} 对应的资源文件索引为 {IndexInfo("id")}")
        Dim IndexUrl As String = If(IndexInfo("url"), "")
        If IndexUrl = "" Then
            Return Nothing
        Else
            Return New NetFile(DlSourceLauncherOrMetaGet(IndexUrl), IndexAddress, New FileChecker With {.IsJson = True})
        End If
    End Function

    ''' <summary>
    ''' 构造补全某 Minecraft 版本的所有文件的加载器列表。失败会抛出异常。
    ''' </summary>
    Public Function DlClientFix(Instance As McInstance, CheckAssetsHash As Boolean, DownloadAssetIndexInBackground As Boolean) As List(Of LoaderBase)
        Dim Loaders As New List(Of LoaderBase)

#Region "下载支持库文件"
        Dim LoadersLib As New List(Of LoaderBase) From {
            New LoaderTask(Of String, List(Of NetFile))("分析缺失支持库文件", Sub(Task As LoaderTask(Of String, List(Of NetFile))) Task.Output = McLibNetFilesFromInstance(Instance)) With {.ProgressWeight = 1},
            New LoaderDownload("下载支持库文件", New List(Of NetFile)) With {.ProgressWeight = 15}
        }
        '构造加载器
        Loaders.Add(New LoaderCombo(Of String)("下载支持库文件（主加载器）", LoadersLib) With {.Block = False, .Show = False, .ProgressWeight = 16})
#End Region

#Region "下载资源文件"
        If ShouldIgnoreFileCheck(Instance) Then
            Logger.Warn("设置要求跳过所有 Assets 检查，文件可能不是最新的", LogBehavior.None)
        Else
            Dim LoadersAssets As New List(Of LoaderBase)
            '获取资源文件索引地址
            Dim TempAddressMain As String = Nothing
            Dim RealAddressMain As String = Nothing
            LoadersAssets.Add(New LoaderTask(Of String, List(Of NetFile))("分析资源文件索引地址",
            Sub(Task As LoaderTask(Of String, List(Of NetFile)))
                Try
                    Dim IndexFile = DlClientAssetIndexGet(Instance)
                    If IndexFile Is Nothing Then
                        Task.Output = New List(Of NetFile)
                        Logger.Warn($"未找到版本 {Instance.Name} 的合适的资源索引下载地址，游戏 assets 可能缺失")
                        Return
                    End If
                    Dim IndexBackgroundFile = PathTemp & "Cache\" & IndexFile.LocalName & "_background"
                    If FileUtils.Exists(IndexBackgroundFile) AndAlso
                       FileUtils.GetInfo(IndexBackgroundFile).LastWriteTime.Date > FileUtils.GetInfo(IndexFile.LocalPath).LastWriteTime.Date Then
                        Logger.Info($"发现后台更新的资源索引缓存文件：{IndexBackgroundFile}")
                        FileUtils.Move(IndexBackgroundFile, IndexFile.LocalPath)
                    End If
                    If DownloadAssetIndexInBackground Then
                        '允许后台更新索引
                        If IndexFile.Check.Check(IndexFile.LocalPath) Is Nothing Then
                            Task.Output = New List(Of NetFile)
                        Else
                            Task.Output = New List(Of NetFile) From {IndexFile}
                        End If
                    Else
                        '强制要求重新下载
                        RealAddressMain = IndexFile.LocalPath
                        TempAddressMain = PathTemp & "Cache\" & IndexFile.LocalName
                        IndexFile.LocalPath = TempAddressMain
                        Task.Output = New List(Of NetFile) From {IndexFile}
                    End If
                Catch ex As Exception
                    Throw New Exception("分析资源文件索引地址失败", ex)
                End Try
            End Sub) With {.ProgressWeight = 0.5, .Show = False})
            '下载资源文件索引
            LoadersAssets.Add(New LoaderDownload("下载资源文件索引", New List(Of NetFile)) With {.ProgressWeight = 2})
            '获取资源文件地址
            LoadersAssets.Add(New LoaderTask(Of String, List(Of NetFile))("获取资源文件列表",
            Sub(Task)
                If TempAddressMain IsNot Nothing Then FileUtils.Move(TempAddressMain, RealAddressMain)
                Task.Output = McAssetsFixList(Instance, CheckAssetsHash, Task)
            End Sub) With {.ProgressWeight = 0.01})
            '下载资源文件
            LoadersAssets.Add(New LoaderDownload("下载资源文件", New List(Of NetFile)) With {.ProgressWeight = 25})
            '构造加载器
            Loaders.Add(New LoaderCombo(Of String)("下载资源文件（主加载器）", LoadersAssets) With {.Block = False, .Show = False, .ProgressWeight = 27})

            '后台独立更新索引
            If DownloadAssetIndexInBackground Then
                Dim RealAddressBg As String = Nothing
                Dim TempAddressBg As String = Nothing
                Dim LoadersAssetsUpdate As New List(Of LoaderBase)
                LoadersAssetsUpdate.Add(New LoaderTask(Of String, List(Of NetFile))("后台分析资源文件索引地址",
                Sub(Task As LoaderTask(Of String, List(Of NetFile)))
                    Dim IndexFile As NetFile = DlClientAssetIndexGet(Instance)
                    If IndexFile Is Nothing Then
                        Logger.Warn($"未找到版本 {Instance.Name} 的合适的资源索引下载地址，游戏 assets 可能缺失")
                        Task.Cancel()
                        Throw New OperationCanceledException
                    End If
                    RealAddressBg = IndexFile.LocalPath
                    TempAddressBg = PathTemp & "Cache\" & IndexFile.LocalName & "_background"
                    IndexFile.LocalPath = TempAddressBg
                    Task.Output = New List(Of NetFile) From {IndexFile}
                    '检查是否需要更新：每天只更新一次
                    Dim LastUpdate As Date = FileUtils.GetInfo(RealAddressBg).LastWriteTime.Date
                    If FileUtils.Exists(RealAddressBg) AndAlso Math.Abs((LastUpdate - Now.Date).TotalDays) < 1 Then
                        Logger.Info($"无需更新资源文件索引，上次更新于 {LastUpdate}")
                        Task.Cancel()
                    End If
                End Sub))
                LoadersAssetsUpdate.Add(New LoaderDownload("后台下载资源文件索引", New List(Of NetFile)))
                Dim Updater As New LoaderCombo(Of String)("后台更新资源文件索引", LoadersAssetsUpdate)
                Logger.Info("开始后台检查资源文件索引")
                Updater.Start()
            End If
        End If
#End Region

        Return Loaders
    End Function

#End Region

#Region "DlClientList | Minecraft 客户端 版本列表"

    ''' <summary>
    ''' 所有正式版的 Minecraft Drop 序数。
    ''' 若从未完成过获取，返回 Nothing；否则必定存在元素，且从高到低排列。
    ''' </summary>
    Public Property AllDrops As List(Of Integer)
        Get
            Return PclDownload.AllDrops
        End Get
        Set(value As List(Of Integer))
            PclDownload.AllDrops = value
        End Set
    End Property

    '主加载器
    ''' <summary>
    ''' Minecraft 客户端 版本列表，主加载器。
    ''' 若要求镜像源必须包含某个版本，则将该版本 ID 作为输入（#5195）。
    ''' </summary>
    Public ReadOnly Property DlClientListLoader As LoaderTask(Of String, DlClientListResult)
        Get
            Return PclDownload.DlClientListLoader
        End Get
    End Property
    ''' <summary>
    ''' Minecraft 客户端 版本列表，Mojang 官方源加载器。
    ''' </summary>
    Public ReadOnly Property DlClientListMojangLoader As LoaderTask(Of String, DlClientListResult)
        Get
            Return PclDownload.DlClientListMojangLoader
        End Get
    End Property
    ''' <summary>
    ''' Minecraft 客户端 版本列表，BMCLAPI 源加载器。
    ''' </summary>
    Public ReadOnly Property DlClientListBmclapiLoader As LoaderTask(Of String, DlClientListResult)
        Get
            Return PclDownload.DlClientListBmclapiLoader
        End Get
    End Property

    ''' <summary>
    ''' 获取某个版本的 Json 下载地址，若失败则返回 Nothing。必须在工作线程执行。
    ''' </summary>
    Public Function DlClientListGet(Id As String)
        Return PclDownload.DlClientListGet(Id)
    End Function

#End Region

#Region "DlOptiFineList | OptiFine 版本列表"

    ''' <summary>
    ''' OptiFine 版本列表，主加载器。
    ''' </summary>
    Public ReadOnly Property DlOptiFineListLoader As LoaderTask(Of Integer, DlOptiFineListResult)
        Get
            Return PclDownload.DlOptiFineListLoader
        End Get
    End Property
    ''' <summary>
    ''' OptiFine 版本列表，官方源。
    ''' </summary>
    Public ReadOnly Property DlOptiFineListOfficialLoader As LoaderTask(Of Integer, DlOptiFineListResult)
        Get
            Return PclDownload.DlOptiFineListOfficialLoader
        End Get
    End Property
    ''' <summary>
    ''' OptiFine 版本列表，BMCLAPI。
    ''' </summary>
    Public ReadOnly Property DlOptiFineListBmclapiLoader As LoaderTask(Of Integer, DlOptiFineListResult)
        Get
            Return PclDownload.DlOptiFineListBmclapiLoader
        End Get
    End Property

#End Region

#Region "DlForgeList | Forge Minecraft 版本列表"

    ''' <summary>
    ''' Forge 版本列表，主加载器。
    ''' </summary>
    Public ReadOnly Property DlForgeListLoader As LoaderTask(Of Integer, DlForgeListResult)
        Get
            Return PclDownload.DlForgeListLoader
        End Get
    End Property
    ''' <summary>
    ''' Forge 版本列表，官方源。
    ''' </summary>
    Public ReadOnly Property DlForgeListOfficialLoader As LoaderTask(Of Integer, DlForgeListResult)
        Get
            Return PclDownload.DlForgeListOfficialLoader
        End Get
    End Property
    ''' <summary>
    ''' Forge 版本列表，BMCLAPI。
    ''' </summary>
    Public ReadOnly Property DlForgeListBmclapiLoader As LoaderTask(Of Integer, DlForgeListResult)
        Get
            Return PclDownload.DlForgeListBmclapiLoader
        End Get
    End Property

#End Region

#Region "DlForgeVersion | Forge 版本列表"

    ''' <summary>
    ''' Forge 版本列表，主加载器。
    ''' </summary>
    Public Sub DlForgeVersionMain(Loader As LoaderTask(Of String, List(Of DlForgeVersionEntry)))
        PclDownload.DlForgeVersionMain(Loader)
    End Sub

    ''' <summary>
    ''' Forge 版本列表，官方源。
    ''' </summary>
    Public Sub DlForgeVersionOfficialMain(Loader As LoaderTask(Of String, List(Of DlForgeVersionEntry)))
        PclDownload.DlForgeVersionOfficialMain(Loader)
    End Sub

    ''' <summary>
    ''' Forge 版本列表，BMCLAPI。
    ''' </summary>
    Public Sub DlForgeVersionBmclapiMain(Loader As LoaderTask(Of String, List(Of DlForgeVersionEntry)))
        PclDownload.DlForgeVersionBmclapiMain(Loader)
    End Sub

#End Region

#Region "DlNeoForgeList | NeoForge 版本列表"

    ''' <summary>
    ''' NeoForge 版本列表，主加载器。
    ''' </summary>
    Public ReadOnly Property DlNeoForgeListLoader As LoaderTask(Of Integer, DlNeoForgeListResult)
        Get
            Return PclDownload.DlNeoForgeListLoader
        End Get
    End Property
    ''' <summary>
    ''' NeoForge 版本列表，官方源。
    ''' </summary>
    Public ReadOnly Property DlNeoForgeListOfficialLoader As LoaderTask(Of Integer, DlNeoForgeListResult)
        Get
            Return PclDownload.DlNeoForgeListOfficialLoader
        End Get
    End Property
    ''' <summary>
    ''' NeoForge 版本列表，BMCLAPI。
    ''' </summary>
    Public ReadOnly Property DlNeoForgeListBmclapiLoader As LoaderTask(Of Integer, DlNeoForgeListResult)
        Get
            Return PclDownload.DlNeoForgeListBmclapiLoader
        End Get
    End Property
    Public Sub DlNeoForgeListBmclapiMain(Loader As LoaderTask(Of Integer, DlNeoForgeListResult))
        PclDownload.DlNeoForgeListBmclapiMain(Loader)
    End Sub

#End Region

#Region "DlLiteLoaderList | LiteLoader 版本列表"

    ''' <summary>
    ''' LiteLoader 版本列表，主加载器。
    ''' </summary>
    Public ReadOnly Property DlLiteLoaderListLoader As LoaderTask(Of Integer, DlLiteLoaderListResult)
        Get
            Return PclDownload.DlLiteLoaderListLoader
        End Get
    End Property
    ''' <summary>
    ''' LiteLoader 版本列表，官方源。
    ''' </summary>
    Public ReadOnly Property DlLiteLoaderListOfficialLoader As LoaderTask(Of Integer, DlLiteLoaderListResult)
        Get
            Return PclDownload.DlLiteLoaderListOfficialLoader
        End Get
    End Property
    ''' <summary>
    ''' LiteLoader 版本列表，BMCLAPI。
    ''' </summary>
    Public ReadOnly Property DlLiteLoaderListBmclapiLoader As LoaderTask(Of Integer, DlLiteLoaderListResult)
        Get
            Return PclDownload.DlLiteLoaderListBmclapiLoader
        End Get
    End Property

#End Region

#Region "DlFabricList | Fabric 列表"

    ''' <summary>
    ''' Fabric 列表，主加载器。
    ''' </summary>
    Public ReadOnly Property DlFabricListLoader As LoaderTask(Of Integer, DlFabricListResult)
        Get
            Return PclDownload.DlFabricListLoader
        End Get
    End Property
    ''' <summary>
    ''' Fabric 列表，官方源。
    ''' </summary>
    Public ReadOnly Property DlFabricListOfficialLoader As LoaderTask(Of Integer, DlFabricListResult)
        Get
            Return PclDownload.DlFabricListOfficialLoader
        End Get
    End Property
    ''' <summary>
    ''' Fabric 列表，BMCLAPI。
    ''' </summary>
    Public ReadOnly Property DlFabricListBmclapiLoader As LoaderTask(Of Integer, DlFabricListResult)
        Get
            Return PclDownload.DlFabricListBmclapiLoader
        End Get
    End Property

    ''' <summary>
    ''' Fabric API 列表，官方源。
    ''' </summary>
    Public ReadOnly Property DlFabricApiLoader As LoaderTask(Of Integer, List(Of ResourceVersion))
        Get
            Return PclDownload.DlFabricApiLoader
        End Get
    End Property

    ''' <summary>
    ''' OptiFabric 列表，官方源。
    ''' </summary>
    Public ReadOnly Property DlOptiFabricLoader As LoaderTask(Of Integer, List(Of ResourceVersion))
        Get
            Return PclDownload.DlOptiFabricLoader
        End Get
    End Property

#End Region

#Region "DlMod | Mod 镜像源请求"

    ''' <summary>
    ''' 对可能涉及 Mod 镜像源的请求进行处理，返回 JToken。
    ''' 调用 NetRequest，会进行重试。
    ''' </summary>
    Public Function DlModRequest(Url As String, Optional Method As HttpMethod = Nothing,
                                 Optional Content As String = Nothing, Optional ContentType As String = Nothing) As JToken
        Return PclNet.DlModRequest(Url, Method, Content, ContentType)
    End Function

#End Region

#Region "DlSource | 镜像下载源"

    ''' <summary>
    ''' 下载文件（而非获取版本列表）的时候，是否优先使用官方源。
    ''' </summary>
    Public ReadOnly Property DlSourcePreferMojang As Boolean
        Get
            Return PclDownload.DlSourcePreferMojang
        End Get
    End Property
    ''' <summary>
    ''' 下载文件（而非获取版本列表）的时候，根据是否优先使用官方源决定使用 Url 的顺序。
    ''' </summary>
    Public Function DlSourceOrder(OfficialUrls As IEnumerable(Of String), MirrorUrls As IEnumerable(Of String)) As IEnumerable(Of String)
        Return PclDownload.DlSourceOrder(OfficialUrls, MirrorUrls)
    End Function
    ''' <summary>
    ''' 获取版本列表（而非下载文件）的时候，是否优先使用官方源。
    ''' </summary>
    Public ReadOnly Property DlVersionListPreferMojang As Boolean
        Get
            Return PclDownload.DlVersionListPreferMojang
        End Get
    End Property
    ''' <summary>
    ''' 获取版本列表（而非下载文件）的时候，根据是否优先使用官方源决定使用 Url 的顺序。
    ''' </summary>
    Public Function DlVersionListOrder(OfficialUrls As IEnumerable(Of String), MirrorUrls As IEnumerable(Of String)) As IEnumerable(Of String)
        Return PclDownload.DlVersionListOrder(OfficialUrls, MirrorUrls)
    End Function


    ''' <summary>
    ''' 下载 Assets 文件。
    ''' </summary>
    Public Function DlSourceAssetsGet(Original As String) As IEnumerable(Of String)
        Return PclDownload.DlSourceAssetsGet(Original)
    End Function
    ''' <summary>
    ''' 下载 Libraries 文件。
    ''' </summary>
    Public Function DlSourceLibraryGet(Original As String) As IEnumerable(Of String)
        Return PclDownload.DlSourceLibraryGet(Original)
    End Function
    ''' <summary>
    ''' 下载 Launcher 或 Meta 文件。
    ''' 不应使用它来获取版本列表（因为它只使用文件下载源设置来决定源顺序）。
    ''' </summary>
    Public Function DlSourceLauncherOrMetaGet(Original As String) As IEnumerable(Of String)
        Return PclDownload.DlSourceLauncherOrMetaGet(Original)
    End Function

    'Mod 下载源
    Public Function DlSourceModGet(Original As String) As String
        Return PclNet.DlSourceModGet(Original)
    End Function

#End Region

End Module
