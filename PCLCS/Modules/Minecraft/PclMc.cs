// 由 VB Modules\Minecraft\ModMinecraft.vb 的纯逻辑部分迁移而来。
// VB UI 依赖（McFolderSelected、McInstanceSelected、Hint、MyMsgBox、页面跳转、AniControl）经 PclUiBridge 桥接，
// 设置读写经 PclCore.SettingProvider/SettingSetter 桥接，ExtractResources 经 PclUiBridge 桥接。
// VB 侧保留 McFolderSelected、SetMcFolder、McFolderListLoadSub、McInstanceSelected、ToListItem、McSkinSelect、
// McDownloadClientUpdateHint、McInstanceListLoader（AddressOf InitMcInstanceList 的 VB Private 转发壳），
// 被迁移的 Public 成员在 VB 保留同名转发壳，确保 VB 调用点无需修改。
#nullable disable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using MeloongCore;
using MeloongCore.Extensions;
using Microsoft.VisualBasic.CompilerServices;
using Newtonsoft.Json.Linq;

namespace PCLCS.Modules;

public static class PclMc {

    #region 文件夹

    /// <summary>
    /// 为 Minecraft 文件夹创建 launcher_profiles.json 文件。
    /// </summary>
    public static void McFolderLauncherProfilesJsonCreate(string Folder) {
        try {
            if (FileUtils.Exists(Folder + "launcher_profiles.json")) return;
            string ResultJson = "{\r\n" +
                "    \"profiles\":  {\r\n" +
                "        \"PCL\": {\r\n" +
                "            \"icon\": \"Grass\",\r\n" +
                "            \"name\": \"PCL\",\r\n" +
                "            \"lastVersionId\": \"latest-release\",\r\n" +
                "            \"type\": \"latest-release\",\r\n" +
                "            \"lastUsed\": \"" + DateTime.Now.ToString("yyyy'-'MM'-'dd") + "T" + DateTime.Now.ToString("HH':'mm':'ss") + ".0000Z\"\r\n" +
                "        }\r\n" +
                "    },\r\n" +
                "    \"selectedProfile\": \"PCL\",\r\n" +
                "    \"clientToken\": \"23323323323323323323323323323333\"\r\n" +
                "}";
            FileUtils.Write(Folder + "launcher_profiles.json", ResultJson, Encoding.GetEncoding("GB18030"));
            Logger.Info($"已创建 launcher_profiles.json：{Folder}");
        } catch (Exception ex) {
            Logger.Error(ex, $"创建 launcher_profiles.json 失败（{Folder}）");
        }
    }

    #endregion

    #region 版本处理

    /// <summary>
    /// 根据版本名获取对应的愚人节版本描述。非愚人节版本会返回空字符串。
    /// </summary>
    public static string GetMcFoolName(string Name) {
        Name = Name.Lower();
        if (Name.StartsWithF("2.0")) {
            return "2013 | 这个秘密计划了两年的更新将游戏推向了一个新高度！";
        } else if (Name == "15w14a") {
            return "2015 | 作为一款全年龄向的游戏，我们需要和平，需要爱与拥抱。";
        } else if (Name == "1.rv-pre1") {
            return "2016 | 是时候将现代科技带入 Minecraft 了！";
        } else if (Name == "3d shareware v1.34") {
            return "2019 | 我们从地下室的废墟里找到了这个开发于 1994 年的杰作！";
        } else if (Name.StartsWithF("20w14inf") || Name == "20w14∞") {
            return "2020 | 我们加入了 20 亿个新的维度，让无限的想象变成了现实！";
        } else if (Name == "22w13oneblockatatime") {
            return "2022 | 一次一个方块更新！迎接全新的挖掘、合成与骑乘玩法吧！";
        } else if (Name == "23w13a_or_b") {
            return "2023 | 研究表明：玩家喜欢作出选择——越多越好！";
        } else if (Name == "24w14potato") {
            return "2024 | 毒马铃薯一直都被大家忽视和低估，于是我们超级加强了它！";
        } else if (Name == "25w14craftmine") {
            return "2025 | 你可以合成任何东西——包括合成你的世界！";
        } else if (Name == "26w14a") {
            return "2026 | 为什么需要物品栏？让方块们跟着你走吧！";
        } else {
            return "";
        }
    }

    /// <summary>
    /// 当前按卡片分类的所有版本列表。
    /// </summary>
    public static Dictionary<McInstanceCardType, List<McInstance>> McInstanceList = new Dictionary<McInstanceCardType, List<McInstance>>();

    #endregion

    #region 版本列表加载

    /// <summary>
    /// 是否要求本次加载强制刷新版本列表。
    /// </summary>
    public static bool McInstanceListForceRefresh = false;
    /// <summary>
    /// 是否为本次打开 PCL 后第一次加载版本列表。
    /// 这会清理所有 .pclignore 文件，而非跳过这些对应版本。
    /// </summary>
    private static bool IsFirstMcInstanceListLoad = true;

    /// <summary>
    /// 加载 Minecraft 文件夹的版本列表。
    /// </summary>
    public static void InitMcInstanceList(LoaderTask<string, int> Loader) {
        // 开始加载
        string PathMc = Loader.Input;
        try {
            // 初始化
            McInstanceList = new Dictionary<McInstanceCardType, List<McInstance>>();

            // 检测缓存是否需要更新
            List<string> FolderList = new List<string>();
            if (DirectoryUtils.Exists(PathMc + "versions")) { // 不要使用 CheckPermission，会导致写入时间改变，从而使得文件夹被强制刷新
                try {
                    foreach (DirectoryInfo Folder in DirectoryUtils.GetInfo(PathMc + "versions").GetDirectories()) {
                        FolderList.Add(Folder.Name);
                    }
                } catch (Exception ex) {
                    throw new Exception("无法读取版本文件夹，可能是由于没有权限（" + PathMc + "versions）", ex);
                }
            }
            // 不可用
            if (!FolderList.Any()) {
                PclCore.WriteIni(PathMc + "PCL.ini", "InstanceCache", ""); // 清空缓存
                goto OnLoaded;
            }
            // 有可用版本
            int FolderListCheck = (int)decimal.Remainder((decimal)((Versions.McInstanceCacheVersion + "#" + FolderList.Join("#")).GetStableHashCode()), int.MaxValue - 1); // 根据文件夹名列表生成辨识码
            bool NeedReload = true;
            if (!McInstanceListForceRefresh && PclCore.Val(PclCore.ReadIni(PathMc + "PCL.ini", "InstanceCache")) == FolderListCheck) {
                // 可以使用缓存
                Dictionary<McInstanceCardType, List<McInstance>> Result = InitMcInstanceListWithCache(PathMc);
                if (Result is not null) {
                    McInstanceList = Result;
                    NeedReload = false;
                }
            }
            // 原 VB 使用 GoTo Reload 从缓存失败分支跳入 Else 块，C# 不允许跨分支跳转，改为标志位重构
            if (NeedReload) {
                // 文件夹列表不符
                McInstanceListForceRefresh = false;
                Logger.Info("文件夹列表变更，重载所有版本");
                PclCore.WriteIni(PathMc + "PCL.ini", "InstanceCache", FolderListCheck.ToString());
                McInstanceList = InitMcInstanceListWithoutCache(PathMc);
            }
            IsFirstMcInstanceListLoad = false;

            // 改变当前选择的版本
        OnLoaded:
            if (Loader.IsCanceled) return;
            if (McInstanceList.Any(v => v.Key != McInstanceCardType.Error)) {
                // 尝试读取已储存的选择
                string SavedSelection = PclCore.ReadIni(PathMc + "PCL.ini", "Version");
                if (SavedSelection != "") {
                    foreach (KeyValuePair<McInstanceCardType, List<McInstance>> Card in McInstanceList) {
                        foreach (McInstance Instance in Card.Value) {
                            if (Instance.Name == SavedSelection && Instance.State != McInstance.McInstanceState.Error) {
                                // 使用已储存的选择
                                PclUiBridge.McInstanceSelectedSet?.Invoke(Instance);
                                Logger.Info($"选择该文件夹储存的 Minecraft 版本：{PclUiBridge.McInstanceSelectedGet().PathVersion}");
                                return;
                            }
                        }
                    }
                }
                if (McInstanceList.First().Value[0].State != McInstance.McInstanceState.Error) {
                    // 自动选择第一项
                    PclUiBridge.McInstanceSelectedSet?.Invoke(McInstanceList.First().Value[0]);
                    Logger.Info($"自动选择 Minecraft 版本：{PclUiBridge.McInstanceSelectedGet().PathVersion}");
                }
            } else {
                PclUiBridge.McInstanceSelectedSet?.Invoke(null);
                Logger.Info("未找到可用 Minecraft 版本");
            }
            if (PclCore.GetSetting<bool>("SystemDebugDelay")) Thread.Sleep(PclCore.RandomInteger(200, 3000));
        } catch (Exception ex) {
            if (Loader.IsCanceled || ex.IsCanceled()) return; // #5617
            PclCore.WriteIni(PathMc + "PCL.ini", "InstanceCache", ""); // 要求下次重新加载
            Logger.Error(ex, "加载 .minecraft 版本列表失败");
        }
    }

