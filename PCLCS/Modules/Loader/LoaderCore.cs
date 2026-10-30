// 由 VB Modules\Base\ModLoader.vb 的加载器框架迁移而来。
// VB UI 依赖（FrmMain.BtnExtraDownload、Settings 等）通过 PclUiBridge / PclCore.SettingProvider 桥接。
#nullable disable

using System;
using System.Collections.Generic;
using System.Threading;
using MeloongCore;

namespace PCLCS.Modules;

/// <summary>
/// 与 VB UI 交互的桥接钩子，由 VB 宿主在启动时注册。
/// </summary>
public static class PclUiBridge {
    /// <summary>对应 FrmMain.BtnExtraDownload.ShowRefresh()。</summary>
    public static Action DownloadExtraRefresh;
    /// <summary>对应 PageInstanceLeft.Instance，用于版本独立设置项的读取。</summary>
    public static Func<object> CurrentVersionInstance;
    /// <summary>对应 FrmMain.ShowWindowToTop()。</summary>
    public static Action ShowWindowToTop;
    /// <summary>对应 MyMsgBox 的多按钮弹窗。参数为（内容, 标题, 按钮1, 按钮2, 按钮3, 按钮2回调），返回按钮序号（1/2/3）。</summary>
    public static Func<string, string, string, string, string, Action, int> ShowCrashMsgBox;
    /// <summary>对应 Hint(Text, Type)。</summary>
    public static Action<string, HintType> ShowHint;
    /// <summary>对应 PageSetupSystem.IsLauncherNewest()。</summary>
    public static Func<bool> IsLauncherNewest;
    /// <summary>对应 Telemetry(Event, Datas())。</summary>
    public static Action<string, string[]> Telemetry;
    /// <summary>对应 FeedbackInfo()。</summary>
    public static Action FeedbackInfo;
    /// <summary>对应 RequestTaskTempFolder()。</summary>
    public static Func<string> RequestTaskTempFolder;
    /// <summary>对应 FilterUserName(Raw, FilterChar)。</summary>
    public static Func<string, char, string> FilterUserName;
    /// <summary>对应 FilterAccessToken(Raw, FilterChar)。</summary>
    public static Func<string, char, string> FilterAccessToken;
    /// <summary>对应 McInstance.VersionDisplayName（参数为 McInstance 对象，可为 null）。</summary>
    public static Func<object, string> GetInstanceDisplayName;
    /// <summary>对应 ModSecret.SecretCdnSign(UrlWithMark)。</summary>
    public static Func<string, string> SecretCdnSign;
    /// <summary>对应 ModSecret.SecretHeadersSign(Url, Req, SimulateBrowserHeaders)。</summary>
    public static Action<string, System.Net.Http.HttpRequestMessage, bool> SecretHeadersSign;
    /// <summary>对应 McFolderList.Select(Function(f) f.Location)。</summary>
    public static Func<IEnumerable<string>> McFolderListProvider;
    /// <summary>对应 ModLoader.LoaderTaskbar.ToList()。</summary>
    public static Func<List<LoaderBase>> LoaderTaskbar;
    /// <summary>对应 ModMinecraft.McDownloadClientUpdateHint(Version, Json)。</summary>
    public static Action<string, Newtonsoft.Json.Linq.JObject> McDownloadClientUpdateHint;
}

/// <summary>
/// 兼容旧 Loader 的下载加载器接口，由 LoaderDownload（已迁移至 C#，PCLCS.Modules.Net.NetCore.cs）实现。
/// </summary>
public interface IDownloadLoader {
    void StartDownload(object Input, bool IsForceRestart);
}

/// <summary>
/// 加载器的统一基类。
/// </summary>
public abstract class LoaderBase : ILoadingTrigger {

    public bool IsLoader => true;

    // 基础属性
    /// <summary>
    /// 加载器的标识编号。
    /// </summary>
    public int Uuid = PclCore.GetUuid();
    /// <summary>
    /// 加载器的名称。
    /// </summary>
    public string Name;
    /// <summary>
    /// 用于状态改变检测的同步锁。
    /// </summary>
    public readonly object LockState = new object();
    /// <summary>
    /// 父加载器。
    /// </summary>
    public LoaderBase Parent = null;
    /// <summary>
    /// 最上级的加载器。
    /// </summary>
    public LoaderBase RealParent {
        get {
            try {
                LoaderBase RealParent = Parent;
                while (RealParent is not null && RealParent.Parent is not null) {
                    RealParent = RealParent.Parent;
                }
                return RealParent;
            } catch (Exception ex) {
                Logger.Error(ex, $"获取父加载器失败（{Name}）");
                return null;
            }
        }
    }

