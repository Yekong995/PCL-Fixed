// 由 VB Modules\Minecraft\ModDownload.vb 迁移而来。
// 依赖 McInstance 的成员（DlClientJarGet、DlClientAssetIndexGet、DlClientFix）保留在 VB ModDownload 中，
// 其余成员全部迁入本类；VB 侧 ModDownload 保留同名转发壳。
// UI 依赖（McDownloadClientUpdateHint）通过 PclUiBridge 桥接，设置读写经由 PclCore.SettingProvider/SettingSetter。
#nullable disable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using MeloongCore;
using MeloongCore.Extensions;
using Newtonsoft.Json.Linq;

namespace PCLCS.Modules;

public static class PclDownload {

    #region DlClientList | Minecraft 客户端 版本列表

    /// <summary>
    /// 所有正式版的 Minecraft Drop 序数。
    /// 若从未完成过获取，返回 Nothing；否则必定存在元素，且从高到低排列。
    /// </summary>
    public static List<int> AllDrops {
        get {
            if (_AllDrops is null) {
                _AllDrops = PclCore.GetSetting<string>("CacheDrops").ToString().Split(',', true).Select(d => Convert.ToInt32(PclCore.Val(d))).ToList();
            }
            return _AllDrops.Any() ? _AllDrops : null; // 不要将 _AllDrops 再设为 Nothing，以防止反复获取设置尝试初始化
        }
        set {
            _AllDrops = value;
            PclCore.SetSetting("CacheDrops", value.Join(','));
        }
    }
    private static List<int> _AllDrops = null;

    // 主加载器
    /// <summary>
    /// Minecraft 客户端 版本列表，主加载器。
    /// 若要求镜像源必须包含某个版本，则将该版本 ID 作为输入（#5195）。
    /// </summary>
    public static readonly LoaderTask<string, DlClientListResult> DlClientListLoader = new LoaderTask<string, DlClientListResult>("DlClientList Main", DlClientListMain);
    private static void DlClientListMain(LoaderTask<string, DlClientListResult> Loader) {
        switch (PclCore.GetSetting<int>("ToolDownloadVersion")) {
            case 0:
                DlSourceLoader(Loader, new List<KeyValuePair<LoaderTask<string, DlClientListResult>, int>> {
                    new KeyValuePair<LoaderTask<string, DlClientListResult>, int>(DlClientListBmclapiLoader, 30),
                    new KeyValuePair<LoaderTask<string, DlClientListResult>, int>(DlClientListMojangLoader, 30 + 60)
                }, Loader.IsForceRestarting);
                break;
            case 1:
                DlSourceLoader(Loader, new List<KeyValuePair<LoaderTask<string, DlClientListResult>, int>> {
                    new KeyValuePair<LoaderTask<string, DlClientListResult>, int>(DlClientListMojangLoader, 5),
                    new KeyValuePair<LoaderTask<string, DlClientListResult>, int>(DlClientListBmclapiLoader, 5 + 30)
                }, Loader.IsForceRestarting);
                break;
            default:
                DlSourceLoader(Loader, new List<KeyValuePair<LoaderTask<string, DlClientListResult>, int>> {
                    new KeyValuePair<LoaderTask<string, DlClientListResult>, int>(DlClientListMojangLoader, 60),
                    new KeyValuePair<LoaderTask<string, DlClientListResult>, int>(DlClientListBmclapiLoader, 60 + 60)
                }, Loader.IsForceRestarting);
                break;
        }
        // 提取所有 Drop 序数
        List<int> Drops = new List<int>();
        foreach (JObject Version in Loader.Output.Value["versions"]) {
            string Id = Version["id"].ToString();
            if (Id.Contains("-")) continue;
            int Drop = McVersion.VersionToDrop(Id);
            if (Drop == 209) continue;
            Drops.Add(Drop);
        }
        AllDrops = Drops.Distinct().OrderByDescending(d => d).ToList();
    }

    // 各个下载源的分加载器
    /// <summary>
    /// Minecraft 客户端 版本列表，Mojang 官方源加载器。
    /// </summary>
    public static readonly LoaderTask<string, DlClientListResult> DlClientListMojangLoader = new LoaderTask<string, DlClientListResult>("DlClientList Mojang", DlClientListMojangMain);
    private static void DlClientListMojangMain(LoaderTask<string, DlClientListResult> Loader) {
        long StartTime = PclCore.GetTimeMs();
        JObject Json = (JObject)NetCore.NetRequestByClientRetry("https://launchermeta.mojang.com/mc/game/version_manifest.json", RequireJson: true).DeserializeJson();
        try {
            JArray Versions = (JArray)Json["versions"];
            if (Versions.Count < 200) throw new Exception("获取到的版本列表长度不足（" + Json + "）");
            // 确定官方源是否可用
            if (!DlPreferMojang) {
                long DeltaTime = PclCore.GetTimeMs() - StartTime;
                DlPreferMojang = DeltaTime < 4000;
                Logger.Info($"Mojang 官方源加载耗时：{DeltaTime}ms，{(DlPreferMojang ? "可优先使用官方源" : "不优先使用官方源")}");
            }
            // 返回
            Loader.Output = new DlClientListResult { IsOfficial = true, SourceName = "Mojang 官方源", Value = Json };
            // MC 更新提示
            string Version;
            // 快照版
            Version = (string)Json["latest"]["snapshot"];
            if (PclCore.GetSetting<bool>("ToolUpdateSnapshot") && PclCore.GetSetting<string>("ToolUpdateSnapshotLast") != "" &&
               PclCore.GetSetting<string>("ToolUpdateSnapshotLast") != Version && !IsHinted) {
                IsHinted = true;
                PclUiBridge.McDownloadClientUpdateHint?.Invoke(Version, Json);
            }
            PclCore.SetSetting("ToolUpdateSnapshotLast", Version ?? "Nothing");
            // 正式版
            Version = (string)Json["latest"]["release"];
            if (PclCore.GetSetting<bool>("ToolUpdateRelease") && PclCore.GetSetting<string>("ToolUpdateReleaseLast") != "" &&
               PclCore.GetSetting<string>("ToolUpdateReleaseLast") != Version && !IsHinted) {
                IsHinted = true;
                PclUiBridge.McDownloadClientUpdateHint?.Invoke(Version, Json);
            }
            PclCore.SetSetting("ToolUpdateReleaseLast", Version);
        } catch (Exception ex) {
            throw new Exception("Minecraft 官方源版本列表解析失败", ex);
        }
    }
    // 由 DlClientListMojangMain 的 Static 局部变量迁移而来
    private static bool IsHinted = false;
    /// <summary>
    /// Minecraft 客户端 版本列表，BMCLAPI 源加载器。
    /// </summary>
    public static readonly LoaderTask<string, DlClientListResult> DlClientListBmclapiLoader = new LoaderTask<string, DlClientListResult>("DlClientList Bmclapi", DlClientListBmclapiMain);
    private static void DlClientListBmclapiMain(LoaderTask<string, DlClientListResult> Loader) {
        JObject Json = (JObject)NetCore.NetRequestByClientRetry("https://bmclapi2.bangbang93.com/mc/game/version_manifest.json", RequireJson: true).DeserializeJson();
        try {
            JArray Versions = (JArray)Json["versions"];
            if (Versions.Count < 200) throw new Exception("获取到的版本列表长度不足（" + Json + "）");
            // 添加 PCL 特供项
            if (FileUtils.Exists(PclCore.GetPathTemp() + "Cache\\download.json")) Versions.Merge(FileUtils.ReadAsJson(PclCore.GetPathTemp() + "Cache\\download.json"));
            // 检查是否有要求的版本（#5195）
            if (!string.IsNullOrEmpty(Loader.Input)) {
                string Id = Loader.Input;
                if (DlClientListLoader.Output.Value is not null && !DlClientListLoader.Output.Value["versions"].Any(v => v["id"]?.ToString() == Id)) {
                    throw new Exception("BMCLAPI 源未包含目标版本 " + Id);
                }
            }
            // 返回
            Loader.Output = new DlClientListResult { IsOfficial = false, SourceName = "BMCLAPI", Value = Json };
        } catch (Exception ex) {
            throw new Exception("Minecraft BMCLAPI 版本列表解析失败（" + Json + "）", ex);
        }
    }

