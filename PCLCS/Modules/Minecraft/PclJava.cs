// 由 VB Modules\Minecraft\ModJava.vb 迁移而来。
// UI 依赖（MyMsgBox、设置页 Java 列表刷新、当前 Java 刷新、切换到版本设置页、迁移版本独立 Java 设置）
// 通过 PclUiBridge 桥接；设置读写经 PclCore.SettingProvider/SettingSetter 桥接。
#nullable disable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using MeloongCore;
using MeloongCore.Extensions;
using Newtonsoft.Json.Linq;

namespace PCLCS.Modules;

public static class PclJava {

    /// <summary>
    /// 初始化 Java 列表。
    /// 若没有任何可用的 Java，则触发搜索。
    /// </summary>
    public static void JavaInit() {
        try {
            // 从老版本迁移
            if (Configs.JavaConfigVersion.Get() == 0) {
                try {
                    // Java 列表
                    Logger.Warn("正在从老版本迁移 Java 列表");
                    List<Java> OldJavaList = PclCore.GetSetting<string>("LaunchArgumentJavaAll").DeserializeJson<JArray>().
                                              Select(JavaObject => new Java(JavaObject["Path"].ToString())).ToList();
                    OldJavaList = JavaUtils.SortAsync(OldJavaList).Run();
                    Logger.Info($"已从老版本设置中发现 {OldJavaList.Count} 个 Java");
                    // 强制选择的 Java
                    string SelectedJavaSetting = PclCore.GetSetting<string>("LaunchArgumentJavaSelect");
                    string SelectedJava = SelectedJavaSetting.StartsWithF("{") ? SelectedJavaSetting.DeserializeJson<JObject>()["Path"].ToString() : "";
                    Java Match = OldJavaList.FirstOrDefault(j => PathUtils.IsSame(j.Folder, SelectedJava));
                    if (Match is not null) {
                        OldJavaList.Remove(Match);
                        OldJavaList.Insert(0, Match);
                        Logger.Info($"已将老版本设置中强制选择的 Java 置顶：{Match}");
                    }
                    Configs.JavaList.Set(new ConcurrentList<Java>(OldJavaList));
                    // 完成迁移
                    Configs.JavaConfigVersion.Set(Versions.JavaConfigVersion);
                } catch (Exception ex) {
                    Logger.Error(ex, "从老版本 PCL 迁移 Java 列表失败");
                }
            }
            // 读取缓存
            ConcurrentList<Java> List = Configs.JavaList.Get();
            if (List.Any()) {
                Logger.Info($"共有 {List.Count} 个可用的 Java：{Environment.NewLine}- {List.Select(j => j.ToString()).Join(Environment.NewLine + "- ")}");
                return;
            }
            // 如果没有 Java，则触发搜索
            Logger.Warn("初始化未找到可用的 Java，将自动触发搜索");
            JavaListRefreshWorker.Start();
        } catch (Exception ex) {
            Logger.Error(ex, "初始化 Java 列表失败");
            Configs.JavaList.Reset();
        }
    }

    /// <summary>
    /// 搜索并获取所有可用的 Java，检查所有 Java 的可用性，写入设置，并在结束后更新设置页面显示。
    /// VB 原为非泛型 RedoableWorker，工作负载无返回值（结果写入 Configs.JavaList）；C# 按约定使用
    /// RedoableWorker&lt;List&lt;Java&gt;&gt;，工作负载返回 null，其 LastResult 无人使用。
    /// </summary>
    public static readonly RedoableWorker<List<Java>> JavaListRefreshWorker = new RedoableWorker<List<Java>>((c, p) => {
        JavaUtils.RefreshListAsync(c ?? default, p).Run();
        return null;
    });

    // VB 原为 WithEvents 字段 + Handles JavaListRefreshWorker.Started, JavaListRefreshWorker.Stopped，在 C# 中改为静态构造时订阅。
    static PclJava() {
        JavaListRefreshWorker.Started += UpdateJavaLists;
        JavaListRefreshWorker.Stopped += UpdateJavaLists;
    }

    #region UI 事件

    /// <summary>
    /// 更新设置页面的 Java 列表显示。
    /// </summary>
    public static void UpdateJavaLists() {
        PclCore.RunInUi(
        () => PclUiBridge.UpdateJavaListPages?.Invoke());
    }

