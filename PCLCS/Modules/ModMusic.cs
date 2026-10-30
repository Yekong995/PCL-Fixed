// 由 VB Modules\ModMusic.vb 迁移而来。
// UI 依赖（FrmMain.BtnExtraMusic、FrmSetupUI.MusicRefreshUI、Hint）通过 PclUiBridge 桥接，
// VB 侧 ModMusic 保留同名转发壳。
#nullable disable

using NAudio.Wave;

namespace PCLCS.Modules;

public static class ModMusic {

    #region 播放列表

    /// <summary>
    /// 接下来要播放的音乐文件路径。未初始化时为 null。
    /// </summary>
    public static List<string> MusicWaitingList = null;
    /// <summary>
    /// 全部音乐文件路径。未初始化时为 null。
    /// </summary>
    public static List<string> MusicAllList = null;
    /// <summary>
    /// 初始化音乐播放列表。
    /// </summary>
    /// <param name="ForceReload">强制全部重新载入列表。</param>
    /// <param name="PreventFirst">在重载列表时避免让某项成为第一项。</param>
    private static void MusicListInit(bool ForceReload, string PreventFirst = null) {
        if (ForceReload) MusicAllList = null;
        try {
            // 初始化全部可用音乐列表
            if (MusicAllList is null) {
                MusicAllList = new List<string>();
                DirectoryUtils.Create(Paths.Base + "PCL\\Musics\\");
                foreach (string File in DirectoryUtils.EnumerateFiles(Paths.Base + "PCL\\Musics\\", true)) {
                    // 文件夹可能会被加入 .ini 文件夹配置文件、一些乱七八糟的 .jpg 文件啥的
                    if (new[] { "ini", "jpg", "txt", "cfg", "lrc", "db", "png" }.Contains(PathUtils.GetExtension(File))) continue;
                    MusicAllList.Add(File);
                }
            }
            // 打乱顺序播放
            MusicWaitingList = PclCore.GetSetting<bool>("UiMusicRandom") ? new List<string>(MusicAllList).Shuffle().ToList() : new List<string>(MusicAllList);
            if (PreventFirst is not null && MusicWaitingList.FirstOrDefault() == PreventFirst) {
                // 若需要避免成为第一项的为第一项，则将它放在最后
                MusicWaitingList.RemoveAt(0);
                MusicWaitingList.Add(PreventFirst);
            }
        } catch (Exception ex) {
            Logger.Error(ex, "初始化音乐列表失败");
        }
    }
    /// <summary>
    /// 获取下一首播放的音乐路径并将其从列表中移除。
    /// 如果没有，可能会返回 null。
    /// </summary>
    private static string DequeueNextMusicAddress() {
        string Result;
        // 初始化，确保存在音乐
        if (MusicAllList is null || !MusicAllList.Any() || !MusicWaitingList.Any()) MusicListInit(false);
        // 出列下一个音乐，如果出列结束则生成新列表
        if (MusicWaitingList.Any()) {
            Result = MusicWaitingList[0];
            MusicWaitingList.RemoveAt(0);
        } else {
            Result = null;
        }
        if (!MusicWaitingList.Any()) MusicListInit(false, Result);
        return Result;
    }

    #endregion

    #region UI 控制

    /// <summary>
    /// 刷新背景音乐按钮 UI 与设置页 UI。
    /// </summary>
    private static void MusicRefreshUI() {
        PclCore.RunInUi(
        () => {
            try {
                if (!MusicAllList.Any()) {
                    // 无背景音乐
                    PclUiBridge.UpdateMusicButton?.Invoke(false, "", 1, "");
                } else {
                    // 有背景音乐（先把提示文本算好，再一次性通过桥设置按钮，语义与 VB 逐属性赋值一致）
                    string IconName;
                    double Scale;
                    string ToolTipText;
                    if (MusicState == MusicStates.Pause) {
                        IconName = "Play";
                        Scale = 0.8;
                        ToolTipText = "已暂停：" + PathUtils.GetFileNameWithoutExtension(MusicCurrent);
                        if (MusicAllList.Count > 1) {
                            ToolTipText += Environment.NewLine + "左键恢复播放，右键播放下一曲。";
                        } else {
                            ToolTipText += Environment.NewLine + "左键恢复播放，右键重新从头播放。";
                        }
                    } else {
                        IconName = "Music";
                        Scale = 1;
                        ToolTipText = "正在播放：" + PathUtils.GetFileNameWithoutExtension(MusicCurrent);
                        if (MusicAllList.Count > 1) {
                            ToolTipText += Environment.NewLine + "左键暂停，右键播放下一曲。";
                        } else {
                            ToolTipText += Environment.NewLine + "左键暂停，右键重新从头播放。";
                        }
                    }
                    PclUiBridge.UpdateMusicButton?.Invoke(true, IconName, Scale, ToolTipText);
                }
                PclUiBridge.MusicRefreshSetupUi?.Invoke();

            } catch (Exception ex) {
                Logger.Error(ex, "刷新背景音乐 UI 失败");
            }
        });
    }