    // 获取版本列表
    private static Dictionary<McInstanceCardType, List<McInstance>> InitMcInstanceListWithCache(string Folder) {
        Dictionary<McInstanceCardType, List<McInstance>> Results = new Dictionary<McInstanceCardType, List<McInstance>>();
        try {
            int CardCount = Conversions.ToInteger(PclCore.ReadIni(Folder + "PCL.ini", "CardCount", "-1"));
            if (CardCount == -1) return null;
            for (int i = 0; i <= CardCount - 1; i++) {
                McInstanceCardType CardType = (McInstanceCardType)Conversions.ToInteger(PclCore.ReadIni(Folder + "PCL.ini", "CardKey" + (i + 1), ":"));
                List<McInstance> InstanceList = new List<McInstance>();

                // 循环读取版本
                foreach (string VersionName in PclCore.ReadIni(Folder + "PCL.ini", "CardValue" + (i + 1), ":").Split(":")) {
                    if (VersionName == "") continue;
                    string FolderVersions = $"{Folder}versions\\{VersionName}\\";
                    if (FileUtils.Exists(FolderVersions + ".pclignore")) {
                        if (IsFirstMcInstanceListLoad) {
                            Logger.Info($"清理残留的忽略项目：{FolderVersions}"); // #2781
                            FileUtils.Delete(FolderVersions + ".pclignore");
                        } else {
                            Logger.Info($"跳过要求忽略的项目：{FolderVersions}");
                            continue;
                        }
                    }
                    try {

                        // 读取单个版本
                        McInstance Instance = new McInstance(FolderVersions);
                        InstanceList.Add(Instance);
                        Instance.Info = PclCore.ReadIni(Instance.PathVersion + "PCL\\Setup.ini", "CustomInfo", "");
                        if (Instance.Info == "") Instance.Info = PclCore.ReadIni(Instance.PathVersion + "PCL\\Setup.ini", "Info", Instance.Info);
                        Instance.Logo = PclCore.ReadIni(Instance.PathVersion + "PCL\\Setup.ini", "Logo", Instance.Logo);
                        Instance.State = (McInstance.McInstanceState)Conversions.ToInteger(PclCore.ReadIni(Instance.PathVersion + "PCL\\Setup.ini", "State", ((int)Instance.State).ToString()));
                        Instance.IsStar = Conversions.ToBoolean(PclCore.ReadIni(Instance.PathVersion + "PCL\\Setup.ini", "IsStar", false.ToString()));
                        Instance.DisplayType = (McInstanceCardType)Conversions.ToInteger(PclCore.ReadIni(Folder + "PCL\\Setup.ini", "DisplayType", ((int)McInstanceCardType.Auto).ToString()));
                        McInstallManifest Manifest = Instance.State == McInstance.McInstanceState.Error ? null : Configs.McInstallManifest.Get(Instance.Config);
                        // 检查 manifest 是否有过期文件，并进行更新
                        if (Manifest is not null && Manifest.SchemaVersion <= Versions.McInstallManifestVersion && Manifest.ManagedFiles.Any(File => File.Component is not null)) {
                            McInstallManifest LatestManifest = Manifest.RefreshModComponents(Instance.PathIndie);
                            if (LatestManifest != Manifest) {
                                Manifest = LatestManifest;
                                Configs.McInstallManifest.Set(Manifest, Instance.Config);
                                Instance.Config.Save();
                            }
                        }
                        string CachedVanillaName = PclCore.ReadIni(Instance.PathVersion + "PCL\\Setup.ini", "VersionVanillaName", "Unknown");
                        if (Instance.State != McInstance.McInstanceState.Error && (Manifest is not null || CachedVanillaName != "Unknown")) {
                            McVersion Version;
                            if (Manifest is not null) {
                                // 从 manifest 中读取版本信息
                                Version = new McVersion(Manifest.Components) {
                                    OptiFineAsMod = Manifest.ManagedFiles.Any(File => File.Component == "OptiFine")
                                };
                            } else {
                                // 从 Setup.ini 中读取版本信息
                                string ReadCachedInfo(string Key) {
                                    string Value = PclCore.ReadIni(Instance.PathVersion + "PCL\\Setup.ini", Key, null);
                                    return Value == "" ? null : Value; // 对老版本 Setup.ini 的空值兼容
                                }
                                Version = new McVersion(CachedVanillaName) { OptiFine = ReadCachedInfo("VersionOptiFine") };
                                foreach (ModLoaders Loader in new[] { ModLoaders.Fabric, ModLoaders.Forge, ModLoaders.NeoForge }) {
                                    string CachedVersion = ReadCachedInfo($"Version{Loader}");
                                    if (CachedVersion is not null) Version.ModLoaderVersions[Loader] = CachedVersion;
                                }
                                if (Conversions.ToBoolean(PclCore.ReadIni(Instance.PathVersion + "PCL\\Setup.ini", "VersionLiteLoader", false.ToString()))) Version.ModLoaderVersions[ModLoaders.LiteLoader] = "未知版本";
                            }
                            Version.ReleaseTime = Conversions.ToDate(PclCore.ReadIni(Instance.PathVersion + "PCL\\Setup.ini", "ReleaseTime", new DateTime(1970, 1, 1, 15, 0, 0).ToString()));
                            Version.IsFool = Version.IsFool || Instance.State == McInstance.McInstanceState.Fool;
                            Version.IsSnapshot = Version.IsSnapshot || Instance.State == McInstance.McInstanceState.Snapshot || Instance.Name.ContainsIgnoreCase("combat");
                            Instance.Version = Version;
                            if (Manifest is not null) Instance.RefreshInfo();
                        } else {
                            Instance.Load();
                        }

                        // 重新检查错误版本
                        if (Instance.State == McInstance.McInstanceState.Error) {
                            // 重新尝试获取版本信息
                            string OldDesc = Instance.Info;
                            Instance.State = McInstance.McInstanceState.Original;
                            Instance.Check();
                            // 校验错误原因是否改变
                            string CustomInfo = PclCore.ReadIni(Instance.PathVersion + "PCL\\Setup.ini", "CustomInfo");
                            if (Instance.State == McInstance.McInstanceState.Original || (CustomInfo == "" && OldDesc != Instance.Info)) {
                                Logger.Info($"版本 {Instance.Name} 的错误状态已变更，新的状态为：{Instance.Info}");
                                return null;
                            }
                        }

                        // 校验未加载的版本
                        if (Instance.Logo == "") {
                            Logger.Info($"版本 {Instance.Name} 未被加载");
                            return null;
                        }

                    } catch (Exception ex) {
                        Logger.Warn(ex, $"读取版本加载缓存失败（{VersionName}）");
                        return null;
                    }
                }

                if (InstanceList.Any()) Results.Add(CardType, InstanceList);
            }
            return Results;
        } catch (Exception ex) {
            Logger.Warn(ex, "读取版本缓存失败");
            return null;
        }
    }
    private static Dictionary<McInstanceCardType, List<McInstance>> InitMcInstanceListWithoutCache(string Folder) {
        List<McInstance> InstanceList = new List<McInstance>();

        #region 循环加载每个版本的信息
        foreach (DirectoryInfo VersionFolderInfo in DirectoryUtils.GetInfo(Folder + "versions").GetDirectories()) {
            if (!VersionFolderInfo.Exists || !VersionFolderInfo.EnumerateFiles().Any()) {
                Logger.Info($"跳过空文件夹：{VersionFolderInfo.FullName}");
                continue;
            }
            if ((VersionFolderInfo.Name == "cache" || VersionFolderInfo.Name == "BLClient" || VersionFolderInfo.Name == "PCL") &&
                !FileUtils.Exists(VersionFolderInfo.FullName + "\\" + VersionFolderInfo.Name + ".json")) {
                Logger.Info($"跳过可能不是版本文件夹的项目：{VersionFolderInfo.FullName}");
                continue;
            }
            string VersionFolder = VersionFolderInfo.FullName + "\\";
            if (FileUtils.Exists(VersionFolder + ".pclignore")) {
                if (IsFirstMcInstanceListLoad) {
                    Logger.Info($"清理残留的忽略项目：{VersionFolder}"); // #2781
                    FileUtils.Delete(VersionFolder + ".pclignore");
                } else {
                    Logger.Info($"跳过要求忽略的项目：{VersionFolder}");
                    continue;
                }
            }
            McInstance Instance = new McInstance(VersionFolder);
            InstanceList.Add(Instance);
            Instance.Load();
        }
        #endregion

        Dictionary<McInstanceCardType, List<McInstance>> Results = new Dictionary<McInstanceCardType, List<McInstance>>();

        #region 将版本分类到各个卡片
        try {

            // 未经过自定义的版本列表
            Dictionary<McInstanceCardType, List<McInstance>> InstanceListOriginal = new Dictionary<McInstanceCardType, List<McInstance>>();

            // 单独列出收藏的版本
            List<McInstance> InstanceStar = new List<McInstance>();
            foreach (McInstance Instance in InstanceList.ToList()) {
                if (!Instance.IsStar) continue;
                if (Instance.DisplayType == McInstanceCardType.Hidden) continue;
                InstanceStar.Add(Instance);
                InstanceList.Remove(Instance);
            }
            if (InstanceStar.Any()) InstanceListOriginal.Add(McInstanceCardType.Star, InstanceStar);

            // 预先筛选出愚人节和错误的版本
            McInstanceFilter(ref InstanceList, ref InstanceListOriginal, new[] { McInstance.McInstanceState.Error }, McInstanceCardType.Error);
            McInstanceFilter(ref InstanceList, ref InstanceListOriginal, new[] { McInstance.McInstanceState.Fool }, McInstanceCardType.Fool);

            // 筛选 API 版本
            McInstanceFilter(ref InstanceList, ref InstanceListOriginal, new[] { McInstance.McInstanceState.Forge, McInstance.McInstanceState.NeoForge, McInstance.McInstanceState.LiteLoader, McInstance.McInstanceState.Fabric }, McInstanceCardType.API);

            // 将老版本预先分类入不常用，只剩余原版、快照、OptiFine
            List<McInstance> InstanceUseful = new List<McInstance>();
            List<McInstance> InstanceRubbish = new List<McInstance>();
            McInstanceFilter(ref InstanceList, new[] { McInstance.McInstanceState.Old }, ref InstanceRubbish);

            // 确认最新版本，若为快照则加入常用列表
            McInstance LargestInstance = InstanceList.
                Where(v => v.State == McInstance.McInstanceState.Original || v.State == McInstance.McInstanceState.Snapshot).
                MaxBy(v => v.ReleaseTime);
            if (LargestInstance is not null && LargestInstance.State == McInstance.McInstanceState.Snapshot) {
                InstanceUseful.Add(LargestInstance);
                InstanceList.Remove(LargestInstance);
            }

            // 将剩余的快照全部拖进不常用列表
            McInstanceFilter(ref InstanceList, new[] { McInstance.McInstanceState.Snapshot }, ref InstanceRubbish);

            // 获取每个 Drop 下最新的原版与 OptiFine
            Dictionary<string, McInstance> NewerInstance = new Dictionary<string, McInstance>();
            List<int> ExistDrops = new List<int>();
            foreach (McInstance Instance in InstanceList) {
                if (!Instance.Version.Valid) continue;
                if (!ExistDrops.Contains(Instance.Version.Drop)) ExistDrops.Add(Instance.Version.Drop);
                string Key = Instance.Version.Drop + "-" + Instance.State;
                if (!NewerInstance.ContainsKey(Key)) {
                    NewerInstance.Add(Key, Instance);
                    continue;
                }
                if (Instance.State == McInstance.McInstanceState.OptiFine) {
                    if (Instance.Version.OptiFineCode > NewerInstance[Key].Version.OptiFineCode) NewerInstance[Key] = Instance; // OptiFine 根据版本号判断
                } else {
                    if (Instance.ReleaseTime > NewerInstance[Key].ReleaseTime) NewerInstance[Key] = Instance; // 原版根据发布时间判断
                }
            }

            // 将每个 Drop 下的最常规版本加入
            foreach (int Drop in ExistDrops) {
                if (NewerInstance.ContainsKey(Drop + "-" + McInstance.McInstanceState.OptiFine) && NewerInstance.ContainsKey(Drop + "-" + McInstance.McInstanceState.Original)) {
                    // 同时存在 OptiFine 与原版
                    McInstance VanillaInstance = NewerInstance[Drop + "-" + McInstance.McInstanceState.Original];
                    McInstance OptiFineInstance = NewerInstance[Drop + "-" + McInstance.McInstanceState.OptiFine];
                    if (VanillaInstance.Version.Drop > OptiFineInstance.Version.Drop) {
                        // 仅在原版比 OptiFine 更新时才加入原版
                        InstanceUseful.Add(VanillaInstance);
                        InstanceList.Remove(VanillaInstance);
                    }
                    InstanceUseful.Add(OptiFineInstance);
                    InstanceList.Remove(OptiFineInstance);
                } else if (NewerInstance.ContainsKey(Drop + "-" + McInstance.McInstanceState.OptiFine)) {
                    // 没有原版，直接加入 OptiFine
                    InstanceUseful.Add(NewerInstance[Drop + "-" + McInstance.McInstanceState.OptiFine]);
                    InstanceList.Remove(NewerInstance[Drop + "-" + McInstance.McInstanceState.OptiFine]);
                } else if (NewerInstance.ContainsKey(Drop + "-" + McInstance.McInstanceState.Original)) {
                    // 没有 OptiFine，直接加入原版
                    InstanceUseful.Add(NewerInstance[Drop + "-" + McInstance.McInstanceState.Original]);
                    InstanceList.Remove(NewerInstance[Drop + "-" + McInstance.McInstanceState.Original]);
                }
            }

            // 将剩余的东西添加进去
            InstanceRubbish.AddRange(InstanceList);
            if (InstanceUseful.Any()) InstanceListOriginal.Add(McInstanceCardType.OriginalLike, InstanceUseful);
            if (InstanceRubbish.Any()) InstanceListOriginal.Add(McInstanceCardType.Rubbish, InstanceRubbish);

            // 按照自定义版本分类重新添加
            foreach (KeyValuePair<McInstanceCardType, List<McInstance>> InstancePair in InstanceListOriginal) {
                foreach (McInstance Instance in InstancePair.Value) {
                    McInstanceCardType RealType = Instance.DisplayType == 0 || InstancePair.Key == McInstanceCardType.Star ? InstancePair.Key : Instance.DisplayType;
                    if (!Results.ContainsKey(RealType)) Results.Add(RealType, new List<McInstance>());
                    Results[RealType].Add(Instance);
                }
            }

        } catch (Exception ex) {
            Results.Clear();
            Logger.Error(ex, "分类版本列表失败");
        }
        #endregion
        #region 对卡片与版本进行排序

        // 卡片排序
        Dictionary<McInstanceCardType, List<McInstance>> SortedInstanceList = new Dictionary<McInstanceCardType, List<McInstance>>();
        foreach (McInstanceCardType SortRule in new[] { McInstanceCardType.Star, McInstanceCardType.API, McInstanceCardType.OriginalLike, McInstanceCardType.Rubbish, McInstanceCardType.Fool, McInstanceCardType.Error, McInstanceCardType.Hidden }) {
            if (Results.ContainsKey(SortRule)) SortedInstanceList.Add(SortRule, Results[SortRule]);
        }
        Results = SortedInstanceList;

        // 版本排序
        foreach (McInstanceCardType CardType in new[] { McInstanceCardType.Star, McInstanceCardType.API, McInstanceCardType.OriginalLike, McInstanceCardType.Rubbish, McInstanceCardType.Fool }) {
            if (!Results.ContainsKey(CardType)) continue;
            int GetComponentCode(McInstance Instance) {
                if (Instance.Version.ForgelikeCode > 0) return Instance.Version.ForgelikeCode;
                if (Instance.Version.HasOptiFine) return Instance.Version.OptiFineCode;
                return 0;
            }
            Results[CardType] = Results[CardType].SortByComparison(
            (Left, Right) => {
                // 发布时间
                if ((Left.ReleaseTime.Year >= 2000 || Right.ReleaseTime.Year >= 2000) &&
                    Left.ReleaseTime != Right.ReleaseTime) return Left.ReleaseTime > Right.ReleaseTime;
                // 附加组件种类
                if (Left.Version.ModLoaders.HasFlagF(ModLoaders.Fabric) != Right.Version.ModLoaders.HasFlagF(ModLoaders.Fabric)) return Left.Version.ModLoaders.HasFlagF(ModLoaders.Fabric);
                if (Left.Version.ModLoaders.HasFlagF(ModLoaders.NeoForge) != Right.Version.ModLoaders.HasFlagF(ModLoaders.NeoForge)) return Left.Version.ModLoaders.HasFlagF(ModLoaders.NeoForge);
                if (Left.Version.ModLoaders.HasFlagF(ModLoaders.Forge) != Right.Version.ModLoaders.HasFlagF(ModLoaders.Forge)) return Left.Version.ModLoaders.HasFlagF(ModLoaders.Forge);
                if (Left.Version.HasOptiFine != Right.Version.HasOptiFine) return Left.Version.HasOptiFine;
                if (Left.Version.ModLoaders.HasFlagF(ModLoaders.LiteLoader) != Right.Version.ModLoaders.HasFlagF(ModLoaders.LiteLoader)) return Left.Version.ModLoaders.HasFlagF(ModLoaders.LiteLoader);
                // 附加组件版本
                if (GetComponentCode(Left) != GetComponentCode(Right)) return GetComponentCode(Left) > GetComponentCode(Right);
                // 名称
                return Left.Name.CompareTo(Right.Name) > 0;
            });
        }

        #endregion
        #region 保存卡片缓存
        PclCore.WriteIni(Folder + "PCL.ini", "CardCount", Results.Count.ToString());
        for (int i = 0; i <= Results.Count - 1; i++) {
            PclCore.WriteIni(Folder + "PCL.ini", "CardKey" + (i + 1), ((int)Results.Keys.ElementAtOrDefault(i)).ToString());
            string Value = "";
            foreach (McInstance Instance in Results.Values.ElementAtOrDefault(i)) {
                Value += Instance.Name + ":";
            }
            PclCore.WriteIni(Folder + "PCL.ini", "CardValue" + (i + 1), Value);
        }
        #endregion

        return Results;
    }
    /// <summary>
    /// 筛选特定种类的版本，并直接添加为卡片。
    /// </summary>
    /// <param name="InstanceList">用于筛选的列表。</param>
    /// <param name="Formula">需要筛选出的版本类型。-2 代表隐藏的版本。</param>
    /// <param name="CardType">卡片的名称。</param>
    private static void McInstanceFilter(ref List<McInstance> InstanceList, ref Dictionary<McInstanceCardType, List<McInstance>> Target, McInstance.McInstanceState[] Formula, McInstanceCardType CardType) {
        List<McInstance> KeepList = InstanceList.Where(v => Formula.Contains(v.State)).ToList();
        // 加入版本列表，并从剩余中删除
        if (KeepList.Any()) {
            Target.Add(CardType, KeepList);
            InstanceList = InstanceList.Except(KeepList).ToList();
        }
    }
    /// <summary>
    /// 筛选特定种类的版本，并增加入一个已有列表中。
    /// </summary>
    /// <param name="InstanceList">用于筛选的列表。</param>
    /// <param name="Formula">需要筛选出的版本类型。-2 代表隐藏的版本。</param>
    /// <param name="KeepList">传入需要增加入的列表。</param>
    private static void McInstanceFilter(ref List<McInstance> InstanceList, McInstance.McInstanceState[] Formula, ref List<McInstance> KeepList) {
        KeepList.AddRange(InstanceList.Where(v => Formula.Contains(v.State)));
        // 加入版本列表，并从剩余中删除
        if (KeepList.Any()) {
            InstanceList = InstanceList.Except(KeepList).ToList();
        }
    }