    /// <summary>
    /// 手动触发导入 Java。
    /// </summary>
    public static void ManuallyImportJava() {
        // 选择
        string NewJavaPath = Dialogs.SelectFile("选择 java.exe（可能在 bin 文件夹中）", false, filter: "java.exe|java.exe").FirstOrDefault();
        if (string.IsNullOrEmpty(NewJavaPath)) return;
        // 验证 Java 可用
        Java NewJava = new Java(PathUtils.RemoveLastPart(NewJavaPath));
        if (!NewJava.CheckAsync().Run()) {
            PclUiBridge.ShowHint?.Invoke("该 Java 存在异常，无法使用", HintType.Red);
            return;
        }
        // 不再移除它
        Configs.JavaRemovedList.Edit((ref List<string> List) => List.RemoveIf(f => PathUtils.IsSame(f, NewJava.Folder)));
        // 加入列表顶部
        Configs.JavaList.Edit(
        (ref ConcurrentList<Java> List) => {
            List.Remove(NewJava);
            List.Insert(0, NewJava);
        });
        PclUiBridge.ShowHint?.Invoke($"已将 Java 加入列表：{NewJavaPath}", HintType.Green);
        UpdateJavaLists();
    }

    /// <summary>
    /// 手动从 Java 列表中移除指定的 Java，并记录到已移除列表中。
    /// </summary>
    public static void ManuallyRemoveJava(Java JavaEntry) {
        Configs.JavaList.Edit((ref ConcurrentList<Java> List) => List.Remove(JavaEntry)); // 从 Java 列表中移除
        Configs.JavaRemovedList.Edit(List => List.Append(JavaEntry.Folder).DistinctBy(s => s.Lower()).ToList()); // 记录已移除
        PclUiBridge.ShowHint?.Invoke($"已将 Java 移除出列表：{JavaEntry}", HintType.Green);
        UpdateJavaLists();
        PclUiBridge.ReloadCurrentJava?.Invoke(); // 如果强制指定了该 Java，这会把它自动重置掉
    }

    /// <summary>
    /// 手动触发搜索 Java，强制检查所有 Java。
    /// </summary>
    public static void ManuallySearchJava(CancellationToken CancellationToken = default) {
        PclCore.RunInThread(
        () => {
            Configs.JavaList.Get().ForAll(java => java.InvalidateChecked()); // 重置已检查的状态
            JavaListRefreshWorker.Start(cancellationToken: CancellationToken);
            JavaListRefreshWorker.WaitIfRunningAsync(cancellationToken: CancellationToken).Run(); // worker 开始/结束时会自动重载 UI
            if (Configs.JavaList.Get().Any()) {
                PclUiBridge.ShowHint?.Invoke("已找到 " + Configs.JavaList.Get().Count + " 个 Java！", HintType.Green);
            } else {
                PclUiBridge.ShowHint?.Invoke("未找到可用的 Java！", HintType.Red);
            }
        });
    }

    #endregion

    #region 选取