    /// <summary>
    /// 让音乐在暂停、播放间切换，并显示提示文本。
    /// </summary>
    public static void MusicControlPause() {
        if (MusicNAudio is null) {
            PclUiBridge.ShowHint("音乐播放尚未开始！", HintType.Red);
        } else {
            switch (MusicState) {
                case MusicStates.Pause:
                    MusicResume();
                    break;
                case MusicStates.Play:
                    MusicPause();
                    break;
                default:
                    Logger.Warn("音乐目前为停止状态，已强制尝试开始播放");
                    MusicRefreshPlay(false);
                    break;
            }
        }
    }

    /// <summary>
    /// 播放下一曲，并显示提示文本。
    /// </summary>
    public static void MusicControlNext() {
        if (MusicAllList.IsSingle()) {
            MusicStartPlay(MusicCurrent);
            PclUiBridge.ShowHint("重新播放：" + PathUtils.GetLastPart(MusicCurrent), HintType.Green);
        } else {
            string Address = DequeueNextMusicAddress();
            if (Address is null) {
                PclUiBridge.ShowHint("没有可以播放的音乐！", HintType.Red);
            } else {
                MusicStartPlay(Address);
                PclUiBridge.ShowHint("正在播放：" + PathUtils.GetLastPart(Address), HintType.Green);
            }
        }
        MusicRefreshUI();
    }

    #endregion

    #region 主状态控制

    /// <summary>
    /// 获取当前的音乐播放状态。
    /// </summary>
    public static MusicStates MusicState {
        get {
            if (MusicNAudio is null) return MusicStates.Stop;
            switch (MusicNAudio.PlaybackState) {
                case PlaybackState.Stopped: // VB 原为数字 0
                    return MusicStates.Stop;
                case PlaybackState.Paused: // VB 原为数字 2
                    return MusicStates.Pause;
                default:
                    return MusicStates.Play;
            }
        }
    }

    // 重载与开始

    /// <summary>
    /// 重载播放列表并尝试开始播放背景音乐。
    /// </summary>
    /// <param name="ShowHint">是否显示刷新提示。</param>
    public static void MusicRefreshPlay(bool ShowHint, bool IsFirstLoad = false) {
        try {

            MusicListInit(true);
            if (!MusicAllList.Any()) {
                if (MusicNAudio is null) {
                    if (ShowHint) PclUiBridge.ShowHint("未检测到可用的背景音乐！", HintType.Red);
                } else {
                    MusicNAudio = null;
                    if (ShowHint) PclUiBridge.ShowHint("背景音乐已清除！", HintType.Green);
                }
            } else {
                string Address = DequeueNextMusicAddress();
                if (Address is null) {
                    if (ShowHint) PclUiBridge.ShowHint("没有可以播放的音乐！", HintType.Red);
                } else {
                    try {
                        MusicStartPlay(Address, IsFirstLoad);
                        if (ShowHint) PclUiBridge.ShowHint("背景音乐已刷新：" + PathUtils.GetLastPart(Address), HintType.Green); // VB 原为 Hint(..., HintType.Green, False)，桥接版 Hint 不带 Log 参数
                    } catch {
                    }
                }
            }
            MusicRefreshUI();

        } catch (Exception ex) {
            Logger.Error(ex, "刷新背景音乐播放失败");
        }
    }
    /// <summary>
    /// 开始播放音乐。
    /// </summary>
    private static void MusicStartPlay(string Address, bool IsFirstLoad = false) {
        if (Address is null) return;
        Logger.Info($"播放开始：{Address}");
        MusicCurrent = Address;
        PclCore.RunInNewThread(() => MusicLoop(IsFirstLoad), "Music", ThreadPriority.BelowNormal);
    }

    // 播放与暂停

    /// <summary>
    /// 暂停音乐播放，返回是否成功切换了状态。
    /// </summary>
    public static bool MusicPause() {
        if (MusicState == MusicStates.Play) {
            PclCore.RunInThread(
            () => {
                Logger.Info("已暂停播放");
                MusicNAudio?.Pause();
                MusicRefreshUI();
            });
            return true;
        } else {
            Logger.Info($"无需暂停播放，当前状态为 {MusicState}");
            return false;
        }
    }
    /// <summary>
    /// 继续音乐播放，返回是否成功切换了状态。
    /// </summary>
    public static bool MusicResume() {
        if (MusicState == MusicStates.Play || !MusicAllList.Any()) {
            Logger.Info($"无需继续播放，当前状态为 {MusicState}");
            return false;
        } else {
            PclCore.RunInThread(
            () => {
                Logger.Info("已恢复播放");
                try {
                    MusicNAudio?.Play();
                } catch { // https://github.com/Meloong-Git/PCL/pull/5415#issuecomment-2751135223
                    MusicNAudio?.Stop();
                    MusicNAudio?.Play();
                }
                MusicRefreshUI();
            });
            return true;
        }
    }

    #endregion