    #endregion

    #region 皮肤

    /// <summary>
    /// 获取 Uuid 对应的皮肤文件地址，失败将抛出异常。
    /// </summary>
    public static string McSkinGetAddress(string UUID, string Type) {
        if (UUID == "") throw new Exception("UUID 为空。");
        if (UUID.StartsWithF("00000") && Type != "Auth") throw new Exception("离线 UUID 无正版皮肤文件：" + UUID);
        // 尝试读取缓存
        string CacheSkinAddress = PclCore.ReadIni(PclCore.GetPathTemp() + "Cache\\Skin\\Index" + Type + ".ini", UUID);
        if (CacheSkinAddress != "") return CacheSkinAddress;
        // 获取皮肤地址
        string Url;
        McInstance SelectedInstance = PclUiBridge.McInstanceSelectedGet();
        switch (Type) {
            case "Mojang":
            case "Ms":
                Url = "https://sessionserver.mojang.com/session/minecraft/profile/";
                break;
            case "Nide":
                Url = "https://auth.mc-user.com:233/" + (SelectedInstance is null ? PclCore.GetSetting<string>("CacheNideServer") : PclCore.GetSetting<string>("VersionServerNide", SelectedInstance)) + "/sessionserver/session/minecraft/profile/";
                break;
            case "Auth":
                Url = (SelectedInstance is null ? PclCore.GetSetting<string>("CacheAuthServerServer") : PclCore.GetSetting<string>("VersionServerAuthServer", SelectedInstance)) + "/sessionserver/session/minecraft/profile/";
                break;
            default:
                throw new ArgumentException("皮肤地址种类无效：" + (Type ?? "null"));
        }
        string SkinString = NetCore.NetRequestByClientRetry(Url + UUID, RequireJson: true);
        if (SkinString == "") throw new Exception("皮肤返回值为空，可能是未设置自定义皮肤的用户");
        // 处理皮肤地址
        string SkinValue = null;
        bool IsFound = false;
        try {
            foreach (JToken SkinProperty in SkinString.DeserializeJson()["properties"]) {
                if ((string)SkinProperty["name"] == "textures") {
                    SkinValue = (string)SkinProperty["value"];
                    IsFound = true;
                    break;
                }
            }
            if (!IsFound) throw new Exception("未从皮肤返回值中找到符合条件的 Property");
        } catch (Exception ex) {
            Logger.Warn(ex, $"无法完成解析的皮肤返回值，可能是未设置自定义皮肤的用户：{SkinString}");
            throw new Exception("皮肤返回值中不包含皮肤数据项，可能是未设置自定义皮肤的用户", ex);
        }
        SkinString = Encoding.UTF8.GetString(Convert.FromBase64String(SkinValue));
        JObject SkinJson = (JObject)SkinString.Lower().DeserializeJson();
        if (SkinJson["textures"] is null || SkinJson["textures"]["skin"] is null || SkinJson["textures"]["skin"]["url"] is null) {
            throw new Exception("用户未设置自定义皮肤");
        } else {
            string SkinUrl = SkinJson["textures"]["skin"]["url"].ToString();
            SkinValue = SkinUrl.Contains("minecraft.net/") ? SkinUrl.Replace("http://", "https://") : SkinUrl;
        }
        // 保存缓存
        PclCore.WriteIni(PclCore.GetPathTemp() + "Cache\\Skin\\Index" + Type + ".ini", UUID, SkinValue);
        Logger.Info($"UUID {UUID} 对应的皮肤文件为 {SkinValue}");
        return SkinValue;
    }