    protected LoaderBase() {
        Name = "未命名任务 " + Uuid + "#";
    }

    public virtual void InitParent(LoaderBase Parent) {
        this.Parent = Parent;
    }

    // 事件

    /// <summary>
    /// 当状态改变时，在工作线程触发代码。在添加事件后，必须将 HasOnStateChangedThread 设为 True。
    /// </summary>
    public event Action<LoaderBase, LoadState, LoadState> OnStateChangedThread;
    public bool HasOnStateChangedThread = false;

    /// <summary>
    /// 当状态改变时，在 UI 线程触发代码。
    /// </summary>
    public event Action<LoaderBase, LoadState, LoadState> OnStateChangedUi;
    /// <summary>
    /// 简易的在 UI 线程添加触发事件的方式。主要用于在新建 Loader 时直接使用 With 绑定事件，以及进行老代码兼容。
    /// </summary>
    public Action<LoaderBase> OnStateChanged {
        set {
            OnStateChangedUi += (Loader, NewState, OldState) => value(Loader);
        }
    }

    /// <summary>
    /// 在加载器目标事件执行完成，加载器状态即将变为 Finish 时调用。可以视为扩展加载器目标事件。
    /// </summary>
    public event Action<LoaderBase> PreviewFinish;
    protected void RaisePreviewFinish() {
        PreviewFinish?.Invoke(this);
    }

    // 状态监控
    /// <summary>
    /// 加载器的状态。
    /// </summary>
    public LoadState State {
        get {
            return _State;
        }
        set {
            if (_State == value) return;
            LoadState OldState = _State;
            if (value == LoadState.Finished && PclCore.GetSetting<bool>("SystemDebugDelay")) Thread.Sleep(PclCore.RandomInteger(100, 2000));
            _State = value;
            if (value == LoadState.Canceled) CancellationTokenSource?.Cancel();
            Logger.Log($"加载器 {GetType().Name} {Name} 状态改变：{value}", value == LoadState.Failed ? LogLevel.Error : LogLevel.Info);
            // 实现 ILoadingTrigger 接口与 OnStateChanged 回调
            PclCore.RunInUi(
            () => {
                switch (value) {
                    case LoadState.Loading:
                        LoadingState = MyLoadingState.Run;
                        break;
                    case LoadState.Failed:
                        LoadingState = MyLoadingState.Error;
                        break;
                    default:
                        LoadingState = MyLoadingState.Stop;
                        break;
                }
                OnStateChangedUi?.Invoke(this, value, OldState);
            });
            if (HasOnStateChangedThread) PclCore.RunInThread(() => OnStateChangedThread?.Invoke(this, value, OldState));
        }
    }
    private LoadState _State = LoadState.Waiting;
    public MyLoadingState LoadingState {
        get {
            return _LoadingState;
        }
        set {
            if (_LoadingState == value) return;
            MyLoadingState OldState = _LoadingState;
            _LoadingState = value;
            LoadingStateChanged?.Invoke(value, OldState);
        }
    }
    private MyLoadingState _LoadingState = MyLoadingState.Stop;
    public event Action<MyLoadingState, MyLoadingState> LoadingStateChanged;
    /// <summary>
    /// 若加载器出错，可提供给外部参考的异常。
    /// </summary>
    public Exception Error = null;
    /// <summary>
    /// 使用 LoaderCombo 加载时，该任务是否会阻碍后续任务的进行。
    /// </summary>
    public bool Block = true;
    /// <summary>
    /// 该加载器是否显示在列表中。
    /// </summary>
    public bool Show = true;
    /// <summary>
    /// 当前加载器是否由 IsForceRestart 强制调起。
    /// 这个属性自身不会干任何事，而是提供给加载器执行的函数，使得加载器调用另一个加载器时，可以继承强制重启属性。
    /// </summary>
    public bool IsForceRestarting = false;

