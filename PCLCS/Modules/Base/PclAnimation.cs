// 由 VB Modules\Base\ModAnimation.vb 整个动画引擎模块迁移而来。
// AniData.Obj 为 Object，VB 靠 Option Strict Off 后期绑定访问其成员与索引，C# 侧使用 dynamic 保持行为一致。
// ModeDebug 经 PclUiBridge.ModeDebugGet 桥接；下载状态经 NetCore.NetManager 访问。
// VB 侧 ModAnimation.vb 保留为转发壳，经 Imports PCLCS.Modules 解析类型并调用 PclAnimation。
#nullable disable

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using MeloongCore;

namespace PCLCS.Modules;

public static class PclAnimation {

    #region 声明

    /// <summary>
    /// 动画速度。最大为 200。
    /// </summary>
    public static double AniSpeed {
        get {
            int Setting = PclCore.GetSetting<int>("SystemDebugAnim");
            return Setting >= 30 ? 200 : (Setting * 0.1 + 0.1).Clamp(0.1, 3);
        }
    }
    /// <summary>
    /// 动画组列表。
    /// </summary>
    public static Dictionary<string, AniGroupEntry> AniGroups = new Dictionary<string, AniGroupEntry>();
    /// <summary>
    /// 上一次记刻的时间。
    /// </summary>
    private static long AniLastTick;
    /// <summary>
    /// 动画模块是否正在运行。
    /// </summary>
    public static bool AniRunning = false;
    private static int _AniControlEnabled = 0;
    private static readonly object AniControlEnabledLock = new object();
    /// <summary>
    /// 控件动画执行是否开启。先 +1，再 -1。
    /// </summary>
    public static int AniControlEnabled {
        get {
            return _AniControlEnabled;
        }
        set {
            lock (AniControlEnabledLock) {
                _AniControlEnabled = value;
            }
        }
    }

    #endregion



    #region 种类

    // DoubleAnimation

    /// <summary>
    /// 移动X轴的动画。
    /// </summary>
    /// <param name="Obj">动画的对象。</param>
    /// <param name="Value">进行移动的值。</param>
    /// <param name="Time">动画长度（毫秒）。</param>
    /// <param name="Delay">动画延迟执行的时间（毫秒）。</param>
    /// <param name="Ease">插值器类型。</param>
    /// <param name="After">是否等到以前的动画完成后才继续本动画。</param>
    public static AniData AaX(object Obj, double Value, int Time = 400, int Delay = 0, AniEase Ease = null, bool After = false) {
        return new AniData { TypeMain = AniType.Number, TypeSub = AniTypeSub.X,
                               TimeTotal = Time, Ease = Ease ?? new AniEaseLinear(), Obj = Obj, Value = Value, IsAfter = After, TimeFinished = -Delay };
    }
    /// <summary>
    /// 移动Y轴的动画。
    /// </summary>
    /// <param name="Obj">动画的对象。</param>
    /// <param name="Value">进行移动的值。</param>
    /// <param name="Time">动画长度（毫秒）。</param>
    /// <param name="Delay">动画延迟执行的时间（毫秒）。</param>
    /// <param name="Ease">插值器类型。</param>
    /// <param name="After">是否等到以前的动画完成后才继续本动画。</param>
    public static AniData AaY(object Obj, double Value, int Time = 400, int Delay = 0, AniEase Ease = null, bool After = false) {
        return new AniData { TypeMain = AniType.Number, TypeSub = AniTypeSub.Y,
                               TimeTotal = Time, Ease = Ease ?? new AniEaseLinear(), Obj = Obj, Value = Value, IsAfter = After, TimeFinished = -Delay };
    }
    /// <summary>
    /// 改变宽度的动画。
    /// </summary>
    /// <param name="Obj">动画的对象。</param>
    /// <param name="Value">宽度改变的值。</param>
    /// <param name="Time">动画长度（毫秒）。</param>
    /// <param name="Delay">动画延迟执行的时间（毫秒）。</param>
    /// <param name="Ease">插值器类型。</param>
    /// <param name="After">是否等到以前的动画完成后才继续本动画。</param>
    public static AniData AaWidth(object Obj, double Value, int Time = 400, int Delay = 0, AniEase Ease = null, bool After = false) {
        return new AniData { TypeMain = AniType.Number, TypeSub = AniTypeSub.Width,
                               TimeTotal = Time, Ease = Ease ?? new AniEaseLinear(), Obj = Obj, Value = Value, IsAfter = After, TimeFinished = -Delay };
    }
    /// <summary>
    /// 改变高度的动画。
    /// </summary>
    /// <param name="Obj">动画的对象。</param>
    /// <param name="Value">高度改变的值。</param>
    /// <param name="Time">动画长度（毫秒）。</param>
    /// <param name="Delay">动画延迟执行的时间（毫秒）。</param>
    /// <param name="Ease">插值器类型。</param>
    /// <param name="After">是否等到以前的动画完成后才继续本动画。</param>
    public static AniData AaHeight(object Obj, double Value, int Time = 400, int Delay = 0, AniEase Ease = null, bool After = false) {
        return new AniData { TypeMain = AniType.Number, TypeSub = AniTypeSub.Height,
                               TimeTotal = Time, Ease = Ease ?? new AniEaseLinear(), Obj = Obj, Value = Value, IsAfter = After, TimeFinished = -Delay };
    }
    /// <summary>
    /// 改变透明度的动画。
    /// </summary>
    /// <param name="Obj">动画的对象。</param>
    /// <param name="Value">透明度改变的值。</param>
    /// <param name="Time">动画长度（毫秒）。</param>
    /// <param name="Delay">动画延迟执行的时间（毫秒）。</param>
    /// <param name="Ease">插值器类型。</param>
    /// <param name="After">是否等到以前的动画完成后才继续本动画。</param>
    public static AniData AaOpacity(object Obj, double Value, int Time = 400, int Delay = 0, AniEase Ease = null, bool After = false) {
        return new AniData { TypeMain = AniType.Number, TypeSub = AniTypeSub.Opacity,
                               TimeTotal = Time, Ease = Ease ?? new AniEaseLinear(), Obj = Obj, Value = Value, IsAfter = After, TimeFinished = -Delay };
    }
    /// <summary>
    /// 改变对象的Value属性的动画。
    /// </summary>
    /// <param name="Obj">动画的对象。</param>
    /// <param name="Value">Value属性改变的值。</param>
    /// <param name="Time">动画长度（毫秒）。</param>
    /// <param name="Delay">动画延迟执行的时间（毫秒）。</param>
    /// <param name="Ease">插值器类型。</param>
    /// <param name="After">是否等到以前的动画完成后才继续本动画。</param>
    public static AniData AaValue(object Obj, double Value, int Time = 400, int Delay = 0, AniEase Ease = null, bool After = false) {
        return new AniData { TypeMain = AniType.Number, TypeSub = AniTypeSub.Value,
                               TimeTotal = Time, Ease = Ease ?? new AniEaseLinear(), Obj = Obj, Value = Value, IsAfter = After, TimeFinished = -Delay };
    }
    /// <summary>
    /// 改变对象的Radius属性的动画。
    /// </summary>
    /// <param name="Obj">动画的对象。</param>
    /// <param name="Value">Radius属性改变的值。</param>
    /// <param name="Time">动画长度（毫秒）。</param>
    /// <param name="Delay">动画延迟执行的时间（毫秒）。</param>
    /// <param name="Ease">插值器类型。</param>
    /// <param name="After">是否等到以前的动画完成后才继续本动画。</param>
    public static AniData AaRadius(object Obj, double Value, int Time = 400, int Delay = 0, AniEase Ease = null, bool After = false) {
        return new AniData { TypeMain = AniType.Number, TypeSub = AniTypeSub.Radius,
                               TimeTotal = Time, Ease = Ease ?? new AniEaseLinear(), Obj = Obj, Value = Value, IsAfter = After, TimeFinished = -Delay };
    }
    /// <summary>
    /// 改变对象的BorderThickness属性的动画。
    /// </summary>
    /// <param name="Obj">动画的对象。</param>
    /// <param name="Value">BorderThickness属性改变的值。</param>
    /// <param name="Time">动画长度（毫秒）。</param>
    /// <param name="Delay">动画延迟执行的时间（毫秒）。</param>
    /// <param name="Ease">插值器类型。</param>
    /// <param name="After">是否等到以前的动画完成后才继续本动画。</param>
    public static AniData AaBorderThickness(object Obj, double Value, int Time = 400, int Delay = 0, AniEase Ease = null, bool After = false) {
        return new AniData { TypeMain = AniType.Number, TypeSub = AniTypeSub.BorderThickness,
                               TimeTotal = Time, Ease = Ease ?? new AniEaseLinear(), Obj = Obj, Value = Value, IsAfter = After, TimeFinished = -Delay };
    }
    /// <summary>
    /// 改变对象的StrokeThickness属性的动画。
    /// </summary>
    /// <param name="Obj">动画的对象。</param>
    /// <param name="Value">StrokeThickness属性改变的值。</param>
    /// <param name="Time">动画长度（毫秒）。</param>
    /// <param name="Delay">动画延迟执行的时间（毫秒）。</param>
    /// <param name="Ease">插值器类型。</param>
    /// <param name="After">是否等到以前的动画完成后才继续本动画。</param>
    public static AniData AaStrokeThickness(object Obj, double Value, int Time = 400, int Delay = 0, AniEase Ease = null, bool After = false) {
        return new AniData { TypeMain = AniType.Number, TypeSub = AniTypeSub.StrokeThickness,
                               TimeTotal = Time, Ease = Ease ?? new AniEaseLinear(), Obj = Obj, Value = Value, IsAfter = After, TimeFinished = -Delay };
    }
    /// <summary>
    /// 改变 Width 的 GridLength 属性的动画。必须为 Star。
    /// </summary>
    /// <param name="Obj">动画的对象。</param>
    /// <param name="Value">GridLength.Value 改变的值。</param>
    /// <param name="Time">动画长度（毫秒）。</param>
    /// <param name="Delay">动画延迟执行的时间（毫秒）。</param>
    /// <param name="Ease">插值器类型。</param>
    /// <param name="After">是否等到以前的动画完成后才继续本动画。</param>
    public static AniData AaGridLengthWidth(object Obj, double Value, int Time = 400, int Delay = 0, AniEase Ease = null, bool After = false) {
        return new AniData { TypeMain = AniType.Number, TypeSub = AniTypeSub.GridLengthWidth,
                               TimeTotal = Time, Ease = Ease ?? new AniEaseLinear(), Obj = Obj, Value = Value, IsAfter = After, TimeFinished = -Delay };
    }

