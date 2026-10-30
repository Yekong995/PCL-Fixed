// 由 VB Modules\Resource\ResourceVersion.vb 迁移而来。
// UI 相关成员（ToListItem、ToNetFile）保留在 VB 扩展文件 ResourceUiExtensions.vb 中。
#nullable disable

using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace PCLCS.Modules;

/// <summary>
/// 社区资源的单个可下载版本。
/// </summary>
public class ResourceVersion {

    // 基础字段

    /// <summary>
    /// 用于唯一性鉴别该版本的 ID。CurseForge 中为 123456 的大整数，Modrinth 中为英文乱码的 Version 字段。
    /// </summary>
    public string Id;
    /// <summary>
    /// 该版本所对应的单一资源类别。
    /// </summary>
    public ResourceTypes ResourceType;

    // 描述性字段

    /// <summary>
    /// 版本描述名。
    /// 并非文件名或版本号，是由上传者完全自定义的字段。
    /// </summary>
    public string Display;
    /// <summary>
    /// 发布时间。
    /// </summary>
    public DateTime ReleaseDate;
    /// <summary>
    /// 下载量计数。
    /// 注意，仅为一个平台的计数，无法反映多平台加起来的下载量，且 CurseForge 可能错误地返回 0。
    /// </summary>
    public int DownloadCount;
    /// <summary>
    /// 版本号。
    /// 不一定是标准格式。CurseForge 默认为 Nothing。
    /// </summary>
    public string Version;
    /// <summary>
    /// 所有支持的 Mod 加载器列表。可能为 <see cref="ModLoaders.None"/>。
    /// </summary>
    public ModLoaders ModLoaders = ModLoaders.None;
    /// <summary>
    /// 支持的游戏版本列表。类型包括："26.1.5"，"26.1"，"26.1 预览版"，"1.18.5"，"1.18"，"1.18 预览版"，"21w15a"，"未知版本"。
    /// </summary>
    public List<string> GameVersions;

    /// <summary>
    /// 发布状态。
    /// </summary>
    public ReleaseTypes ReleaseType;
    /// <summary>
    /// 发布状态的友好描述。
    /// 例如："正式版"，"Beta 版"。
    /// </summary>
    public string ReleaseTypeDisplay {
        get {
            switch (ReleaseType) {
                case ReleaseTypes.Release:
                    return "正式版";
                case ReleaseTypes.Beta:
                    return PclCore.GetSetting<bool>("SystemDebugMode") ? "Beta 版" : "测试版";
                default:
                    return PclCore.GetSetting<bool>("SystemDebugMode") ? "Alpha 版" : "早期测试版";
            }
        }
    }
    public enum ReleaseTypes {
        // https://docs.curseforge.com/rest-api/#tocS_FileReleaseType
        Release = 1,
        Beta = 2,
        Alpha = 3,
    }

    // 下载信息

    /// <summary>
    /// 下载信息是否可用。
    /// </summary>
    public bool DownloadAvailable {
        get {
            return FileName is not null && DownloadUrls is not null;
        }
    }
    /// <summary>
    /// 文件名。
    /// </summary>
    public string FileName = null;
    /// <summary>
    /// 对应文件的所有可能的下载 URL。
    /// </summary>
    public List<string> DownloadUrls;
    /// <summary>
    /// 对应文件的 SHA1 或 MD5。固定为小写。
    /// </summary>
    public string Hash = null;
    /// <summary>
    /// 文件大小。不可用时为 -1。
    /// </summary>
    public int Size = -1;
    /// <summary>
    /// 作为该版本依赖项的项目 ID。
    /// 它们可能没有加载，在加载后会添加到 Dependencies 中（主要是因为 Modrinth 返回的是字符串 ID 而非 Slug，导致 Project.Id 查询不到）。
    /// </summary>
    private List<string> RawDependencies = new List<string>();
    /// <summary>
    /// 作为该版本依赖项的项目 ID。
    /// </summary>
    public List<string> Dependencies = new List<string>();

    // 初始化