    // 进度监控
    /// <summary>
    /// 加载器的执行进度，为 0 至 1 的小数。
    /// </summary>
    public virtual double Progress {
        get {
            switch (State) {
                case LoadState.Waiting:
                    return 0;
                case LoadState.Loading:
                    return _Progress == -1 ? 0 : _Progress;
                default:
                    return 1;
            }
        }
        set {
            if (_Progress == value) return;
            double OldValue = _Progress;
            _Progress = value;
            ProgressChanged?.Invoke(value, OldValue);
        }
    }
    private double _Progress = -1;
    public event Action<double, double> ProgressChanged;
    /// <summary>
    /// 创建一个 ProgressProvider，在其进度改变时，并将其进度单向映射到此加载器上。
    /// 这是 Loader 对新框架的兼容层。
    /// </summary>
    public ProgressProvider CreateSyncProgressProvider(double FromPercentage = 0, double ToPercentage = 1) {
        ProgressProvider Provider = new ProgressProvider();
        Provider.ProgressChanged +=
        ChangedProgress => {
            (double actual, double skiped) RawProgress = Provider.GetRaw();
            Progress = FromPercentage + (RawProgress.actual + RawProgress.skiped) * (ToPercentage - FromPercentage);
        };
        return Provider;
    }
    /// <summary>
    /// 计算总进度时的权重。它应该为预计时间（秒）。
    /// </summary>
    public double ProgressWeight = 1;

    // 状态变化
    public abstract void Start(object Input = null, bool IsForceRestart = false);
    public abstract void Cancel();
    public abstract void Failed(Exception Ex);
    private CancellationTokenSource CancellationTokenSource;
    /// <summary>
    /// 创建一个随此加载器取消的取消令牌；令牌取消时也会调用此加载器的 Cancel。
    /// 这是 Loader 对新框架的兼容层。
    /// </summary>
    public CancellationToken CreateCancellationToken() {
        lock (LockState) {
            if (CancellationTokenSource is null || CancellationTokenSource.IsCancellationRequested) {
                CancellationTokenSource = new CancellationTokenSource();
                CancellationTokenSource.Token.Register(Cancel);
            }
            if (State == LoadState.Canceled) CancellationTokenSource.Cancel();
            return CancellationTokenSource.Token;
        }
    }

    // 等待结束
    public const string WaitForExitTimeoutMessage = "等待加载器执行超时。";

    /// <summary>
    /// 无限期地等待加载器完成，直到结束或抛出异常。若加载器尚未开始，则会开始执行。
    /// </summary>
    public void WaitForExit(object Input = null, LoaderBase LoaderToSyncProgress = null, bool IsForceRestart = false,
                           CancellationToken c = default, ProgressProvider p = null) {
        Start(Input, IsForceRestart);
        do {
            if (LoaderToSyncProgress is not null) LoaderToSyncProgress.Progress = Progress;
            p?.Set(Progress);
            c.ThrowIfCancellationRequested();
            Thread.Sleep(10);
        } while (State == LoadState.Loading);
        if (State == LoadState.Finished) {
            return;
        } else if (State == LoadState.Canceled) {
            throw new OperationCanceledException("加载器执行已中断");
        } else if (Error is null) {
            throw new Exception("未知错误！");
        } else {
            throw new Exception(Error.Message, Error); // 保留调用堆栈，同时不影响信息输出与单元测试
        }
    }
    /// <summary>
    /// 等待加载器完成，直到结束、抛出异常或超时。若加载器尚未开始，则会开始执行。
    /// </summary>
    /// <param name="Timeout">等待的超时时间，以毫秒为单位。</param>
    /// <param name="TimeoutMessage">若执行超时，将会抛出的异常信息。</param>
    public void WaitForExitTime(int Timeout, object Input = null, string TimeoutMessage = WaitForExitTimeoutMessage, object LoaderToSyncProgress = null, bool IsForceRestart = false) {
        Start(Input, IsForceRestart);
        do {
            if (LoaderToSyncProgress is not null) ((dynamic)LoaderToSyncProgress).Progress = Progress;
            Thread.Sleep(10);
            Timeout -= 10;
            if (Timeout < 0) throw new TimeoutException(TimeoutMessage);
        } while (State == LoadState.Loading);
        if (State == LoadState.Finished) {
            return;
        } else if (State == LoadState.Canceled) {
            throw new OperationCanceledException("加载器执行已中断");
        } else if (Error is null) {
            throw new Exception("未知错误！");
        } else {
            throw Error;
        }
    }

