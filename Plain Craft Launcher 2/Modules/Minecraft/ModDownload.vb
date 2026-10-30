Public Module ModDownload

#Region "DlClient* | Minecraft 客户端"

    ''' <summary>
    ''' 返回某 Minecraft 版本对应的原版主 Jar 文件的下载信息，要求对应依赖版本已存在。
    ''' 失败则抛出异常，不需要下载则返回 Nothing。
    ''' </summary>
    Public Function DlClientJarGet(Instance As McInstance, ReturnNothingOnFileUseable As Boolean) As NetFile
        Return PclDownload.DlClientJarGet(Instance, ReturnNothingOnFileUseable)
    End Function

    ''' <summary>
    ''' 返回某 Minecraft 版本对应的原版主 AssetIndex 文件的下载信息，要求对应依赖版本已存在。
    ''' 若未找到，则会返回 Legacy 资源文件或 Nothing。
    ''' </summary>
    Public Function DlClientAssetIndexGet(Instance As McInstance) As NetFile
        Return PclDownload.DlClientAssetIndexGet(Instance)
    End Function

    ''' <summary>
    ''' 构造补全某 Minecraft 版本的所有文件的加载器列表。失败会抛出异常。
    ''' </summary>
    Public Function DlClientFix(Instance As McInstance, CheckAssetsHash As Boolean, DownloadAssetIndexInBackground As Boolean) As List(Of LoaderBase)
        Return PclDownload.DlClientFix(Instance, CheckAssetsHash, DownloadAssetIndexInBackground)
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