    /// <summary>
    /// 分析指定 Minecraft 版本的基础 Java 版本要求。不受设置影响。
    /// 如果由于 Java 版本要求冲突而导致没有可用的 Java，则返回 null。
    /// </summary>
    public static (ValueRange<Version> Range, string RecommendedComponent) GetJavaRequirement(McInstance Instance) {

        // 版本设置已经强制指定了 Java 版本要求
        if (PclCore.GetSetting<int>("VersionArgumentJavaV2", Instance) == 1) {
            try {
                ValueRange<Version> Range = ValueRange<Version>.FromString(PclCore.GetSetting<string>("VersionArgumentJavaRange", Instance), s => StringUtils.ParseVersionWithDefaults(s));
                return (Range, null);
            } catch (Exception) {
                Logger.Warn($"版本设置中指定的 Java 版本范围有误（{PclCore.GetSetting<string>("VersionArgumentJavaRange", Instance)}），这可能是因为首次修改此设置，尚未初始化");
            }
        }

        // ----------------------------------- 从 Minecraft 版本信息获取需求 -----------------------------------

        ValueRange<Version> JavaRange = ValueRange<Version>.All();
        Action<ValueRange<Version>> AddConstraint =
        Constraint => {
            JavaRange = JavaRange?.Intersect(Constraint);
            if (JavaRange.IsEmpty()) {
                Logger.Warn($"Java 版本要求冲突，若启动失败请在版本设置手动指定 Java：当前追加的要求为 {Constraint}，游戏版本为 {Instance.VersionDisplayName()}", LogBehavior.Toast);
                JavaRange = Constraint;
            }
        };

        // 原版 26-：通过版本号判断
        if ((!Instance.Version.Valid && Instance.ReleaseTime >= new DateTime(2024, 4, 2)) ||
           (Instance.Version.Valid && Instance.Version.Vanilla >= new Version(20, 0, 5))) {
            // 1.20.5+（24w14a+）：至少 Java 21
            AddConstraint(ValueRange<Version>.AtLeast(new Version(21, 0)));
        } else if ((!Instance.Version.Valid && Instance.ReleaseTime >= new DateTime(2021, 11, 16)) ||
                   (Instance.Version.Valid && Instance.Version.Vanilla.Major >= 18)) {
            // 1.18 pre2+：至少 Java 17
            AddConstraint(ValueRange<Version>.AtLeast(new Version(17, 0)));
        } else if ((!Instance.Version.Valid && Instance.ReleaseTime >= new DateTime(2021, 5, 11)) ||
                   (Instance.Version.Valid && Instance.Version.Vanilla.Major >= 17)) {
            // 1.17+ (21w19a+)：至少 Java 16
            AddConstraint(ValueRange<Version>.AtLeast(new Version(16, 0)));
        } else if (Instance.ReleaseTime.Year >= 2017) { // Minecraft 1.12 与 1.11 的分界线正好是 2017 年，太棒了
            // 1.12+：至少 Java 8
            AddConstraint(ValueRange<Version>.AtLeast(new Version(8, 0)));
        } else if (Instance.ReleaseTime <= new DateTime(2013, 5, 1) && Instance.ReleaseTime.Year >= 2001) { // 避免某些版本写个 1960 年
            // 1.5.2-：最高 Java 8
            AddConstraint(ValueRange<Version>.LessThan(new Version(9, 0)));
        }

        // 原版 26+：获取 Mojang 要求的 Java 版本
        string RecommendedComponent = null;
        int RecommendedCode =
            Instance.JsonObject?["javaVersion"]?["majorVersion"]?.ToObject<int>() ??
            Instance.JsonVersion?["java_version"]?.ToObject<int>() ?? 0;
        if (RecommendedCode >= 22) {
            Logger.Info("Mojang 要求至少使用 Java " + RecommendedCode);
            AddConstraint(ValueRange<Version>.AtLeast(new Version(RecommendedCode, 0)));
            RecommendedComponent =
                Instance.JsonObject?["javaVersion"]?["component"]?.ToString() ??
                Instance.JsonVersion?["java_component"]?.ToString();
            if (RecommendedComponent == "") RecommendedComponent = null;
        }

        // OptiFine 检测
        if (Instance.Version.HasOptiFine && Instance.Version.Valid) { // 不管非标准版本
            if (Instance.Version.Vanilla.Major < 7) {
                // <1.7：至多 Java 8
                AddConstraint(ValueRange<Version>.LessThan(new Version(9, 0)));
            } else if (Instance.Version.Vanilla.Major >= 8 && Instance.Version.Vanilla.Major < 12) {
                // 1.8 - 1.11：必须恰好 Java 8
                AddConstraint(ValueRange<Version>.ClosedOpen(new Version(8, 0), new Version(9, 0)));
            } else if (Instance.Version.Vanilla.Major == 12) {
                // 1.12：最高 Java 8
                AddConstraint(ValueRange<Version>.LessThan(new Version(9, 0)));
            }
        }

        // Forge 检测（部分测试结果见 #8432）
        if (Instance.Version.ModLoaders.HasFlagF(ModLoaders.Forge)) {
            if (Instance.Version.Vanilla >= new Version(6, 0, 1) && Instance.Version.Vanilla <= new Version(7, 0, 2)) {
                // 1.6.1 - 1.7.2：必须 Java 7
                AddConstraint(ValueRange<Version>.ClosedOpen(new Version(7, 0), new Version(8, 0)));
            } else if (Instance.Version.Vanilla.Major <= 12 || !Instance.Version.Valid) { // 非标准版本
                // <=1.12：Java 8
                AddConstraint(ValueRange<Version>.LessThan(new Version(9, 0)));
            } else if (Instance.Version.Vanilla.Major <= 14) {
                // 1.13 - 1.14：Java 8 - 10
                AddConstraint(ValueRange<Version>.ClosedOpen(new Version(8, 0), new Version(11, 0)));
            } else if (Instance.Version.Vanilla.Major == 15) {
                // 1.15：Java 8 - 15
                AddConstraint(ValueRange<Version>.ClosedOpen(new Version(8, 0), new Version(16, 0)));
            } else if (McUtils.CompareVersionGE(Instance.Version.ModLoaderVersions[ModLoaders.Forge], "34.0.0") && McUtils.CompareVersionGE("36.2.25", Instance.Version.ModLoaderVersions[ModLoaders.Forge])) {
                // 1.16.3~5，Forge 34.0.0 ~ 36.2.25：最高 Java 8u320
                AddConstraint(ValueRange<Version>.AtMost(new Version(8, 0, 320)));
            } else if (McUtils.CompareVersionGE(Instance.Version.ModLoaderVersions[ModLoaders.Forge], "36.2.26") && McUtils.CompareVersionGE("36.999999.999999", Instance.Version.ModLoaderVersions[ModLoaders.Forge])) {
                // 1.16.5，Forge 36.2.26+，<37.0.0：最高 Java 23
                AddConstraint(ValueRange<Version>.LessThan(new Version(24, 0)));
            } else if (McUtils.CompareVersionGE(Instance.Version.ModLoaderVersions[ModLoaders.Forge], "37.0.0") && McUtils.CompareVersionGE("37.0.79", Instance.Version.ModLoaderVersions[ModLoaders.Forge])) {
                // 1.17.1，Forge 37.0.0 ~ 37.0.79：最高 Java 16
                AddConstraint(ValueRange<Version>.LessThan(new Version(17, 0)));
            } else if (Instance.Version.Vanilla.Major == 18 && Instance.Version.HasOptiFine) { // #305
                // 1.18：若安装了 OptiFine，最高 Java 18
                AddConstraint(ValueRange<Version>.LessThan(new Version(19, 0)));
            } else if (McUtils.CompareVersionGE(Instance.Version.ModLoaderVersions[ModLoaders.Forge], "45.0.21") && McUtils.CompareVersionGE("45.0.65", Instance.Version.ModLoaderVersions[ModLoaders.Forge])) {
                // 1.19.4，Forge 45.0.21 ~ 45.0.65：最高 Java 19
                AddConstraint(ValueRange<Version>.LessThan(new Version(20, 0)));
            } else if (McUtils.CompareVersionGE(Instance.Version.ModLoaderVersions[ModLoaders.Forge], "45.0.66") && McUtils.CompareVersionGE("47.4.8", Instance.Version.ModLoaderVersions[ModLoaders.Forge])) {
                // 1.19.4~1.20.1，Forge 45.0.66 ~ 47.4.8：最高 Java 21
                AddConstraint(ValueRange<Version>.LessThan(new Version(22, 0)));
            }
        }

        // NeoForge 检测
        if (Instance.Version.ModLoaders.HasFlagF(ModLoaders.NeoForge)) {
            if (Instance.Version.Vanilla == new Version(20, 0, 1) ||
               (McUtils.CompareVersionGE("20.2.62-beta", Instance.Version.ModLoaderVersions[ModLoaders.NeoForge]) && !Instance.Version.ModLoaderVersions[ModLoaders.NeoForge].Contains("25w14craftmine"))) {
                // 1.20.1，以及 1.20.2 的 20.2.62-beta 之前：最高 Java 21
                AddConstraint(ValueRange<Version>.LessThan(new Version(22, 0)));
            }
        }

        // Fabric 检测
        if (Instance.Version.ModLoaders.HasFlagF(ModLoaders.Fabric) && Instance.Version.Valid) { // 不管非标准版本
            if (Instance.Version.Vanilla.Major >= 15 && Instance.Version.Vanilla.Major <= 16) {
                // 1.15 - 1.16：Java 8+
                AddConstraint(ValueRange<Version>.AtLeast(new Version(8, 0)));
            } else if (Instance.Version.Vanilla.Major >= 18) {
                // 1.18+：Java 17+
                AddConstraint(ValueRange<Version>.AtLeast(new Version(17, 0)));
            }
            if (Instance.Version.ModLoaderVersions[ModLoaders.Fabric] != "未知版本" && McUtils.CompareVersion(Instance.Version.ModLoaderVersions[ModLoaders.Fabric], "0.17.0") < 0) {
                // Fabric Loader 0.16.x 及更早版本的 Mixin/ASM 不兼容 Java 25
                AddConstraint(ValueRange<Version>.LessThan(new Version(25, 0)));
            }
        }

        // LiteLoader 检测
        if (Instance.Version.ModLoaders.HasFlagF(ModLoaders.LiteLoader) && Instance.Version.Valid) { // 不管非标准版本
            // 最高 Java 8
            AddConstraint(ValueRange<Version>.LessThan(new Version(9, 0)));
        }

        // 统一通行证检测
        // McLoginType 为 VB 枚举（Nide = 2），PCLCS 无法反向引用 VB 程序集，故按底层整数值比较。
        if (PclCore.GetSetting<int>("LoginType") == 2) {
            // 至少 Java 8u101
            AddConstraint(ValueRange<Version>.AtLeast(new Version(8, 0, 141)));
        }

        return (JavaRange, RecommendedComponent);
    }