    /// <summary>
    /// 获取某个版本的 Json 下载地址，若失败则返回 Nothing。必须在工作线程执行。
    /// </summary>
    public static string DlClientListGet(string Id) {
        try {
            // 确认版本格式标准
            Id = Id.Replace("_", "-"); // 1.7.10_pre4 在版本列表中显示为 1.7.10-pre4
            if (Id != "1.0" && Id.EndsWithF(".0")) Id = Id.Substring(0, Id.Length - 2); // OptiFine 1.8 的下载会触发此问题，显示版本为 1.8.0
            // 获取 Minecraft 版本列表
            switch (DlClientListLoader.State) {
                case LoadState.Finished:
                    // 从当前的结果获取目标版本…
                    foreach (JObject Version in DlClientListLoader.Output.Value["versions"]) {
                        if ((string)Version["id"] == Id) return Version["url"].ToString();
                    }
                    // …如果没有，则重新尝试获取（在版本刚更新时可能出现这种情况，#5195）
                    DlClientListLoader.WaitForExit(Id, IsForceRestart: true);
                    break;
                case LoadState.Loading:
                    DlClientListLoader.WaitForExit(Id);
                    break;
                case LoadState.Failed:
                case LoadState.Canceled:
                case LoadState.Waiting:
                    DlClientListLoader.WaitForExit(Id, IsForceRestart: true);
                    break;
            }
            // 重新查找版本
            foreach (JObject Version in DlClientListLoader.Output.Value["versions"]) {
                if ((string)Version["id"] == Id) return Version["url"].ToString();
            }
            Logger.Warn($"未发现版本 {Id} 的 json 下载地址，版本列表返回为：{Environment.NewLine}{DlClientListLoader.Output.Value}");
            return null;
        } catch (Exception ex) {
            Logger.Warn(ex, $"获取版本 {Id} 的 json 下载地址失败");
            return null;
        }
    }

    #endregion

    #region DlOptiFineList | OptiFine 版本列表

    /// <summary>
    /// OptiFine 版本列表，主加载器。
    /// </summary>
    public static readonly LoaderTask<int, DlOptiFineListResult> DlOptiFineListLoader = new LoaderTask<int, DlOptiFineListResult>("DlOptiFineList Main", DlOptiFineListMain);
    private static void DlOptiFineListMain(LoaderTask<int, DlOptiFineListResult> Loader) {
        switch (PclCore.GetSetting<int>("ToolDownloadVersion")) {
            case 0:
                DlSourceLoader(Loader, new List<KeyValuePair<LoaderTask<int, DlOptiFineListResult>, int>> {
                    new KeyValuePair<LoaderTask<int, DlOptiFineListResult>, int>(DlOptiFineListBmclapiLoader, 30),
                    new KeyValuePair<LoaderTask<int, DlOptiFineListResult>, int>(DlOptiFineListOfficialLoader, 30 + 60)
                }, Loader.IsForceRestarting);
                break;
            case 1:
                DlSourceLoader(Loader, new List<KeyValuePair<LoaderTask<int, DlOptiFineListResult>, int>> {
                    new KeyValuePair<LoaderTask<int, DlOptiFineListResult>, int>(DlOptiFineListOfficialLoader, 5),
                    new KeyValuePair<LoaderTask<int, DlOptiFineListResult>, int>(DlOptiFineListBmclapiLoader, 5 + 30)
                }, Loader.IsForceRestarting);
                break;
            default:
                DlSourceLoader(Loader, new List<KeyValuePair<LoaderTask<int, DlOptiFineListResult>, int>> {
                    new KeyValuePair<LoaderTask<int, DlOptiFineListResult>, int>(DlOptiFineListOfficialLoader, 60),
                    new KeyValuePair<LoaderTask<int, DlOptiFineListResult>, int>(DlOptiFineListBmclapiLoader, 60 + 60)
                }, Loader.IsForceRestarting);
                break;
        }
    }

    /// <summary>
    /// OptiFine 版本列表，官方源。
    /// </summary>
    public static readonly LoaderTask<int, DlOptiFineListResult> DlOptiFineListOfficialLoader = new LoaderTask<int, DlOptiFineListResult>("DlOptiFineList Official", DlOptiFineListOfficialMain);
    private static void DlOptiFineListOfficialMain(LoaderTask<int, DlOptiFineListResult> Loader) {
        string Result = NetCore.NetRequestByClientRetry("https://optifine.net/downloads", Encoding: Encoding.Default);
        if (Result.Length < 200) throw new Exception("获取到的版本列表长度不足（" + Result + "）");
        try {
            // 获取所有版本信息
            List<string> Forge = Result.RegexSearch("(?<=colForge'>)[^<]*").ToList();
            List<string> ReleaseTime = Result.RegexSearch("(?<=colDate'>)[^<]+").ToList();
            List<string> Name = Result.RegexSearch("(?<=OptiFine_)[0-9A-Za-z_.]+(?=.jar\")").ToList();
            if (ReleaseTime.Count != Name.Count) throw new Exception("版本与发布时间数据无法对应");
            if (Forge.Count != Name.Count) throw new Exception("版本与 Forge 兼容数据无法对应");
            if (ReleaseTime.Count < 10) throw new Exception("获取到的版本数量不足（" + Result + "）");
            // 转化为列表输出
            List<DlOptiFineListEntry> Versions = new List<DlOptiFineListEntry>();
            for (int i = 0; i < ReleaseTime.Count; i++) {
                Name[i] = Name[i].Replace("_", " ");
                DlOptiFineListEntry Entry = new DlOptiFineListEntry {
                    DisplayName = Name[i].Replace("HD U ", "").Replace(".0 ", " "),
                    ReleaseTime = new[] { ReleaseTime[i].Split('.')[2], ReleaseTime[i].Split('.')[1], ReleaseTime[i].Split('.')[0] }.Join('/'),
                    IsPreview = Name[i].ContainsIgnoreCase("pre"),
                    Inherit = Name[i].ToString().Split(' ')[0],
                    FileName = (Name[i].ContainsIgnoreCase("pre") ? "preview_" : "") + "OptiFine_" + Name[i].Replace(" ", "_") + ".jar",
                    RequiredForgeVersion = Forge[i].Replace("Forge ", "").Replace("#", "")
                };
                if (Entry.RequiredForgeVersion.Contains("N/A")) Entry.RequiredForgeVersion = null;
                Entry.InstanceName = Entry.Inherit + "-OptiFine_" + Name[i].AfterFirst(" ").Replace(" ", "_");
                Versions.Add(Entry);
            }
            Loader.Output = new DlOptiFineListResult { IsOfficial = true, SourceName = "OptiFine 官方源", Value = Versions };
        } catch (Exception ex) {
            throw new Exception("OptiFine 官方源版本列表解析失败（" + Result + "）", ex);
        }
    }

    /// <summary>
    /// OptiFine 版本列表，BMCLAPI。
    /// </summary>
    public static readonly LoaderTask<int, DlOptiFineListResult> DlOptiFineListBmclapiLoader = new LoaderTask<int, DlOptiFineListResult>("DlOptiFineList Bmclapi", DlOptiFineListBmclapiMain);
    private static void DlOptiFineListBmclapiMain(LoaderTask<int, DlOptiFineListResult> Loader) {
        JArray Json = (JArray)NetCore.NetRequestByClientRetry("https://bmclapi2.bangbang93.com/optifine/versionList", RequireJson: true).DeserializeJson();
        try {
            List<DlOptiFineListEntry> Versions = new List<DlOptiFineListEntry>();
            foreach (JObject Token in Json) {
                DlOptiFineListEntry Entry = new DlOptiFineListEntry {
                    DisplayName = (Token["mcversion"].ToString() + Token["type"].ToString().Replace("HD_U", "").Replace("_", " ") + " " + Token["patch"].ToString()).Replace(".0 ", " "),
                    ReleaseTime = "",
                    IsPreview = Token["patch"].ToString().ContainsIgnoreCase("pre"),
                    Inherit = Token["mcversion"].ToString(),
                    FileName = Token["filename"].ToString(),
                    RequiredForgeVersion = (Token["forge"] ?? (JToken)"").ToString().Replace("Forge ", "").Replace("#", "")
                };
                if (Entry.RequiredForgeVersion.Contains("N/A")) Entry.RequiredForgeVersion = null;
                Entry.InstanceName = Entry.Inherit + "-OptiFine_" + (Token["type"].ToString() + " " + Token["patch"].ToString()).Replace(".0 ", " ").Replace(" ", "_").Replace(Entry.Inherit + "_", "");
                Versions.Add(Entry);
            }
            Loader.Output = new DlOptiFineListResult { IsOfficial = false, SourceName = "BMCLAPI", Value = Versions };
        } catch (Exception ex) {
            throw new Exception("OptiFine BMCLAPI 版本列表解析失败（" + Json + "）", ex);
        }
    }