    // 相同重载
    public override bool Equals(object obj) {
        LoaderBase baseLoader = obj as LoaderBase;
        return baseLoader is not null && Uuid == baseLoader.Uuid;
    }
    public override int GetHashCode() {
        return Uuid;
    }

}

/// <summary>
/// 用于异步执行并监控单一函数的加载器。
/// </summary>
public class LoaderTask<TIn, TOut> : LoaderBase {

    // 线程设定
    protected internal ThreadPriority ThreadPriority;
    // 执行事件
    protected internal Action<LoaderTask<TIn, TOut>> LoadDelegate;
    protected internal Func<object> InputDelegate;
    // 状态指示
    /// <summary>
    /// 当前执行线程是否应当中断。只应用在加载器的工作线程中判断，不可跨线程调用。
    /// </summary>
    public bool IsCanceled {
        get {
            return IsCanceledWithThread(Thread.CurrentThread);
        }
    }
    /// <summary>
    /// 当前执行线程是否应当中断。需要手动提供加载器线程，用于需要跨线程检查的情况。
    /// </summary>
    public bool IsCanceledWithThread(Thread Thread) {
        return LastRunningThread is null || !ReferenceEquals(Thread, LastRunningThread) || State == LoadState.Canceled;
    }
    /// <summary>
    /// 在输入相同时使用原有结果的超时，单位为毫秒。
    /// </summary>
    public int ReloadTimeout = -1;
    /// <summary>
    /// 上次完成加载时的时间。
    /// </summary>
    public long LastFinishedTime = 0;
    /// <summary>
    /// 最后一次运行加载器的线程。可能为 Nothing，或线程已结束。
    /// </summary>
    public Thread LastRunningThread = null;
    // 输入输出
    public TIn Input = default;
    public TOut Output = default;