    private static readonly object McSkinDownloadLock = new object();
    /// <summary>
    /// 从 Url 下载皮肤，可能会使用缓存。返回本地文件路径，失败将抛出异常。
    /// </summary>
    public static string McSkinDownload(string Address) {
        PathUtils.GetLastPart(Address); // 原 VB：Dim SkinName As String = PathUtils.GetLastPart(Address)，变量未使用
        string FileAddress = PclCore.GetPathTemp() + "Cache\\Skin\\" + Address.GetStableHashCode() + ".png";
        lock (McSkinDownloadLock) {
            if (!FileUtils.Exists(FileAddress)) {
                NetCore.NetDownloadByClient(Address, FileAddress + ".PCLDownloading");
                FileUtils.Move(FileAddress + ".PCLDownloading", FileAddress);
                Logger.Info($"皮肤下载成功：{FileAddress}");
            }
            return FileAddress;
        }
    }

    /// <summary>
    /// 获取 Uuid 对应的皮肤，返回“Steve”或“Alex”。
    /// </summary>
    public static string McSkinSex(string Uuid) {
        if (Uuid.Length != 32) return "Steve";
        int a = int.Parse(Uuid[7].ToString(), System.Globalization.NumberStyles.AllowHexSpecifier);
        int b = int.Parse(Uuid[15].ToString(), System.Globalization.NumberStyles.AllowHexSpecifier);
        int c = int.Parse(Uuid[23].ToString(), System.Globalization.NumberStyles.AllowHexSpecifier);
        int d = int.Parse(Uuid[31].ToString(), System.Globalization.NumberStyles.AllowHexSpecifier);
        return (a ^ b ^ c ^ d) % 2 != 0 ? "Alex" : "Steve";
        //Math.floorMod(uuid.hashCode(), 18)

        //Public Function hashCode(ByVal str As String) As Integer
        //Dim hash As Integer = 0
        //Dim n As Integer = str.Length
        //If n = 0 Then
        //    Return hash
        //End If
        //For i As Integer = 0 To n - 1
        //    hash = hash + Asc(str(i)) * (1 << (n - i - 1))
        //Next
        //Return hash
        //End Function
    }