    /// <summary>
    /// 从平台 API 返回的 JSON 中初始化实例。若出错会抛出异常。
    /// </summary>
    public static ResourceVersion FromPlatformJson(JObject Data, ResourceTypes ExpectedResourceType) {
        ResourceVersion Result = new ResourceVersion();
        Result.ResourceType = ExpectedResourceType;
        if (Data.ContainsKey("gameId")) {
            // CurseForge
            // 简单信息
            Result.Id = Data["id"]?.ToString();
            Result.Display = Data["displayName"].ToString().Replace("\t", "").Trim(' ');
            Result.Version = null;
            Result.ReleaseDate = Data["fileDate"]?.ToObject<DateTime>() ?? default;
            Result.ReleaseType = (ReleaseTypes)(Data["releaseType"]?.ToObject<int>() ?? 0);
            Result.DownloadCount = Data["downloadCount"]?.ToObject<int>() ?? 0;
            Result.FileName = Data["fileName"]?.ToString();
            Result.Size = Data["fileLength"]?.ToObject<int>() ?? 0;
            if (Result.Size == 0) Result.Size = -1; // CurseForge 可能错误地返回 0 尺寸（#9104）
            Result.Hash = ((JArray)Data["hashes"]).ToList().FirstOrDefault(s => s["algo"]?.ToObject<int>() == 1)?["value"]?.ToString().Lower();
            if (Result.Hash is null) Result.Hash = ((JArray)Data["hashes"]).ToList().FirstOrDefault(s => s["algo"]?.ToObject<int>() == 2)?["value"]?.ToString().Lower();
            // DownloadAddress
            string Url = Data["downloadUrl"].ToString();
            if (Url == "") Url = $"https://edge.forgecdn.net/files/{int.Parse(Result.Id.Substring(0, 4))}/{int.Parse(Result.Id.Substring(4))}/{Result.FileName}";
            Result.DownloadUrls = ParseCurseForgeDownloadUrls(Url.Replace(Result.FileName, StringUtils.UrlEscape(Result.FileName))); // 对脑残 CurseForge 的下载地址进行多种修正
            Result.DownloadUrls.Add(PclNet.DlSourceModGet(Url)); // 添加镜像源；注意 MCIM 源不支持 URL 编码后的文件名，必须传入 URL 编码前的文件名
            Result.DownloadUrls = Result.DownloadUrls.Distinct().ToList(); // 最终去重
            // Dependencies
            if (Data.ContainsKey("dependencies")) {
                Result.RawDependencies = Data["dependencies"].
                        Where(d => d["relationType"]?.ToObject<int>() == 3 && // 种类为依赖
                                          d["modId"]?.ToObject<int>() != 306612 && d["modId"]?.ToObject<int>() != 634179). // 排除 Fabric API 和 Quilt API
                        Select(d => d["modId"].ToString()).ToList();
            }
            // GameVersions
            List<string> RawVersions = Data["gameVersions"].Select(t => t.ToString().Trim().Lower()).ToList();
            Result.GameVersions = RawVersions.
                    Where(v => McVersion.IsStandardFormat(v)).
                    Select(v => v.Replace("-snapshot", " 预览版")).
                    Distinct().ToList();
            if (Result.GameVersions.IsSingle()) {
                Result.GameVersions = Result.GameVersions.ToList();
            } else if (Result.GameVersions.Count > 1) {
                Result.GameVersions = Result.GameVersions.SortByComparison(McUtils.CompareVersionGE).ToList();
                if (Result.ResourceType == ResourceTypes.ModPack) Result.GameVersions = new List<string> { Result.GameVersions[0] }.ToList(); // 整合包理应只 “支持” 一个版本
            } else {
                Result.GameVersions = new List<string> { "未知版本" };
            }
            // ModLoaders
            Result.ModLoaders = ModLoaders.None;
            foreach (ModLoaders Loader in EnumUtils.GetAllFlags<ModLoaders>()) {
                if (RawVersions.Contains(Loader.ToString().Lower())) Result.ModLoaders = Result.ModLoaders | Loader;
            }
        } else {
            // Modrinth
            // 简单信息
            Result.Id = Data["id"]?.ToString();
            Result.Display = Data["name"].ToString().Replace("\t", "").Trim(' ');
            Result.Version = Data["version_number"]?.ToString();
            Result.ReleaseDate = Data["date_published"]?.ToObject<DateTime>() ?? default;
            Result.ReleaseType = Data["version_type"].ToString() == "release" ? ReleaseTypes.Release : (Data["version_type"].ToString() == "beta" ? ReleaseTypes.Beta : ReleaseTypes.Alpha);
            Result.DownloadCount = Data["downloads"]?.ToObject<int>() ?? 0;
            if (((JArray)Data["files"]).Any()) { // 可能为空
                JToken File = Data["files"][0];
                Result.FileName = File["filename"]?.ToString();
                Result.DownloadUrls = new List<string> { File["url"].ToString(), PclNet.DlSourceModGet(File["url"].ToString()) }.Distinct().ToList(); // 同时添加了镜像源
                Result.Size = File["size"]?.ToObject<int>() ?? 0;
                Result.Hash = File["hashes"]?["sha1"]?.ToString().Lower();
            }
            // 类别与 Loaders
            // 结果可能混杂着 Mod、数据包和服务端插件
            List<string> RawLoaders = Data["loaders"].Select(v => v.ToString()).ToList();
            Result.ModLoaders = ModLoaders.None;
            foreach (ModLoaders Loader in EnumUtils.GetAllFlags<ModLoaders>()) {
                if (RawLoaders.Contains(Loader.ToString().Lower())) Result.ModLoaders = Result.ModLoaders | Loader;
            }
            if (Result.ResourceType.HasFlagF(ResourceTypes.Mod) || Result.ResourceType.HasFlagF(ResourceTypes.DataPack)) {
                if (RawLoaders.Intersect(new[] { "bukkit", "folia", "paper", "purpur", "spigot" }).Any()) Result.ResourceType = ResourceTypes.Plugin; // Veinminer Enchantment 同时支持服务端与 Fabric
                if (RawLoaders.Contains("datapack")) Result.ResourceType = ResourceTypes.DataPack;
                if (Result.ModLoaders.Flags().Any()) Result.ResourceType = ResourceTypes.Mod;
            } else {
                // 使用传入的类别，不作修改（#8377）
            }
            // Dependencies
            if (Data.ContainsKey("dependencies")) {
                Result.RawDependencies = Data["dependencies"].
                        Where(d => d["dependency_type"]?.ToString() == "required" && // 种类为依赖
                                          d["project_id"]?.ToString() != "P7dR8mSH" && d["project_id"]?.ToString() != "qvIfYCYJ" && // 排除 Fabric API 和 Quilt API
                                          d["project_id"].ToString().Length > 0). // 有时候真的会空……
                        Select(d => d["project_id"].ToString()).ToList();
            }
            // GameVersions
            List<string> RawVersions = Data["game_versions"].Select(t => t.ToString().Trim().Lower()).ToList();
            Result.GameVersions = RawVersions.Where(v => v.Contains(".")).
                                           Select(v => v.Contains("-") ? v.BeforeFirst("-") + " 预览版" : (v.StartsWithF("b1.") ? "远古版本" : v)).
                                           Distinct().ToList();
            if (Result.GameVersions.IsSingle()) {
                // 无需处理
            } else if (Result.GameVersions.Count > 1) {
                Result.GameVersions = Result.GameVersions.SortByComparison(McUtils.CompareVersionGE).ToList();
                if (Result.ResourceType == ResourceTypes.ModPack) Result.GameVersions = new List<string> { Result.GameVersions[0] }; // 整合包理应只 “支持” 一个版本
            } else if (RawVersions.Any(v => v.RegexCheck("[0-9]{2}w[0-9]{2}[a-z]"))) {
                Result.GameVersions = RawVersions.Where(v => v.RegexCheck("[0-9]{2}w[0-9]{2}[a-z]")).ToList();
            } else {
                Result.GameVersions = new List<string> { "未知版本" };
            }
        }
        return Result;
    }
    /// <summary>
    /// 从项目 ID 获取其下的全部版本列表，并获取其前置项目信息。
    /// </summary>
    public static List<ResourceVersion> FromProjectId(string ProjectId, ResourcePlatforms Platform, bool LoadDependencies = true) {
        if (!Platform.Flags().IsSingle()) throw new ArgumentException($"必须指定单一平台，当前值为 {Platform}");
        ResourceProject TargetProject = ResourceProject.FromProjectId(ProjectId, Platform);
        // 获取项目对象的文件列表
        if (!ProjectFilesCache.ContainsKey(ProjectId)) {
            Logger.Info($"开始获取项目版本列表：{ProjectId}");
            JArray ResultJsonArray = null;
            if (Platform == ResourcePlatforms.CurseForge) {
                for (int RetryCount = 0; RetryCount <= 2; RetryCount++) {
                    JObject ResultJson = (JObject)PclNet.DlModRequest($"https://api.curseforge.com/v1/mods/{ProjectId}/files?pageSize=" +
                    (10000 + RetryCount)); // 每次重试多请求一个文件，以避免触发 CDN 缓存
                    if (ResultJson["pagination"]["resultCount"]?.ToObject<int>() == ResultJson["pagination"]["totalCount"]?.ToObject<int>()) {
                        ResultJsonArray = (JArray)ResultJson["data"];
                        break;
                    } else if (RetryCount < 2) {
                        Logger.Warn($"CurseForge 返回的资源版本列表存在缺失，即将进行第 {RetryCount + 1} 次重试"); // #6224
                        Logger.Info($"返回的原始内容如下：{Environment.NewLine}{ResultJson}");
                    } else {
                        Logger.Info($"CurseForge 返回的资源版本列表存在缺失，返回的原始内容如下：{Environment.NewLine}{ResultJson}");
                        throw new Exception("CurseForge 返回的资源版本列表存在缺失");
                    }
                }
            } else { // Modrinth
                ResultJsonArray = (JArray)PclNet.DlModRequest($"https://api.modrinth.com/v2/project/{ProjectId}/version?include_changelog=false");
            }
            ProjectFilesCache[ProjectId] = ResultJsonArray.Select(a => FromPlatformJson((JObject)a, TargetProject.Types)).
                Where(a => a.DownloadAvailable).
                DistinctBy(a => a.Id).ToList(); // CurseForge 可能会重复返回相同项（#1330）
        }
        List<ResourceVersion> Result = ProjectFilesCache[ProjectId];
        if (!LoadDependencies) return Result;
        // 加载前置信息
        List<string> Dependencies = Result.SelectMany(f => f.RawDependencies).Distinct().ToList();
        if (Dependencies.Any()) {
            Logger.Info($"项目 {ProjectId} 中需要加载的前置：{Dependencies.Join("，")}");
            foreach (ResourceProject DependencyProject in ResourceProject.FromProjectIds(Dependencies, TargetProject.Platform)) {
                foreach (ResourceVersion File in Result) {
                    if (File.RawDependencies.Contains(DependencyProject.Id) && DependencyProject.Id != ProjectId && !File.Dependencies.Contains(DependencyProject.Id)) {
                        File.Dependencies.Add(DependencyProject.Id);
                    }
                }
            }
        }
        return Result;
    }
    /// <summary>
    /// 项目 ID 对应的版本信息的缓存。不会保存到文件。
    /// </summary>
    public static Dictionary<string, List<ResourceVersion>> ProjectFilesCache = new Dictionary<string, List<ResourceVersion>>();