    // 获取输入
    public TIn StartGetInput(TIn Input = default, Func<TIn> InputDelegate = null) { // InputDelegate 参数存在匿名调用
        if (InputDelegate is null) InputDelegate = (Func<TIn>)(object)this.InputDelegate;
        TIn NewInput = default; // 若 InputType 不能为 Nothing，则会导致 Input Is Nothing 永远失败，因此需要额外判断
        if ((Input == null || (NewInput != null && Input.Equals(NewInput))) && InputDelegate is not null) {
            PclCore.RunInUiWait(() => Input = InputDelegate());
        }
        return Input;
    }
    // 代码执行
    public bool ShouldStart(ref TIn Input, bool IsForceRestart = false, bool IgnoreReloadTimeout = false) {
        // 获取输入
        try {
            Input = StartGetInput(Input);
        } catch (Exception ex) {
            Logger.Error(ex, $"加载输入获取失败（{Name}）", LogBehavior.Toast);
            Error = ex;
            lock (LockState) {
                State = LoadState.Failed;
            }
        }
        // 检验输入以确定情况
        if (IsForceRestart) return true; // 强制要求重启
        if ((Input == null) != (this.Input == null) || (Input != null && !Input.Equals(this.Input))) return true; // 输入不同

        if ((State == LoadState.Loading || State == LoadState.Finished) && // 正在加载或已结束…
           (IgnoreReloadTimeout || ReloadTimeout == -1 ||
           LastFinishedTime == 0 || PclCore.GetTimeMs() - LastFinishedTime < ReloadTimeout)) { // …且没有超时…
            return false; // …则不重试
        } else {
            return true; // 否则需要重启
        }
    }
    public override void Start(object Input = null, bool IsForceRestart = false) {
        // 模拟 VB 中 Object 参数经 ByRef TIn 往返的语义
        TIn TypedInput = Input is null ? default : (TIn)Input;
        // 确认是否开始加载
        if (ShouldStart(ref TypedInput, IsForceRestart)) {
            // 输入不同或失败，开始加载
            if (State == LoadState.Loading) TriggerThreadInterrupt();
            this.Input = TypedInput;
            lock (LockState) {
                State = LoadState.Loading;
                Progress = -1;
            }
        } else {
            return;
        }

        LastRunningThread = new Thread(
        () => {
            try {
                IsForceRestarting = IsForceRestart;
                Logger.Trace(() => $"加载线程 {Name} 已{(IsForceRestarting ? "强制" : "")}启动");
                LoadDelegate(this);
                if (IsCanceled) {
                    Logger.Warn($"加载线程 {Name} 已中断但线程正常运行至结束，输出被弃用（最新线程：{(LastRunningThread is null ? -1 : LastRunningThread.ManagedThreadId)}）");
                    return;
                }
                Logger.Trace(() => $"加载线程 {Name} 已完成");
                RaisePreviewFinish();
                State = LoadState.Finished;
                LastFinishedTime = PclCore.GetTimeMs(); // 未中断，本次输出有效
            } catch (Exception ex) {
                if (ex.IsCanceled()) {
                    // 如果线程是因为判断到 IsCanceled 而提前中止，则代表已有新线程被重启，此时不应当改为 Canceled
                    // 如果线程是在没有 IsCanceled 时手动引发了取消异常，则代表没有重启线程，这通常代表用户手动取消，应当改为 Canceled
                    Logger.Trace(ex, $"加载线程 {Name} 已取消，当前已完成 {Math.Round(Progress * 100)}%");
                    if (!IsCanceled) State = LoadState.Canceled;
                } else {
                    Failed(ex);
                }
            }
        }) { Name = "L/" + Name, Priority = this.ThreadPriority };
        try {
            LastRunningThread.Start(); // 不能使用 RunInNewThread，否则在函数返回前线程就会运行完，导致误判 IsCanceled
        } catch (ThreadStateException) { // 若遇到偶发的 “线程正在运行或被终止”，则等待后重试
            Thread.Sleep(500);
            LastRunningThread.Start();
        }
    }
    public override void Failed(Exception ex) {
        Error = ex;
        lock (LockState) {
            if (IsCanceled || State >= LoadState.Finished) return;
            State = LoadState.Failed;
        }
        Logger.Warn(ex, $"加载线程 {Name} 出错，已完成 {Math.Round(Progress * 100)}%");
        TriggerThreadInterrupt();
    }
    public override void Cancel() {
        lock (LockState) {
            if (State != LoadState.Loading) return;
            State = LoadState.Canceled;
        }
        TriggerThreadInterrupt();
    }
    private void TriggerThreadInterrupt() {
        if (LastRunningThread is null) return;
        if (LastRunningThread.IsAlive) {
            LastRunningThread.Interrupt();
            Logger.Trace(() => $"加载线程 {Name} 已中断");
        }
        LastRunningThread = null;
    }

    public LoaderTask() {
        // 仅仅是为了避免一些智障报错（继承类必须重写 New 的情况）
    }
    public LoaderTask(string Name, Action<LoaderTask<TIn, TOut>> LoadDelegate, Func<TIn> InputDelegate = null, ThreadPriority Priority = ThreadPriority.Normal) {
        this.Name = Name;
        this.LoadDelegate = LoadDelegate;
        this.InputDelegate = InputDelegate is null ? null : () => InputDelegate();
        this.ThreadPriority = Priority;
    }

}

/// <summary>
/// 新 Worker 对老 Loader 的兼容包装类。
/// </summary>
public class LoaderWorker : LoaderBase {
    public IWorker Worker { get; }
    public LoaderWorker(string Name, IWorker Worker) {
        this.Name = Name;
        this.Worker = Worker;
        Worker.Progress.ProgressChanged += Raw => Progress = Raw.actual + Raw.skiped;
        Worker.Succeeded += Worker_Succeeded;
        Worker.Canceled += CancelCore;
        Worker.Failed += Worker_Failed;
    }

