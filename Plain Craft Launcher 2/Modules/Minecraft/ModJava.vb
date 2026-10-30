Public Module ModJava

    '================================================================================
    '逻辑已迁移至 PCLCS\Modules\Minecraft\PclJava.cs，本模块保留 VB 侧转发壳，
    '以及依赖 VB UI（FrmInstanceSetup / SettingService / RunInUiWait / Hint / Settings）的
    'MigrateInstanceJavaSettings（经 PclUiBridge.MigrateInstanceJavaSettings 桥接调用）。
    '================================================================================

    ''' <summary>
    ''' 初始化 Java 列表。
    ''' 若没有任何可用的 Java，则触发搜索。
    ''' </summary>
    Public Sub JavaInit()
        PclJava.JavaInit()
    End Sub

    ''' <summary>
    ''' 搜索并获取所有可用的 Java，检查所有 Java 的可用性，写入设置，并在结束后更新设置页面显示。
    ''' </summary>
    Public ReadOnly Property JavaListRefreshWorker As RedoableWorker(Of List(Of Java))
        Get
            Return PclJava.JavaListRefreshWorker
        End Get
    End Property

#Region "UI 事件"

    ''' <summary>
    ''' 更新设置页面的 Java 列表显示。
    ''' </summary>
    Public Sub UpdateJavaLists()
        PclJava.UpdateJavaLists()
    End Sub

    ''' <summary>
    ''' 手动触发导入 Java。
    ''' </summary>
    Public Sub ManuallyImportJava()
        PclJava.ManuallyImportJava()
    End Sub

    ''' <summary>
    ''' 手动从 Java 列表中移除指定的 Java，并记录到已移除列表中。
    ''' </summary>
    Public Sub ManuallyRemoveJava(JavaEntry As Java)
        PclJava.ManuallyRemoveJava(JavaEntry)
    End Sub

    ''' <summary>
    ''' 手动触发搜索 Java，强制检查所有 Java。
    ''' </summary>
    Public Sub ManuallySearchJava(Optional CancellationToken As CancellationToken = Nothing)
        PclJava.ManuallySearchJava(CancellationToken)
    End Sub

#End Region

#Region "选取"

    ''' <summary>
    ''' 分析指定 Minecraft 版本的基础 Java 版本要求。不受设置影响。
    ''' 如果由于 Java 版本要求冲突而导致没有可用的 Java，则返回 null。
    ''' </summary>
    Public Function GetJavaRequirement(Instance As McInstance) As (Range As ValueRange(Of Version), RecommendedComponent As String)
        Return PclJava.GetJavaRequirement(Instance)
    End Function

    ''' <summary>
    ''' 根据版本或 Java 范围要求寻找适合的 Java。
    ''' 若指定了 <paramref name="TryFixOrHint"/>，则会在找不到时会尝试下载 Java 或提示用户下载，否则只会静默运行。
    ''' </summary>
    ''' <returns>最适合的 Java。若找不到或失败则返回 null，除非取消否则不会抛出异常。</returns>
    Public Function SelectOrDownloadJava(Target As OneOf(Of ValueRange(Of Version), McInstance), TryFixOrHint As Boolean, Optional c As CancellationToken = Nothing, Optional p As ProgressProvider = Nothing) As Java
        Return PclJava.SelectOrDownloadJava(Target, TryFixOrHint, c, p)
    End Function

    '原为 Private，因 ModBase.InitBridges 中的桥接注册需要跨模块调用，改为 Friend；函数体保持原样。
    ''' <summary>
    ''' 从老版本 PCL 迁移版本独立的 Java 设置。
    ''' </summary>
    Friend Sub MigrateInstanceJavaSettings(Instance As McInstance, Optional c As CancellationToken = Nothing, Optional p As ProgressProvider = Nothing)
        If Configs.JavaMigrated.Get(Instance.Config) Then p?.Skip() : Return
        If FrmInstanceSetup IsNot Nothing Then RunInUiWait(Sub() FrmInstanceSetup.ComboArgumentJava.IsEnabled = False)
        Try
            Logger.Info($"正在尝试迁移版本独立的 Java 设置（{Instance.Name}）")
            '强制使用指定 Java
            Dim ForcedJavaSetting = Settings.Get(Of String)("VersionArgumentJavaSelect", Instance)
            If Not ForcedJavaSetting.StartsWithF("{") Then
                p?.Set(0.4, True) : GoTo NoForcedJava
            End If
            Dim ForcedJava = New Java(ForcedJavaSetting.DeserializeJson(Of JObject)()("Path").ToString)
            If Not ForcedJava.CheckAsync(c).Run() Then
                Hint("该版本的设置中强制指定的 Java 存在异常，该设置已重置", HintType.Red)
                p?.Set(0.4) : GoTo NoForcedJava
            End If
            Logger.Info($"将版本独立设置改为老版本中强制指定的 Java：{ForcedJava}")
            If Not Configs.JavaList.Get().Contains(ForcedJava) Then
                Configs.JavaList.Edit(Sub(ByRef List) List.Add(ForcedJava)) '加入列表底部
                Logger.Info($"已将强制指定的 Java 加入 Java 列表底部：{ForcedJava}")
            End If
            Configs.JavaForced.Set(ForcedJava, Instance.Config)
            Settings.Set("VersionArgumentJavaV2", 3, Instance)
            UpdateJavaLists()
            Configs.JavaMigrated.Set(True, Instance.Config) '完成
            p?.Finish() : Return
NoForcedJava:
            Dim JavaFound = JavaUtils.SearchFoldersAsync(True, {Instance.PathVersion}, c, p?.SplitTo(1)).Run()
            If JavaFound.Any() Then
                '从版本文件夹中发现了 Java
                Logger.Info($"在版本文件夹中发现了 Java，将版本独立设置改为优先使用版本文件夹的 Java：{JavaFound}")
                Settings.Set("VersionArgumentJavaV2", 2, Instance)
            Else
                '自动选择
                Logger.Info("没有需要迁移的内容，自动选择 Java")
                Settings.Set("VersionArgumentJavaV2", 0, Instance)
            End If
            Configs.JavaMigrated.Set(True, Instance.Config) '完成
        Finally
            If FrmInstanceSetup IsNot Nothing Then RunInUiWait(
            Sub()
                SettingService.RefreshSettings(FrmInstanceSetup.ComboArgumentJava)
                SettingService.RefreshSettings(FrmInstanceSetup.ComboArgumentJavaSelect)
                FrmInstanceSetup.ComboArgumentJava.IsEnabled = True
            End Sub)
        End Try
    End Sub

#End Region

End Module