    // DoubleAnimation（Obj, Prop, [Res]）

    /// <summary>
    /// 改变数字属性的动画。
    /// </summary>
    /// <param name="Obj">动画的对象。</param>
    /// <param name="Prop">动画的依赖属性。</param>
    /// <param name="Value">改变的值。</param>
    /// <param name="Time">动画长度（毫秒）。</param>
    /// <param name="Delay">动画延迟执行的时间（毫秒）。</param>
    /// <param name="Ease">插值器类型。</param>
    /// <param name="After">是否等到以前的动画完成后才继续本动画。</param>
    public static AniData AaDouble(object Obj, DependencyProperty Prop, double Value, int Time = 400, int Delay = 0, AniEase Ease = null, bool After = false) {
        return new AniData { TypeMain = AniType.Number, TypeSub = AniTypeSub.Double, TimeTotal = Time, Ease = Ease ?? new AniEaseLinear(), Obj = new object[] { Obj, Prop, "" }, Value = Value, IsAfter = After, TimeFinished = -Delay };
    }
    /// <summary>
    /// 获取数字动画值。
    /// </summary>
    /// <param name="Value">改变的值。</param>
    /// <param name="Time">动画长度（毫秒）。</param>
    /// <param name="Delay">动画延迟执行的时间（毫秒）。</param>
    /// <param name="Ease">插值器类型。</param>
    /// <param name="After">是否等到以前的动画完成后才继续本动画。</param>
    public static AniData AaDouble(ParameterizedThreadStart Lambda, double Value, int Time = 400, int Delay = 0, AniEase Ease = null, bool After = false) {
        return new AniData { TypeMain = AniType.Number, TypeSub = AniTypeSub.DoubleParam, TimeTotal = Time, Ease = Ease ?? new AniEaseLinear(), Obj = Lambda, Value = Value, IsAfter = After, TimeFinished = -Delay };
    }

    // ColorAnimation（Obj, Prop, [Res]）

