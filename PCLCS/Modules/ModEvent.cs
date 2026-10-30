// 由 VB Modules\ModEvent.vb 迁移而来。
// UI 依赖（MyMsgBox、Hint、PageOtherHelp、FrmLaunchRight、FrmMain.PageRight、ClipboardSet、OpenWebsite、
// PageOtherLeft、PageOtherTest、PageLinkMain、ModpackInstall、McLaunchStart、FrmMain.PageChange、Settings.SetSafe）
// 通过 PclUiBridge / PclCore 桥接。
// CustomEventService / CustomEventCollection / CustomEvent 类型在 VB 侧 PCL 命名空间保留继承转发壳，
// 以兼容 XAML 中的 local:CustomEventService / local:CustomEventCollection / local:CustomEvent 引用；
// RaiseCustomEvent 扩展方法也在 VB ModMain 中保留转发壳。
#nullable disable

using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Markup;

namespace PCLCS.Modules;

/// <summary>
/// 与 VB ModMain 的 RaiseCustomEvent 扩展方法对应的自定义事件逻辑。
/// </summary>
public static class ModEvent {

    /// <summary>
    /// 触发该控件上的自定义事件。
    /// 事件会在新线程中执行。
    /// </summary>
    public static void RaiseCustomEvent(DependencyObject Control) {
        // 收集事件列表
        List<CustomEvent> Events = CustomEventService.GetEvents(Control).ToList();
        CustomEvent.EventType EventType = CustomEventService.GetEventType(Control);
        if (EventType != CustomEvent.EventType.None) Events.Add(new CustomEvent(EventType, CustomEventService.GetEventData(Control)));
        // 执行事件
        if (!Events.Any()) return;
        PclCore.RunInNewThread(
        () => {
            foreach (CustomEvent e in Events) {
                e.Raise();
            }
        }, "执行自定义事件 " + PclCore.GetUuid());
    }

}

/// <summary>
/// 用于在 XAML 中初始化列表对象。
/// 附加属性无法在 XAML 中为每个对象初始化独立的列表对象，因此需要一个包装类，然后在 XAML 中显式初始化。
/// </summary>
[ContentProperty("Events")]
public class CustomEventCollection : IEnumerable<CustomEvent> {
    private List<CustomEvent> _Events = new List<CustomEvent>();
    public List<CustomEvent> Events {
        get {
            return _Events;
        }
    }
    public IEnumerator<CustomEvent> GetEnumerator() {
        return ((IEnumerable<CustomEvent>)Events).GetEnumerator();
    }
    IEnumerator IEnumerable.GetEnumerator() {
        return ((IEnumerable)Events).GetEnumerator();
    }
}

/// <summary>
/// 提供自定义事件的附加属性。
/// </summary>
public class CustomEventService {

    // Events
    public static readonly DependencyProperty EventsProperty =
            DependencyProperty.RegisterAttached("Events", typeof(CustomEventCollection), typeof(CustomEventService), new PropertyMetadata(null));
    [AttachedPropertyBrowsableForType(typeof(DependencyObject))]
    public static void SetEvents(DependencyObject d, CustomEventCollection value) {
        d.SetValue(EventsProperty, value);
    }
    [AttachedPropertyBrowsableForType(typeof(DependencyObject))]
    public static CustomEventCollection GetEvents(DependencyObject d) {
        if (d.GetValue(EventsProperty) is null) d.SetValue(EventsProperty, new CustomEventCollection());
        return (CustomEventCollection)d.GetValue(EventsProperty);
    }

    // EventType
    public static readonly DependencyProperty EventTypeProperty =
            DependencyProperty.RegisterAttached("EventType", typeof(CustomEvent.EventType), typeof(CustomEventService), new PropertyMetadata(null));
    [AttachedPropertyBrowsableForType(typeof(DependencyObject))]
    public static void SetEventType(DependencyObject d, CustomEvent.EventType value) {
        d.SetValue(EventTypeProperty, value);
    }
    [AttachedPropertyBrowsableForType(typeof(DependencyObject))]
    public static CustomEvent.EventType GetEventType(DependencyObject d) {
        // VB 中 Nothing 会被转换为枚举默认值，这里用 ?? 保持相同行为
        return (CustomEvent.EventType)(d.GetValue(EventTypeProperty) ?? CustomEvent.EventType.None);
    }