    #endregion

    #region 支持库文件（Library）

    /// <summary>
    /// 检查是否符合 Json 中的 Rules。
    /// </summary>
    /// <param name="RuleToken">Json 中的 "rules" 项目。</param>
    public static bool McJsonRuleCheck(JToken RuleToken) {
        if (RuleToken is null) return true;

        // 初始化
        bool Required = false;
        foreach (JToken Rule in RuleToken) {

            // 单条条件验证
            bool IsRightRule = true; // 是否为正确的规则
            if (Rule["os"] is not null) { // 操作系统
                if (Rule["os"]["name"] is not null) { // 操作系统名称
                    string OsName = Rule["os"]["name"].ToString();
                    if (OsName == "unknown") {
                    } else if (OsName == "windows") {
                        if (Rule["os"]["version"] is not null) { // 操作系统版本
                            IsRightRule = IsRightRule && OSVersion.RegexCheck(Rule["os"]["version"].ToString());
                        }
                    } else {
                        IsRightRule = false;
                    }
                }
                if (Rule["os"]["arch"] is not null) { // 操作系统架构
                    // VB 中 JToken.Contains("arm") 经 Option Strict Off 解析为 Enumerable.Contains(Of JToken)，
                    // 即对 JToken 的子项序列进行包含检查，此处保持相同调用方式以维持行为一致
                    IsRightRule = IsRightRule && Rule["os"]["arch"].ToString() != "x86" && !((IEnumerable<JToken>)Rule["os"]["arch"]).Contains((JToken)"arm");
                }
            }
            if (Rule["features"] is not null) { // 标签
                IsRightRule = IsRightRule && Rule["features"]["is_demo_user"] is null; // 反选是否为 Demo 用户
                if (((JObject)Rule["features"]).Children().Any(j => ((JProperty)j).Name.Contains("quick_play"))) {
                    IsRightRule = false; // 不开 Quick Play，让玩家自己加去
                }
            }

            // 反选确认
            if (Rule["action"].ToString() == "allow") {
                if (IsRightRule) Required = true; // allow
            } else {
                if (IsRightRule) Required = false; // disallow
            }

        }
        return Required;
    }
    private static string OSVersion = new Microsoft.VisualBasic.Devices.ComputerInfo().OSVersion;

    /// <summary>
    /// 递归获取 Minecraft 某一版本的完整支持库列表。
    /// </summary>
    public static List<McLibEntry> McLibListGet(McInstance Instance, bool IncludeMainJar) {

        // 获取当前支持库列表
        Logger.Info($"获取支持库列表：{Instance.Name}");
        List<McLibEntry> Result = McLibListGetWithJson(Instance.JsonObject);

        // 需要添加原版 Jar
        if (IncludeMainJar) {
            McInstance RealInstance;
            string RequiredJar = Instance.JsonObject["jar"]?.ToString();
            if (Instance.IsHmclFormatJson || RequiredJar is null) {
                // HMCL 项直接使用自身的 Jar
                // 根据 Inherit 获取最深层版本
                McInstance OriginalInstance = Instance;
                // 1.17+ 的 Forge 不寻找 Inherit
                if (!((Instance.Version.ModLoaders.HasFlagF(ModLoaders.Forge) || Instance.Version.ModLoaders.HasFlagF(ModLoaders.NeoForge)) && Instance.Version.Vanilla.Major >= 17)) {
                    while (OriginalInstance.InheritName != "") {
                        if (OriginalInstance.InheritName == OriginalInstance.Name) break;
                        OriginalInstance = new McInstance(PclUiBridge.McFolderSelectedGet() + "versions\\" + OriginalInstance.InheritName + "\\");
                    }
                }
                // 需要新建对象，否则后面的 Check 会导致 McInstanceCurrent 的 State 变回 Original
                // 复现：启动一个 Snapshot 版本
                RealInstance = new McInstance(OriginalInstance.PathVersion);
            } else {
                // Json 已提供 Jar 字段，使用该字段的信息
                RealInstance = new McInstance(RequiredJar);
            }
            string ClientUrl = null, ClientSHA1 = null;
            // 判断需求的版本是否存在
            // 不能调用 RealVersion.Check()，可能会莫名其妙地触发 CheckPermission 正被另一进程使用，导致误判前置不存在
            if (!FileUtils.Exists(RealInstance.PathVersion + RealInstance.Name + ".json")) {
                RealInstance = Instance;
                Logger.Warn($"可能缺少前置版本 {RealInstance.Name}，找不到对应的 json 文件");
            }
            // 获取详细下载信息
            if (RealInstance.JsonObject["downloads"] is not null && RealInstance.JsonObject["downloads"]["client"] is not null) {
                ClientUrl = (string)RealInstance.JsonObject["downloads"]["client"]["url"];
                ClientSHA1 = (string)RealInstance.JsonObject["downloads"]["client"]["sha1"];
            } else {
                ClientUrl = null;
                ClientSHA1 = null;
            }
            // 把所需的原版 Jar 添加进去
            Result.Add(new McLibEntry { LocalPath = RealInstance.PathVersion + RealInstance.Name + ".jar", Size = 0, IsNatives = false, Url = ClientUrl, SHA1 = ClientSHA1 });
        }

        return Result;
    }
    /// <summary>
    /// 获取 Minecraft 某一版本忽视继承的支持库列表，即结果中没有继承项。
    /// </summary>
    public static List<McLibEntry> McLibListGetWithJson(JObject JsonObject, bool KeepSameNameDifferentVersionResult = false, string CustomMcFolder = null) {
        CustomMcFolder = CustomMcFolder ?? PclUiBridge.McFolderSelectedGet();

        // 转换为 LibToken
        List<McLibEntry> BasicArray = new List<McLibEntry>();
        foreach (JObject Library in ((JArray)JsonObject["libraries"]).Children()) {

            // 清理 null 项（BakaXL 会把没有的项序列化为 null，但会被 Newtonsoft 转换为 JValue，导致 Is Nothing = false；这导致了 #409）
            for (int i = Library.Properties().Count() - 1; i >= 0; i--) {
                if (Library.Properties().ElementAtOrDefault(i).Value.Type == JTokenType.Null) Library.Remove(Library.Properties().ElementAtOrDefault(i).Name);
            }

            // 检查是否需要（Rules）
            if (!McJsonRuleCheck(Library["rules"])) continue;

            // 获取根节点下的 url
            string RootUrl = (string)Library["url"];
            if (RootUrl is not null) {
                RootUrl += McLibGet((string)Library["name"], false, true, CustomMcFolder).Replace("\\", "/");
            }

            // 根据是否本地化处理（Natives）
            if (Library["natives"] is null) { // 没有 Natives
                string LocalPath;
                LocalPath = McLibGet((string)Library["name"], CustomMcFolder: CustomMcFolder);
                try {
                    if (Library["downloads"] is not null && Library["downloads"]["artifact"] is not null) {
                        BasicArray.Add(new McLibEntry {
                            OriginalName = (string)Library["name"],
                            Url = (string)(Library["downloads"]["artifact"]["url"] ?? (JToken)RootUrl),
                            LocalPath = Library["downloads"]["artifact"]["path"] is null ? McLibGet((string)Library["name"],
                                CustomMcFolder: CustomMcFolder) : CustomMcFolder + "libraries\\" + Library["downloads"]["artifact"]["path"].ToString().Replace("/", "\\"),
                            Size = Convert.ToInt64(PclCore.Val(Library["downloads"]["artifact"]["size"].ToString())),
                            IsNatives = false,
                            SHA1 = Library["downloads"]["artifact"]["sha1"]?.ToString()
                        });
                    } else {
                        BasicArray.Add(new McLibEntry { OriginalName = (string)Library["name"], Url = RootUrl, LocalPath = LocalPath, Size = 0, IsNatives = false, SHA1 = null });
                    }
                } catch (Exception ex) {
                    Logger.Warn(ex, $"处理实际支持库列表失败（无 Natives，{Library["name"] ?? (JToken)"Nothing"}）");
                    BasicArray.Add(new McLibEntry { OriginalName = (string)Library["name"], Url = RootUrl, LocalPath = LocalPath, Size = 0, IsNatives = false, SHA1 = null });
                }
            } else if (Library["natives"]["windows"] is not null) { // 有 Windows Natives
                string NativeName = (string)Library["natives"]["windows"];
                try {
                    if (Library["downloads"] is not null && Library["downloads"]["classifiers"] is not null && Library["downloads"]["classifiers"][NativeName] is not null) {
                        JToken NativeData = Library["downloads"]["classifiers"][NativeName];
                        BasicArray.Add(new McLibEntry {
                            OriginalName = (string)Library["name"],
                            Url = (string)(NativeData["url"] ?? (JToken)RootUrl),
                            LocalPath = NativeData["path"] is null
                                ? McLibGet((string)Library["name"], CustomMcFolder: CustomMcFolder).
                                    Replace(".jar", "-" + NativeName + ".jar").
                                    Replace("${arch}", Environment.Is64BitOperatingSystem ? "64" : "32")
                                : $"{CustomMcFolder}libraries\\{NativeData["path"].ToString().Replace("/", "\\")}",
                            Size = Convert.ToInt64(PclCore.Val(NativeData["size"].ToString())),
                            IsNatives = true,
                            SHA1 = NativeData["sha1"].ToString()
                        });
                    } else {
                        BasicArray.Add(
                        new McLibEntry { OriginalName = (string)Library["name"], Url = RootUrl, Size = 0, IsNatives = true, SHA1 = null,
                            LocalPath = McLibGet((string)Library["name"], CustomMcFolder: CustomMcFolder).
                                Replace(".jar", $"-{NativeName}.jar").
                                Replace("${arch}", Environment.Is64BitOperatingSystem ? "64" : "32")
                        });
                    }
                } catch (Exception ex) {
                    Logger.Warn(ex, $"处理实际支持库列表失败（有 Natives，{Library["name"] ?? (JToken)"Nothing"}）");
                    BasicArray.Add(
                        new McLibEntry { OriginalName = (string)Library["name"], Url = RootUrl, Size = 0, IsNatives = true, SHA1 = null,
                            LocalPath = McLibGet((string)Library["name"], CustomMcFolder: CustomMcFolder).
                                Replace(".jar", $"-{NativeName}.jar").
                                Replace("${arch}", Environment.Is64BitOperatingSystem ? "64" : "32")
                        });
                }
            }

        }

        // 去重
        Dictionary<string, McLibEntry> ResultArray = new Dictionary<string, McLibEntry>();
        string GetVersion(McLibEntry Token) {
            // 测试例：
            // D:\Minecraft\test\libraries\net\neoforged\mergetool\2.0.0\mergetool-2.0.0-api.jar
            // D:\Minecraft\test\libraries\org\apache\commons\commons-collections4\4.2\commons-collections4-4.2.jar
            // D:\Minecraft\test\libraries\com\google\guava\guava\31.1-jre\guava-31.1-jre.jar
            return PathUtils.GetLastPart(PathUtils.RemoveLastPart(Token.LocalPath));
        }
        for (int i = 0; i < BasicArray.Count; i++) {
            string Key = BasicArray[i].Name + BasicArray[i].IsNatives.ToString();
            if (ResultArray.ContainsKey(Key)) {
                string BasicArrayVersion = GetVersion(BasicArray[i]);
                string ResultArrayVersion = GetVersion(ResultArray[Key]);
                if (BasicArrayVersion != ResultArrayVersion && KeepSameNameDifferentVersionResult) {
                    Logger.Info($"发现疑似重复的支持库：{BasicArray[i]} ({BasicArrayVersion}) 与 {ResultArray[Key]} ({ResultArrayVersion})");
                    ResultArray.Add(Key + PclCore.GetUuid(), BasicArray[i]);
                } else {
                    Logger.Info($"发现重复的支持库：{BasicArray[i]} ({BasicArrayVersion}) 与 {ResultArray[Key]} ({ResultArrayVersion})，已忽略其中之一");
                    if (McUtils.CompareVersionGE(BasicArrayVersion, ResultArrayVersion)) {
                        ResultArray[Key] = BasicArray[i];
                    }
                }
            } else {
                ResultArray.Add(Key, BasicArray[i]);
            }
        }
        return ResultArray.Values.ToList();
    }