    #endregion

    #region DlForgeList | Forge Minecraft 版本列表

    /// <summary>
    /// Forge 版本列表，主加载器。
    /// </summary>
    public static readonly LoaderTask<int, DlForgeListResult> DlForgeListLoader = new LoaderTask<int, DlForgeListResult>("DlForgeList Main", DlForgeListMain);
    private static void DlForgeListMain(LoaderTask<int, DlForgeListResult> Loader) {
        switch (PclCore.GetSetting<int>("ToolDownloadVersion")) {
            case 0:
                DlSourceLoader(Loader, new List<KeyValuePair<LoaderTask<int, DlForgeListResult>, int>> {
                    new KeyValuePair<LoaderTask<int, DlForgeListResult>, int>(DlForgeListBmclapiLoader, 30),
                    new KeyValuePair<LoaderTask<int, DlForgeListResult>, int>(DlForgeListOfficialLoader, 30 + 60)
                }, Loader.IsForceRestarting);
                break;
            case 1:
                DlSourceLoader(Loader, new List<KeyValuePair<LoaderTask<int, DlForgeListResult>, int>> {
                    new KeyValuePair<LoaderTask<int, DlForgeListResult>, int>(DlForgeListOfficialLoader, 5),
                    new KeyValuePair<LoaderTask<int, DlForgeListResult>, int>(DlForgeListBmclapiLoader, 5 + 30)
                }, Loader.IsForceRestarting);
                break;
            default:
                DlSourceLoader(Loader, new List<KeyValuePair<LoaderTask<int, DlForgeListResult>, int>> {
                    new KeyValuePair<LoaderTask<int, DlForgeListResult>, int>(DlForgeListOfficialLoader, 60),
                    new KeyValuePair<LoaderTask<int, DlForgeListResult>, int>(DlForgeListBmclapiLoader, 60 + 60)
                }, Loader.IsForceRestarting);
                break;
        }
    }

    /// <summary>
    /// Forge 版本列表，官方源。
    /// </summary>
    public static readonly LoaderTask<int, DlForgeListResult> DlForgeListOfficialLoader = new LoaderTask<int, DlForgeListResult>("DlForgeList Official", DlForgeListOfficialMain);
    private static void DlForgeListOfficialMain(LoaderTask<int, DlForgeListResult> Loader) {
        string Result = NetCore.NetRequestByClientRetry("https://files.minecraftforge.net/maven/net/minecraftforge/forge/index_1.2.4.html", Encoding: Encoding.Default, Accept: "text/html", SimulateBrowserHeaders: true);
        if (Result.Length < 200) throw new Exception("获取到的版本列表长度不足（" + Result + "）");
        // 获取所有版本信息
        List<string> Names = Result.RegexSearch(@"(?<=a href=""index_)[0-9.]+((_pre|[_-]snapshot[_-]?)[0-9]?)?(?=.html)").ToList();
        Names.Add("1.2.4"); // 1.2.4 不会被匹配上
        if (Names.Count < 10) throw new Exception("获取到的版本数量不足（" + Result + "）");
        Loader.Output = new DlForgeListResult { IsOfficial = true, SourceName = "Forge 官方源", Value = Names };
    }

    /// <summary>
    /// Forge 版本列表，BMCLAPI。
    /// </summary>
    public static readonly LoaderTask<int, DlForgeListResult> DlForgeListBmclapiLoader = new LoaderTask<int, DlForgeListResult>("DlForgeList Bmclapi", DlForgeListBmclapiMain);
    private static void DlForgeListBmclapiMain(LoaderTask<int, DlForgeListResult> Loader) {
        JArray Result = (JArray)NetCore.NetRequestByClientRetry("https://bmclapi2.bangbang93.com/forge/minecraft",
                                                       Encoding: Encoding.Default, RequireJson: true).DeserializeJson();
        List<string> Names = Result.Select(v => v.ToString()).ToList();
        if (Names.Count < 10) throw new Exception("获取到的版本数量不足");
        Loader.Output = new DlForgeListResult { IsOfficial = false, SourceName = "BMCLAPI", Value = Names };
    }

    #endregion

    #region DlForgeVersion | Forge 版本列表

    /// <summary>
    /// Forge 版本列表，主加载器。
    /// </summary>
    public static void DlForgeVersionMain(LoaderTask<string, List<DlForgeVersionEntry>> Loader) {
        LoaderTask<string, List<DlForgeVersionEntry>> DlForgeVersionOfficialLoader = new LoaderTask<string, List<DlForgeVersionEntry>>("DlForgeVersion Official", DlForgeVersionOfficialMain);
        LoaderTask<string, List<DlForgeVersionEntry>> DlForgeVersionBmclapiLoader = new LoaderTask<string, List<DlForgeVersionEntry>>("DlForgeVersion Bmclapi", DlForgeVersionBmclapiMain);
        switch (PclCore.GetSetting<int>("ToolDownloadVersion")) {
            case 0:
                DlSourceLoader(Loader, new List<KeyValuePair<LoaderTask<string, List<DlForgeVersionEntry>>, int>> {
                    new KeyValuePair<LoaderTask<string, List<DlForgeVersionEntry>>, int>(DlForgeVersionBmclapiLoader, 30),
                    new KeyValuePair<LoaderTask<string, List<DlForgeVersionEntry>>, int>(DlForgeVersionOfficialLoader, 30 + 60)
                }, Loader.IsForceRestarting);
                break;
            case 1:
                DlSourceLoader(Loader, new List<KeyValuePair<LoaderTask<string, List<DlForgeVersionEntry>>, int>> {
                    new KeyValuePair<LoaderTask<string, List<DlForgeVersionEntry>>, int>(DlForgeVersionOfficialLoader, 5),
                    new KeyValuePair<LoaderTask<string, List<DlForgeVersionEntry>>, int>(DlForgeVersionBmclapiLoader, 5 + 30)
                }, Loader.IsForceRestarting);
                break;
            default:
                DlSourceLoader(Loader, new List<KeyValuePair<LoaderTask<string, List<DlForgeVersionEntry>>, int>> {
                    new KeyValuePair<LoaderTask<string, List<DlForgeVersionEntry>>, int>(DlForgeVersionOfficialLoader, 60),
                    new KeyValuePair<LoaderTask<string, List<DlForgeVersionEntry>>, int>(DlForgeVersionBmclapiLoader, 60 + 60)
                }, Loader.IsForceRestarting);
                break;
        }
    }

