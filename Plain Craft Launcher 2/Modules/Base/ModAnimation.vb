'动画引擎模块
'使用 Ani 作为方法或属性的开头，使用 Aa 作为单个动画对象的开头（便于自动补全）
'本模块已整体迁移至 C#（PCLCS\Modules\Base\PclAnimation.cs），此处保留为转发壳。
'AniData、AniType、AniTypeSub、AniEasePower、AniEase 及其全部子类、AniGroupEntry 均定义于 C# 侧，
'经 Imports PCLCS.Modules 解析，VB 调用点无需改动。

Imports System.Windows.Media.Effects

Public Module ModAnimation

#Region "声明"

    ''' <summary>
    ''' 动画速度。最大为 200。
    ''' </summary>
    Public ReadOnly Property AniSpeed As Double
        Get
            Return PclAnimation.AniSpeed
        End Get
    End Property
    ''' <summary>
    ''' 动画组列表。
    ''' </summary>
    Public Property AniGroups As Dictionary(Of String, AniGroupEntry)
        Get
            Return PclAnimation.AniGroups
        End Get
        Set(value As Dictionary(Of String, AniGroupEntry))
            PclAnimation.AniGroups = value
        End Set
    End Property
    ''' <summary>
    ''' 动画模块是否正在运行。
    ''' </summary>
    Public Property AniRunning As Boolean
        Get
            Return PclAnimation.AniRunning
        End Get
        Set(value As Boolean)
            PclAnimation.AniRunning = value
        End Set
    End Property
    ''' <summary>
    ''' 控件动画执行是否开启。先 +1，再 -1。
    ''' </summary>
    Public Property AniControlEnabled() As Integer
        Get
            Return PclAnimation.AniControlEnabled
        End Get
        Set(value As Integer)
            PclAnimation.AniControlEnabled = value
        End Set
    End Property

#End Region

