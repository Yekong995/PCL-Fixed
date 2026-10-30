// 由 VB Modules\Base\PclLogger.vb 迁移而来。
// 日志输出行为（Hint/弹窗/反馈/遥测）经 PclUiBridge 桥接，脱敏函数使用 PclMc.FilterUserName/FilterAccessToken。
#nullable disable

using System;
using System.Threading;
using MeloongCore;

namespace PCLCS.Modules;

public class PclLogger : FileLogger {

    /// <inheritdoc/>
    public override string Format(string Text, LogLevel Level, string FilePath, Exception Ex) {
        Text = base.Format(Text, Level, FilePath, Ex);
        Text = PclMc.FilterUserName(PclMc.FilterAccessToken(Text, '*'), '*');
        return Text;
    }

    // 对应 VB 的 Static FirstTrigger（跨实例共享）
    private static bool FirstTrigger = true;

    /// <inheritdoc/>
    public override void HandleBehavior(string RawMessage, string FormattedMessage, LogBehavior Behavior, Exception Ex) {
        if (PclCore.IsProgramEnding) return;
        base.HandleBehavior(RawMessage, FormattedMessage, Behavior, Ex);
        string BriefText = Ex is null ? RawMessage : (RawMessage is null ? "" : $"{RawMessage}：") + Ex.GetDisplay(false);
        BriefText = PclMc.FilterUserName(PclMc.FilterAccessToken(BriefText, '*'), '*');
        string DetailText = Ex is null ? RawMessage : (RawMessage is null ? "" : $"{RawMessage}：") + Ex.GetDisplay(true);
        DetailText = PclMc.FilterUserName(PclMc.FilterAccessToken(DetailText, '*'), '*');
        switch (Behavior) {
            case LogBehavior.None:
                // 啥也不干
                break;
            case LogBehavior.ToastIfDebug:
                if (PclUiBridge.BuildTypeNumberGet() == 100 || PclUiBridge.ModeDebugGet() == true) PclUiBridge.ShowHintSilent?.Invoke("[调试模式] " + BriefText, HintType.Blue);
                break;
            case LogBehavior.Toast:
                PclUiBridge.ShowHintSilent?.Invoke(BriefText, HintType.Red);
                break;
            case LogBehavior.Alert:
                PclUiBridge.ShowModpackMsgBox?.Invoke(DetailText, "错误", null, null, null, true, false);
                break;
            case LogBehavior.AlertThenFeedback:
                if (PclUiBridge.IsLauncherNewest?.Invoke() == true) {
                    if (PclUiBridge.ShowModpackMsgBox?.Invoke(DetailText + Environment.NewLine + Environment.NewLine + "是否反馈此问题？如果不反馈，这个问题可能永远无法得到解决！", "错误", "反馈", "取消", null, true, false) == 1) PclUiBridge.Feedback?.Invoke(false, true);
                } else {
                    PclUiBridge.ShowModpackMsgBox?.Invoke(DetailText + Environment.NewLine + Environment.NewLine + "将 PCL 更新至最新版或许可以解决这个问题……", "错误", null, null, null, true, false);
                }
                break;
            case LogBehavior.AlertThenCrash:
                if (FirstTrigger) {
                    // 首次触发
                    FirstTrigger = false;
                    if (PclUiBridge.IsLauncherNewest?.Invoke() == true) {
                        if (PclUiBridge.ShowCriticalMsgBox?.Invoke(DetailText + Environment.NewLine + Environment.NewLine + "是否反馈此问题？如果不反馈，这个问题可能永远无法得到解决！") == true) PclUiBridge.Feedback?.Invoke(false, true);
                    } else {
                        PclUiBridge.ShowCriticalMsgBox?.Invoke(DetailText + Environment.NewLine + Environment.NewLine + "将 PCL 更新至最新版或许可以解决这个问题……");
                    }
                } else {
                    // 多次触发，直接使程序崩溃（这通常代表着在其他线程循环触发严重异常）
                    Thread.Sleep(2000);
                }
                PclUiBridge.EndProgramForce?.Invoke((int)ProcessReturnValues.Exception);
                break;
        }
        // 遥测
        if (Behavior >= LogBehavior.Toast) PclUiBridge.Telemetry?.Invoke("错误日志", new[] { "Exception", DetailText });
    }

}