    /// <summary>
    /// Forge 版本列表，官方源。
    /// </summary>
    public static void DlForgeVersionOfficialMain(LoaderTask<string, List<DlForgeVersionEntry>> Loader) {
        string Result;
        try {
            Result = NetCore.NetRequestByLoader("https://files.minecraftforge.net/maven/net/minecraftforge/forge/index_" +
                                  Loader.Input.Replace("-", "_") + // 兼容 Forge 1.7.10-pre4，#4057
                                  ".html", SimulateBrowserHeaders: true);
        } catch (Exception ex) {
            if (ex.GetDisplay(false).Contains("(404)")) {
                throw new Exception("无");
            } else {
                throw;
            }
        }
        if (Result.Length < 1000) throw new Exception("获取到的版本列表长度不足（" + Result + "）");
        List<DlForgeVersionEntry> Versions = new List<DlForgeVersionEntry>();
        try {
            // 分割版本信息
            string[] VersionCodes = Result.Substring(0, Result.LastIndexOfF("</table>")).Split("<td class=\"download-version\"");
            // 获取所有版本信息
            for (int i = 1; i < VersionCodes.Length; i++) {
                string VersionCode = VersionCodes[i];
                try {
                    // 基础信息获取
                    string Name = VersionCode.RegexSeek("(?<=[^(0-9)]+)[0-9\\.]+");
                    bool IsRecommended = VersionCode.Contains("fa promo-recommended");
                    string Inherit = Loader.Input;
                    // 分支获取
                    string Branch = VersionCode.RegexSeek($"(?<=-{Name}-)[^-\"]+(?=-[a-z]+.[a-z]{{3}})");
                    if (string.IsNullOrWhiteSpace(Branch)) Branch = null;
                    // 发布时间获取
                    string ReleaseTimeOriginal = VersionCode.RegexSeek("(?<=\"download-time\" title=\")[^\"]+");
                    string[] ReleaseTimeSplit = ReleaseTimeOriginal.Split(" -:".ToCharArray()); // 原格式："2021-02-15 03:24:02"
                    DateTime ReleaseDate = new DateTime(Convert.ToInt32(ReleaseTimeSplit[0]), Convert.ToInt32(ReleaseTimeSplit[1]), Convert.ToInt32(ReleaseTimeSplit[2]), // 年月日
                                                Convert.ToInt32(ReleaseTimeSplit[3]), Convert.ToInt32(ReleaseTimeSplit[4]), Convert.ToInt32(ReleaseTimeSplit[5]), // 时分秒
                                                0, DateTimeKind.Utc); // 以 UTC 时间作为标准
                    string ReleaseTime = ReleaseDate.ToLocalTime().ToString("yyyy'/'MM'/'dd HH':'mm"); // 时区与格式转换
                    // 分类与 MD5 获取
                    string MD5, Category;
                    if (VersionCode.Contains("classifier-installer\"")) {
                        // 类型为 installer.jar，支持范围 ~753 (~ 1.6.1 部分), 738~684 (1.5.2 全部)
                        VersionCode = VersionCode.Substring(VersionCode.IndexOfF("installer.jar"));
                        MD5 = VersionCode.RegexSeek("(?<=MD5:</strong> )[^<]+");
                        Category = "installer";
                    } else if (VersionCode.Contains("classifier-universal\"")) {
                        // 类型为 universal.zip，支持范围 751~449 (1.6.1 部分), 682~183 (1.5.1 ~ 1.3.2 部分)
                        VersionCode = VersionCode.Substring(VersionCode.IndexOfF("universal.zip"));
                        MD5 = VersionCode.RegexSeek("(?<=MD5:</strong> )[^<]+");
                        Category = "universal";
                    } else if (VersionCode.Contains("client.zip")) {
                        // 类型为 client.zip，支持范围 182~ (1.3.2 部分 ~)
                        VersionCode = VersionCode.Substring(VersionCode.IndexOfF("client.zip"));
                        MD5 = VersionCode.RegexSeek("(?<=MD5:</strong> )[^<]+");
                        Category = "client";
                    } else {
                        // 没有任何下载（1.6.4 有一部分这种情况）
                        continue;
                    }
                    // 添加进列表
                    Versions.Add(new DlForgeVersionEntry(Name, Branch, Inherit) { Category = Category, IsRecommended = IsRecommended, Hash = MD5.ReplaceLineEndings(""), ReleaseTime = ReleaseTime });
                } catch (Exception ex) {
                    throw new Exception("Forge 官方源版本信息提取失败（" + VersionCode + "）", ex);
                }
            }
        } catch (Exception ex) {
            throw new Exception("Forge 官方源版本列表解析失败（" + Result + "）", ex);
        }
        if (!Versions.Any()) throw new Exception("无");
        Loader.Output = Versions;
    }

    /// <summary>
    /// Forge 版本列表，BMCLAPI。
    /// </summary>
    public static void DlForgeVersionBmclapiMain(LoaderTask<string, List<DlForgeVersionEntry>> Loader) {
        JArray Json = (JArray)NetCore.NetRequestByClientRetry("https://bmclapi2.bangbang93.com/forge/minecraft/" +
            Loader.Input.Replace("-", "_"), RequireJson: true).DeserializeJson(); // 兼容 Forge 1.7.10-pre4，#4057
        List<DlForgeVersionEntry> Versions = new List<DlForgeVersionEntry>();
        try {
            string Recommended = ForgeRecommendedGet(Loader.Input);
            foreach (JObject Token in Json) {
                // 分类与 Hash 获取
                string Hash = null, Category = "unknown"; int Proi = -1;
                foreach (JObject File in Token["files"]) {
                    switch (File["category"].ToString()) {
                        case "installer":
                            if (File["format"].ToString() == "jar") {
                                // 类型为 installer.jar，支持范围 ~753 (~ 1.6.1 部分), 738~684 (1.5.2 全部)
                                Hash = (string)File["hash"];
                                Category = "installer";
                                Proi = 2;
                            }
                            break;
                        case "universal":
                            if (Proi <= 1 && File["format"].ToString() == "zip") {
                                // 类型为 universal.zip，支持范围 751~449 (1.6.1 部分), 682~183 (1.5.1 ~ 1.3.2 部分)
                                Hash = (string)File["hash"];
                                Category = "universal";
                                Proi = 1;
                            }
                            break;
                        case "client":
                            if (Proi <= 0 && File["format"].ToString() == "zip") {
                                // 类型为 client.zip，支持范围 182~ (1.3.2 部分 ~)
                                Hash = (string)File["hash"];
                                Category = "client";
                                Proi = 0;
                            }
                            break;
                    }
                }
                // 获取 Entry
                string Branch = (string)Token["branch"];
                string Name = (string)Token["version"];
                // 基础信息获取
                DlForgeVersionEntry Entry = new DlForgeVersionEntry(Name, Branch, Loader.Input) { Hash = Hash, Category = Category, IsRecommended = Recommended == Name };
                _ = Token["modified"].ToString().Split('-', 'T', ':', '.', ' ', '/');
                Entry.ReleaseTime = Token["modified"].ToObject<DateTime>().ToLocalTime().ToString("yyyy'/'MM'/'dd HH':'mm");
                // 添加项
                Versions.Add(Entry);
            }
        } catch (Exception ex) {
            throw new Exception("Forge BMCLAPI 版本列表解析失败（" + Json + "）", ex);
        }
        if (!Versions.Any()) throw new Exception("无");
        Loader.Output = Versions;
    }

    /// <summary>
    /// 尝试获取某个 MC 版本对应的 Forge 推荐版本。如果不可用会返回 Nothing。
    /// 由 ModDownloadLib.McDownloadForgeRecommendedGet 迁移而来（VB 原实现暂保留在 ModDownloadLib 中，待其迁移时移除）。
    /// </summary>
    private static string ForgeRecommendedGet(string McVersionName) {
        try {
            if (McVersionName is null) return null;
            string List = FileUtils.TryReadAsString(PclCore.GetPathTemp() + "Cache\\ForgeRecommendedList.json");
            if (string.IsNullOrEmpty(List)) {
                Logger.Info("没有 Forge 推荐版本缓存文件");
                return null;
            }
            JObject Json = (JObject)List.DeserializeJson();
            if (Json is null || !(McVersionName ?? "").Contains(".") || !Json.ContainsKey(McVersionName)) return null;
            return (Json[McVersionName] ?? (JToken)"").ToString();
        } catch (Exception ex) {
            Logger.Error(ex, $"获取 Forge 推荐版本失败（{McVersionName ?? "null"}）");
            return null;
        }
    }

    #endregion

    #region DlNeoForgeList | NeoForge 版本列表

    /// <summary>
    /// NeoForge 版本列表，主加载器。
    /// </summary>
    public static readonly LoaderTask<int, DlNeoForgeListResult> DlNeoForgeListLoader = new LoaderTask<int, DlNeoForgeListResult>("DlNeoForgeList Main", DlNeoForgeListMain);
    private static void DlNeoForgeListMain(LoaderTask<int, DlNeoForgeListResult> Loader) {
        switch (PclCore.GetSetting<int>("ToolDownloadVersion")) {
            case 0:
                DlSourceLoader(Loader, new List<KeyValuePair<LoaderTask<int, DlNeoForgeListResult>, int>> {
                    new KeyValuePair<LoaderTask<int, DlNeoForgeListResult>, int>(DlNeoForgeListBmclapiLoader, 30),
                    new KeyValuePair<LoaderTask<int, DlNeoForgeListResult>, int>(DlNeoForgeListOfficialLoader, 30 + 60)
                }, Loader.IsForceRestarting);
                break;
            case 1:
                DlSourceLoader(Loader, new List<KeyValuePair<LoaderTask<int, DlNeoForgeListResult>, int>> {
                    new KeyValuePair<LoaderTask<int, DlNeoForgeListResult>, int>(DlNeoForgeListOfficialLoader, 5),
                    new KeyValuePair<LoaderTask<int, DlNeoForgeListResult>, int>(DlNeoForgeListBmclapiLoader, 5 + 30)
                }, Loader.IsForceRestarting);
                break;
            default:
                DlSourceLoader(Loader, new List<KeyValuePair<LoaderTask<int, DlNeoForgeListResult>, int>> {
                    new KeyValuePair<LoaderTask<int, DlNeoForgeListResult>, int>(DlNeoForgeListOfficialLoader, 60),
                    new KeyValuePair<LoaderTask<int, DlNeoForgeListResult>, int>(DlNeoForgeListBmclapiLoader, 60 + 60)
                }, Loader.IsForceRestarting);
                break;
        }
    }