#Region "种类"

    'DoubleAnimation

    ''' <summary>
    ''' 移动X轴的动画。
    ''' </summary>
    ''' <param name="Obj">动画的对象。</param>
    ''' <param name="Value">进行移动的值。</param>
    ''' <param name="Time">动画长度（毫秒）。</param>
    ''' <param name="Delay">动画延迟执行的时间（毫秒）。</param>
    ''' <param name="Ease">插值器类型。</param>
    ''' <param name="After">是否等到以前的动画完成后才继续本动画。</param>
    Public Function AaX(Obj As Object, Value As Double, Optional Time As Integer = 400, Optional Delay As Integer = 0, Optional Ease As AniEase = Nothing, Optional After As Boolean = False) As AniData
        Return PclAnimation.AaX(Obj, Value, Time, Delay, Ease, After)
    End Function
    ''' <summary>
    ''' 移动Y轴的动画。
    ''' </summary>
    ''' <param name="Obj">动画的对象。</param>
    ''' <param name="Value">进行移动的值。</param>
    ''' <param name="Time">动画长度（毫秒）。</param>
    ''' <param name="Delay">动画延迟执行的时间（毫秒）。</param>
    ''' <param name="Ease">插值器类型。</param>
    ''' <param name="After">是否等到以前的动画完成后才继续本动画。</param>
    Public Function AaY(Obj As Object, Value As Double, Optional Time As Integer = 400, Optional Delay As Integer = 0, Optional Ease As AniEase = Nothing, Optional After As Boolean = False) As AniData
        Return PclAnimation.AaY(Obj, Value, Time, Delay, Ease, After)
    End Function
    ''' <summary>
    ''' 改变宽度的动画。
    ''' </summary>
    ''' <param name="Obj">动画的对象。</param>
    ''' <param name="Value">宽度改变的值。</param>
    ''' <param name="Time">动画长度（毫秒）。</param>
    ''' <param name="Delay">动画延迟执行的时间（毫秒）。</param>
    ''' <param name="Ease">插值器类型。</param>
    ''' <param name="After">是否等到以前的动画完成后才继续本动画。</param>
    Public Function AaWidth(Obj As Object, Value As Double, Optional Time As Integer = 400, Optional Delay As Integer = 0, Optional Ease As AniEase = Nothing, Optional After As Boolean = False) As AniData
        Return PclAnimation.AaWidth(Obj, Value, Time, Delay, Ease, After)
    End Function
    ''' <summary>
    ''' 改变高度的动画。
    ''' </summary>
    ''' <param name="Obj">动画的对象。</param>
    ''' <param name="Value">高度改变的值。</param>
    ''' <param name="Time">动画长度（毫秒）。</param>
    ''' <param name="Delay">动画延迟执行的时间（毫秒）。</param>
    ''' <param name="Ease">插值器类型。</param>
    ''' <param name="After">是否等到以前的动画完成后才继续本动画。</param>
    Public Function AaHeight(Obj As Object, Value As Double, Optional Time As Integer = 400, Optional Delay As Integer = 0, Optional Ease As AniEase = Nothing, Optional After As Boolean = False) As AniData
        Return PclAnimation.AaHeight(Obj, Value, Time, Delay, Ease, After)
    End Function
    ''' <summary>
    ''' 改变透明度的动画。
    ''' </summary>
    ''' <param name="Obj">动画的对象。</param>
    ''' <param name="Value">透明度改变的值。</param>
    ''' <param name="Time">动画长度（毫秒）。</param>
    ''' <param name="Delay">动画延迟执行的时间（毫秒）。</param>
    ''' <param name="Ease">插值器类型。</param>
    ''' <param name="After">是否等到以前的动画完成后才继续本动画。</param>
    Public Function AaOpacity(Obj As Object, Value As Double, Optional Time As Integer = 400, Optional Delay As Integer = 0, Optional Ease As AniEase = Nothing, Optional After As Boolean = False) As AniData
        Return PclAnimation.AaOpacity(Obj, Value, Time, Delay, Ease, After)
    End Function
    ''' <summary>
    ''' 改变对象的Value属性的动画。
    ''' </summary>
    ''' <param name="Obj">动画的对象。</param>
    ''' <param name="Value">Value属性改变的值。</param>
    ''' <param name="Time">动画长度（毫秒）。</param>
    ''' <param name="Delay">动画延迟执行的时间（毫秒）。</param>
    ''' <param name="Ease">插值器类型。</param>
    ''' <param name="After">是否等到以前的动画完成后才继续本动画。</param>
    Public Function AaValue(Obj As Object, Value As Double, Optional Time As Integer = 400, Optional Delay As Integer = 0, Optional Ease As AniEase = Nothing, Optional After As Boolean = False) As AniData
        Return PclAnimation.AaValue(Obj, Value, Time, Delay, Ease, After)
    End Function
    ''' <summary>
    ''' 改变对象的Radius属性的动画。
    ''' </summary>
    ''' <param name="Obj">动画的对象。</param>
    ''' <param name="Value">Radius属性改变的值。</param>
    ''' <param name="Time">动画长度（毫秒）。</param>
    ''' <param name="Delay">动画延迟执行的时间（毫秒）。</param>
    ''' <param name="Ease">插值器类型。</param>
    ''' <param name="After">是否等到以前的动画完成后才继续本动画。</param>
    Public Function AaRadius(Obj As Object, Value As Double, Optional Time As Integer = 400, Optional Delay As Integer = 0, Optional Ease As AniEase = Nothing, Optional After As Boolean = False) As AniData
        Return PclAnimation.AaRadius(Obj, Value, Time, Delay, Ease, After)
    End Function
    ''' <summary>
    ''' 改变对象的BorderThickness属性的动画。
    ''' </summary>
    ''' <param name="Obj">动画的对象。</param>
    ''' <param name="Value">BorderThickness属性改变的值。</param>
    ''' <param name="Time">动画长度（毫秒）。</param>
    ''' <param name="Delay">动画延迟执行的时间（毫秒）。</param>
    ''' <param name="Ease">插值器类型。</param>
    ''' <param name="After">是否等到以前的动画完成后才继续本动画。</param>
    Public Function AaBorderThickness(Obj As Object, Value As Double, Optional Time As Integer = 400, Optional Delay As Integer = 0, Optional Ease As AniEase = Nothing, Optional After As Boolean = False) As AniData
        Return PclAnimation.AaBorderThickness(Obj, Value, Time, Delay, Ease, After)
    End Function
    ''' <summary>
    ''' 改变对象的StrokeThickness属性的动画。
    ''' </summary>
    ''' <param name="Obj">动画的对象。</param>
    ''' <param name="Value">StrokeThickness属性改变的值。</param>
    ''' <param name="Time">动画长度（毫秒）。</param>
    ''' <param name="Delay">动画延迟执行的时间（毫秒）。</param>
    ''' <param name="Ease">插值器类型。</param>
    ''' <param name="After">是否等到以前的动画完成后才继续本动画。</param>
    Public Function AaStrokeThickness(Obj As Object, Value As Double, Optional Time As Integer = 400, Optional Delay As Integer = 0, Optional Ease As AniEase = Nothing, Optional After As Boolean = False) As AniData
        Return PclAnimation.AaStrokeThickness(Obj, Value, Time, Delay, Ease, After)
    End Function
    ''' <summary>
    ''' 改变 Width 的 GridLength 属性的动画。必须为 Star。
    ''' </summary>
    ''' <param name="Obj">动画的对象。</param>
    ''' <param name="Value">GridLength.Value 改变的值。</param>
    ''' <param name="Time">动画长度（毫秒）。</param>
    ''' <param name="Delay">动画延迟执行的时间（毫秒）。</param>
    ''' <param name="Ease">插值器类型。</param>
    ''' <param name="After">是否等到以前的动画完成后才继续本动画。</param>
    Public Function AaGridLengthWidth(Obj As Object, Value As Double, Optional Time As Integer = 400, Optional Delay As Integer = 0, Optional Ease As AniEase = Nothing, Optional After As Boolean = False) As AniData
        Return PclAnimation.AaGridLengthWidth(Obj, Value, Time, Delay, Ease, After)
    End Function

    'DoubleAnimation（Obj, Prop, [Res]）

    ''' <summary>
    ''' 改变数字属性的动画。
    ''' </summary>
    ''' <param name="Obj">动画的对象。</param>
    ''' <param name="Prop">动画的依赖属性。</param>
    ''' <param name="Value">改变的值。</param>
    ''' <param name="Time">动画长度（毫秒）。</param>
    ''' <param name="Delay">动画延迟执行的时间（毫秒）。</param>
    ''' <param name="Ease">插值器类型。</param>
    ''' <param name="After">是否等到以前的动画完成后才继续本动画。</param>
    Public Function AaDouble(Obj As Object, Prop As DependencyProperty, Value As Double, Optional Time As Integer = 400, Optional Delay As Integer = 0, Optional Ease As AniEase = Nothing, Optional After As Boolean = False) As AniData
        Return PclAnimation.AaDouble(Obj, Prop, Value, Time, Delay, Ease, After)
    End Function
    ''' <summary>
    ''' 获取数字动画值。
    ''' </summary>
    ''' <param name="Value">改变的值。</param>
    ''' <param name="Time">动画长度（毫秒）。</param>
    ''' <param name="Delay">动画延迟执行的时间（毫秒）。</param>
    ''' <param name="Ease">插值器类型。</param>
    ''' <param name="After">是否等到以前的动画完成后才继续本动画。</param>
    Public Function AaDouble(Lambda As ParameterizedThreadStart, Value As Double, Optional Time As Integer = 400, Optional Delay As Integer = 0, Optional Ease As AniEase = Nothing, Optional After As Boolean = False) As AniData
        Return PclAnimation.AaDouble(Lambda, Value, Time, Delay, Ease, After)
    End Function

    'ColorAnimation（Obj, Prop, [Res]）

    ''' <summary>
    ''' 改变颜色属性的动画。
    ''' </summary>
    ''' <param name="Obj">动画的对象。</param>
    ''' <param name="Prop">动画的依赖属性。</param>
    ''' <param name="Value">颜色改变的值。以RGB加减法进行计算。不用担心超额。</param>
    ''' <param name="Time">动画长度（毫秒）。</param>
    ''' <param name="Delay">动画延迟执行的时间（毫秒）。</param>
    ''' <param name="Ease">插值器类型。</param>
    ''' <param name="After">是否等到以前的动画完成后才继续本动画。</param>
    Public Function AaColor(Obj As FrameworkElement, Prop As DependencyProperty, Value As MyColor, Optional Time As Integer = 400, Optional Delay As Integer = 0, Optional Ease As AniEase = Nothing, Optional After As Boolean = False) As AniData
        Return PclAnimation.AaColor(Obj, Prop, Value, Time, Delay, Ease, After)
    End Function
    ''' <summary>
    ''' 改变颜色属性为一个资源的动画。
    ''' </summary>
    ''' <param name="Obj">动画的对象。</param>
    ''' <param name="Prop">动画的依赖属性。</param>
    ''' <param name="Res">要将颜色改变为该资源值。</param>
    ''' <param name="Time">动画长度（毫秒）。</param>
    ''' <param name="Delay">动画延迟执行的时间（毫秒）。</param>
    ''' <param name="Ease">插值器类型。</param>
    ''' <param name="After">是否等到以前的动画完成后才继续本动画。</param>
    Public Function AaColor(Obj As FrameworkElement, Prop As DependencyProperty, Res As String, Optional Time As Integer = 400, Optional Delay As Integer = 0, Optional Ease As AniEase = Nothing, Optional After As Boolean = False) As AniData
        Return PclAnimation.AaColor(Obj, Prop, Res, Time, Delay, Ease, After)
    End Function

    'Scale

    ''' <summary>
    ''' 缩放控件的动画。
    ''' </summary>
    ''' <param name="Obj">动画的对象。</param>
    ''' <param name="Value">大小改变的百分比（如-0.6）或值。</param>
    ''' <param name="Time">动画长度（毫秒）。</param>
    ''' <param name="Delay">动画延迟执行的时间（毫秒）。</param>
    ''' <param name="Ease">插值器类型。</param>
    ''' <param name="After">是否等到以前的动画完成后才继续本动画。</param>
    ''' <param name="Absolute">大小改变是否为绝对值。若为 True 则为绝对像素，若为 False 则为相对百分比。</param>
    Public Function AaScale(Obj As Object, Value As Double, Optional Time As Integer = 400, Optional Delay As Integer = 0, Optional Ease As AniEase = Nothing, Optional After As Boolean = False, Optional Absolute As Boolean = False) As AniData
        Return PclAnimation.AaScale(Obj, Value, Time, Delay, Ease, After, Absolute)
    End Function

    'TextAppear

    ''' <summary>
    ''' 让一段文字一个个字出现或消失的动画。
    ''' </summary>
    ''' <param name="Obj">动画的对象。必须是Label或TextBlock。</param>
    ''' <param name="Hide">是否为一个个字隐藏。默认为False（一个个字出现）。这些字必须已经存在了。</param>
    ''' <param name="TimePerText">是否采用根据文本长度决定时间的方式。</param>
    ''' <param name="Time">动画长度（毫秒）。若TimePerText为True，这代表每个字所占据的时间。</param>
    ''' <param name="Delay">动画延迟执行的时间（毫秒）。</param>
    ''' <param name="After">是否等到以前的动画完成后才继续本动画。</param>
    Public Function AaTextAppear(Obj As Object, Optional Hide As Boolean = False, Optional TimePerText As Boolean = True, Optional Time As Integer = 70, Optional Delay As Integer = 0, Optional Ease As AniEase = Nothing, Optional After As Boolean = False) As AniData
        Return PclAnimation.AaTextAppear(Obj, Hide, TimePerText, Time, Delay, Ease, After)
    End Function

    'Code

    ''' <summary>
    ''' 执行代码。
    ''' </summary>
    ''' <param name="Code">一个ThreadStart。这将会在执行时在主线程调用。</param>
    ''' <param name="Delay">代码延迟执行的时间（毫秒）。</param>
    ''' <param name="After">是否等到以前的动画完成后才执行。</param>
    Public Function AaCode(Code As ThreadStart, Optional Delay As Integer = 0, Optional After As Boolean = False) As AniData
        Return PclAnimation.AaCode(Code, Delay, After)
    End Function

    'ScaleTransform

    ''' <summary>
    ''' 按照 WPF 方式缩放控件的动画。
    ''' </summary>
    ''' <param name="Obj">动画的对象。它必须已经拥有了单一的 ScaleTransform 值。</param>
    ''' <param name="Value">大小改变的百分比（如-0.6）。</param>
    ''' <param name="Time">动画长度（毫秒）。</param>
    ''' <param name="Delay">动画延迟执行的时间（毫秒）。</param>
    ''' <param name="Ease">插值器类型。</param>
    ''' <param name="After">是否等到以前的动画完成后才继续本动画。</param>
    Public Function AaScaleTransform(Obj As Object, Value As Double, Optional Time As Integer = 400, Optional Delay As Integer = 0, Optional Ease As AniEase = Nothing, Optional After As Boolean = False) As AniData
        Return PclAnimation.AaScaleTransform(Obj, Value, Time, Delay, Ease, After)
    End Function

    'RotateTransform

    ''' <summary>
    ''' 按照 WPF 方式旋转控件的动画。
    ''' </summary>
    ''' <param name="Obj">动画的对象。它必须已经拥有了单一的 ScaleTransform 值。</param>
    ''' <param name="Value">旋转角度（度）。</param>
    ''' <param name="Time">动画长度（毫秒）。</param>
    ''' <param name="Delay">动画延迟执行的时间（毫秒）。</param>
    ''' <param name="Ease">插值器类型。</param>
    ''' <param name="After">是否等到以前的动画完成后才继续本动画。</param>
    Public Function AaRotateTransform(Obj As Object, Value As Double, Optional Time As Integer = 400, Optional Delay As Integer = 0, Optional Ease As AniEase = Nothing, Optional After As Boolean = False) As AniData
        Return PclAnimation.AaRotateTransform(Obj, Value, Time, Delay, Ease, After)
    End Function

    'TranslateTransform

    ''' <summary>
    ''' 利用 TranslateTransform 移动 X 轴的动画，这不会造成布局更新。
    ''' </summary>
    ''' <param name="Obj">动画的对象。</param>
    ''' <param name="Value">进行移动的值。</param>
    ''' <param name="Time">动画长度（毫秒）。</param>
    ''' <param name="Delay">动画延迟执行的时间（毫秒）。</param>
    ''' <param name="Ease">插值器类型。</param>
    ''' <param name="After">是否等到以前的动画完成后才继续本动画。</param>
    Public Function AaTranslateX(Obj As Object, Value As Double, Optional Time As Integer = 400, Optional Delay As Integer = 0, Optional Ease As AniEase = Nothing, Optional After As Boolean = False) As AniData
        Return PclAnimation.AaTranslateX(Obj, Value, Time, Delay, Ease, After)
    End Function
    ''' <summary>
    ''' 利用 TranslateTransform 移动 Y 轴的动画，这不会造成布局更新。
    ''' </summary>
    ''' <param name="Obj">动画的对象。</param>
    ''' <param name="Value">进行移动的值。</param>
    ''' <param name="Time">动画长度（毫秒）。</param>
    ''' <param name="Delay">动画延迟执行的时间（毫秒）。</param>
    ''' <param name="Ease">插值器类型。</param>
    ''' <param name="After">是否等到以前的动画完成后才继续本动画。</param>
    Public Function AaTranslateY(Obj As Object, Value As Double, Optional Time As Integer = 400, Optional Delay As Integer = 0, Optional Ease As AniEase = Nothing, Optional After As Boolean = False) As AniData
        Return PclAnimation.AaTranslateY(Obj, Value, Time, Delay, Ease, After)
    End Function

    '特殊

    ''' <summary>
    ''' 将一个StackPanel中的各个项目依次显示。
    ''' </summary>
    Public Function AaStack(Stack As StackPanel, Optional Time As Integer = 100, Optional Delay As Integer = 25) As List(Of AniData)
        Return PclAnimation.AaStack(Stack, Time, Delay)
    End Function