    /// <summary>
    /// 改变颜色属性的动画。
    /// </summary>
    /// <param name="Obj">动画的对象。</param>
    /// <param name="Prop">动画的依赖属性。</param>
    /// <param name="Value">颜色改变的值。以RGB加减法进行计算。不用担心超额。</param>
    /// <param name="Time">动画长度（毫秒）。</param>
    /// <param name="Delay">动画延迟执行的时间（毫秒）。</param>
    /// <param name="Ease">插值器类型。</param>
    /// <param name="After">是否等到以前的动画完成后才继续本动画。</param>
    public static AniData AaColor(FrameworkElement Obj, DependencyProperty Prop, MyColor Value, int Time = 400, int Delay = 0, AniEase Ease = null, bool After = false) {
        return new AniData { TypeMain = AniType.Color, TimeTotal = Time, Ease = Ease ?? new AniEaseLinear(), Obj = new object[] { Obj, Prop, "" }, Value = Value, IsAfter = After, TimeFinished = -Delay, ValueLast = new MyColor(0, 0, 0, 0) };
    }
    /// <summary>
    /// 改变颜色属性为一个资源的动画。
    /// </summary>
    /// <param name="Obj">动画的对象。</param>
    /// <param name="Prop">动画的依赖属性。</param>
    /// <param name="Res">要将颜色改变为该资源值。</param>
    /// <param name="Time">动画长度（毫秒）。</param>
    /// <param name="Delay">动画延迟执行的时间（毫秒）。</param>
    /// <param name="Ease">插值器类型。</param>
    /// <param name="After">是否等到以前的动画完成后才继续本动画。</param>
    public static AniData AaColor(FrameworkElement Obj, DependencyProperty Prop, string Res, int Time = 400, int Delay = 0, AniEase Ease = null, bool After = false) {
        return new AniData { TypeMain = AniType.Color, TimeTotal = Time, Ease = Ease ?? new AniEaseLinear(), Obj = new object[] { Obj, Prop, Res }, Value = new MyColor(Application.Current.FindResource(Res)) - new MyColor(Obj.GetValue(Prop)), IsAfter = After, TimeFinished = -Delay, ValueLast = new MyColor(0, 0, 0, 0) };
    }

    // Scale

    /// <summary>
    /// 缩放控件的动画。
    /// </summary>
    /// <param name="Obj">动画的对象。</param>
    /// <param name="Value">大小改变的百分比（如-0.6）或值。</param>
    /// <param name="Time">动画长度（毫秒）。</param>
    /// <param name="Delay">动画延迟执行的时间（毫秒）。</param>
    /// <param name="Ease">插值器类型。</param>
    /// <param name="After">是否等到以前的动画完成后才继续本动画。</param>
    /// <param name="Absolute">大小改变是否为绝对值。若为 True 则为绝对像素，若为 False 则为相对百分比。</param>
    public static AniData AaScale(object Obj, double Value, int Time = 400, int Delay = 0, AniEase Ease = null, bool After = false, bool Absolute = false) {
        MyRect ChangeRect;
        if (Absolute) {
            ChangeRect = new MyRect(-0.5 * Value, -0.5 * Value, Value, Value);
        } else {
            ChangeRect = new MyRect(-0.5 * ((dynamic)Obj).ActualWidth * Value, -0.5 * ((dynamic)Obj).ActualHeight * Value, ((dynamic)Obj).ActualWidth * Value, ((dynamic)Obj).ActualHeight * Value);
        }
        return new AniData { TypeMain = AniType.Scale, TimeTotal = Time, Ease = Ease ?? new AniEaseLinear(), Obj = Obj, Value = ChangeRect, IsAfter = After, TimeFinished = -Delay };
    }

    // TextAppear

    /// <summary>
    /// 让一段文字一个个字出现或消失的动画。
    /// </summary>
    /// <param name="Obj">动画的对象。必须是Label或TextBlock。</param>
    /// <param name="Hide">是否为一个个字隐藏。默认为False（一个个字出现）。这些字必须已经存在了。</param>
    /// <param name="TimePerText">是否采用根据文本长度决定时间的方式。</param>
    /// <param name="Time">动画长度（毫秒）。若TimePerText为True，这代表每个字所占据的时间。</param>
    /// <param name="Delay">动画延迟执行的时间（毫秒）。</param>
    /// <param name="After">是否等到以前的动画完成后才继续本动画。</param>
    public static AniData AaTextAppear(object Obj, bool Hide = false, bool TimePerText = true, int Time = 70, int Delay = 0, AniEase Ease = null, bool After = false) {
        // Are we cool yet？
        string Text = (Obj is TextBlock) ? ((TextBlock)Obj).Text : ((dynamic)Obj).Context.ToString();
        return new AniData { TypeMain = AniType.TextAppear, Ease = Ease ?? new AniEaseLinear(), TimeTotal = (TimePerText ? Time * Text.Length : Time), Obj = Obj, Value = new object[] { Text, Hide }, IsAfter = After, TimeFinished = -Delay };
    }

    // Code

    /// <summary>
    /// 执行代码。
    /// </summary>
    /// <param name="Code">一个ThreadStart。这将会在执行时在主线程调用。</param>
    /// <param name="Delay">代码延迟执行的时间（毫秒）。</param>
    /// <param name="After">是否等到以前的动画完成后才执行。</param>
    public static AniData AaCode(ThreadStart Code, int Delay = 0, bool After = false) {
        return new AniData { TypeMain = AniType.Code,
                               TimeTotal = 1, Value = Code, IsAfter = After, TimeFinished = -Delay };
    }

    // ScaleTransform

    /// <summary>
    /// 按照 WPF 方式缩放控件的动画。
    /// </summary>
    /// <param name="Obj">动画的对象。它必须已经拥有了单一的 ScaleTransform 值。</param>
    /// <param name="Value">大小改变的百分比（如-0.6）。</param>
    /// <param name="Time">动画长度（毫秒）。</param>
    /// <param name="Delay">动画延迟执行的时间（毫秒）。</param>
    /// <param name="Ease">插值器类型。</param>
    /// <param name="After">是否等到以前的动画完成后才继续本动画。</param>
    public static AniData AaScaleTransform(object Obj, double Value, int Time = 400, int Delay = 0, AniEase Ease = null, bool After = false) {
        return new AniData { TypeMain = AniType.ScaleTransform, TimeTotal = Time, Ease = Ease ?? new AniEaseLinear(), Obj = Obj, Value = Value, IsAfter = After, TimeFinished = -Delay };
    }

    // RotateTransform