    /// <summary>
    /// NeoForge 版本列表，官方源。
    /// </summary>
    public static readonly LoaderTask<int, DlNeoForgeListResult> DlNeoForgeListOfficialLoader = new LoaderTask<int, DlNeoForgeListResult>("DlNeoForgeList Official", DlNeoForgeListOfficialMain);
    private static void DlNeoForgeListOfficialMain(LoaderTask<int, DlNeoForgeListResult> Loader) {
        // 获取版本列表 JSON
        string ResultLatest = NetCore.NetRequestByClientRetry("https://maven.neoforged.net/api/maven/versions/releases/net/neoforged/neoforge", RequireJson: true);
        string ResultLegacy = NetCore.NetRequestByClientRetry("https://maven.neoforged.net/api/maven/versions/releases/net/neoforged/forge", RequireJson: true);
        if (ResultLatest.Length < 100 || ResultLegacy.Length < 100) throw new Exception("获取到的版本列表长度不足（" + ResultLatest + "）");
        // 解析
        try {
            Loader.Output = new DlNeoForgeListResult { IsOfficial = true, SourceName = "NeoForge 官方源",
                Value = GetNeoForgeEntries(ResultLatest, ResultLegacy)
            };
        } catch (Exception ex) {
            throw new Exception("NeoForge 官方源版本列表解析失败（" + ResultLatest + Environment.NewLine + Environment.NewLine + ResultLegacy + "）", ex);
        }
    }

    /// <summary>
    /// NeoForge 版本列表，BMCLAPI。
    /// </summary>
    public static readonly LoaderTask<int, DlNeoForgeListResult> DlNeoForgeListBmclapiLoader = new LoaderTask<int, DlNeoForgeListResult>("DlNeoForgeList Bmclapi", DlNeoForgeListBmclapiMain);
    public static void DlNeoForgeListBmclapiMain(LoaderTask<int, DlNeoForgeListResult> Loader) {
        // 获取版本列表 JSON
        string ResultLatest = NetCore.NetRequestByClientRetry("https://bmclapi2.bangbang93.com/neoforge/meta/api/maven/details/releases/net/neoforged/neoforge", RequireJson: true);
        string ResultLegacy = NetCore.NetRequestByClientRetry("https://bmclapi2.bangbang93.com/neoforge/meta/api/maven/details/releases/net/neoforged/forge", RequireJson: true);
        if (ResultLatest.Length < 100 || ResultLegacy.Length < 100) throw new Exception("获取到的版本列表长度不足（" + ResultLatest + "）");
        // 解析
        try {
            Loader.Output = new DlNeoForgeListResult { IsOfficial = true, SourceName = "BMCLAPI",
                Value = GetNeoForgeEntries(ResultLatest, ResultLegacy)
            };
        } catch (Exception ex) {
            throw new Exception("NeoForge BMCLAPI 版本列表解析失败（" + ResultLatest + Environment.NewLine + Environment.NewLine + ResultLegacy + "）", ex);
        }
    }

    private static List<DlNeoForgeListEntry> GetNeoForgeEntries(string LatestJson, string LatestLegacyJson) {
        IEnumerable<string> VersionNames = (LatestLegacyJson + LatestJson).RegexSearch(
            "(?<=\")(1\\.20\\.1-)?\\d+\\.[^\\.]+\\.\\d+(\\.\\d+)?(-(beta|alpha)(\\.\\d+)?)?(\\+snapshot-\\d+)?(?=\")");
        List<DlNeoForgeListEntry> Versions = VersionNames.
            Where(name => name != "47.1.82"). // 这个版本虽然在版本列表中，但不能下载
            Select(name => new DlNeoForgeListEntry(name)).
            OrderByDescending(a => a).ToList();
        if (!Versions.Any()) throw new Exception("无");
        return Versions;
    }

    #endregion

    #region DlLiteLoaderList | LiteLoader 版本列表

    /// <summary>
    /// LiteLoader 版本列表，主加载器。
    /// </summary>
    public static readonly LoaderTask<int, DlLiteLoaderListResult> DlLiteLoaderListLoader = new LoaderTask<int, DlLiteLoaderListResult>("DlLiteLoaderList Main", DlLiteLoaderListMain);
    private static void DlLiteLoaderListMain(LoaderTask<int, DlLiteLoaderListResult> Loader) {
        switch (PclCore.GetSetting<int>("ToolDownloadVersion")) {
            case 0:
                DlSourceLoader(Loader, new List<KeyValuePair<LoaderTask<int, DlLiteLoaderListResult>, int>> {
                    new KeyValuePair<LoaderTask<int, DlLiteLoaderListResult>, int>(DlLiteLoaderListBmclapiLoader, 30),
                    new KeyValuePair<LoaderTask<int, DlLiteLoaderListResult>, int>(DlLiteLoaderListOfficialLoader, 30 + 60)
                }, Loader.IsForceRestarting);
                break;
            case 1:
                DlSourceLoader(Loader, new List<KeyValuePair<LoaderTask<int, DlLiteLoaderListResult>, int>> {
                    new KeyValuePair<LoaderTask<int, DlLiteLoaderListResult>, int>(DlLiteLoaderListOfficialLoader, 5),
                    new KeyValuePair<LoaderTask<int, DlLiteLoaderListResult>, int>(DlLiteLoaderListBmclapiLoader, 5 + 30)
                }, Loader.IsForceRestarting);
                break;
            default:
                DlSourceLoader(Loader, new List<KeyValuePair<LoaderTask<int, DlLiteLoaderListResult>, int>> {
                    new KeyValuePair<LoaderTask<int, DlLiteLoaderListResult>, int>(DlLiteLoaderListOfficialLoader, 60),
                    new KeyValuePair<LoaderTask<int, DlLiteLoaderListResult>, int>(DlLiteLoaderListBmclapiLoader, 60 + 60)
                }, Loader.IsForceRestarting);
                break;
        }
    }

    /// <summary>
    /// LiteLoader 版本列表，官方源。
    /// </summary>
    public static readonly LoaderTask<int, DlLiteLoaderListResult> DlLiteLoaderListOfficialLoader = new LoaderTask<int, DlLiteLoaderListResult>("DlLiteLoaderList Official", DlLiteLoaderListOfficialMain);
    private static void DlLiteLoaderListOfficialMain(LoaderTask<int, DlLiteLoaderListResult> Loader) {
        JObject Result = (JObject)NetCore.NetRequestByClientRetry("https://dl.liteloader.com/versions/versions.json", RequireJson: true).DeserializeJson();
        try {
            JObject Json = (JObject)Result["versions"];
            List<DlLiteLoaderListEntry> Versions = new List<DlLiteLoaderListEntry>();
            foreach (KeyValuePair<string, JToken> Pair in Json) {
                if (Pair.Key.StartsWithF("1.6") || Pair.Key.StartsWithF("1.5")) continue;
                JToken RealEntry = (Pair.Value["artefacts"] ?? Pair.Value["snapshots"])["com.mumfrey:liteloader"]["latest"];
                Versions.Add(new DlLiteLoaderListEntry {
                             Inherit = Pair.Key,
                             IsLegacy = int.Parse(Pair.Key.Split('.')[1]) < 8,
                             IsPreview = RealEntry["stream"].ToString().Lower() == "snapshot",
                             FileName = "liteloader-installer-" + Pair.Key + (Pair.Key == "1.8" || Pair.Key == "1.9" ? ".0" : "") + "-00-SNAPSHOT.jar",
                             MD5 = (string)RealEntry["md5"],
                             ReleaseTime = PclCore.GetLocalTime(PclCore.GetDate(Convert.ToInt32(RealEntry["timestamp"]))).ToString("yyyy'/'MM'/'dd HH':'mm"),
                             JsonToken = RealEntry
                         });
            }
            Loader.Output = new DlLiteLoaderListResult { IsOfficial = true, SourceName = "LiteLoader 官方源", Value = Versions };
        } catch (Exception ex) {
            throw new Exception("LiteLoader 官方源版本列表解析失败（" + Result + "）", ex);
        }
    }