    // EventData
    public static readonly DependencyProperty EventDataProperty =
            DependencyProperty.RegisterAttached("EventData", typeof(string), typeof(CustomEventService), new PropertyMetadata(null));
    [AttachedPropertyBrowsableForType(typeof(DependencyObject))]
    public static void SetEventData(DependencyObject d, string value) {
        d.SetValue(EventDataProperty, value);
    }
    [AttachedPropertyBrowsableForType(typeof(DependencyObject))]
    public static string GetEventData(DependencyObject d) {
        return (string)d.GetValue(EventDataProperty);
    }

}

/// <summary>
/// 自定义事件。
/// </summary>
public class CustomEvent : DependencyObject {

    #region 属性与触发

    public EventType Type {
        get {
            EventType Value = EventType.None;
            PclCore.RunInUiWait(() => Value = (EventType)GetValue(TypeProperty));
            return Value;
        }
        set {
            SetValue(TypeProperty, value);
        }
    }
    public static readonly DependencyProperty TypeProperty =
        DependencyProperty.Register("Type", typeof(EventType), typeof(CustomEvent), new PropertyMetadata(EventType.None));

    public string Data {
        get {
            string Value = null;
            PclCore.RunInUiWait(() => Value = (string)GetValue(DataProperty));
            return Value;
        }
        set {
            SetValue(DataProperty, value);
        }
    }
    public static readonly DependencyProperty DataProperty =
        DependencyProperty.Register("Data", typeof(string), typeof(CustomEvent), new PropertyMetadata(null));

    public CustomEvent() {
    }
    public CustomEvent(EventType Type, string Data) {
        this.Type = Type;
        this.Data = Data;
    }

    /// <summary>
    /// 在当前线程中触发该自定义事件。
    /// </summary>
    public void Raise() {
        Raise(Type, Data);
    }

    #endregion

    public enum EventType {
        None = 0,
        打开网页,
        打开文件,
        打开帮助,
        执行命令,
        启动游戏,
        复制文本,
        刷新主页,
        刷新页面,
        刷新帮助,
        今日人品,
        内存优化,
        清理垃圾,
        弹出窗口,
        弹出提示,
        切换页面,
        导入整合包,
        安装整合包,
        下载文件,
        修改设置,
        写入设置,
        修改变量,
        写入变量,
        加入房间,
        检查更新,
    }

