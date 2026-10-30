// 由 VB Controls\MyLoading.xaml.vb 中的枚举与接口迁移而来，供 C# 加载器框架与 VB 加载动画控件共同使用。
#nullable disable

using System;

namespace PCLCS.Modules;

/// <summary>
/// 加载动画状态枚举（原 VB 中嵌套于 MyLoading 的 MyLoadingState）。
/// </summary>
public enum MyLoadingState {
    Unloaded = -1,
    Run = 0,
    Stop = 1,
    Error = 2,
}

/// <summary>
/// 由 MyLoading 加载动画控件触发更新的接口（原 VB Controls\MyLoading 中的接口）。
/// </summary>
public interface ILoadingTrigger {
    bool IsLoader { get; }
    MyLoadingState LoadingState { get; set; }
    event Action<MyLoadingState, MyLoadingState> LoadingStateChanged;
    event Action<double, double> ProgressChanged;
}