    /// <summary>
    /// LiteLoader 版本列表，BMCLAPI。
    /// </summary>
    public static readonly LoaderTask<int, DlLiteLoaderListResult> DlLiteLoaderListBmclapiLoader = new LoaderTask<int, DlLiteLoaderListResult>("DlLiteLoaderList Bmclapi", DlLiteLoaderListBmclapiMain);
    private static void DlLiteLoaderListBmclapiMain(LoaderTask<int, DlLiteLoaderListResult> Loader) {
        JObject Result = (JObject)NetCore.NetRequestByClientRetry("https://bmclapi2.bangbang93.com/maven/com/mumfrey/liteloader/versions.json", RequireJson: true).DeserializeJson();
        try {
            JObject Json = (JObject)Result["versions"];
            List<DlLiteLoaderListEntry> Versions = new List<DlLiteLoaderListEntry>();
            foreach (KeyValuePair<string, JToken> Pair in Json) {
                if (Pair.Key.StartsWithF("1.6") || Pair.Key.StartsWithF("1.5")) continue;
                JToken RealEntry = (Pair.Value["artefacts"] ?? Pair.Value["snapshots"])["com.mumfrey:liteloader"]["latest"];
                Versions.Add(new DlLiteLoaderListEntry {
                             Inherit = Pair.Key,
                             IsLegacy = int.Parse(Pair.Key.Split('.')[1]) < 8,
                             IsPreview = RealEntry["stream"].ToString().Lower() == "snapshot",
                             FileName = "liteloader-installer-" + Pair.Key + (Pair.Key == "1.8" || Pair.Key == "1.9" ? ".0" : "") + "-00-SNAPSHOT.jar",
                             MD5 = (string)RealEntry["md5"],
                             ReleaseTime = PclCore.GetLocalTime(PclCore.GetDate(Convert.ToInt32(RealEntry["timestamp"]))).ToString("yyyy'/'MM'/'dd HH':'mm"),
                             JsonToken = RealEntry
                         });
            }
            Loader.Output = new DlLiteLoaderListResult { IsOfficial = false, SourceName = "BMCLAPI", Value = Versions };
        } catch (Exception ex) {
            throw new Exception("LiteLoader BMCLAPI 版本列表解析失败（" + Result + "）", ex);
        }
    }

    #endregion

    #region DlFabricList | Fabric 列表

    /// <summary>
    /// Fabric 列表，主加载器。
    /// </summary>
    public static readonly LoaderTask<int, DlFabricListResult> DlFabricListLoader = new LoaderTask<int, DlFabricListResult>("DlFabricList Main", DlFabricListMain);
    private static void DlFabricListMain(LoaderTask<int, DlFabricListResult> Loader) {
        switch (PclCore.GetSetting<int>("ToolDownloadVersion")) {
            case 0:
                DlSourceLoader(Loader, new List<KeyValuePair<LoaderTask<int, DlFabricListResult>, int>> {
                    new KeyValuePair<LoaderTask<int, DlFabricListResult>, int>(DlFabricListBmclapiLoader, 30),
                    new KeyValuePair<LoaderTask<int, DlFabricListResult>, int>(DlFabricListOfficialLoader, 30 + 60)
                }, Loader.IsForceRestarting);
                break;
            case 1:
                DlSourceLoader(Loader, new List<KeyValuePair<LoaderTask<int, DlFabricListResult>, int>> {
                    new KeyValuePair<LoaderTask<int, DlFabricListResult>, int>(DlFabricListOfficialLoader, 5),
                    new KeyValuePair<LoaderTask<int, DlFabricListResult>, int>(DlFabricListBmclapiLoader, 5 + 30)
                }, Loader.IsForceRestarting);
                break;
            default:
                DlSourceLoader(Loader, new List<KeyValuePair<LoaderTask<int, DlFabricListResult>, int>> {
                    new KeyValuePair<LoaderTask<int, DlFabricListResult>, int>(DlFabricListOfficialLoader, 60),
                    new KeyValuePair<LoaderTask<int, DlFabricListResult>, int>(DlFabricListBmclapiLoader, 60 + 60)
                }, Loader.IsForceRestarting);
                break;
        }
    }

    /// <summary>
    /// Fabric 列表，官方源。
    /// </summary>
    public static readonly LoaderTask<int, DlFabricListResult> DlFabricListOfficialLoader = new LoaderTask<int, DlFabricListResult>("DlFabricList Official", DlFabricListOfficialMain);
    private static void DlFabricListOfficialMain(LoaderTask<int, DlFabricListResult> Loader) {
        JObject Result = (JObject)NetCore.NetRequestByClientRetry("https://meta.fabricmc.net/v2/versions", RequireJson: true).DeserializeJson();
        try {
            DlFabricListResult Output = new DlFabricListResult { IsOfficial = true, SourceName = "Fabric 官方源", Value = Result };
            if (Output.Value["game"] is null || Output.Value["loader"] is null || Output.Value["installer"] is null) throw new Exception("获取到的列表缺乏必要项");
            Loader.Output = Output;
        } catch (Exception ex) {
            throw new Exception("Fabric 官方源版本列表解析失败（" + Result + "）", ex);
        }
    }

    /// <summary>
    /// Fabric 列表，BMCLAPI。
    /// </summary>
    public static readonly LoaderTask<int, DlFabricListResult> DlFabricListBmclapiLoader = new LoaderTask<int, DlFabricListResult>("DlFabricList Bmclapi", DlFabricListBmclapiMain);
    private static void DlFabricListBmclapiMain(LoaderTask<int, DlFabricListResult> Loader) {
        JObject Result = (JObject)NetCore.NetRequestByClientRetry("https://bmclapi2.bangbang93.com/fabric-meta/v2/versions", RequireJson: true).DeserializeJson();
        try {
            DlFabricListResult Output = new DlFabricListResult { IsOfficial = false, SourceName = "BMCLAPI", Value = Result };
            if (Output.Value["game"] is null || Output.Value["loader"] is null || Output.Value["installer"] is null) throw new Exception("获取到的列表缺乏必要项");
            Loader.Output = Output;
        } catch (Exception ex) {
            throw new Exception("Fabric BMCLAPI 版本列表解析失败（" + Result + "）", ex);
        }
    }

    /// <summary>
    /// Fabric API 列表，官方源。
    /// </summary>
    public static readonly LoaderTask<int, List<ResourceVersion>> DlFabricApiLoader = new LoaderTask<int, List<ResourceVersion>>("Fabric API List Loader",
        Task => Task.Output = ResourceVersion.FromProjectId("fabric-api", ResourcePlatforms.Modrinth, LoadDependencies: false));

    /// <summary>
    /// OptiFabric 列表，官方源。
    /// </summary>
    public static readonly LoaderTask<int, List<ResourceVersion>> DlOptiFabricLoader = new LoaderTask<int, List<ResourceVersion>>("OptiFabric List Loader",
        Task => Task.Output = ResourceVersion.FromProjectId("322385", ResourcePlatforms.CurseForge, LoadDependencies: false));

    #endregion

    #region DlSource | 镜像下载源

    private static bool DlPreferMojang = false;
    /// <summary>
    /// 下载文件（而非获取版本列表）的时候，是否优先使用官方源。
    /// </summary>
    public static bool DlSourcePreferMojang {
        get {
            return PclCore.GetSetting<int>("ToolDownloadSource") == 2 || (PclCore.GetSetting<int>("ToolDownloadSource") == 1 && DlPreferMojang);
        }
    }
    /// <summary>
    /// 下载文件（而非获取版本列表）的时候，根据是否优先使用官方源决定使用 Url 的顺序。
    /// </summary>
    public static IEnumerable<string> DlSourceOrder(IEnumerable<string> OfficialUrls, IEnumerable<string> MirrorUrls) {
        return DlSourcePreferMojang ? OfficialUrls.Union(MirrorUrls) : MirrorUrls.Union(OfficialUrls);
    }
    /// <summary>
    /// 获取版本列表（而非下载文件）的时候，是否优先使用官方源。
    /// </summary>
    public static bool DlVersionListPreferMojang {
        get {
            return PclCore.GetSetting<int>("ToolDownloadVersion") == 2 || (PclCore.GetSetting<int>("ToolDownloadVersion") == 1 && DlPreferMojang);
        }
    }
    /// <summary>
    /// 获取版本列表（而非下载文件）的时候，根据是否优先使用官方源决定使用 Url 的顺序。
    /// </summary>
    public static IEnumerable<string> DlVersionListOrder(IEnumerable<string> OfficialUrls, IEnumerable<string> MirrorUrls) {
        return DlVersionListPreferMojang ? OfficialUrls.Union(MirrorUrls) : MirrorUrls.Union(OfficialUrls);
    }


