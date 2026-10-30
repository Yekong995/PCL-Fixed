'ResourceProject / ResourceVersion 的 UI 扩展方法。
'这两个类已迁移至 C#（PCLCS.Modules），此处保留需要构建 VB UI 控件的成员，其余逻辑见 C# 源文件。

Imports System.Runtime.CompilerServices

Public Module ResourceUiExtensions

    ''' <summary>
    ''' 将当前工程信息实例化为控件。
    ''' </summary>
    <Extension>
    Public Function ToResourceItem(Project As ResourceProject, ShowMcVersionDesc As Boolean, ShowLoaderDesc As Boolean) As MyVirtualizingElement(Of MyResourceItem)
        '获取版本描述
        Dim GameVersionDescription As String
        If Project.Drops Is Nothing OrElse Not Project.Drops.Any() Then
            GameVersionDescription = "仅快照版本" '#5412
        Else
            Dim Segments As New List(Of String)
            Dim IsOld As Boolean = False
            For i = 0 To Project.Drops.Count - 1 '版本号一定为降序
                '获取当前连续的版本号段
                Dim StartDrop As Integer = Project.Drops(i), EndDrop As Integer = Project.Drops(i)
                If StartDrop < 120 Then '如果支持新版本，则不显示 1.11-
                    If Segments.Any() AndAlso Not IsOld Then
                        Exit For
                    Else
                        IsOld = True
                    End If
                End If
                For ii = i + 1 To Project.Drops.Count - 1
                    If AllDrops Is Nothing OrElse AllDrops.IndexOf(Project.Drops(ii)) <> AllDrops.IndexOf(EndDrop) + 1 Then Exit For
                    EndDrop = Project.Drops(ii)
                    i = ii
                Next
                '将版本号段转为描述文本
                Dim StartName = McVersion.DropToVersion(StartDrop)
                Dim EndName = McVersion.DropToVersion(EndDrop)
                If StartDrop = EndDrop Then
                    Segments.Add(StartName)
                ElseIf AllDrops IsNot Nothing AndAlso StartDrop >= AllDrops.First Then
                    If EndDrop < 120 Then
                        Segments.Clear()
                        Segments.Add("全版本")
                        Exit For
                    Else
                        Segments.Add(EndName & "+")
                    End If
                ElseIf EndDrop < 120 Then
                    Segments.Add(StartName & "-")
                    Exit For
                ElseIf AllDrops Is Nothing OrElse AllDrops.IndexOf(EndDrop) - AllDrops.IndexOf(StartDrop) = 1 Then
                    Segments.Add(StartName & ", " & EndName)
                Else
                    Segments.Add(StartName & "~" & EndName)
                End If
            Next
            GameVersionDescription = Segments.Join(", ")
        End If
        '获取 Mod 加载器描述
        Dim ModLoaderDescriptionFull As String, ModLoaderDescriptionPart As String
        Dim ModLoadersForDesc = Project.ModLoaders.Flags().ToList()
        Select Case ModLoadersForDesc.Count
            Case 0
                If ModLoadersForDesc.IsSingle Then
                    ModLoaderDescriptionFull = "仅 " & ModLoadersForDesc.Single.ToString
                    ModLoaderDescriptionPart = ModLoadersForDesc.Single.ToString
                Else
                    ModLoaderDescriptionFull = "未知"
                    ModLoaderDescriptionPart = ""
                End If
            Case 1
                ModLoaderDescriptionFull = "仅 " & ModLoadersForDesc.Single.ToString
                ModLoaderDescriptionPart = ModLoadersForDesc.Single.ToString
            Case Else
                Dim NewestDrop As Integer = If(Project.Drops.Any, Project.Drops.First, 9999)
                If ModLoadersForDesc.Contains(ModLoaders.Forge) AndAlso
                   (NewestDrop < 140 OrElse ModLoadersForDesc.Contains(ModLoaders.Fabric)) AndAlso
                   (NewestDrop < 200 OrElse ModLoadersForDesc.Contains(ModLoaders.NeoForge)) Then
                    ModLoaderDescriptionFull = "任意"
                    ModLoaderDescriptionPart = ""
                Else
                    ModLoaderDescriptionFull = ModLoadersForDesc.Join(" / ")
                    ModLoaderDescriptionPart = ModLoadersForDesc.Join(" / ")
                End If
        End Select
        '实例化 UI
        Return New MyVirtualizingElement(Of MyResourceItem)(
        Function()
            Dim NewItem As New MyResourceItem With {.Tag = Project}
            Project.ApplyLogoToMyImage(NewItem.PathLogo)
            Dim Titles = Project.GetControlTitle(True)
            NewItem.Title = Titles.Title
            If Titles.SubTitle = "" Then
                CType(NewItem.LabTitleRaw.Parent, StackPanel).Children.Remove(NewItem.LabTitleRaw)
            Else
                NewItem.SubTitle = Titles.SubTitle
            End If
            NewItem.Tags = Project.Tags
            NewItem.Description = Project.Description.ReplaceLineEndings("")
            '下边栏
            If Not ShowMcVersionDesc AndAlso Not ShowLoaderDesc Then
                '全部隐藏
                CType(NewItem.PathVersion.Parent, Grid).Children.Remove(NewItem.PathVersion)
                CType(NewItem.LabVersion.Parent, Grid).Children.Remove(NewItem.LabVersion)
                NewItem.ColumnVersion1.Width = New GridLength(0)
                NewItem.ColumnVersion2.MaxWidth = 0
                NewItem.ColumnVersion3.Width = New GridLength(0)
            ElseIf ShowMcVersionDesc AndAlso ShowLoaderDesc Then
                '全部显示
                NewItem.LabVersion.Text = If(ModLoaderDescriptionPart = "", "", ModLoaderDescriptionPart & " ") & GameVersionDescription
            ElseIf ShowMcVersionDesc Then
                '仅显示版本
                NewItem.LabVersion.Text = GameVersionDescription
            Else
                '仅显示 Mod 加载器
                NewItem.LabVersion.Text = ModLoaderDescriptionFull
            End If
            NewItem.LabSource.Text = Project.Platform.ToString
            NewItem.LabTime.Text = StringUtils.FormatTimeSpan(Project.LastUpdate - Date.Now, True)
            NewItem.LabDownload.Text =
                If(Project.DownloadCount > 100000000, Math.Round(Project.DownloadCount / 100000000, 2) & " 亿",
                If(Project.DownloadCount > 100000, Math.Floor(Project.DownloadCount / 10000) & " 万", Project.DownloadCount))
            Return NewItem
        End Function) With {.Height = 64}
    End Function

    ''' <summary>
    ''' 将 Logo 应用到指定图片控件。
    ''' </summary>
    <Extension>
    Public Sub ApplyLogoToMyImage(Project As ResourceProject, Img As MyImage)
        If String.IsNullOrEmpty(Project.LogoUrl) Then
            Img.Source = PathImage & "Icons/NoIcon.png"
        Else
            Img.Source = Project.LogoUrl
            Img.FallbackSource = DlSourceModGet(Project.LogoUrl)
        End If
    End Sub

    ''' <summary>
    ''' 转为列表项。
    ''' </summary>
    <Extension>
    Public Function ToListItem(Version As ResourceVersion, ClickHandler As MyListItem.ClickEventHandler, Optional SaveAsButtonHandler As MyIconButton.ClickEventHandler = Nothing,
                               Optional IsBadDisplay As Boolean = False) As MyVirtualizingElement(Of MyListItem)
        Return New MyVirtualizingElement(Of MyListItem)(
        Function()
            Dim Result As New MyListItem
            With Result

                '基础属性
                .SnapsToDevicePixels = True
                .Height = 42
                .Type = MyListItem.CheckType.Clickable
                .Tag = Version
                .Title = If(IsBadDisplay, Version.FileName, Version.Display).BeforeLastOfAny({".zip", ".jar", ".mrpack", ".litemod"}, True)
                .Logo = $"{PathImage}ReleaseTypes/{Version.ReleaseType}.png"
                AddHandler .Click, ClickHandler
                '描述
                .Info =
                Iterator Function()
                    If Version.ModLoaders <> ModLoaders.None Then Yield Version.ModLoaders.Flags.Join("、"c)
                    If .Title <> PathUtils.GetFileNameWithoutExtension(Version.FileName) Then Yield PathUtils.GetFileNameWithoutExtension(Version.FileName)
                    If Version.Dependencies.Any Then Yield $"{Version.Dependencies.Count} 项前置"
                    If Version.GameVersions.All(Function(v) Not v.Contains(".") OrElse {"w", "snapshot", "rc", "pre", "experimental", "-"}.Any(Function(s) v.ContainsIgnoreCase(s))) Then Yield $"游戏版本 {Version.GameVersions.Join("、"c)}"
                    If Version.DownloadCount > 0 Then Yield $"下载 {If(Version.DownloadCount > 100000, Math.Round(Version.DownloadCount / 10000) & " 万", Version.DownloadCount.ToString() + " ")}次" 'CurseForge 的下载次数经常错误地返回 0
                    Yield $"更新于 {StringUtils.FormatTimeSpan(Version.ReleaseDate - Date.Now, False)}"
                    If Version.ReleaseType <> ResourceVersion.ReleaseTypes.Release Then Yield Version.ReleaseTypeDisplay
                End Function().Join("，"c)

                '另存为按钮
                If SaveAsButtonHandler IsNot Nothing Then
                    Dim BtnSave As New MyIconButton With {.Logo = Logo.IconButtonSave, .ToolTip = "另存为"}
                    AddHandler BtnSave.Click, SaveAsButtonHandler
                    .Buttons = {BtnSave}
                End If

            End With
            Return Result
        End Function) With {.Height = 42}
    End Function

    ''' <summary>
    ''' 获取下载信息。
    ''' </summary>
    ''' <param name="LocalFileOrFolder">目标本地文件夹，或完整的文件路径。会自动判断类型。</param>
    <Extension>
    Public Function ToNetFile(Version As ResourceVersion, LocalFileOrFolder As String,
            Optional TrackDownloadReason As ResourceVersion.DownloadReason? = Nothing, Optional TrackGameVersion As String = Nothing, Optional TrackLoader As ModLoaders = ModLoaders.None) As NetFile
        Return New NetFile(
            ResourceVersion.ParseModrinthTrackArguments(Version.DownloadUrls, TrackDownloadReason, TrackGameVersion, TrackLoader),
            LocalFileOrFolder & If(LocalFileOrFolder.EndsWithF("\"), Version.FileName, ""),
            New FileChecker With {.Hash = Version.Hash, .ActualSize = Version.Size},
            SimulateBrowserHeaders:=True)
    End Function

End Module