    // 序列化

    /// <summary>
    /// 从缓存 JSON 读取字段数据。
    /// </summary>
    public static ResourceVersion FromCacheJson(JObject Data) {
        ResourceVersion Result = new ResourceVersion();
        Result.ResourceType = (ResourceTypes)Data["ResourceType"].ToObject<int>();
        Result.Id = Data["Id"].ToString();
        Result.Display = Data["DisplayName"].ToString();
        if (Data.ContainsKey("Version")) Result.Version = Data["Version"].ToString();
        Result.ReleaseDate = Data["ReleaseDate"].ToObject<DateTime>();
        Result.DownloadCount = Data["DownloadCount"].ToObject<int>();
        Result.ReleaseType = (ReleaseTypes)Data["ReleaseType"].ToObject<int>();
        if (Data.ContainsKey("FileName")) Result.FileName = Data["FileName"].ToString();
        if (Data.ContainsKey("DownloadUrls")) Result.DownloadUrls = Data["DownloadUrls"].ToObject<List<string>>();
        if (Data.ContainsKey("ModLoaders")) Result.ModLoaders = (ModLoaders)Data["ModLoaders"].ToObject<int>();
        if (Data.ContainsKey("Size")) Result.Size = Data["Size"].ToObject<int>();
        if (Data.ContainsKey("Hash")) Result.Hash = Data["Hash"].ToString();
        if (Data.ContainsKey("GameVersions")) Result.GameVersions = Data["GameVersions"].ToObject<List<string>>();
        if (Data.ContainsKey("RawDependencies")) Result.RawDependencies = Data["RawDependencies"].ToObject<List<string>>();
        if (Data.ContainsKey("Dependencies")) Result.Dependencies = Data["Dependencies"].ToObject<List<string>>();
        return Result;
    }
    /// <summary>
    /// 转为缓存 JSON。
    /// </summary>
    public JObject ToCacheJson() {
        JObject Result = new JObject();
        Result.Add("ResourceType", (int)ResourceType);
        Result.Add("Id", Id);
        if (Version is not null) Result.Add("Version", Version);
        Result.Add("DisplayName", Display);
        Result.Add("ReleaseDate", ReleaseDate);
        Result.Add("DownloadCount", DownloadCount);
        Result.Add("ModLoaders", (int)ModLoaders);
        Result.Add("GameVersions", new JArray(GameVersions));
        Result.Add("ReleaseType", (int)ReleaseType);
        if (FileName is not null) Result.Add("FileName", FileName);
        if (DownloadUrls is not null) Result.Add("DownloadUrls", new JArray(DownloadUrls));
        if (Hash is not null) Result.Add("Hash", Hash);
        if (Size >= 0) Result.Add("Size", Size);
        Result.Add("RawDependencies", new JArray(RawDependencies));
        Result.Add("Dependencies", new JArray(Dependencies));
        return Result;
    }