    /// <summary>
    /// 按照 WPF 方式旋转控件的动画。
    /// </summary>
    /// <param name="Obj">动画的对象。它必须已经拥有了单一的 ScaleTransform 值。</param>
    /// <param name="Value">旋转角度（度）。</param>
    /// <param name="Time">动画长度（毫秒）。</param>
    /// <param name="Delay">动画延迟执行的时间（毫秒）。</param>
    /// <param name="Ease">插值器类型。</param>
    /// <param name="After">是否等到以前的动画完成后才继续本动画。</param>
    public static AniData AaRotateTransform(object Obj, double Value, int Time = 400, int Delay = 0, AniEase Ease = null, bool After = false) {
        return new AniData { TypeMain = AniType.RotateTransform, TimeTotal = Time, Ease = Ease ?? new AniEaseLinear(), Obj = Obj, Value = Value, IsAfter = After, TimeFinished = -Delay };
    }

    // TranslateTransform

    /// <summary>
    /// 利用 TranslateTransform 移动 X 轴的动画，这不会造成布局更新。
    /// </summary>
    /// <param name="Obj">动画的对象。</param>
    /// <param name="Value">进行移动的值。</param>
    /// <param name="Time">动画长度（毫秒）。</param>
    /// <param name="Delay">动画延迟执行的时间（毫秒）。</param>
    /// <param name="Ease">插值器类型。</param>
    /// <param name="After">是否等到以前的动画完成后才继续本动画。</param>
    public static AniData AaTranslateX(object Obj, double Value, int Time = 400, int Delay = 0, AniEase Ease = null, bool After = false) {
        return new AniData { TypeMain = AniType.Number, TypeSub = AniTypeSub.TranslateX,
                               TimeTotal = Time, Ease = Ease ?? new AniEaseLinear(), Obj = Obj, Value = Value, IsAfter = After, TimeFinished = -Delay };
    }
    /// <summary>
    /// 利用 TranslateTransform 移动 Y 轴的动画，这不会造成布局更新。
    /// </summary>
    /// <param name="Obj">动画的对象。</param>
    /// <param name="Value">进行移动的值。</param>
    /// <param name="Time">动画长度（毫秒）。</param>
    /// <param name="Delay">动画延迟执行的时间（毫秒）。</param>
    /// <param name="Ease">插值器类型。</param>
    /// <param name="After">是否等到以前的动画完成后才继续本动画。</param>
    public static AniData AaTranslateY(object Obj, double Value, int Time = 400, int Delay = 0, AniEase Ease = null, bool After = false) {
        return new AniData { TypeMain = AniType.Number, TypeSub = AniTypeSub.TranslateY,
                               TimeTotal = Time, Ease = Ease ?? new AniEaseLinear(), Obj = Obj, Value = Value, IsAfter = After, TimeFinished = -Delay };
    }

    // 特殊

    /// <summary>
    /// 将一个StackPanel中的各个项目依次显示。
    /// </summary>
    public static List<AniData> AaStack(StackPanel Stack, int Time = 100, int Delay = 25) {
        var Result = new List<AniData>();
        int AniDelay = 0;
        foreach (UIElement Item in Stack.Children) {
            Item.Opacity = 0;
            Result.Add(AaOpacity(Item, 1, Time, AniDelay));
            AniDelay += Delay;
        }
        return Result;
    }

    #endregion



    #region 接口（开始、中断、检测）

    /// <summary>
    /// 开始一个动画组。
    /// </summary>
    /// <param name="AniGroup">由 Aa 开头的函数初始化的 AniData 对象集合。</param>
    /// <param name="Name">动画组的名称。如果重复会直接停止同名动画组。</param>
    public static void AniStart(IList AniGroup, string Name = "", bool RefreshTime = false) {
        if (RefreshTime) AniLastTick = PclCore.GetTimeMs(); // 避免处理动画时已经造成了极大的延迟，导致动画突然结束
        // 添加到正在执行的动画组
        var AllAnis = new List<AniData>();
        foreach (object Element in AniGroup) {
            if (Element is ICollection) {
                foreach (object SubElement in (ICollection)Element) {
                    AllAnis.Add((AniData)SubElement);
                }
            } else {
                AllAnis.Add((AniData)Element);
            }
        }
        var NewEntry = new AniGroupEntry { Data = AllAnis, StartTick = PclCore.GetTimeMs() };
        if (Name == "") {
            Name = NewEntry.Uuid.ToString(); // VB 隐式将 Integer 转为 String
        } else {
            AniStop(Name);
        }
        AniGroups.Add(Name, NewEntry);
    }
    /// <summary>
    /// 开始一个动画组。
    /// </summary>
    public static void AniStart(AniData AniGroup, string Name = "", bool RefreshTime = false) {
        AniStart(new List<AniData> { AniGroup }, Name, RefreshTime);
    }
    /// <summary>
    /// 直接停止一个动画组。
    /// </summary>
    /// <param name="name">需要停止的动画组的名称。</param>
    public static void AniStop(string Name) {
        AniGroups.Remove(Name);
    }
    /// <summary>
    /// 获取动画是否正在进行中。
    /// </summary>
    public static bool AniIsRun(string Name) {
        return AniGroups.ContainsKey(Name);
    }

    #endregion

    private static int AniCount = 0;
    private static int AniFPSCounter = 0;
    private static long AniFPSTimer = 0;
    /// <summary>
    /// 当前的动画 FPS。
    /// </summary>
    public static int AniFPS = 0;