    public override void Start(object Input = null, bool IsForceRestart = false) {
        lock (LockState) {
            State = LoadState.Loading;
        }
        Logger.Trace(() => $"Worker {Name} 已启动");
        IsForceRestarting = IsForceRestart;
        Worker.Start();
    }
    private void Worker_Succeeded() {
        Logger.Trace(() => $"Worker {Name} 已完成");
        RaisePreviewFinish();
        lock (LockState) {
            State = LoadState.Finished;
        }
    }
    public override void Cancel() {
        CancelCore();
    }
    private void CancelCore() {
        lock (LockState) {
            if (State != LoadState.Loading) return;
            State = LoadState.Canceled;
        }
        Logger.Trace(() => $"Worker {Name} 已取消，当前已完成 {Math.Round(Progress * 100)}%");
        Worker.Cancel();
    }
    public override void Failed(Exception Ex) {
        Worker_Failed(Ex);
    }
    private void Worker_Failed(Exception Ex) {
        lock (LockState) {
            if (State >= LoadState.Finished) return;
            State = LoadState.Failed;
        }
        Logger.Warn(Ex, $"Worker {Name} 出错，已完成 {Math.Round(Progress * 100)}%");
        Error = Ex;
        Worker.Cancel();
    }
}

/// <summary>
/// 支持多个加载器连续运作的复合加载器。
/// </summary>
public class LoaderCombo<InputType> : LoaderBase {

    public override double Progress {
        get {
            switch (State) {
                case LoadState.Waiting:
                    return 0;
                case LoadState.Loading: {
                    double Total = 0, Finished = 0;
                    foreach (LoaderBase Loader in Loaders) {
                        Total += Loader.ProgressWeight;
                        Finished += Loader.ProgressWeight * Loader.Progress;
                    }
                    if (Total == 0) return 0;
                    return Finished / Total;
                }
                default:
                    return 1;
            }
        }
        set {
            throw new Exception("多重加载器不支持设置进度");
        }
    }

    public List<LoaderBase> Loaders = new List<LoaderBase>();
    public InputType Input;
    public LoaderCombo(string Name, IEnumerable<LoaderBase> Loaders) {
        this.Loaders.Clear();
        foreach (LoaderBase Loader in Loaders) {
            if (Loader is not null) {
                this.Loaders.Add(Loader);
                Loader.OnStateChangedThread += SubTaskStateChanged;
                Loader.HasOnStateChangedThread = true;
            }
        }
        InitParent(null);
        this.Name = Name;
    }
    public override void InitParent(LoaderBase Parent) {
        this.Parent = Parent;
        foreach (LoaderBase Loader in Loaders) {
            Loader.InitParent(this);
        }
    }
    public override void Start(object Input = null, bool IsForceRestart = false) {
        IsForceRestarting = IsForceRestart;
        // 改变状态
        lock (LockState) {
            if (State == LoadState.Loading) {
                return;
            } else {
                State = LoadState.Loading;
            }
        }
        // 启动加载
        this.Input = Input is null ? default : (InputType)Input;
        if (IsForceRestart) {
            foreach (LoaderBase Loader in Loaders) {
                Loader.State = LoadState.Waiting;
            }
        }
        PclCore.RunInThread(Update);
    }
    public override void Cancel() {
        // 改变状态
        lock (LockState) {
            if (State == LoadState.Loading || State == LoadState.Waiting) {
                State = LoadState.Canceled;
            } else {
                return;
            }
        }
        PclCore.RunInThread(
        () => {
            // 中断加载器
            foreach (LoaderBase Loader in Loaders) {
                Loader.Cancel();
            }
        });
    }
    public override void Failed(Exception Ex) {
        Error = Ex; // 先设置错误再调整状态，防止父加载器获取不到异常
        lock (LockState) {
            if (State >= LoadState.Finished) return;
            State = LoadState.Failed;
        }
        foreach (LoaderBase Loader in Loaders) {
            Loader.Cancel();
        }
        PclUiBridge.DownloadExtraRefresh?.Invoke();
    }