    /// <summary>
    /// 在当前线程中触发一个自定义事件。
    /// </summary>
    public static void Raise(EventType Type, string Arg) {
        if (Type == EventType.None) return;
        Logger.Info($"执行自定义事件：{Type}, {Arg}");
        if (Arg is null) Arg = "";
        string[] Args = Arg.Split('|');
        string Arg0 = Arg.BeforeFirst("|");
        string Arg1 = Arg.AfterFirst("|");
        try {
            switch (Type) {
                case EventType.打开网页:
                    Arg = Arg.Replace("\\", "/");
                    if (!Arg.Contains("://") || Arg.StartsWithF("file", true)) { // 为了支持更多协议（#2200）
                        PclUiBridge.ShowModpackMsgBox("EventData 必须为一个网址。" + Environment.NewLine + "如果想要启动程序，请将 EventType 改为 打开文件。", "事件执行失败", null, null, null, false, false);
                        return;
                    }
                    PclUiBridge.ShowHint("正在开启，请稍候：" + Arg, HintType.Blue);
                    PclCore.RunInThread(() => PclUiBridge.OpenWebsite(Arg));
                    break;

                case EventType.打开文件:
                case EventType.打开帮助:
                case EventType.执行命令:
                    PclCore.RunInThread(
                    () => {
                        try {
                            // 确认实际路径
                            string[] ActualPaths = GetAbsoluteUrls(Arg0, Type);
                            string Location = ActualPaths[0], WorkingDir = ActualPaths[1];
                            Logger.Info($"打开类自定义事件实际路径：{Location}，工作目录：{WorkingDir}");
                            // 执行
                            if (Type == EventType.打开帮助) {
                                PclUiBridge.EnterHelpPage(Location);
                            } else {
                                if (!EventSafetyConfirm("即将执行：" + Location + (Args.Length >= 2 ? " " + Arg1 : ""))) return;
                                ProcessStartInfo Info = new ProcessStartInfo {
                                    Arguments = Args.Length >= 2 ? Arg1 : "",
                                    FileName = Location,
                                    WorkingDirectory = WorkingDir
                                };
                                PclCore.StartProcess(Info);
                            }
                        } catch (Exception ex) {
                            Logger.Error(ex, "执行打开类自定义事件失败", LogBehavior.Alert);
                        }
                    });
                    break;

                case EventType.启动游戏:
                    if (Arg0 == "\\current") {
                        McInstance CurrentInstance = PclUiBridge.McInstanceSelectedGet();
                        if (CurrentInstance is null) {
                            PclUiBridge.ShowHint("请先选择一个 Minecraft 版本！", HintType.Red);
                            return;
                        } else {
                            Arg0 = CurrentInstance.Name;
                        }
                    }
                    PclCore.RunInUi(
                    () => {
                        if (PclUiBridge.McLaunchStart(new McLaunchOptions {
                            ServerIp = Args.Length >= 2 ? Args[1] : null,
                            Instance = new McInstance(Arg0)
                        })) {
                            PclUiBridge.ShowHint($"正在启动 {Arg0}……", HintType.Blue);
                        }
                    });
                    break;

                case EventType.复制文本:
                    PclUiBridge.ClipboardSet(Arg);
                    break;

                case EventType.刷新主页:
                    PclCore.RunInUi(
                    () => {
                        PclUiBridge.ForceRefreshLaunchRight();
                        if (string.IsNullOrEmpty(Arg)) PclUiBridge.ShowHint("已刷新！", HintType.Green);
                    });
                    break;

                case EventType.刷新页面:
                    if (PclUiBridge.RefreshPageRight()) {
                        if (string.IsNullOrEmpty(Arg)) PclUiBridge.ShowHint("已刷新！", HintType.Green);
                    } else {
                        PclUiBridge.ShowHint("当前页面不支持刷新操作！", HintType.Red);
                    }
                    break;

                case EventType.刷新帮助:
                    PclCore.RunInUiWait(() => PclUiBridge.RefreshHelp());
                    if (string.IsNullOrEmpty(Arg)) PclUiBridge.ShowHint("已刷新！", HintType.Green);
                    break;

                case EventType.今日人品:
                    PclUiBridge.Jrrp();
                    break;

                case EventType.内存优化:
                    PclCore.RunInThread(() => PclUiBridge.MemoryOptimize());
                    break;

                case EventType.清理垃圾:
                    PclCore.RunInThread(() => PclUiBridge.RubbishClear());
                    break;

                case EventType.弹出窗口:
                    if (Args.Length == 1) throw new Exception($"EventType {Type} 需要至少 2 个以 | 分割的参数，例如 弹窗标题|弹窗内容");
                    PclUiBridge.ShowModpackMsgBox(Arg1.BeforeLast("|").Replace("\n", Environment.NewLine), Arg0.Replace("\n", Environment.NewLine), Args.Length > 2 ? Arg1.AfterLast("|") : "确定", null, null, false, false);
                    break;

                case EventType.弹出提示:
                    PclUiBridge.ShowHint(Arg0.Replace("\n", Environment.NewLine), Args.Length == 1 ? HintType.Blue : EnumUtils.FromString<HintType>(Arg1));
                    break;

                case EventType.切换页面:
                    PclCore.RunInUi(() => PclUiBridge.PageChangeByEvent(Arg0, Args.Length == 1 ? null : Arg1));
                    break;

                case EventType.导入整合包:
                case EventType.安装整合包:
                    PclCore.RunInUi(() => PclUiBridge.ModpackInstall());
                    break;

                case EventType.下载文件:
                    Arg0 = Arg0.Replace("\\", "/");
                    if (!(Arg0.StartsWithF("http://", true) || Arg0.StartsWithF("https://", true))) {
                        PclUiBridge.ShowModpackMsgBox("EventData 必须为以 http:// 或 https:// 开头的网址。" + Environment.NewLine + "PCL 不支持其他乱七八糟的下载协议。", "事件执行失败", null, null, null, false, false);
                        return;
                    }
                    if (!EventSafetyConfirm("即将从该网址下载文件：" + Environment.NewLine + Arg0)) return;
                    PclCore.RunInUi(
                    () => {
                        try {
                            switch (Args.Length) {
                                case 1:
                                    PclUiBridge.StartCustomDownload(Arg0, PathUtils.GetLastPart(Arg0), null);
                                    break;
                                case 2:
                                    PclUiBridge.StartCustomDownload(Arg0, Arg1, null);
                                    break;
                                default:
                                    PclUiBridge.StartCustomDownload(Arg0, Args[1], Args[2]);
                                    break;
                            }
                        } catch {
                            PclUiBridge.StartCustomDownload(Arg0, "未知", null);
                        }
                    });
                    break;

                case EventType.修改设置:
                case EventType.写入设置:
                    if (Args.Length == 1) throw new Exception($"EventType {Type} 需要至少 2 个以 | 分割的参数，例如 UiLauncherTransparent|400");
                    PclUiBridge.SettingSetSafe(Arg0, Args[1], PclUiBridge.McInstanceSelectedGet());
                    if (Args.Length == 2) PclUiBridge.ShowHint($"已写入设置：{Arg0} → {Args[1]}", HintType.Green);
                    break;

                case EventType.修改变量:
                case EventType.写入变量:
                    if (Args.Length == 1) throw new Exception($"EventType {Type} 需要至少 2 个以 | 分割的参数，例如 VariableName|SomeValue");
                    try {
                        RegistryUtils.Write($"HKEY_CURRENT_USER\\Software\\{ModSecret.RegFolder}", $"CustomEvent{Arg0}", Args[1]);
                        if (Args.Length == 2) PclUiBridge.ShowHint($"已写入变量：{Arg0} → {Args[1]}", HintType.Green);
                    } catch (Exception ex) {
                        Logger.Warn(ex, $"写入变量失败：{Arg0} → {Args[1]}", LogBehavior.Toast);
                    }
                    break;

                case EventType.加入房间:
                    PclCore.RunInUi(
                    () => {
                        if (string.IsNullOrWhiteSpace(Arg)) {
                            PclUiBridge.PageLinkJoin(null);
                        } else {
                            if (!EventSafetyConfirm("即将加入第三方联机房间。")) return;
                            PclUiBridge.PageLinkJoin(Arg);
                        }
                    });
                    break;

                case EventType.检查更新:
                    ModSecret.UpdateCheckByButton();
                    break;

                default:
                    PclUiBridge.ShowModpackMsgBox("未知的事件类型：" + Type + Environment.NewLine + "请检查事件类型填写是否正确，或者 PCL 是否为最新版本。", "事件执行失败", null, null, null, false, false);
                    break;
            }
        } catch (Exception ex) {
            Logger.Error(ex, $"事件执行失败（{Type}, {Arg}）", LogBehavior.Alert);
        }
    }