#End Region

#Region "接口（开始、中断、检测）"

    ''' <summary>
    ''' 开始一个动画组。
    ''' </summary>
    ''' <param name="AniGroup">由 Aa 开头的函数初始化的 AniData 对象集合。</param>
    ''' <param name="Name">动画组的名称。如果重复会直接停止同名动画组。</param>
    Public Sub AniStart(AniGroup As IList, Optional Name As String = "", Optional RefreshTime As Boolean = False)
        PclAnimation.AniStart(AniGroup, Name, RefreshTime)
    End Sub
    ''' <summary>
    ''' 开始一个动画组。
    ''' </summary>
    Public Sub AniStart(AniGroup As AniData, Optional Name As String = "", Optional RefreshTime As Boolean = False)
        PclAnimation.AniStart(AniGroup, Name, RefreshTime)
    End Sub
    ''' <summary>
    ''' 直接停止一个动画组。
    ''' </summary>
    ''' <param name="name">需要停止的动画组的名称。</param>
    Public Sub AniStop(Name As String)
        PclAnimation.AniStop(Name)
    End Sub
    ''' <summary>
    ''' 获取动画是否正在进行中。
    ''' </summary>
    Public Function AniIsRun(Name As String) As Boolean
        Return PclAnimation.AniIsRun(Name)
    End Function

#End Region

    ''' <summary>
    ''' 当前的动画 FPS。
    ''' </summary>
    Public Property AniFPS As Integer
        Get
            Return PclAnimation.AniFPS
        End Get
        Set(value As Integer)
            PclAnimation.AniFPS = value
        End Set
    End Property

    ''' <summary>
    ''' 开始动画执行。
    ''' </summary>
    Public Sub AniStart()
        PclAnimation.AniStart()
    End Sub

    ''' <summary>
    ''' 动画定时器事件。
    ''' </summary>
    Public Sub AniTimer(DeltaTick As Double)
        PclAnimation.AniTimer(DeltaTick)
    End Sub

End Module