    /// <summary>
    /// 根据版本或 Java 范围要求寻找适合的 Java。
    /// 若指定了 <paramref name="TryFixOrHint"/>，则会在找不到时会尝试下载 Java 或提示用户下载，否则只会静默运行。
    /// </summary>
    /// <returns>最适合的 Java。若找不到或失败则返回 null，除非取消否则不会抛出异常。</returns>
    public static Java SelectOrDownloadJava(OneOf<ValueRange<Version>, McInstance> Target, bool TryFixOrHint, CancellationToken c = default, ProgressProvider p = null) {
        try {
            if (Target.Is<McInstance>()) PclUiBridge.MigrateInstanceJavaSettings?.Invoke(Target.As<McInstance>(), c, p?.SplitTo(0.2)); // 迁移
            McInstance Instance = Target.Is<McInstance>() ? Target.As<McInstance>() : null;
            switch (Instance is null ? 0 : PclCore.GetSetting<int>("VersionArgumentJavaV2", Instance)) {

                case 2: // ============================= 使用版本文件夹中的 Java =============================

                    // 查找版本文件夹下的 Java
                    Logger.Info($"版本设置要求使用版本文件夹中的 Java：{Instance.PathVersion}");
                    Java JavaFound = JavaUtils.SearchFoldersAsync(true, new[] { Instance.PathVersion }, c, p?.SplitTo(1)).Run().FirstOrDefault();
                    if (JavaFound is not null) {
                        Logger.Info($"已发现版本文件夹中的 Java：{JavaFound}");
                        return JavaFound;
                    }
                    // 未能找到
                    if (TryFixOrHint) {
                        PclUiBridge.SwitchToInstanceSetup?.Invoke(Instance);
                        PclUiBridge.ShowJavaMsgBox?.Invoke($"该版本的版本设置选择了 {PclCore.vbLQ}使用版本文件夹中的 Java{PclCore.vbRQ}，但版本文件夹里没能找到 Java。{Environment.NewLine}请先修改启动选项中的 Java 设置，或在版本文件夹中放一个 Java，然后再试。",
                            "未找到 Java", true);
                    }
                    return null;

                case 3: // =============================== 强制指定特定 Java ===============================

                    Java ChosenJava = Configs.JavaForced.Get(Instance.Config);
                    Logger.Info($"版本设置中强制指定的 Java：{Instance.PathVersion} → {ChosenJava}");
                    if (ChosenJava is null) {
                        if (TryFixOrHint) {
                            PclUiBridge.SwitchToInstanceSetup?.Invoke(Instance);
                            PclUiBridge.ShowJavaMsgBox?.Invoke($"该版本的版本设置选择了 {PclCore.vbLQ}使用指定的 Java{PclCore.vbRQ}，但还没有指定任何 Java。{Environment.NewLine}请先选择你想使用的 Java，然后再试。",
                                "未选择 Java", true);
                        }
                        p?.Skip(); return null; // <==== 设置中未指定 Java
                    }
                    if (!Configs.JavaList.Get().Contains(ChosenJava)) {
                        Configs.JavaForced.Reset(Instance.Config);
                        UpdateJavaLists();
                        if (TryFixOrHint) {
                            PclUiBridge.SwitchToInstanceSetup?.Invoke(Instance);
                            Logger.Error("版本设置中指定的 Java 不在 Java 列表中，请重新选择一个 Java！", LogBehavior.Alert);
                        }
                        p?.Skip(); return null; // <==== 设置中指定的 Java 不在列表中
                    }
                    if (!ChosenJava.CheckAsync(c).Run()) {
                        Configs.JavaForced.Reset(Instance.Config);
                        UpdateJavaLists();
                        if (TryFixOrHint) {
                            PclUiBridge.SwitchToInstanceSetup?.Invoke(Instance);
                            Logger.Error("版本设置中指定的 Java 已无法使用，请重新选择一个 Java，然后再试。", LogBehavior.Alert);
                        } else {
                            Logger.Error("版本设置中指定的 Java 已无法使用，请重新选择一个 Java！", LogBehavior.Toast);
                        }
                        p?.Finish(); return null; // <==== 指定的 Java 存在异常
                    }
                    p?.Finish(); return ChosenJava; // <==== 成功找到并检查通过

                default: // ======================== 根据版本范围自动选择 JavaList 中的 Java ========================

                    // 获取范围
                    ValueRange<Version> Range =
                        Target.Is<McInstance>() ? GetJavaRequirement(Instance).Range : Target.As<ValueRange<Version>>();
                    string RecommendedComponent =
                        Target.Is<McInstance>() ? GetJavaRequirement(Instance).RecommendedComponent : null;
                    Logger.Info($"自动选择以下范围的 Java：{Range} {RecommendedComponent}");
                    if (Range.IsEmpty()) throw new Exception("Java 版本需求存在冲突，导致没有任何可能适配的 Java，请查看 PCL 的日志了解详细信息！");

                    // 搜索 Java
                    Func<Java> SelectJavaFromJavaList =
                    () => {
                        JavaListRefreshWorker.WaitIfRunning(); // 等待进行中的搜索结束
                        foreach (Java ChosenJava in Configs.JavaList.Get()) {
                            if (!ChosenJava.CheckAsync(c).Run()) { // 检查
                                Logger.Warn($"Java 检查失败，将尝试列表中的下一个 Java：{ChosenJava}");
                                continue;
                            }
                            if (ChosenJava.Version is null || !Range.Contains(ChosenJava.Version)) continue; // 版本范围
                            Logger.Info($"已选定 Java：{ChosenJava}");
                            return ChosenJava;
                        }
                        return null;
                    };
                    p?.Set(0.25);
                    Java Result = SelectJavaFromJavaList();
                    if (Result is not null) { p?.Skip(); return Result; }

                    lock (JavaLock) { // #3797
                        // 既有列表中没有合适的 Java，重新触发搜索
                        Logger.Warn("既有列表中没有合适的 Java，重新触发搜索");
                        JavaListRefreshWorker.Start(c, p?.SplitTo(0.3));
                        Result = SelectJavaFromJavaList();
                        if (Result is not null) { p?.Skip(); return Result; }
                        if (!TryFixOrHint) return null;

                        // 电脑中没有合适的 Java，获取 Mojang 提供的 Java 下载列表
                        Logger.Warn("电脑中没有合适的 Java，开始获取 Mojang 提供的 Java 下载列表");
                        if (!DownloadListWorker.HasSucceeded) DownloadListWorker.Start(c, p?.SplitTo(0.4));
                        DownloadListWorker.WaitIfRunning(cancellationToken: c);
                        p?.Set(0.4, skiped: true);
                        List<JavaDownloadInfo> Components = DownloadListWorker.LastResult;

                        // 确定要下载的目标 Java
                        JavaDownloadInfo JavaToDownload = Components.FirstOrDefault(
                            Comp => Comp.Java["version"]["name"].ToString() == RecommendedComponent, // 优先选择指定的 Component
                            Comp => Range.Contains(Comp.JavaVersion));
                        if (JavaToDownload is null) {
                            Logger.Warn("Mojang 提供的 Java 列表中没有合适的 Java，无法自动下载");
                            // 提示需要手动下载 Java
                            string Description;
                            bool HasPureLower = Range.HasLower && Range.IsLowerInclusive &&
                            Range.Lower.Minor <= 0 && Range.Lower.Build <= 0 && Range.Lower.Revision <= 0;
                            bool HasPureUpper = Range.HasUpper && !Range.IsUpperInclusive &&
                            Range.Upper.Minor <= 0 && Range.Upper.Build <= 0 && Range.Upper.Revision <= 0;
                            if (Range.HasLower && Range.HasUpper) { // 有上下界
                                if (HasPureLower && HasPureUpper) {
                                    if (Range.Lower.Major == Range.Upper.Major - 1) {
                                        Description = $" Java {Range.Lower.Major}";
                                    } else {
                                        Description = $" Java {Range.Lower.Major} ~ {Range.Upper.Major - 1}";
                                    }
                                } else {
                                    Description = $" Java {Range}"; // 摆烂
                                }
                            } else if (Range.HasLower) { // 只有下界
                                if (HasPureLower) {
                                    Description = $" Java {Range.Lower.Major} 或更高版本的 Java";
                                } else if (Range.IsLowerInclusive) {
                                    Description = $" Java {Range.Lower} 或更高版本的 Java";
                                } else {
                                    Description = $"版本高于 {Range.Lower} 的 Java";
                                }
                            } else { // 只有上界
                                if (HasPureUpper) {
                                    Description = $" Java {Range.Upper.Major - 1} 或更低版本的 Java";
                                } else if (Range.IsUpperInclusive) {
                                    Description = $" Java {Range.Upper} 或更低版本的 Java";
                                } else {
                                    Description = $"版本低于 {Range.Upper} 的 Java";
                                }
                            }
                            PclUiBridge.ShowJavaMsgBox?.Invoke($"PCL 没能找到合适的 Java。{Environment.NewLine}请在网上搜索并手动安装{Description}， 然后在【设置 → 启动 → 高级选项 → Java 列表】中导入它。", "需要手动安装 Java", false);
                            return null;
                        }

                        // 自动下载 Java
                        LoaderCombo<int> JavaLoader = CreateJavaDownloadLoader(JavaToDownload);
                        try {
                            JavaLoader.WaitForExit(IsForceRestart: true, c: c, p: p?.SplitTo(0.95));
                        } finally {
                            JavaLoader.Cancel(); // 确保取消时中止 Java 下载
                        }
                        Result = SelectJavaFromJavaList();
                        if (Result is not null) { p?.Finish(); return Result; }
                        throw new Exception($"下载 Java {JavaToDownload.ComponentName} 后仍然无法找到合适的 Java");
                    }
            }
        } catch (Exception ex) {
            ex.ThrowIfCanceled();
            try {
                System.IO.File.WriteAllText(Paths.Base + "PCL\\java_error.txt", ex.ToString()); // 兜底落盘，绕过日志系统
            } catch {
            }
            Logger.Error(ex, "选择 Java 时出现异常", LogBehavior.Toast);
            p?.Skip(); return null;
        }
    }
    private static readonly object JavaLock = new object();