    /// <summary>
    /// 开始动画执行。
    /// </summary>
    public static void AniStart() {
        // 初始化计时器
        AniLastTick = PclCore.GetTimeMs();
        AniFPSTimer = AniLastTick;
        AniRunning = true; // 标记动画执行开始

        PclCore.RunInNewThread(
        () => {
            try {
                Logger.Info("动画线程开始");
                while (true) {
                    // 两帧之间的间隔时间
                    long DeltaTime = (PclCore.GetTimeMs() - AniLastTick).Clamp(0, 100000);
                    if (DeltaTime < 3) goto Sleeper; // 保留 VB 的 GoTo Sleeper（C# 同方法内 goto 合法）
                    AniLastTick = PclCore.GetTimeMs();
                    // 记录 FPS
                    if (PclUiBridge.ModeDebugGet?.Invoke() == true) {
                        if ((AniLastTick - AniFPSTimer).Clamp(0, 100000) >= 500) {
                            AniFPS = AniFPSCounter;
                            AniFPSCounter = 0;
                            AniFPSTimer = AniLastTick;
                        }
                        AniFPSCounter += 2;
                    }
                    // 执行动画
                    PclCore.RunInUiWait(
                    () => {
                        AniCount = 0;
                        AniTimer(DeltaTime * AniSpeed);
                        if (PclCore.RandomInteger(0, 64 * (PclUiBridge.ModeDebugGet?.Invoke() == true ? 5 : 30)) == 0 && ((AniFPS < 62 && AniFPS > 0) || AniCount > 4 || NetCore.NetManager.FileRemain != 0)) {
                            Logger.Info($"FPS {AniFPS}, 动画 {AniCount}, 下载中 {NetCore.NetManager.FileRemain}（{StringUtils.FormatByteSize(NetCore.NetManager.Speed)}/s）");
                            if (NetCore.NetManager.FileRemain < 5) {
                                foreach (NetFile File in NetCore.NetManager.AllFiles.Values) {
                                    if (File.State < NetState.Finished) Logger.Trace($"- {File.State} {StringUtils.FormatByteSize(File.Speed)}/s {File.LocalPath}");
                                }
                            }
                        }
                    });
Sleeper:
                    // 控制 FPS
                    Thread.Sleep(1);
                }
            } catch (Exception ex) {
                Logger.Error(ex, "动画帧执行失败", LogBehavior.AlertThenCrash);
            }
        }, "Animation", ThreadPriority.AboveNormal);
    }

    /// <summary>
    /// 动画定时器事件。
    /// </summary>
    public static void AniTimer(double DeltaTick) {
        try {
            if (DeltaTick / AniSpeed > 100) Logger.Info($"两个动画帧间隔 {DeltaTick} ms");
            int i = -1;
            // 循环每个动画组
            while (i + 1 < AniGroups.Count) {
                i += 1;
                // 初始化
                AniGroupEntry Entry = AniGroups.Values.ElementAt(i); // 对应 VB 的 AniGroups.Values(i)
                if (Entry.StartTick > AniLastTick) continue; // 跳过本刻之后开始的动画
                bool CanRemoveAfter = true; // 是否应该去除“之后”标记
                int ii = 0;

                // 循环每个动画
                while (ii < Entry.Data.Count) {
                    AniData Anim = Entry.Data[ii];
                    // 执行种类
                    if (!Anim.IsAfter) { // 之前
                        CanRemoveAfter = false; // 取消“之后”标记
                        // 增加执行时间
                        Anim.TimeFinished += DeltaTick;
                        // 执行动画
                        if (Anim.TimeFinished > 0) {
                            Anim = AniRun(Anim);
                            AniCount += 1;
                        }
                        // 如果当前动画已执行完毕
                        if (Anim.TimeFinished >= Anim.TimeTotal) {
                            // 如果是去向颜色资源的动画，设置引用
                            if (Anim.TypeMain == AniType.Color && (string)((dynamic)Anim.Obj)[2] != "") ((dynamic)((dynamic)Anim.Obj)[0]).SetResourceReference(((dynamic)Anim.Obj)[1], ((dynamic)Anim.Obj)[2]);
                            // 删除
                            Entry.Data.RemoveAt(ii);
                            goto NextAni; // 保留 VB 的 GoTo NextAni：跳过 ii += 1，处理移位后的元素
                        }
                        Entry.Data[ii] = Anim;
                    } else { // 之后
                        if (CanRemoveAfter) {
                            // 之后改为之前
                            CanRemoveAfter = false;
                            Anim.IsAfter = false;
                            Entry.Data[ii] = Anim;
                            // 重新循环该动画
                            goto NextAni;
                        } else {
                            // 不能去除该“之后”标记，结束该动画组
                            break;
                        }
                    }
                    ii += 1;
NextAni: ;
                }

                // 如果当前动画组都执行完毕则删除
                if (!Entry.Data.Any()) {
                    // 为了避免新添加的动画影响顺序，不能 RemoveAt(i)
                    // 为了允许动画在执行中添加同名动画组，不能按名字移除
                    for (int Current = 0; Current < AniGroups.Count; Current++) {
                        if (AniGroups.ElementAt(Current).Value.Uuid == Entry.Uuid) {
                            AniGroups.Remove(AniGroups.ElementAt(Current).Key);
                            break;
                        }
                    }
                    i -= 1;
                }
            }
        } catch (Exception ex) {
            Logger.Error(ex, "动画刻执行失败", LogBehavior.Toast);
        }
    }