    /// <summary>
    /// 获取版本所需支持库文件的 NetFile。
    /// </summary>
    public static List<NetFile> McLibNetFilesFromInstance(McInstance Instance) {
        if (!Instance.IsLoaded) Instance.Load();
        List<NetFile> Result = new List<NetFile>();

        // 更新此方法时需要同步更新 Forge 新版自动安装方法！

        // 主 Jar 文件
        try {
            NetFile MainJar = PclDownload.DlClientJarGet(Instance, true);
            if (MainJar is not null) Result.Add(MainJar);
        } catch (Exception ex) {
            Logger.Warn(ex, "版本缺失主 jar 文件所必须的信息");
        }

        // Library 文件
        Result.AddRange(McLibNetFilesFromTokens(McLibListGet(Instance, false)));

        // 统一通行证文件
        if (PclCore.GetSetting<int>("VersionServerLogin", Instance) == 3) {
            string TargetFile = PclCore.PathPure.Value + "nide8auth.jar";
            try {
                // 测试链接：https://auth.mc-user.com:233/00000000000000000000000000000000/
                Logger.Info("开始获取统一通行证下载信息");
                JObject DownloadInfo = (JObject)NetCore.NetRequestByClientRetry("https://auth.mc-user.com:233/" + PclCore.GetSetting<string>("VersionServerNide", Instance), RequireJson: true).DeserializeJson();
                Result.Add(new NetFile(
                    Urls: new[] { "https://login.mc-user.com:233/index/jar" },
                    LocalPath: TargetFile,
                    Checker: new FileChecker { Hash = DownloadInfo["jarHash"].ToString() }));
            } catch (Exception ex) {
                if (FileUtils.Exists(TargetFile)) {
                    Logger.Warn(ex, "获取统一通行证下载信息失败");
                } else {
                    throw new Exception("获取统一通行证下载信息失败", ex);
                }
            }
        }

        // Authlib-Injector 文件
        if (PclCore.GetSetting<int>("VersionServerLogin", Instance) == 4) {
            string TargetFile = PclCore.PathPure.Value + "authlib-injector.jar";
            try {
                Logger.Info("开始获取 Authlib-Injector 下载信息");
                JObject DownloadInfo = (JObject)NetCore.NetRequestByClientRetry(
                        "https://authlib-injector.yushi.moe/artifact/latest.json",
                        BackupUrl: "https://bmclapi2.bangbang93.com/mirrors/authlib-injector/artifact/latest.json", RequireJson: true).DeserializeJson();
                string DownloadAddress = DownloadInfo["download_url"].ToString().
                        Replace("bmclapi2.bangbang93.com/mirrors/authlib-injector", "authlib-injector.yushi.moe");
                Result.Add(new NetFile(new[] {
                    DownloadAddress,
                    DownloadAddress.Replace("authlib-injector.yushi.moe", "bmclapi2.bangbang93.com/mirrors/authlib-injector")
                }, TargetFile, new FileChecker { Hash = DownloadInfo["checksums"]["sha256"].ToString() }));
            } catch (Exception ex) {
                if (FileUtils.Exists(TargetFile)) {
                    Logger.Warn(ex, "获取 Authlib-Injector 下载信息失败");
                } else {
                    throw new Exception("获取 Authlib-Injector 下载信息失败", ex);
                }
            }
        }

        // 跳过校验
        if (ShouldIgnoreFileCheck(Instance)) {
            Logger.Warn("设置要求尽量忽略文件检查，这可能会保留有误的文件");
            Result = Result.Where(
            f => {
                if (FileUtils.Exists(f.LocalPath)) {
                    Logger.Warn($"跳过下载的支持库文件：{f.LocalPath}");
                    return false;
                } else {
                    return true;
                }
            }).ToList();
        }

        return Result;
    }
    /// <summary>
    /// 将 McLibToken 列表转换为 NetFile。
    /// </summary>
    public static List<NetFile> McLibNetFilesFromTokens(List<McLibEntry> Libs, string CustomMcFolder = null) {
        CustomMcFolder = CustomMcFolder ?? PclUiBridge.McFolderSelectedGet();
        List<NetFile> Result = new List<NetFile>();
        // 获取
        foreach (McLibEntry Token in Libs) {
            // 检查器
            FileChecker Checker;
            if (Token.Name.ContainsIgnoreCase("labymod")) {
                Checker = new FileChecker(); // 不检查 LabyMod 的文件，它们提供的文件校验信息是错的（#3225）
            } else {
                Checker = new FileChecker { ActualSize = Token.Size == 0 ? -1 : Token.Size, Hash = Token.SHA1 };
            }
            // URL
            List<string> Urls = new List<string>();
            if (Token.Url is null && Token.Name == "net.minecraftforge:forge:universal") { // 特判修复 Forge 部分 universal 文件缺失 URL（#5455）
                Token.Url = "https://maven.minecraftforge.net" + Token.LocalPath.Replace(CustomMcFolder + "libraries", "").Replace("\\", "/");
            }
            if (Token.Url is not null) {
                // 获取 URL 的真实地址
                Urls.Add(Token.Url);
                if (Token.Url.Contains("launcher.mojang.com/v1/objects") || Token.Url.Contains("client.txt") ||
                   Token.Url.Contains(".tsrg")) {
                    Urls.AddRange(PclDownload.DlSourceLauncherOrMetaGet(Token.Url)); // Mappings（#4425）
                }
                if (Token.Url.Contains("maven")) {
                    string BmclapiUrl =
                        Token.Url.Replace(Token.Url.Substring(0, Token.Url.IndexOfF("maven")), "https://bmclapi2.bangbang93.com/").Replace("maven.fabricmc.net", "maven").Replace("maven.minecraftforge.net", "maven").Replace("maven.neoforged.net/releases", "maven");
                    if (PclDownload.DlSourcePreferMojang) {
                        Urls.Add(BmclapiUrl); // 官方源优先
                    } else {
                        Urls.Insert(0, BmclapiUrl); // 镜像源优先
                    }
                }
            }
            if (Token.LocalPath.Contains("transformer-discovery-service")) {
                // Transformer 文件释放
                if (!FileUtils.Exists(Token.LocalPath)) PclUiBridge.ExtractResources?.Invoke(Token.LocalPath, "Transformer");
                Logger.Warn("已自动释放 Transformer Discovery Service");
                continue;
            } else if (Token.LocalPath.Contains("optifine\\OptiFine")) {
                // OptiFine 主 Jar
                string OptiFineBase = Token.LocalPath.Replace(CustomMcFolder + "libraries\\optifine\\OptiFine\\", "").Split("_")[0] + "/" + PathUtils.GetLastPart(Token.LocalPath).Replace("-", "_");
                OptiFineBase = "/maven/com/optifine/" + OptiFineBase;
                if (OptiFineBase.Contains("_pre")) OptiFineBase = OptiFineBase.Replace("com/optifine/", "com/optifine/preview_");
                Urls.Add("https://bmclapi2.bangbang93.com" + OptiFineBase);
            } else if (Urls.Count <= 2) {
                // 普通文件
                Urls.AddRange(PclDownload.DlSourceLibraryGet("https://libraries.minecraft.net" + Token.LocalPath.Replace(CustomMcFolder + "libraries", "").Replace("\\", "/")));
            }
            Result.Add(new NetFile(Urls.Distinct(), Token.LocalPath, Checker));
        }
        // 去重并返回
        return Result.DistinctBy(a => a.LocalPath).ToList();
    }
    /// <summary>
    /// 获取对应的支持库文件地址。
    /// </summary>
    /// <param name="Original">原始地址，如 com.mumfrey:liteloader:1.12.2-SNAPSHOT。</param>
    /// <param name="WithHead">是否包含 Lib 文件夹头部，若不包含，则会类似以 com\xxx\ 开头。</param>
    public static string McLibGet(string Original, bool WithHead = true, bool IgnoreLiteLoader = false, string CustomMcFolder = null) {
        CustomMcFolder = CustomMcFolder ?? PclUiBridge.McFolderSelectedGet();
        // 有时候原始的有四段，例如 net.minecraftforge:forge:1.21.4-54.0.34:client
        // 文件夹名应该忽略最后一段（client），但文件名需要保留
        // 参见 #7306（以及 #5376 也是因为这里没有处理好导致的）
        string[] Splited = Original.Split(":");
        string Result = (WithHead ? CustomMcFolder + "libraries\\" : "") +
            $"{Splited[0].Replace(".", "\\")}\\{Splited[1]}\\{Splited[2]}\\{Splited.Skip(1).Join("-")}.jar";
        return Result;
    }