    /// <summary>
    /// 返回自定义事件的绝对 Url。实际返回 {绝对 Url, WorkingDir}。
    /// 失败会抛出异常。
    /// </summary>
    public static string[] GetAbsoluteUrls(string RelativeUrl, EventType Type) {

        // 网页确认
        if (RelativeUrl.StartsWithF("http", true)) {
            if (PclCore.RunInUi()) {
                throw new Exception("能打开联网帮助页面的 MyListItem 必须手动设置 Title、Info 属性！");
            }
            // 获取文件名
            string RawFileName;
            try {
                RawFileName = PathUtils.GetLastPart(RelativeUrl);
                if (PathUtils.GetExtension(RawFileName) != "json") throw new Exception("未指向 .json 后缀的文件");
            } catch (Exception ex) {
                throw new Exception("联网帮助页面须指向一个帮助 JSON 文件，并在同路径下包含相应 XAML 文件！" + Environment.NewLine +
                                    "例如：" + Environment.NewLine +
                                    " - https://www.baidu.com/test.json（填写这个路径）" + Environment.NewLine +
                                    " - https://www.baidu.com/test.xaml（同时也需要包含这个文件）", ex);
            }
            // 下载文件
            string LocalTemp = PclUiBridge.RequestTaskTempFolder() + RawFileName;
            Logger.Info($"转换网络资源：{RelativeUrl} -> {LocalTemp}");
            try {
                NetCore.NetDownloadByClient(RelativeUrl, LocalTemp);
                NetCore.NetDownloadByClient(RelativeUrl.Replace(".json", ".xaml"), LocalTemp.Replace(".json", ".xaml"));
            } catch (Exception ex) {
                throw new Exception("下载指定的文件失败！" + Environment.NewLine +
                                    "注意，联网帮助页面须指向一个帮助 JSON 文件，并在同路径下包含相应 XAML 文件！" + Environment.NewLine +
                                    "例如：" + Environment.NewLine +
                                    " - https://www.baidu.com/test.json（填写这个路径）" + Environment.NewLine +
                                    " - https://www.baidu.com/test.xaml（同时也需要包含这个文件）", ex);
            }
            RelativeUrl = LocalTemp;
        }
        RelativeUrl = RelativeUrl.Replace("/", "\\").Lower().TrimStart('\\');

        // 确认实际路径
        string Location, WorkingDir = Paths.Base + "PCL";
        PclUiBridge.HelpTryExtract();
        if (RelativeUrl.Contains(":\\")) {
            // 绝对路径
            Location = RelativeUrl;
            Logger.Info($"自定义事件中由绝对路径{Type}：{Location}");
        } else if (FileUtils.Exists(Paths.Base + "PCL\\" + RelativeUrl)) {
            // 相对 PCL 文件夹的路径
            Location = Paths.Base + "PCL\\" + RelativeUrl;
            Logger.Info($"自定义事件中由相对 PCL 文件夹的路径{Type}：{Location}");
        } else if (FileUtils.Exists(Paths.Base + "PCL\\Help\\" + RelativeUrl)) {
            // 相对 PCL 本地帮助文件夹的路径
            Location = Paths.Base + "PCL\\Help\\" + RelativeUrl;
            WorkingDir = Paths.Base + "PCL\\Help\\";
            Logger.Info($"自定义事件中由相对 PCL 本地帮助文件夹的路径{Type}：{Location}");
        } else if (Type == EventType.打开帮助 && FileUtils.Exists(PclCore.GetPathTemp() + "Help\\" + RelativeUrl)) {
            // 相对 PCL 自带帮助文件夹的路径
            Location = PclCore.GetPathTemp() + "Help\\" + RelativeUrl;
            WorkingDir = PclCore.GetPathTemp() + "Help\\";
            Logger.Info($"自定义事件中由相对 PCL 自带帮助文件夹的路径{Type}：{Location}");
        } else if (Type == EventType.打开文件 || Type == EventType.执行命令) {
            // 直接使用原有路径启动程序
            Location = RelativeUrl;
            Logger.Info($"自定义事件中直接{Type}：{Location}");
        } else {
            // 打开帮助，但是格式不对劲
            throw new FileNotFoundException("未找到 EventData 指向的本地 xaml 文件：" + RelativeUrl, RelativeUrl);
        }

        return new[] { Location, WorkingDir };
    }

    /// <summary>
    /// 弹出安全确认弹窗。返回是否继续执行。
    /// </summary>
    private static bool EventSafetyConfirm(string Message) {
        if (PclCore.GetSetting<bool>("HintCustomCommand")) return true;
        switch (PclUiBridge.ShowModpackMsgBox(Message + Environment.NewLine + "请在确认没有安全隐患后再继续。", "执行确认", "继续", "继续且今后不再要求确认", "取消", false, false)) {
            case 1:
                return true;
            case 2:
                PclCore.SetSetting("HintCustomCommand", true);
                return true;
            default:
                return false;
        }
    }

}