    /// <summary>
    /// 执行一个动画。
    /// </summary>
    /// <param name="Ani">执行的动画对象。</param>
    private static AniData AniRun(AniData Ani) {
        try {
            switch (Ani.TypeMain) {

                case AniType.Number: {
                    double Delta = MathUtils.Lerp(0, (double)Ani.Value, Ani.Ease.GetDelta(Ani.TimeFinished / Ani.TimeTotal, Ani.TimePercent));
                    if (Delta != 0) {
                        switch (Ani.TypeSub) {
                            case AniTypeSub.X:
                                if (Ani.Obj is Window) {
                                    ((Window)Ani.Obj).Left += Delta;
                                } else {
                                    switch (((dynamic)Ani.Obj).HorizontalAlignment) {
                                        case HorizontalAlignment.Left:
                                        case HorizontalAlignment.Stretch:
                                            ((dynamic)Ani.Obj).Margin = new Thickness(((dynamic)Ani.Obj).Margin.Left + Delta, ((dynamic)Ani.Obj).Margin.Top, ((dynamic)Ani.Obj).Margin.Right, ((dynamic)Ani.Obj).Margin.Bottom);
                                            break;
                                        case HorizontalAlignment.Right:
                                            ((dynamic)Ani.Obj).Margin = new Thickness(((dynamic)Ani.Obj).Margin.Left, ((dynamic)Ani.Obj).Margin.Top, ((dynamic)Ani.Obj).Margin.Right - Delta, ((dynamic)Ani.Obj).Margin.Bottom);
                                            break;
                                    }
                                }
                                break;
                            case AniTypeSub.Y:
                                if (Ani.Obj is Window) {
                                    ((Window)Ani.Obj).Top += Delta;
                                } else {
                                    switch (((dynamic)Ani.Obj).VerticalAlignment) {
                                        case VerticalAlignment.Top:
                                            ((dynamic)Ani.Obj).Margin = new Thickness(((dynamic)Ani.Obj).Margin.Left, ((dynamic)Ani.Obj).Margin.Top + Delta, ((dynamic)Ani.Obj).Margin.Right, ((dynamic)Ani.Obj).Margin.Bottom);
                                            break;
                                        case VerticalAlignment.Bottom:
                                            ((dynamic)Ani.Obj).Margin = new Thickness(((dynamic)Ani.Obj).Margin.Left, ((dynamic)Ani.Obj).Margin.Top, ((dynamic)Ani.Obj).Margin.Right, ((dynamic)Ani.Obj).Margin.Bottom - Delta);
                                            break;
                                    }
                                }
                                break;
                            case AniTypeSub.Opacity:
                                if (Ani.Obj is UIElement OpacityElement) {
                                    OpacityElement.Opacity = (OpacityElement.Opacity + Delta).Clamp(0, 1);
                                } else if (Ani.Obj is DropShadowEffect OpacityShadow) {
                                    OpacityShadow.Opacity = (OpacityShadow.Opacity + Delta).Clamp(0, 1);
                                }
                                break;
                            case AniTypeSub.Width: {
                                FrameworkElement Obj = (FrameworkElement)Ani.Obj;
                                Obj.Width = Math.Max((double.IsNaN(Obj.Width) ? Obj.ActualWidth : Obj.Width) + Delta, 0);
                                break;
                            }
                            case AniTypeSub.Height: {
                                FrameworkElement Obj = (FrameworkElement)Ani.Obj;
                                Obj.Height = Math.Max((double.IsNaN(Obj.Height) ? Obj.ActualHeight : Obj.Height) + Delta, 0);
                                break;
                            }
                            case AniTypeSub.Value:
                                ((dynamic)Ani.Obj).Value += Delta;
                                break;
                            case AniTypeSub.Radius:
                                ((dynamic)Ani.Obj).Radius += Delta;
                                break;
                            case AniTypeSub.StrokeThickness:
                                ((dynamic)Ani.Obj).StrokeThickness = Math.Max(((dynamic)Ani.Obj).StrokeThickness + Delta, 0);
                                break;
                            case AniTypeSub.BorderThickness:
                                ((dynamic)Ani.Obj).BorderThickness = new Thickness(((Thickness)((dynamic)Ani.Obj).BorderThickness).Bottom + Delta);
                                break;
                            case AniTypeSub.TranslateX:
                                if (((dynamic)Ani.Obj).RenderTransform is null || !(((dynamic)Ani.Obj).RenderTransform is TranslateTransform)) ((dynamic)Ani.Obj).RenderTransform = new TranslateTransform(0, 0);
                                ((TranslateTransform)((dynamic)Ani.Obj).RenderTransform).X += Delta;
                                break;
                            case AniTypeSub.TranslateY:
                                if (((dynamic)Ani.Obj).RenderTransform is null || !(((dynamic)Ani.Obj).RenderTransform is TranslateTransform)) ((dynamic)Ani.Obj).RenderTransform = new TranslateTransform(0, 0);
                                ((TranslateTransform)((dynamic)Ani.Obj).RenderTransform).Y += Delta;
                                break;
                            case AniTypeSub.Double:
                                ((dynamic)((dynamic)Ani.Obj)[0]).SetValue(((dynamic)Ani.Obj)[1], ((dynamic)((dynamic)Ani.Obj)[0]).GetValue(((dynamic)Ani.Obj)[1]) + Delta);
                                break;
                            case AniTypeSub.DoubleParam:
                                ((ParameterizedThreadStart)Ani.Obj)(Delta);
                                break;
                            case AniTypeSub.GridLengthWidth:
                                ((dynamic)Ani.Obj).Width = new GridLength(Math.Max(((dynamic)Ani.Obj).Width.Value + Delta, 0), GridUnitType.Star);
                                break;
                        }
                    }
                    break;
                }

                case AniType.Color: {
                    // 利用 Last 记录了余下的小数值
                    MyColor Delta = MyColor.Lerp(new MyColor(0, 0, 0, 0), (MyColor)Ani.Value, Ani.Ease.GetDelta(Ani.TimeFinished / Ani.TimeTotal, Ani.TimePercent)) + (MyColor)Ani.ValueLast;
                    FrameworkElement Obj = (FrameworkElement)((dynamic)Ani.Obj)[0];
                    DependencyProperty Prop = (DependencyProperty)((dynamic)Ani.Obj)[1];
                    MyColor NewColor = new MyColor(Obj.GetValue(Prop)) + Delta;
                    Obj.SetValue(Prop, Prop.PropertyType.Name == "Color" ? (Color)NewColor : (SolidColorBrush)NewColor);
                    Ani.ValueLast = NewColor - new MyColor(Obj.GetValue(Prop));
                    break;
                }

                case AniType.Scale: {
                    FrameworkElement Obj = (FrameworkElement)Ani.Obj;
                    double Delta = Ani.Ease.GetDelta(Ani.TimeFinished / Ani.TimeTotal, Ani.TimePercent);
                    MyRect Rect = (MyRect)Ani.Value;
                    Obj.Margin = new Thickness(Obj.Margin.Left + MathUtils.Lerp(0, Rect.Left, Delta), Obj.Margin.Top + MathUtils.Lerp(0, Rect.Top, Delta), Obj.Margin.Right + MathUtils.Lerp(0, Rect.Left, Delta), Obj.Margin.Bottom + MathUtils.Lerp(0, Rect.Top, Delta));
                    Obj.Width = Math.Max(Obj.Width + MathUtils.Lerp(0, Rect.Width, Delta), 0);
                    Obj.Height = Math.Max(Obj.Height + MathUtils.Lerp(0, Rect.Height, Delta), 0);
                    break;
                }

                case AniType.TextAppear: {
                    object[] TextValue = (object[])Ani.Value;
                    string Text = (string)TextValue[0];
                    int TextCount = Convert.ToInt32(((bool)TextValue[1] ? Text.Length : 0) +
                                                   Math.Round(Text.Length * ((bool)TextValue[1] ? -1 : 1) * Ani.Ease.GetDelta(Ani.TimeFinished / Ani.TimeTotal, 0))); // Convert.ToInt32 与 VB 隐式收窄一致（银行家舍入）
                    string NewText = Text.Substring(0, TextCount);
                    // 添加乱码
                    if (TextCount < Text.Length) {
                        string NextText = Text.Substring(TextCount, 1);
                        if (Convert.ToInt32(NextText[0]) >= Convert.ToInt32(Convert.ToChar(128))) {
                            NewText += Encoding.GetEncoding("GB18030").GetString(new byte[] { (byte)PclCore.RandomInteger(16 + 160, 87 + 160), (byte)PclCore.RandomInteger(1 + 160, 89 + 160) });
                        } else {
                            NewText += PclCore.RandomInteger(0, 85).ConvertRadix(86);
                        }
                    }
                    // 设置文本
                    if (Ani.Obj is TextBlock TextBlockObj) {
                        TextBlockObj.Text = NewText;
                    } else {
                        ((dynamic)Ani.Obj).Context = NewText;
                    }
                    break;
                }

                case AniType.Code:
                    Ani.TimeFinished = Ani.TimeTotal;
                    ((ThreadStart)Ani.Value)();
                    break;

                case AniType.ScaleTransform: {
                    FrameworkElement Obj = (FrameworkElement)Ani.Obj;
                    if (!(Obj.RenderTransform is ScaleTransform)) {
                        if (Obj.RenderTransformOrigin == new Point(0, 0)) Obj.RenderTransformOrigin = new Point(0.5, 0.5);
                        Obj.RenderTransform = new ScaleTransform(1, 1);
                    }
                    double Delta = MathUtils.Lerp(0, (double)Ani.Value, Ani.Ease.GetDelta(Ani.TimeFinished / Ani.TimeTotal, Ani.TimePercent));
                    ((ScaleTransform)Obj.RenderTransform).ScaleX = Math.Max(((ScaleTransform)Obj.RenderTransform).ScaleX + Delta, 0);
                    ((ScaleTransform)Obj.RenderTransform).ScaleY = Math.Max(((ScaleTransform)Obj.RenderTransform).ScaleY + Delta, 0);
                    break;
                }

                case AniType.RotateTransform: {
                    FrameworkElement Obj = (FrameworkElement)Ani.Obj;
                    if (!(Obj.RenderTransform is RotateTransform)) {
                        if (Obj.RenderTransformOrigin == new Point(0, 0)) Obj.RenderTransformOrigin = new Point(0.5, 0.5);
                        Obj.RenderTransform = new RotateTransform(0);
                    }
                    double Delta = MathUtils.Lerp(0, (double)Ani.Value, Ani.Ease.GetDelta(Ani.TimeFinished / Ani.TimeTotal, Ani.TimePercent));
                    ((RotateTransform)Obj.RenderTransform).Angle = ((RotateTransform)Obj.RenderTransform).Angle + Delta;
                    break;
                }

            }
            Ani.TimePercent = Ani.TimeFinished / Ani.TimeTotal; // 修改执行百分比
        } catch (Exception ex) {
            Logger.Error(ex, $"执行动画失败：{Ani}", LogBehavior.Toast);
        }
        return Ani;
    }

}