    /// <summary>
    /// 子任务状态变更。
    /// </summary>
    private void SubTaskStateChanged(LoaderBase Loader, LoadState NewState, LoadState OldState) {
        switch (NewState) {
            case LoadState.Loading:
                // 开始，啥都不干
                break;
            case LoadState.Waiting:
                // 子加载器可能由于外部输入改变而暂时变为 Waiting，之后会立即重新启动
                // 所以啥都不干就行
                break;
            case LoadState.Finished:
                // 正常结束，触发刷新
                Update();
                break;
            case LoadState.Canceled:
                // 被中断，这个任务也中断
                Cancel();
                break;
            case LoadState.Failed:
                // 完蛋，出错了
                Failed(new Exception(Loader.Name + "失败", Loader.Error));
                break;
        }
    }
    /// <summary>
    /// 触发一次更新，以启动新加载器或完成。
    /// </summary>
    private void Update() {
        if (State == LoadState.Finished || State == LoadState.Failed || State == LoadState.Canceled) return;
        bool IsFinished = true;
        bool Blocked = false;
        object Input = this.Input;
        foreach (LoaderBase Loader in Loaders) {
            switch (Loader.State) {
                case LoadState.Finished:
                    // 检查 LoaderTask 是否需要重启
                    if (GeneralUtils.IsGenericInstanceOf(Loader, typeof(LoaderTask<,>))) {
                        if (((dynamic)Loader).ShouldStart(Input is not null && Loader.GetType().GenericTypeArguments[0] == Input.GetType() ? Input : null, false, true)) {
                            Logger.Info($"由于输入条件变更，重启已完成的加载器 {Loader.Name}");
                            goto Restart;
                        }
                        // 更新下一个加载器的输入
                        Input = ((dynamic)Loader).Output;
                    }
                    // 如果不让继续启动，且已有加载器正在加载中，就不继续启动
                    if (Loader.Block && !IsFinished) Blocked = true;
                    break;
                case LoadState.Loading:
                    // 检查 LoaderTask 是否需要重启
                    if (GeneralUtils.IsGenericInstanceOf(Loader, typeof(LoaderTask<,>))) {
                        if (((dynamic)Loader).ShouldStart(Input is not null && Loader.GetType().GenericTypeArguments[0] == Input.GetType() ? Input : null, false, true)) {
                            Logger.Warn($"由于输入条件变更，重启进行中的加载器 {Loader.Name}");
                            goto Restart;
                        }
                    }
                    // 已经有正在加载中的了，不需要再启动了
                    IsFinished = false;
                    Blocked = true;
                    break;
                default:
                Restart:
                    // 未启动，则启动加载器
                    IsFinished = false;
                    if (Blocked) continue;
                    if (Input is not null) {
                        // 若输入类型与下一个加载器相同才继续
                        if (GeneralUtils.IsGenericInstanceOf(Loader, typeof(LoaderTask<,>)) || GeneralUtils.IsGenericInstanceOf(Loader, typeof(LoaderCombo<>))) {
                            Loader.Start(Loader.GetType().GenericTypeArguments[0] == Input.GetType() ? Input : null, IsForceRestarting);
                        } else if (Loader is LoaderWorker) {
                            Loader.Start(null, IsForceRestarting);
                        } else if (Loader is IDownloadLoader downloadLoader) {
                            downloadLoader.StartDownload(Input, IsForceRestarting);
                        } else {
                            throw new Exception("未知的加载器类型（" + Loader.GetType().Name + "）");
                        }
                    } else {
                        Loader.Start(null, IsForceRestarting);
                    }
                    // 阻止继续
                    if (Loader.Block) Blocked = true;
                    break;
            }
        }
        if (IsFinished) {
            // 顺利完成，贼棒
            RaisePreviewFinish();
            State = LoadState.Finished;
            PclUiBridge.DownloadExtraRefresh?.Invoke();
        }
    }

    /// <summary>
    /// 获得最底层的，应被显示给用户的加载器列表，并追加于 List。
    /// </summary>
    public static void GetLoaderList(object Loader, ref List<LoaderBase> List, bool RequireShow = true) {
        foreach (LoaderBase SubLoader in ((dynamic)Loader).Loaders) {
            if (SubLoader.Show || !RequireShow) List.Add(SubLoader);
            if (GeneralUtils.IsGenericInstanceOf(SubLoader, typeof(LoaderCombo<>))) GetLoaderList(SubLoader, ref List, RequireShow);
        }
    }
    /// <summary>
    /// 获得最底层的，应被显示给用户的加载器列表，并追加于 List。
    /// </summary>
    public void GetLoaderList(ref List<LoaderBase> List, bool RequireShow = true) {
        GetLoaderList(this, ref List, RequireShow);
    }
    /// <summary>
    /// 获得最底层的，应被显示给用户的加载器列表。
    /// </summary>
    public List<LoaderBase> GetLoaderList(bool RequireShow = true) {
        List<LoaderBase> List = new List<LoaderBase>();
        GetLoaderList(this, ref List, RequireShow);
        return List;
    }

}