    /// <summary>
    /// 下载 Assets 文件。
    /// </summary>
    public static IEnumerable<string> DlSourceAssetsGet(string Original) {
        Original = Original.Replace("http://resources.download.minecraft.net", "https://resources.download.minecraft.net");
        return DlSourceOrder(
            new[] { Original },
            new[] { Original.
                Replace("https://piston-data.mojang.com", "https://bmclapi2.bangbang93.com/assets").
                Replace("https://piston-meta.mojang.com", "https://bmclapi2.bangbang93.com/assets").
                Replace("https://resources.download.minecraft.net", "https://bmclapi2.bangbang93.com/assets")
            });
    }
    /// <summary>
    /// 下载 Libraries 文件。
    /// </summary>
    public static IEnumerable<string> DlSourceLibraryGet(string Original) {
        if (new[] { "minecraftforge", "fabricmc", "neoforged" }.Any(k => Original.Contains(k))) { // 不添加原版源
            return new[] {
                Original.
                    Replace("https://piston-data.mojang.com", "https://bmclapi2.bangbang93.com/maven").
                    Replace("https://piston-meta.mojang.com", "https://bmclapi2.bangbang93.com/maven").
                    Replace("https://libraries.minecraft.net", "https://bmclapi2.bangbang93.com/maven"),
                Original.
                    Replace("https://piston-data.mojang.com", "https://bmclapi2.bangbang93.com/libraries").
                    Replace("https://piston-meta.mojang.com", "https://bmclapi2.bangbang93.com/libraries").
                    Replace("https://libraries.minecraft.net", "https://bmclapi2.bangbang93.com/libraries")
            };
        } else {
            return DlSourceOrder(
                new[] { Original },
                new[] { Original.
                    Replace("https://piston-data.mojang.com", "https://bmclapi2.bangbang93.com/maven").
                    Replace("https://piston-meta.mojang.com", "https://bmclapi2.bangbang93.com/maven").
                    Replace("https://libraries.minecraft.net", "https://bmclapi2.bangbang93.com/maven"),
                 Original.
                    Replace("https://piston-data.mojang.com", "https://bmclapi2.bangbang93.com/libraries").
                    Replace("https://piston-meta.mojang.com", "https://bmclapi2.bangbang93.com/libraries").
                    Replace("https://libraries.minecraft.net", "https://bmclapi2.bangbang93.com/libraries")
                });
        }
    }
    /// <summary>
    /// 下载 Launcher 或 Meta 文件。
    /// 不应使用它来获取版本列表（因为它只使用文件下载源设置来决定源顺序）。
    /// </summary>
    public static IEnumerable<string> DlSourceLauncherOrMetaGet(string Original) {
        if (Original is null) throw new Exception("无对应的 json 下载地址");
        return DlSourceOrder(
            new[] { Original },
            new[] { Original.
                Replace("https://piston-data.mojang.com", "https://bmclapi2.bangbang93.com").
                Replace("https://piston-meta.mojang.com", "https://bmclapi2.bangbang93.com").
                Replace("https://launcher.mojang.com", "https://bmclapi2.bangbang93.com").
                Replace("https://launchermeta.mojang.com", "https://bmclapi2.bangbang93.com")
            });
    }

    // Loader 自动切换
    private static void DlSourceLoader<InputType, OutputType>(LoaderTask<InputType, OutputType> MainLoader,
                                                             List<KeyValuePair<LoaderTask<InputType, OutputType>, int>> LoaderList,
                                                             bool IsForceRestart = false) {
        int WaitCycle = 0;
        while (true) {
            // 检查状态
            bool BeforeLoadersAllFailed = true;
            foreach (KeyValuePair<LoaderTask<InputType, OutputType>, int> SubLoader in LoaderList) {
                if (WaitCycle == 0) { // 判断是否可以不加载，直接使用已经加载好的结果
                    if (IsForceRestart) continue; // 强制刷新，不行
                    if ((SubLoader.Key.Input == null ^ MainLoader.Input == null) ||
                       (SubLoader.Key.Input != null && !SubLoader.Key.Input.Equals(MainLoader.Input))) continue; // 父子加载器的输入不一样，也不行
                }
                if (SubLoader.Key.State != LoadState.Failed) BeforeLoadersAllFailed = false;
                if (SubLoader.Key.State == LoadState.Finished) {
                    // 检查加载器成功
                    MainLoader.Output = SubLoader.Key.Output;
                    DlSourceLoaderCancel(LoaderList);
                    return;
                } else if (BeforeLoadersAllFailed) {
                    // 此前的加载器全部失败，直接启动后续加载器
                    if (WaitCycle < SubLoader.Value * 100) WaitCycle = SubLoader.Value * 100;
                }
            }
            // 第一轮时：既然不直接使用已经加载好的结果，那就启动第一个加载器
            if (WaitCycle == 0) {
                LoaderList.First().Key.Start(MainLoader.Input, IsForceRestart);
                foreach (KeyValuePair<LoaderTask<InputType, OutputType>, int> Loader in LoaderList.Skip(1)) {
                    Loader.Key.State = LoadState.Waiting; // 将其他源标记为未启动，以确保可以切换下载源（#184）
                }
            }
            // 检查加载器失败或超时
            for (int i = 0; i < LoaderList.Count; i++) {
                if (WaitCycle != LoaderList[i].Value * 100) continue;
                if (i < LoaderList.Count - 1 && !LoaderList.All(l => l.Key.State == LoadState.Failed)) {
                    // 若还有下一个源，则启动下一个源
                    LoaderList[i + 1].Key.Start(MainLoader.Input, IsForceRestart);
                } else {
                    // 若没有，则失败
                    Exception ErrorInfo = null;
                    for (int ii = 0; ii < LoaderList.Count; ii++) {
                        LoaderList[ii].Key.Input = default; // 重置输入，以免以同样的输入“重试加载”时直接失败
                        if (LoaderList[ii].Key.Error is not null) {
                            if (ErrorInfo is null || LoaderList[ii].Key.Error.Message == "无") {
                                ErrorInfo = LoaderList[ii].Key.Error;
                            }
                        }
                    }
                    if (ErrorInfo is null) ErrorInfo = new TimeoutException("下载源连接超时");
                    DlSourceLoaderCancel(LoaderList);
                    throw ErrorInfo;
                }
                break;
            }
            // 计时
            Thread.Sleep(10);
            WaitCycle += 1;
            // 检查父加载器中断
            if (MainLoader.IsCanceled) {
                DlSourceLoaderCancel(LoaderList);
                return;
            }
        }
    }
    private static void DlSourceLoaderCancel<InputType, OutputType>(List<KeyValuePair<LoaderTask<InputType, OutputType>, int>> LoaderList) {
        foreach (KeyValuePair<LoaderTask<InputType, OutputType>, int> Loader in LoaderList) {
            if (Loader.Key.State == LoadState.Loading) Loader.Key.Cancel();
        }
    }

    #endregion

}

public struct DlClientListResult {
    /// <summary>
    /// 数据来源名称，如“Mojang”，“BMCLAPI”。
    /// </summary>
    public string SourceName;
    /// <summary>
    /// 是否为官方的实时数据。
    /// </summary>
    public bool IsOfficial;
    /// <summary>
    /// 获取到的 Json 数据。
    /// </summary>
    public JObject Value;
}

public struct DlOptiFineListResult {
    /// <summary>
    /// 数据来源名称，如“Official”，“BMCLAPI”。
    /// </summary>
    public string SourceName;
    /// <summary>
    /// 是否为官方的实时数据。
    /// </summary>
    public bool IsOfficial;
    /// <summary>
    /// 获取到的数据。
    /// </summary>
    public List<DlOptiFineListEntry> Value;
}

public class DlOptiFineListEntry {
    /// <summary>
    /// 显示名称，已去除 HD_U 字样，如“1.12.2 C8”。
    /// </summary>
    public string DisplayName;
    /// <summary>
    /// 原始文件名称，如“preview_OptiFine_1.11_HD_U_E1_pre.jar”。
    /// </summary>
    public string FileName;
    /// <summary>
    /// 对应的版本名称，如“1.13.2-OptiFine_HD_U_E6”。
    /// </summary>
    public string InstanceName;
    /// <summary>
    /// 是否为测试版。
    /// </summary>
    public bool IsPreview;
    /// <summary>
    /// 对应的 Minecraft 版本，如“1.12.2”。
    /// </summary>
    public string Inherit {
        get {
            return _inherit;
        }
        set {
            if (value.EndsWithF(".0")) value = value.Substring(0, value.Length - 2);
            _inherit = value;
        }
    }
    private string _inherit;
    /// <summary>
    /// 发布时间，格式为“yyyy/mm/dd”。OptiFine 源无此数据。
    /// </summary>
    public string ReleaseTime;
    /// <summary>
    /// 需要的最低 Forge 版本。空字符串为无限制，Nothing 为不兼容，“28.1.56” 表示版本号，“1161” 表示版本号的最后一位。
    /// </summary>
    public string RequiredForgeVersion;
}