#region 类与枚举

/// <summary>
/// 动画组条目。
/// </summary>
public class AniGroupEntry {
    public List<AniData> Data;
    public long StartTick;
    public int Uuid = PclCore.GetUuid();
}
/// <summary>
/// 单个动画对象。
/// </summary>
public struct AniData {

    /// <summary>
    /// 动画种类。
    /// </summary>
    public AniType TypeMain;
    /// <summary>
    /// 动画副种类。
    /// </summary>
    public AniTypeSub TypeSub;

    /// <summary>
    /// 动画总长度。
    /// </summary>
    public int TimeTotal;
    /// <summary>
    /// 已经执行的动画长度。如果为负数则为延迟。
    /// </summary>
    public double TimeFinished;
    /// <summary>
    /// 已经完成的百分比。
    /// </summary>
    public double TimePercent;

    /// <summary>
    /// 是否为“以后”。
    /// </summary>
    public bool IsAfter;

    /// <summary>
    /// 插值器类型。
    /// </summary>
    public AniEase Ease;
    /// <summary>
    /// 动画对象。
    /// </summary>
    public object Obj;
    /// <summary>
    /// 动画值。
    /// </summary>
    public object Value;
    /// <summary>
    /// 上次执行时的动画值。
    /// </summary>
    public object ValueLast;

    public override string ToString() {
        return TypeMain.ToString() + " | " + TimeFinished + "/" + TimeTotal + "(" + Math.Round(TimePercent * 100) + "%)" + (Obj is null ? "" : " | " + Obj.ToString() + "(" + Obj.GetType().Name + ")");
    }

}

/// <summary>
/// 动画基础种类。
/// </summary>
public enum AniType {
    /// <summary>
    /// 单个Double的动画，包括位置、长宽、透明度等。这需要附属类型。
    /// </summary>
    Number,
    /// <summary>
    /// 颜色属性的动画。这需要附属类型。
    /// </summary>
    Color,
    /// <summary>
    /// 缩放控件大小。比起4个DoubleAnimation来说效率更高。
    /// </summary>
    Scale,
    /// <summary>
    /// 文字一个个出现。
    /// </summary>
    TextAppear,
    /// <summary>
    /// 执行代码。
    /// </summary>
    Code,
    /// <summary>
    /// 以 WPF 方式缩放控件。
    /// </summary>
    ScaleTransform,
    /// <summary>
    /// 以 WPF 方式旋转控件。
    /// </summary>
    RotateTransform,
}
/// <summary>
/// 动画扩展种类。
/// </summary>
public enum AniTypeSub {
    X,
    Y,
    Width,
    Height,
    Opacity,
    Value,
    Radius,
    BorderThickness,
    StrokeThickness,
    TranslateX,
    TranslateY,
    Double,
    DoubleParam,
    GridLengthWidth,
}

#endregion

#region 缓动函数

// 基类
public enum AniEasePower {
    Weak = 2,
    Middle = 3,
    Strong = 4,
    ExtraStrong = 5,
}
/// <summary>
/// 缓动函数基类。
/// </summary>
public abstract class AniEase {
    /// <summary>
    /// 获取函数值。
    /// </summary>
    /// <param name="t">时间百分比。</param>
    public abstract double GetValue(double t);
    /// <summary>
    /// 获取增量值。
    /// </summary>
    /// <param name="t1">较大的 X。</param>
    /// <param name="t0">较小的 X。</param>
    public virtual double GetDelta(double t1, double t0) {
        return GetValue(t1) - GetValue(t0);
    }
}
/// <summary>
/// 渐入渐出组合。
/// </summary>
public class AniEaseInout : AniEase {
    private readonly AniEase EaseIn, EaseOut;
    private readonly double EaseInPercent;
    public AniEaseInout(AniEase EaseIn, AniEase EaseOut, double EaseInPercent = 0.5) {
        this.EaseIn = EaseIn;
        this.EaseOut = EaseOut;
        this.EaseInPercent = EaseInPercent;
    }
    public override double GetValue(double t) {
        if (t < EaseInPercent) {
            return EaseInPercent * EaseIn.GetValue(t / EaseInPercent);
        } else {
            return (1 - EaseInPercent) * EaseOut.GetValue((t - EaseInPercent) / (1 - EaseInPercent)) + EaseInPercent;
        }
    }
}