    // 由 SelectOrDownloadJava 中的 Static 局部变量迁移而来（VB Static 在所有调用间共享）。
    private static readonly RedoableWorker<List<JavaDownloadInfo>> DownloadListWorker = new RedoableWorker<List<JavaDownloadInfo>>(
    cancellationToken => {
        // 下载 Mojang 提供的 Java 列表
        string IndexFileStr = NetCore.NetRequestByLoader(PclDownload.DlVersionListOrder(
            new[] { "https://piston-meta.mojang.com/v1/products/java-runtime/2ec0cc96c44e5a76b9c8b7c39df7210883d12871/all.json" },
            new[] { "https://bmclapi2.bangbang93.com/v1/products/java-runtime/2ec0cc96c44e5a76b9c8b7c39df7210883d12871/all.json" }
        ), IsJson: true);
        cancellationToken?.ThrowIfCancellationRequested();
        // 解析 Java 列表
        List<JavaDownloadInfo> Results = new List<JavaDownloadInfo>();
        foreach (JProperty Prop in IndexFileStr.DeserializeJson<JObject>()["windows-x64"]) {
            cancellationToken?.ThrowIfCancellationRequested();
            if (Prop.Name == "minecraft-java-exe") continue; // 不是完整 Java
            try {
                JToken Entry = Prop.Value.FirstOrDefault();
                if (Entry is null) continue;
                string JavaName = Entry["version"]["name"].ToString(); // 可能的名称格式："12"、"8u51-cacert462b08"、"17.0.8.1"、"17.0.1.12.1"、"21.0.7"
                if (JavaName.Contains(".")) {
                    string[] Parts = JavaName.Split('.');
                    Results.Add(new JavaDownloadInfo { ComponentName = Prop.Name, Java = (JObject)Entry, JavaVersion = new Version(Convert.ToInt32(PclCore.Val(Parts[0])), Convert.ToInt32(PclCore.Val(Parts[1])), Parts.Length >= 3 ? Convert.ToInt32(PclCore.Val(Parts[2])) : 0, Parts.Length >= 4 ? Convert.ToInt32(PclCore.Val(Parts[3])) : 0) });
                } else if (JavaName.StartsWithF("8u51-")) {
                    Results.Add(new JavaDownloadInfo { ComponentName = Prop.Name, Java = (JObject)Entry, JavaVersion = new Version(8, 0, 51, 0) });
                } else {
                    Results.Add(new JavaDownloadInfo { ComponentName = Prop.Name, Java = (JObject)Entry, JavaVersion = new Version(Convert.ToInt32(PclCore.Val(JavaName.BeforeFirstOfAny(new[] { "u", "." }))), 0, 0, 0) });
                }
            } catch (Exception ex) {
                Logger.Warn(ex, $"解析 Mojang 提供的 Java 列表项失败，已跳过（{Prop.Name}）");
            }
        }
        return Results;
    });

