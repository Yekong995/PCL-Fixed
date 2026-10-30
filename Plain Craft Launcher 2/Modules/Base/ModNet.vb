Public Module ModNet

#Region "网络请求"

    ''' <summary>
    ''' 发送一次网络请求并获取返回内容。
    ''' </summary>
    Public Function NetRequestByClient(Url As String, Optional Method As HttpMethod = Nothing,
            Optional Content As Object = Nothing, Optional ContentType As String = Nothing, Optional Accept As String = "*/*",
            Optional Timeout As Integer = 25000, Optional Headers As String(,) = Nothing, Optional RequireJson As Boolean = False,
            Optional Encoding As Encoding = Nothing, Optional SimulateBrowserHeaders As Boolean = False, Optional MakeLog As Boolean = True) As String
        Return NetCore.NetRequestByClient(Url, Method, Content, ContentType, Accept, Timeout, Headers, RequireJson, Encoding, SimulateBrowserHeaders, MakeLog)
    End Function
    ''' <summary>
    ''' 发送网络请求并获取返回内容。
    ''' 会进行至多 45 秒 3 次的尝试，允许最长 30s 的超时。
    ''' </summary>
    ''' <param name="BackupUrl">如果第一次尝试失败，换用的备用 URL。</param>
    Public Function NetRequestByClientRetry(Url As String, Optional Method As HttpMethod = Nothing, Optional BackupUrl As String = Nothing,
            Optional Content As Object = Nothing, Optional ContentType As String = Nothing, Optional Accept As String = "*/*",
            Optional Headers As String(,) = Nothing, Optional RequireJson As Boolean = False,
            Optional Encoding As Encoding = Nothing, Optional SimulateBrowserHeaders As Boolean = False) As String
        Return NetCore.NetRequestByClientRetry(Url, Method, BackupUrl, Content, ContentType, Accept, Headers, RequireJson, Encoding, SimulateBrowserHeaders)
    End Function
    ''' <summary>
    ''' 生成数个线程，同时发送网络请求并获取返回内容。
    ''' 每个线程开始之前会略有延迟。
    ''' </summary>
    Public Function NetRequestByClientMultiple(Url As String, Optional Method As HttpMethod = Nothing, Optional ThreadCount As Integer = 3,
            Optional Content As Object = Nothing, Optional ContentType As String = Nothing, Optional Accept As String = "*/*",
            Optional Headers As String(,) = Nothing, Optional RequireJson As Boolean = False, Optional Timeout As Integer = 25000,
            Optional Encoding As Encoding = Nothing, Optional SimulateBrowserHeaders As Boolean = False) As String
        Return NetCore.NetRequestByClientMultiple(Url, Method, ThreadCount, Content, ContentType, Accept, Headers, RequireJson, Timeout, Encoding, SimulateBrowserHeaders)
    End Function

    ''' <summary>
    ''' 以多线程下载网页文件的方式获取内容。
    ''' 不支持缓存协商；若需缓存协商，需要换用 NetRequestByClient。
    ''' </summary>
    Public Function NetRequestByLoader(Url As String, Optional Timeout As Integer = 45000, Optional IsJson As Boolean = False, Optional SimulateBrowserHeaders As Boolean = False) As String
        Return NetCore.NetRequestByLoader(Url, Timeout, IsJson, SimulateBrowserHeaders)
    End Function
    ''' <summary>
    ''' 以多线程下载网页文件的方式获取内容。
    ''' 不支持缓存协商；若需缓存协商，需要换用 NetRequestByClient。
    ''' </summary>
    Public Function NetRequestByLoader(Urls As IEnumerable(Of String), Optional Timeout As Integer = 45000, Optional IsJson As Boolean = False, Optional SimulateBrowserHeaders As Boolean = False) As String
        Return NetCore.NetRequestByLoader(Urls, Timeout, IsJson, SimulateBrowserHeaders)
    End Function

#End Region