    // 杂项

    /// <summary>
    /// 添加 Modrinth 追踪参数。
    /// </summary>
    public static IEnumerable<string> ParseModrinthTrackArguments(List<string> Urls,
            DownloadReason? TrackDownloadReason = null, string TrackGameVersion = null, ModLoaders TrackLoader = ModLoaders.None) {
        Dictionary<string, string> Arguments = new Dictionary<string, string>();
        if (TrackDownloadReason is not null) Arguments.Add("mr_download_reason", TrackDownloadReason.Value.ToString().Lower());
        if (TrackGameVersion is not null) Arguments.Add("mr_game_version", TrackGameVersion);
        if (TrackLoader.Flags().Any()) Arguments.Add("mr_loader", TrackLoader.Flags().First().ToString().Lower());
        string Argument = Arguments.Select(a => $"{a.Key}={a.Value}").Join("&");
        foreach (string Url in Urls) {
            if (!Url.ContainsIgnoreCase("cdn.modrinth.com/")) yield return Url;
            yield return $"{Url}{(Url.Contains("?") ? "&" : "?")}{Argument}";
        }
    }
    public enum DownloadReason {
        Standalone,
        Dependency,
        ModPack,
        Update,
    }
    /// <summary>
    /// 重新整理 CurseForge 的下载地址。
    /// </summary>
    public static List<string> ParseCurseForgeDownloadUrls(string Url) {
        return new List<string> {
            Url.Replace("-service.overwolf.wtf", ".forgecdn.net").Replace("://edge.", "://mediafilez.").Replace("://media.", "://mediafilez."),
            Url.Replace("://edge.", "://mediafilez.").Replace("://media.", "://mediafilez."),
            Url.Replace("-service.overwolf.wtf", ".forgecdn.net"),
            Url.Replace("://media.", "://edge."),
            Url
        }.Distinct().ToList();
    }
    public override string ToString() {
        return $"{Id}: {FileName}";
    }

}
