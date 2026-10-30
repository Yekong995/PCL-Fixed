'本文件的逻辑已迁移至 PCLCS\Modules\ModMusic.cs，此处保留同名转发壳。

Imports NAudio.Wave

Public Module ModMusic

#Region "播放列表"

    ''' <summary>
    ''' 接下来要播放的音乐文件路径。未初始化时为 Nothing。
    ''' </summary>
    Public Property MusicWaitingList As List(Of String)
        Get
            Return PCLCS.Modules.ModMusic.MusicWaitingList
        End Get
        Set(value As List(Of String))
            PCLCS.Modules.ModMusic.MusicWaitingList = value
        End Set
    End Property
    ''' <summary>
    ''' 全部音乐文件路径。未初始化时为 Nothing。
    ''' </summary>
    Public Property MusicAllList As List(Of String)
        Get
            Return PCLCS.Modules.ModMusic.MusicAllList
        End Get
        Set(value As List(Of String))
            PCLCS.Modules.ModMusic.MusicAllList = value
        End Set
    End Property

#End Region

#Region "UI 控制"

    ''' <summary>
    ''' 让音乐在暂停、播放间切换，并显示提示文本。
    ''' </summary>
    Public Sub MusicControlPause()
        PCLCS.Modules.ModMusic.MusicControlPause()
    End Sub

    ''' <summary>
    ''' 播放下一曲，并显示提示文本。
    ''' </summary>
    Public Sub MusicControlNext()
        PCLCS.Modules.ModMusic.MusicControlNext()
    End Sub

#End Region

#Region "主状态控制"

    ''' <summary>
    ''' 获取当前的音乐播放状态。
    ''' </summary>
    Public ReadOnly Property MusicState As MusicStates
        Get
            Return PCLCS.Modules.ModMusic.MusicState
        End Get
    End Property

    '重载与开始

    ''' <summary>
    ''' 重载播放列表并尝试开始播放背景音乐。
    ''' </summary>
    ''' <param name="ShowHint">是否显示刷新提示。</param>
    Public Sub MusicRefreshPlay(ShowHint As Boolean, Optional IsFirstLoad As Boolean = False)
        PCLCS.Modules.ModMusic.MusicRefreshPlay(ShowHint, IsFirstLoad)
    End Sub

    '播放与暂停

    ''' <summary>
    ''' 暂停音乐播放，返回是否成功切换了状态。
    ''' </summary>
    Public Function MusicPause() As Boolean
        Return PCLCS.Modules.ModMusic.MusicPause()
    End Function
    ''' <summary>
    ''' 继续音乐播放，返回是否成功切换了状态。
    ''' </summary>
    Public Function MusicResume() As Boolean
        Return PCLCS.Modules.ModMusic.MusicResume()
    End Function

#End Region

    ''' <summary>
    ''' 当前正在播放的 WaveOutEvent。
    ''' 如果为它赋了类别，则会没有必要地加载 NAudio.dll。
    ''' </summary>
    Public Property MusicNAudio As Object
        Get
            Return PCLCS.Modules.ModMusic.MusicNAudio
        End Get
        Set(value As Object)
            PCLCS.Modules.ModMusic.MusicNAudio = TryCast(value, WaveOutEvent)
        End Set
    End Property

End Module