#Region "文件下载"

    ''' <summary>
    ''' 通过网络请求直接下载小文件，若文件已存在将被覆盖。
    ''' 不建议用于下载大文件。
    ''' </summary>
    Public Sub NetDownloadByClient(Url As String, LocalPath As String, Optional SimulateBrowserHeaders As Boolean = False)
        NetCore.NetDownloadByClient(Url, LocalPath, SimulateBrowserHeaders)
    End Sub

    ''' <summary>
    ''' 通过多线程下载引擎下载文件。
    ''' </summary>
    Public Sub NetDownloadByLoader(Url As String, LocalPath As String, Optional LoaderToSyncProgress As LoaderBase = Nothing, Optional Check As FileChecker = Nothing, Optional SimulateBrowserHeaders As Boolean = False)
        NetCore.NetDownloadByLoader(Url, LocalPath, LoaderToSyncProgress, Check, SimulateBrowserHeaders)
    End Sub
    ''' <summary>
    ''' 通过多线程下载引擎下载文件。
    ''' </summary>
    ''' <param name="Urls">文件的 Url 列表。</param>
    ''' <param name="LocalPath">下载的本地地址。</param>
    Public Sub NetDownloadByLoader(Urls As IEnumerable(Of String), LocalPath As String, Optional LoaderToSyncProgress As LoaderBase = Nothing, Optional Check As FileChecker = Nothing, Optional SimulateBrowserHeaders As Boolean = False)
        NetCore.NetDownloadByLoader(Urls, LocalPath, LoaderToSyncProgress, Check, SimulateBrowserHeaders)
    End Sub

#End Region

#Region "多线程下载引擎"

    ''' <summary>
    ''' 最大线程数。
    ''' </summary>
    Public ReadOnly Property NetTaskThreadLimit As Integer
        Get
            Return NetCore.NetTaskThreadLimit
        End Get
    End Property
    ''' <summary>
    ''' 速度下限。
    ''' </summary>
    Public Property NetTaskSpeedLimitLow As Long
        Get
            Return NetCore.NetTaskSpeedLimitLow
        End Get
        Set(value As Long)
            NetCore.NetTaskSpeedLimitLow = value
        End Set
    End Property
    ''' <summary>
    ''' 速度上限。若无限制则为 -1。
    ''' </summary>
    Public ReadOnly Property NetTaskSpeedLimitHigh As Long
        Get
            Return NetCore.NetTaskSpeedLimitHigh
        End Get
    End Property
    Public Sub UpdateNetTaskSpeedLimitHigh(Value As Integer)
        NetCore.UpdateNetTaskSpeedLimitHigh(Value)
    End Sub
    ''' <summary>
    ''' 基于限速，当前可以下载的剩余量。
    ''' </summary>
    Public Property NetTaskSpeedLimitLeft As Long
        Get
            Return NetCore.NetTaskSpeedLimitLeft
        End Get
        Set(value As Long)
            NetCore.NetTaskSpeedLimitLeft = value
        End Set
    End Property
    ''' <summary>
    ''' 正在运行中的线程数。
    ''' </summary>
    Public Property NetTaskThreadCount As Integer
        Get
            Return NetCore.NetTaskThreadCount
        End Get
        Set(value As Integer)
            NetCore.NetTaskThreadCount = value
        End Set
    End Property

    Public ReadOnly Property NetManager As NetManagerClass
        Get
            Return NetCore.NetManager
        End Get
    End Property

    ''' <summary>
    ''' 是否有正在进行中、需要在任务管理页面显示的下载任务？
    ''' </summary>
    Public Function HasDownloadingTask(Optional IgnoreCustomDownload As Boolean = False) As Boolean
        Return NetCore.HasDownloadingTask(IgnoreCustomDownload)
    End Function

#End Region

#Region "端口"

    ''' <summary>
    ''' 随机获取单个可用的端口。
    ''' </summary>
    Public Function FindFreePort() As Integer
        Return NetCore.FindFreePort()
    End Function

    ''' <summary>
    ''' 获取当前已被占用的端口列表。
    ''' </summary>
    Public Function GetUsedPorts() As List(Of Integer)
        Return NetCore.GetUsedPorts()
    End Function

    ''' <summary>
    ''' 寻找数个连续编号的可用端口。
    ''' </summary>
    Public Function FindFreePorts(ConsecutiveCount As Integer, ParamArray ExtraBlackLists As Integer()) As List(Of Integer)
        Return NetCore.FindFreePorts(ConsecutiveCount, ExtraBlackLists)
    End Function

#End Region

    ''' <summary>
    ''' 测试 Ping。失败则返回 -1。
    ''' </summary>
    Public Function Ping(Ip As String, Optional Timeout As Integer = 10000, Optional MakeLog As Boolean = True) As Integer
        Return NetCore.Ping(Ip, Timeout, MakeLog)
    End Function

    ''' <summary>
    ''' 设置是否应当在正版登录时校验 SSL 证书。
    ''' </summary>
    Public Sub ShouldValidateSslCertificateOnLogin(Enabled As Boolean)
        NetCore.ShouldValidateSslCertificateOnLogin(Enabled)
    End Sub

End Module