// Linear / 线性
/// <summary>
/// 线性，无缓动。
/// </summary>
public class AniEaseLinear : AniEase {
    public override double GetValue(double t) {
        return t.Clamp(0, 1);
    }
    public override double GetDelta(double t1, double t0) {
        return t1.Clamp(0, 1) - t0.Clamp(0, 1);
    }
}

// Fluent / 平滑
/// <summary>
/// 平滑开始。
/// </summary>
public class AniEaseInFluent : AniEase {
    private readonly AniEasePower p;
    public AniEaseInFluent(AniEasePower Power = AniEasePower.Middle) {
        p = Power;
    }
    public override double GetValue(double t) {
        return Math.Pow(t.Clamp(0, 1), (int)p); // VB 的 ^ 幂运算对应 Math.Pow
    }
}
/// <summary>
/// 平滑结束。
/// </summary>
public class AniEaseOutFluent : AniEase {
    private readonly AniEasePower p;
    public AniEaseOutFluent(AniEasePower Power = AniEasePower.Middle) {
        p = Power;
    }
    public override double GetValue(double t) {
        return 1 - Math.Pow((1 - t).Clamp(0, 1), (int)p);
    }
}
/// <summary>
/// 平滑开始与结束。
/// </summary>
public class AniEaseInoutFluent : AniEase {
    private AniEaseInout Ease;
    public AniEaseInoutFluent(AniEasePower Power = AniEasePower.Middle, double Middle = 0.5) {
        Ease = new AniEaseInout(new AniEaseInFluent(Power), new AniEaseOutFluent(Power), Middle);
    }
    public override double GetValue(double t) {
        return Ease.GetValue(t);
    }
}
/// <summary>
/// 以特定速度开始的平滑结束。
/// </summary>
public class AniEaseOutFluentWithInitial : AniEase {
    private readonly double alpha; // (初速度 / 平均速度) – 1
    /// <param name="InitialPixelPerSecond">初速度，px/s</param>
    /// <param name="TotalSecond">总时长，s</param>
    /// <param name="TotalDistance">总路程，px</param>
    public AniEaseOutFluentWithInitial(double InitialPixelPerSecond, double TotalSecond, double TotalDistance) {
        double v0_norm = InitialPixelPerSecond * TotalSecond / TotalDistance; // 归一化初速度
        alpha = v0_norm - 1.0;
        if (alpha < 0) alpha = 0; // 初速度小于平均速度时，退化为线性
    }
    public override double GetValue(double percent) {
        double p = percent.Clamp(0, 1);
        if (alpha == 0) return p; // 退化到线性
        return (alpha + 1) * p / (1 + alpha * p);
    }
}

// Back / 回弹
/// <summary>
/// 回弹开始。有效时间为 1/3。
/// </summary>
public class AniEaseInBack : AniEase {
    private readonly double p;
    public AniEaseInBack(AniEasePower Power = AniEasePower.Middle) {
        p = 3 - (int)Power * 0.5;
    }
    public override double GetValue(double t) {
        t = t.Clamp(0, 1);
        return Math.Pow(t, p) * Math.Cos(1.5 * Math.PI * (1 - t));
    }
}
/// <summary>
/// 回弹结束。有效时间为 1/3。
/// </summary>
public class AniEaseOutBack : AniEase {
    private readonly double p;
    public AniEaseOutBack(AniEasePower Power = AniEasePower.Middle) {
        p = 3 - (int)Power * 0.5;
    }
    public override double GetValue(double t) {
        t = t.Clamp(0, 1);
        return 1 - Math.Pow(1 - t, p) * Math.Cos(1.5 * Math.PI * t);
    }
}

// Car / 平滑-回弹
/// <summary>
/// 回弹开始，短平滑结束。
/// </summary>
public class AniEaseInCar : AniEase {
    private AniEaseInout Ease;
    public AniEaseInCar(double Middle = 0.7, AniEasePower Power = AniEasePower.Middle) {
        Ease = new AniEaseInout(new AniEaseInBack(Power), new AniEaseOutFluent(Power), Middle);
    }
    public override double GetValue(double t) {
        return Ease.GetValue(t);
    }
}
/// <summary>
/// 短平滑开始，回弹结束。
/// </summary>
public class AniEaseOutCar : AniEase {
    private AniEaseInout Ease;
    public AniEaseOutCar(double Middle = 0.3, AniEasePower Power = AniEasePower.Middle) {
        Ease = new AniEaseInout(new AniEaseInFluent(Power), new AniEaseOutBack(Power), Middle);
    }
    public override double GetValue(double t) {
        return Ease.GetValue(t);
    }
}

// Elastic / 弹簧
/// <summary>
/// 弹簧开始。约在 60% 到达最小值。
/// </summary>
public class AniEaseInElastic : AniEase {
    private readonly int p; // 6~9
    public AniEaseInElastic(AniEasePower Power = AniEasePower.Middle) {
        p = (int)Power + 4;
    }
    public override double GetValue(double t) {
        t = t.Clamp(0, 1);
        return Math.Pow(t, (p - 1) * 0.25) * Math.Cos((p - 3.5) * Math.PI * Math.Pow(1 - t, 1.5));
    }
}
/// <summary>
/// 弹簧结束。约在 40% 到达最大值。
/// </summary>
public class AniEaseOutElastic : AniEase {
    private readonly int p;
    public AniEaseOutElastic(AniEasePower Power = AniEasePower.Middle) {
        p = (int)Power + 4;
    }
    public override double GetValue(double t) {
        t = 1 - t.Clamp(0, 1);
        return 1 - Math.Pow(t, (p - 1) * 0.25) * Math.Cos((p - 3.5) * Math.PI * Math.Pow(1 - t, 1.5));
    }
}

#endregion