    /// <summary>
    /// 检查设置，是否应当忽略文件检查？
    /// </summary>
    public static bool ShouldIgnoreFileCheck(McInstance Instance) {
        return PclCore.GetSetting<bool>("VersionAdvanceAssetsV2", Instance) || (PclCore.GetSetting<int>("VersionAdvanceAssets", Instance) == 2);
    }

    #endregion

    #region 资源文件（Assets）

    // 获取索引
    /// <summary>
    /// 获取某版本资源文件索引的对应 Json 项，详见版本 Json 中的 assetIndex 项。失败会抛出异常。
    /// </summary>
    public static JToken McAssetsGetIndex(McInstance Instance, bool ReturnLegacyOnError = false, bool CheckURLEmpty = false) {
        try {
            while (true) {
                JToken Index = Instance.JsonObject["assetIndex"];
                if (Index is not null && Index["id"] is not null) return Index;
                // 原 VB 将结果存入 AssetsName，但该变量仅在下方注释掉的旧逻辑中使用
                if (Instance.JsonObject["assets"] is not null) _ = Instance.JsonObject["assets"].ToString();
                if (CheckURLEmpty && Index["url"] is not null) return Index;
                // 下一个版本
                if (Instance.InheritName == "") break;
                Instance = new McInstance(PclUiBridge.McFolderSelectedGet() + "versions\\" + Instance.InheritName);
            }
        } catch {
        }
        // 无法获取到下载地址
        if (ReturnLegacyOnError) {
            // 返回 assets 文件名会由于没有下载地址导致全局失败
            //If AssetsName IsNot Nothing AndAlso AssetsName <> "legacy" Then
            //    Log("[Minecraft] 无法获取资源文件索引下载地址，使用 assets 项提供的资源文件名：" & AssetsName)
            //    Return ("{""id"": """ & AssetsName & """}").DeserializeJson()
            //Else
            Logger.Info("无法获取资源文件索引下载地址，使用默认的 legacy 下载地址");
            return @"{
                ""id"": ""legacy"",
                ""sha1"": ""c0fd82e8ce9fbc93119e40d96d5a4e62cfa3f729"",
                ""size"": 134284,
                ""url"": ""https://launchermeta.mojang.com/mc-staging/assets/legacy/c0fd82e8ce9fbc93119e40d96d5a4e62cfa3f729/legacy.json"",
                ""totalSize"": 111220701
            }".DeserializeJson();
            //End If
        } else {
            throw new Exception("该版本不存在资源文件索引信息");
        }
    }
    /// <summary>
    /// 获取某版本资源文件索引名，优先使用 assetIndex，其次使用 assets。失败会返回 legacy。
    /// </summary>
    public static string McAssetsGetIndexName(McInstance Instance) {
        try {
            while (true) {
                if (Instance.JsonObject["assetIndex"] is not null && Instance.JsonObject["assetIndex"]["id"] is not null) {
                    return Instance.JsonObject["assetIndex"]["id"].ToString();
                }
                if (Instance.JsonObject["assets"] is not null) {
                    return Instance.JsonObject["assets"].ToString();
                }
                if (Instance.InheritName == "") break;
                Instance = new McInstance(PclUiBridge.McFolderSelectedGet() + "versions\\" + Instance.InheritName);
            }
        } catch (Exception ex) {
            Logger.Warn(ex, "获取资源文件索引名失败");
        }
        return "legacy";
    }

    // 获取列表
    /// <summary>
    /// 获取 Minecraft 的资源文件列表。失败会抛出异常。
    /// </summary>
    private static List<McAssetsToken> McAssetsListGet(McInstance Instance) {
        string IndexName = McAssetsGetIndexName(Instance);
        try {

            // 初始化
            if (!FileUtils.Exists($"{PclUiBridge.McFolderSelectedGet()}assets\\indexes\\{IndexName}.json")) throw new FileNotFoundException("未找到 Asset Index", PclUiBridge.McFolderSelectedGet() + "assets\\indexes\\" + IndexName + ".json");
            List<McAssetsToken> Result = new List<McAssetsToken>();
            JObject Json = (JObject)FileUtils.ReadAsJson($"{PclUiBridge.McFolderSelectedGet()}assets\\indexes\\{IndexName}.json");

            // 读取列表
            foreach (JProperty File in Json["objects"].Children()) {
                string LocalPath;
                if (Json["map_to_resources"] is not null && Json["map_to_resources"].ToObject<bool>()) {
                    // Remap
                    LocalPath = $"{Instance.PathIndie}resources\\{File.Name}";
                } else if (Json["virtual"] is not null && Json["virtual"].ToObject<bool>()) {
                    // Virtual
                    LocalPath = $"{PclUiBridge.McFolderSelectedGet()}assets\\virtual\\legacy\\{File.Name}";
                } else {
                    // 正常
                    LocalPath = $"{PclUiBridge.McFolderSelectedGet()}assets\\objects\\{File.Value["hash"].ToString().Substring(0, 2)}\\{File.Value["hash"]}";
                }
                Result.Add(new McAssetsToken {
                    LocalPath = LocalPath.Replace("/", "\\"),
                    SourcePath = File.Name,
                    Hash = File.Value["hash"].ToString(),
                    Size = Convert.ToInt64(File.Value["size"].ToString())
                });
            }
            return Result;

        } catch (Exception ex) {
            Logger.Warn(ex, $"获取资源文件列表失败：{IndexName}");
            throw;
        }
    }

    /// <summary>
    /// 获取版本资源文件所对应的 NetFile。
    /// </summary>
    public static List<NetFile> McAssetsFixList(McInstance Instance, bool CheckHash, LoaderBase ProgressFeed = null) {
        // 如果需要检查 Hash，则留到下载时处理，以借助多线程加快检查速度
        if (CheckHash) {
            return McAssetsListGet(Instance).
                Select(Token => new NetFile(
                    PclDownload.DlSourceAssetsGet($"https://resources.download.minecraft.net/{Token.Hash.Substring(0, 2)}/{Token.Hash}"),
                    LocalPath: Token.LocalPath,
                    Checker: new FileChecker { ActualSize = Token.Size == 0 ? -1 : Token.Size, Hash = Token.Hash })).ToList();
        }
        // 如果不检查 Hash，则立即处理
        List<NetFile> Result = new List<NetFile>();
        List<McAssetsToken> AssetsList;
        try {
            AssetsList = McAssetsListGet(Instance);
            McAssetsToken Token;
            if (ProgressFeed is not null) ProgressFeed.Progress = 0.04;
            for (int i = 0; i < AssetsList.Count; i++) {
                // 初始化
                Token = AssetsList[i];
                if (ProgressFeed is not null) ProgressFeed.Progress = 0.05 + 0.94 * i / AssetsList.Count;
                // 检查文件是否存在
                FileInfo File = FileUtils.GetInfo(Token.LocalPath);
                if (File.Exists && (Token.Size == 0 || Token.Size == File.Length)) continue;
                // 文件不存在，添加下载
                Result.Add(new NetFile(PclDownload.DlSourceAssetsGet($"https://resources.download.minecraft.net/{Token.Hash.Substring(0, 2)}/{Token.Hash}"), Token.LocalPath, new FileChecker { ActualSize = Token.Size == 0 ? -1 : Token.Size, Hash = Token.Hash }));
            }
        } catch (Exception ex) {
            Logger.Warn(ex, "获取版本缺失的资源文件下载列表失败");
        }
        if (ProgressFeed is not null) ProgressFeed.Progress = 0.99;
        return Result;
    }

    #endregion

    /// <summary>
    /// 打码字符串中的 AccessToken。
    /// </summary>
    public static string FilterAccessToken(string Raw, char FilterChar) {
        // 打码 "accessToken " 后的内容
        if (Raw.Contains("accessToken ")) {
            foreach (string Token in Raw.RegexSearch("(?<=accessToken ([^ ]{5}))[^ ]+(?=[^ ]{5})")) {
                Raw = Raw.Replace(Token, new string(FilterChar, Token.Length));
            }
        }
        // 打码当前登录的结果（桥可能尚未注册或登录未完成，需空安全）
        McLoginResult LoginOutput = PclUiBridge.McLoginLoaderOutputGet?.Invoke() ?? default;
        string AccessToken = LoginOutput.AccessToken;
        if (AccessToken is not null && AccessToken.Length >= 10 && Raw.ContainsIgnoreCase(AccessToken) &&
            LoginOutput.Uuid != AccessToken) { // UUID 和 AccessToken 一样则不打码
            Raw = Raw.Replace(AccessToken, AccessToken.Substring(0, 5) + new string(FilterChar, AccessToken.Length - 10) + AccessToken.Substring(AccessToken.Length - 5));
        }
        return Raw;
    }
    /// <summary>
    /// 打码字符串中的 Windows 用户名。
    /// </summary>
    public static string FilterUserName(string Raw, char FilterChar) {
        if (Raw.Contains(":\\Users\\")) {
            Raw = System.Text.RegularExpressions.Regex.Replace(Raw, "(?<=:\\Users\\)[^\\]+", Token => new string(FilterChar, Token.Length));
        }
        if (Raw.Contains(":/Users/")) {
            Raw = System.Text.RegularExpressions.Regex.Replace(Raw, "(?<=:/Users/)[^/]+", Token => new string(FilterChar, Token.Length));
        }
        return Raw;
    }

}