    /// <summary>
    /// 当前正在播放的 WaveOutEvent。
    /// 如果为它赋了类别，则会没有必要地加载 NAudio.dll。
    /// </summary>
    public static WaveOutEvent MusicNAudio = null;
    /// <summary>
    /// 当前播放的音乐地址。
    /// </summary>
    private static string MusicCurrent = "";

    /// <summary>
    /// 在 MusicUuid 不变的前提下，持续播放某地址的音乐，且在播放结束后随机播放下一曲。
    /// </summary>
    private static void MusicLoop(bool IsFirstLoad = false) {
        WaveOutEvent CurrentWave = null;
        WaveStream Reader = null;
        try {
            // 开始播放
            CurrentWave = new WaveOutEvent();
            MusicNAudio = CurrentWave;
            CurrentWave.DeviceNumber = -1;
            try {
                Reader = new AudioFileReader(MusicCurrent);
            } catch (Exception ex) {
                Logger.Warn(ex, "使用 AudioFileReader 加载音频文件失败，换用 MediaFoundationReader 重试");
                Reader = new MediaFoundationReader(MusicCurrent);
            }
            CurrentWave.Init(Reader);
            CurrentWave.Play();
            // 第一次打开的暂停
            if (IsFirstLoad && !PclCore.GetSetting<bool>("UiMusicAuto")) CurrentWave.Pause();
            MusicRefreshUI();
            // 停止条件：播放完毕或变化
            int PreviousVolume = 0;
            while (CurrentWave.Equals(MusicNAudio) && !(CurrentWave.PlaybackState == PlaybackState.Stopped)) {
                if (PclCore.GetSetting<int>("UiMusicVolume") != PreviousVolume) {
                    // 更新音量
                    PreviousVolume = PclCore.GetSetting<int>("UiMusicVolume");
#pragma warning disable CS0612, CS0618 // 类型或成员已过时（VB 原为 #Disable Warning BC40008）
                    CurrentWave.Volume = Convert.ToSingle(PreviousVolume / 1000d); // VB 的 / 为 Double 除法，再隐式收窄为 Single
#pragma warning restore CS0612, CS0618
                }
                // 更新进度条
                double Percent = Reader.CurrentTime.TotalMilliseconds / Reader.TotalTime.TotalMilliseconds;
                PclCore.RunInUi(() => PclUiBridge.UpdateMusicButtonProgress?.Invoke(Percent));
                Thread.Sleep(100);
            }
            // 当前音乐已播放结束，继续下一曲
            if (CurrentWave.PlaybackState == PlaybackState.Stopped && MusicAllList.Any()) MusicStartPlay(DequeueNextMusicAddress());
        } catch (Exception ex) {
            Logger.Warn(ex, $"播放音乐出现内部错误（{MusicCurrent}）");
            if (ex is NAudio.MmException && ex.Message.Contains("AlreadyAllocated")) {
                PclUiBridge.ShowHint("你的音频设备正被其他程序占用。请在关闭占用的程序后重启 PCL，才能恢复音乐播放功能！", HintType.Red);
                Thread.Sleep(1000000000);
            }
            if (ex is NAudio.MmException && (ex.Message.Contains("NoDriver") || ex.Message.Contains("BadDeviceId"))) {
                PclUiBridge.ShowHint("由于音频设备变更，音乐播放功能在重启 PCL 后才能恢复！", HintType.Red);
                Thread.Sleep(1000000000);
            }
            if (!(new[] { "wav", "mp3", "flac" }.Contains(PathUtils.GetExtension(MusicCurrent))) ||
                ex.Message.Contains("0xC00D36C4")) { // #5096：不支持给定的 URL 的字节流类型。 (异常来自 HRESULT:0xC00D36C4)
                PclUiBridge.ShowHint("播放音乐失败（" + PathUtils.GetLastPart(MusicCurrent) + "）：PCL 可能不支持此音乐格式，请将格式转换为 .wav、.mp3 或 .flac 后再试", HintType.Red);
            } else {
                Logger.Error(ex, $"播放音乐失败（{PathUtils.GetLastPart(MusicCurrent)}）", LogBehavior.Toast);
            }
            // 将播放错误的音乐从列表中移除
            MusicAllList.Remove(MusicCurrent);
            MusicWaitingList.Remove(MusicCurrent);
            MusicRefreshUI();
            // 等待 2 秒后继续播放
            Thread.Sleep(2000);
            if (ex is FileNotFoundException) {
                MusicRefreshPlay(true, IsFirstLoad);
            } else {
                MusicStartPlay(DequeueNextMusicAddress(), IsFirstLoad);
            }
        } finally {
            if (CurrentWave is not null) CurrentWave.Dispose();
            if (Reader is not null) Reader.Dispose();
            MusicRefreshUI();
        }
    }

}

/// <summary>
/// 音乐播放状态。
/// </summary>
public enum MusicStates {
    /// <summary>
    /// 停止。
    /// </summary>
    Stop,
    Play,
    Pause,
}