    #endregion

    #region 下载（均为内部工具方法，无外部调用）

    /// <summary>
    /// 构建下载指定 Java 的加载器。
    /// </summary>
    private static LoaderCombo<int> CreateJavaDownloadLoader(JavaDownloadInfo Target) {

        // 分析需要下载的 Java 文件列表
        string Address = (string)Target.Java["manifest"]["url"];
        Logger.Info($"准备下载 Java {Target.Java["version"]["name"]}（{Target.ComponentName}）：{Address}");
        string ListFileStr = NetCore.NetRequestByLoader(PclDownload.DlSourceOrder(new[] { Address }, new[] { Address.Replace("piston-meta.mojang.com", "bmclapi2.bangbang93.com") }), IsJson: true);
        string BaseDir = $"{Paths.AppData}.minecraft\\runtime\\{Target.ComponentName}\\";
        List<NetFile> FilesToDownload = new List<NetFile>();
        foreach (JProperty File in ((JObject)ListFileStr.DeserializeJson())["files"]) {
            if (((JObject)File.Value)["downloads"]?["raw"] == null) continue;
            JObject Info = (JObject)((JObject)File.Value)["downloads"]["raw"];
            FileChecker Checker = new FileChecker { ActualSize = (long)Info["size"], Hash = (string)Info["sha1"] };
            if (Checker.Hash == "12976a6c2b227cbac58969c1455444596c894656" || Checker.Hash == "c80e4bab46e34d02826eab226a4441d0970f2aba" || Checker.Hash == "84d2102ad171863db04e7ee22a259d1f6c5de4a5") {
                continue; // 跳过 3 个无意义大量重复文件（#3827）
            }
            string Url = (string)Info["url"];
            FilesToDownload.Add(new NetFile(
                PclDownload.DlSourceOrder(new[] { Url }, new[] { Url.Replace("piston-data.mojang.com", "bmclapi2.bangbang93.com") }),
                BaseDir + File.Name, Checker));
        }
        Logger.Info($"该 Java 需要下载 {FilesToDownload.Count} 个文件，目标文件夹：{BaseDir}");

        // 构建加载器
        LoaderCombo<int> Loader = new LoaderCombo<int>("下载 Java", new List<LoaderBase> {
            new LoaderDownload("下载 Java 文件", FilesToDownload) { ProgressWeight = 10 },
            new LoaderWorker("刷新 Java 列表", JavaListRefreshWorker)
        }) { OnStateChanged =
        RawLoader => {
            if (RawLoader.State == LoadState.Failed || RawLoader.State == LoadState.Canceled) {
                Logger.Warn($"由于 Java 下载未完成，清理未下载完成的 Java 文件夹：{BaseDir}");
                DirectoryUtils.Delete(BaseDir);
            }
        } };
        return Loader;

    }

    #endregion

}

/// <summary>
/// 由 ModJava 中 SelectOrDownloadJava 使用的嵌套类迁移而来（VB 原为模块内 Private 嵌套类，仅模块内使用）。
/// </summary>
internal class JavaDownloadInfo {
    public string ComponentName { get; set; }
    public JObject Java { get; set; }
    public Version JavaVersion { get; set; }
}