/// <summary>
/// Minecraft 文件夹。
/// </summary>
public class McFolder {
    public string Name;
    /// <summary>
    /// 文件夹路径。
    /// 以 \ 结尾，例如 "D:\Game\MC\.minecraft\"。
    /// </summary>
    public string Location;
    public Types Type;
    public enum Types {
        Vanilla,
        RenamedVanilla,
        Custom
    }

    public override bool Equals(object obj) {
        if (!(obj is McFolder)) return false;
        McFolder Folder = (McFolder)obj;
        return Name == Folder.Name && PathUtils.IsSame(Location, Folder.Location) && Type == Folder.Type;
    }
    public override string ToString() {
        return Location;
    }
    // VB 在仅重写 Equals 时自动生成的 GetHashCode 调用 RuntimeHelpers.GetHashCode，此处保持一致。
    public override int GetHashCode() {
        return System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this);
    }
}

/// <summary>
/// 皮肤信息。
/// </summary>
public struct McSkinInfo {
    public bool IsSlim;
    public string LocalFile;
    public bool IsVaild;
}

/// <summary>
/// 单个支持库文件的信息。
/// </summary>
public class McLibEntry {
    /// <summary>
    /// 文件的完整本地路径。
    /// </summary>
    public string LocalPath;
    /// <summary>
    /// 文件大小。若无有效数据即为 0。
    /// </summary>
    public long Size = 0;
    /// <summary>
    /// 是否为 Natives 文件。
    /// </summary>
    public bool IsNatives = false;
    /// <summary>
    /// 文件的 SHA1。
    /// </summary>
    public string SHA1 = null;
    /// <summary>
    /// 由 Json 提供的 URL，若没有则为 Nothing。
    /// </summary>
    public string Url {
        get {
            return _Url;
        }
        set {
            // 孤儿 Forge 作者喜欢把没有 URL 的写个空字符串
            _Url = string.IsNullOrWhiteSpace(value) ? null : value;
        }
    }
    private string _Url;
    /// <summary>
    /// 原 Json 中 Name 项除去版本号部分的较前部分。可能为 Nothing。
    /// </summary>
    public string Name {
        get {
            if (OriginalName is null) return null;
            List<string> Splited = new List<string>(OriginalName.Split(":"));
            Splited.RemoveAt(2); // Java 的此格式下版本号固定为第三段，第四段可能包含架构、分包等其他信息
            return Splited.Join(":");
        }
    }
    /// <summary>
    /// 原 Json 中的 Name 项。
    /// </summary>
    public string OriginalName;

    public override string ToString() {
        return (IsNatives ? "[Native] " : "") + StringUtils.FormatByteSize(Size) + " | " + LocalPath;
    }
}

/// <summary>
/// 单个资源文件的信息。
/// </summary>
internal struct McAssetsToken {
    /// <summary>
    /// 文件的完整本地路径。
    /// </summary>
    public string LocalPath;
    /// <summary>
    /// Json 中书写的源路径。例如 minecraft/sounds/mob/stray/death2.ogg 。
    /// </summary>
    public string SourcePath;
    /// <summary>
    /// 文件大小。若无有效数据即为 0。
    /// </summary>
    public long Size;
    /// <summary>
    /// 文件的 Hash 校验码。
    /// </summary>
    public string Hash;

    public override string ToString() {
        return StringUtils.FormatByteSize(Size) + " | " + LocalPath;
    }
}