public struct DlForgeListResult {
    /// <summary>
    /// 数据来源名称，如“Official”，“BMCLAPI”。
    /// </summary>
    public string SourceName;
    /// <summary>
    /// 是否为官方的实时数据。
    /// </summary>
    public bool IsOfficial;
    /// <summary>
    /// 获取到的数据。
    /// </summary>
    public List<string> Value;
}

public abstract class DlForgelikeEntry : IComparable<DlForgelikeEntry> {

    public bool IsNeoForge;
    /// <summary>
    /// 加载器名称。Forge 或 NeoForge。
    /// </summary>
    public abstract string LoaderName { get; }
    /// <summary>
    /// 文件扩展名。不以小数点开头。
    /// </summary>
    public abstract string FileExtension { get; }
    /// <summary>
    /// 标准化后的版本号，仅可用于比较与排序，且 NeoForge 项不包含 alpha/beta 编号！
    /// 格式：Major.Minor.Build.Revision
    /// Forge：如 “50.1.9.0”（最后一位固定为 0）、“14.22.1.2478”（Legacy）。
    /// NeoForge：如 “20.4.30.0”（最后一位固定为 0）、“19.47.1.99”（Legacy：第一位固定为 19）。
    /// </summary>
    public Version Version;
    /// <summary>
    /// 可读的非格式化版本名。
    /// Forge：如 “50.1.9”、“14.22.1.2478”（Legacy）。
    /// NeoForge：如 “20.4.30-beta”、“47.1.99”（Legacy）。
    /// </summary>
    public string VersionName;
    /// <summary>
    /// 对应的 Minecraft 版本，如 1.12.2、26.1-snapshot-1。
    /// </summary>
    public string Inherit;

    public int CompareTo(DlForgelikeEntry other) {
        if (Version != other.Version) {
            return Version.CompareTo(other.Version);
        } else {
            return McUtils.CompareVersion(VersionName, other.VersionName);
        }
    }
}

public class DlForgeVersionEntry : DlForgelikeEntry {

    /// <summary>
    /// 发布时间，格式为“yyyy/MM/dd HH:mm”。
    /// </summary>
    public string ReleaseTime;
    /// <summary>
    /// 文件的 MD5 或 SHA1（BMCLAPI 的老版本是 MD5，新版本是 SHA1；官方源总是 MD5）。
    /// </summary>
    public string Hash = null;
    /// <summary>
    /// 是否为推荐版本。
    /// </summary>
    public bool IsRecommended;
    /// <summary>
    /// 安装类型。有 installer、client、universal 三种。
    /// </summary>
    public string Category;
    /// <summary>
    /// 用于下载的文件版本名。可能在 Version 的基础上添加了分支。
    /// </summary>
    public string FileVersion;

    /// <summary>
    /// 加载器名称。Forge 或 NeoForge。
    /// </summary>
    public override string LoaderName {
        get {
            return "Forge";
        }
    }
    /// <summary>
    /// 文件扩展名。不以小数点开头。
    /// </summary>
    public override string FileExtension {
        get {
            return Category == "installer" ? "jar" : "zip";
        }
    }

    public DlForgeVersionEntry(string Version, string Branch, string Inherit) {
        // 司马版本的特殊处理
        if (Version == "11.15.1.2318" || Version == "11.15.1.1902" || Version == "11.15.1.1890") Branch = "1.8.9";
        if (Branch is null && Inherit == "1.7.10" && int.Parse(Version.Split('.')[3]) >= 1300) Branch = "1.7.10";
        // 为 DlForgelikeEntry 提供所有信息
        IsNeoForge = false;
        VersionName = Version;
        this.Version = new System.Version(Version);
        this.Inherit = Inherit;
        FileVersion = Version + (Branch is null ? "" : "-" + Branch);
    }

}

public struct DlNeoForgeListResult {
    /// <summary>
    /// 数据来源名称，如“Official”，“BMCLAPI”。
    /// </summary>
    public string SourceName;
    /// <summary>
    /// 是否为官方的实时数据。
    /// </summary>
    public bool IsOfficial;
    /// <summary>
    /// 所有版本的列表。已经按从新到老排序。
    /// </summary>
    public List<DlNeoForgeListEntry> Value;
}

public class DlNeoForgeListEntry : DlForgelikeEntry {

    /// <summary>
    /// 是否是 Beta 版。
    /// </summary>
    public bool IsBeta;
    /// <summary>
    /// API 使用的原始版本字符串，如 “20.4.30-beta”、“1.20.1-47.1.99”（Legacy）。
    /// </summary>
    public string ApiName;
    /// <summary>
    /// 文件在官网的基础地址，不包含后缀。
    /// </summary>
    public string UrlBase {
        get {
            string PackageName = Inherit == "1.20.1" ? "forge" : "neoforge";
            return $"https://maven.neoforged.net/releases/net/neoforged/{PackageName}/{ApiName}/{PackageName}-{ApiName}";
        }
    }

    /// <summary>
    /// 加载器名称。Forge 或 NeoForge。
    /// </summary>
    public override string LoaderName {
        get {
            return "NeoForge";
        }
    }
    /// <summary>
    /// 文件扩展名。不以小数点开头。
    /// </summary>
    public override string FileExtension {
        get {
            return "jar";
        }
    }

    public DlNeoForgeListEntry(string ApiName) {
        IsNeoForge = true;
        this.ApiName = ApiName;
        IsBeta = ApiName.ContainsIgnoreCase("beta") || ApiName.ContainsIgnoreCase("alpha");
        if (ApiName.Contains("1.20.1")) { // 1.20.1-47.1.99
            VersionName = ApiName.Replace("1.20.1-", "");
            Version = new Version("19." + VersionName);
            Inherit = "1.20.1";
        } else if (ApiName.StartsWithF("0.")) { // 0.25w14craftmine.3-beta
            VersionName = ApiName;
            string[] Segments = ApiName.BeforeFirst("-").Split('.');
            Version = new Version(0, 0, int.Parse(Segments.Last()));
            Inherit = Segments[1];
        } else { // 20.4.30-beta；26.1.0.0-alpha.1+snapshot-1
            VersionName = ApiName;
            Version = new Version(ApiName.BeforeFirst("-"));
            if (Version.Major >= 24) {
                Inherit = $"{Version.Major}.{Version.Minor}.{Version.Build}";
            } else {
                Inherit = $"1.{Version.Major}.{Version.Minor}";
            }
            if (Inherit.EndsWithF(".0")) Inherit = Inherit.Substring(0, Inherit.Length - 2);
            if (VersionName.Contains("+")) Inherit += "-" + VersionName.AfterFirst("+");
        }
    }
}

public struct DlLiteLoaderListResult {
    /// <summary>
    /// 数据来源名称，如“Official”，“BMCLAPI”。
    /// </summary>
    public string SourceName;
    /// <summary>
    /// 是否为官方的实时数据。
    /// </summary>
    public bool IsOfficial;
    /// <summary>
    /// 获取到的数据。
    /// </summary>
    public List<DlLiteLoaderListEntry> Value;
    /// <summary>
    /// 官方源的失败原因。若没有则为 Nothing。
    /// </summary>
    public Exception OfficialError;
}

public class DlLiteLoaderListEntry {
    /// <summary>
    /// 实际的文件名，如“liteloader-installer-1.12-00-SNAPSHOT.jar”。
    /// </summary>
    public string FileName;
    /// <summary>
    /// 是否为测试版。
    /// </summary>
    public bool IsPreview;
    /// <summary>
    /// 对应的 Minecraft 版本，如“1.12.2”。
    /// </summary>
    public string Inherit;
    /// <summary>
    /// 是否为 1.7 及更早的远古版。
    /// </summary>
    public bool IsLegacy;
    /// <summary>
    /// 发布时间，格式为“yyyy/mm/dd HH:mm”。
    /// </summary>
    public string ReleaseTime;
    /// <summary>
    /// 文件的 MD5。
    /// </summary>
    public string MD5;
    /// <summary>
    /// 对应的 Json 项。
    /// </summary>
    public JToken JsonToken;
}

public struct DlFabricListResult {
    /// <summary>
    /// 数据来源名称，如“Official”，“BMCLAPI”。
    /// </summary>
    public string SourceName;
    /// <summary>
    /// 是否为官方的实时数据。
    /// </summary>
    public bool IsOfficial;
    /// <summary>
    /// 获取到的数据。
    /// </summary>
    public JObject Value;
}
