// 由 VB Modules\Resource\ResourceSearcher.vb 迁移而来。
#nullable disable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.VisualBasic;
using Newtonsoft.Json.Linq;

namespace PCLCS.Modules;

public class ResourceSearcher {
    private const int RESULT_PAGE_SIZE = 40;
    private const string MSG_NO_CHINESE_RESULT = "无搜索结果，请尝试搜索其英文名称";

    // 输入
    public class SearchRequest {

        // 结果要求

        /// <summary>
        /// 结果存储器，会从中获取需加载的页面等。
        /// 加载后也应输出到此处。
        /// </summary>
        public SearchResult Storage;
        /// <summary>
        /// 应当尽量达成的结果数量。
        /// </summary>
        public int TargetResultCount;
        /// <summary>
        /// 根据加载位置记录，是否还可以继续获取内容。
        /// </summary>
        public bool CanContinue {
            get {
                if (Tag.StartsWithF("/") || !Sources.HasFlagF(ResourcePlatforms.CurseForge)) Storage.CurseForgeTotal = 0;
                if (Tag.EndsWithF("/") || !Sources.HasFlagF(ResourcePlatforms.Modrinth)) Storage.ModrinthTotal = 0;
                if (Storage.CurseForgeTotal == -1 || Storage.ModrinthTotal == -1) return true;
                return Storage.CurseForgeOffset < Storage.CurseForgeTotal || Storage.ModrinthOffset < Storage.ModrinthTotal;
            }
        }

        // 输入内容

        /// <summary>
        /// 筛选资源种类。
        /// </summary>
        public ResourceTypes Type;
        /// <summary>
        /// 筛选资源标签。空字符串代表不限制。格式例如 "406/worldgen"，分别是 CurseForge 和 Modrinth 的 ID。
        /// </summary>
        public string Tag = "";
        /// <summary>
        /// 筛选 Mod 加载器类别。
        /// <see cref="ModLoaders.None"/> 代表不筛选。
        /// 对于 Modrinth 请求，支持多个枚举值。
        /// </summary>
        public ModLoaders ModLoaders = ModLoaders.None;
        /// <summary>
        /// 筛选 MC 版本。
        /// </summary>
        public string GameVersion = null;
        /// <summary>
        /// 搜索的初始内容。
        /// </summary>
        public string SearchText = null;
        /// <summary>
        /// 允许的来源。
        /// </summary>
        public ResourcePlatforms Sources = ResourcePlatforms.Any;
        public SearchRequest(ResourceTypes Type, SearchResult Storage, int TargetResultCount) {
            this.Type = Type;
            this.Storage = Storage;
            this.TargetResultCount = TargetResultCount;
        }

        // 构造请求

        /// <summary>
        /// 获取对应的 CurseForge API 请求链接。
        /// </summary>
        public string GetCurseForgeAddress(string SearchText, bool IgnoreModLoaderFilter) {
            if (Tag.StartsWithF("/")) Storage.CurseForgeTotal = 0;
            // 应用筛选参数
            string Address = $"https://api.curseforge.com/v1/mods/search?gameId=432&sortField=2&sortOrder=desc&pageSize={RESULT_PAGE_SIZE}";
            switch (Type) {
                case ResourceTypes.Mod:
                    Address += "&classId=6";
                    break;
                case ResourceTypes.ModPack:
                    Address += "&classId=4471";
                    break;
                case ResourceTypes.DataPack:
                    Address += "&classId=6945";
                    break;
                case ResourceTypes.Shader:
                    Address += "&classId=6552";
                    break;
                case ResourceTypes.ResourcePack:
                    Address += "&classId=12";
                    break;
            }
            if (Tag != "") Address += "&categoryId=" + Tag.BeforeFirst("/");
            if (ModLoaders != ModLoaders.None && !IgnoreModLoaderFilter) Address += "&modLoaderType=" + Resource.ToCurseForgeModLoaderType(ModLoaders.Flags().First());
            if (!string.IsNullOrEmpty(GameVersion)) Address += "&gameVersion=" + GameVersion;
            if (!string.IsNullOrEmpty(SearchText)) Address += "&searchFilter=" + StringUtils.UrlEscape(SearchText);
            if (Storage.CurseForgeOffset > 0) Address += "&index=" + Storage.CurseForgeOffset;
            return Address;
        }
        /// <summary>
        /// 获取对应的 Modrinth API 请求链接。
        /// </summary>
        public string GetModrinthAddress(string SearchText, bool IgnoreModLoaderFilter) {
            if (Tag.EndsWithF("/")) Storage.ModrinthTotal = 0;
            // 应用筛选参数
            string Address = $"https://api.modrinth.com/v2/search?limit={RESULT_PAGE_SIZE}&index=relevance";
            if (!string.IsNullOrEmpty(SearchText)) Address += "&query=" + StringUtils.UrlEscape(SearchText);
            if (Storage.ModrinthOffset > 0) Address += "&offset=" + Storage.ModrinthOffset;
            // facets=[["categories:'game-mechanics'"],["categories:forge","categories:fabric"],["versions:1.19.3"],["project_type:mod"]]
            List<string> Facets = new List<string>();
            Facets.Add($"[\"project_type:{Type.ToString().Lower()}\"]");
            if (!string.IsNullOrEmpty(Tag)) Facets.Add($"[\"categories:'{Tag.AfterLast("/")}'\"]");
            if (ModLoaders != ModLoaders.None && !IgnoreModLoaderFilter) {
                Facets.Add($"[\"categories:{ModLoaders.Flags().Select(f => f.ToString().Lower()).Join("\",\"categories:")}\"]");
            }
            if (!string.IsNullOrEmpty(GameVersion)) Facets.Add($"[\"versions:'{GameVersion}'\"]");
            Address += "&facets=[" + string.Join(",", Facets) + "]";
            return Address;
        }

        // 相同判断
        public override bool Equals(object obj) {
            SearchRequest request = obj as SearchRequest;
            return request is not null &&
                Type == request.Type && TargetResultCount == request.TargetResultCount &&
                Tag == request.Tag && ModLoaders == request.ModLoaders && Sources == request.Sources &&
                GameVersion == request.GameVersion && SearchText == request.SearchText;
        }
        public override int GetHashCode() {
            return (Type, Tag, ModLoaders, GameVersion, SearchText, Sources).GetHashCode();
        }
        public static bool operator ==(SearchRequest left, SearchRequest right) {
            return EqualityComparer<SearchRequest>.Default.Equals(left, right);
        }
        public static bool operator !=(SearchRequest left, SearchRequest right) {
            return !(left == right);
        }

    }

    // 输出
    public class SearchResult {

        // 加载位置记录

        public int CurseForgeOffset = 0;
        public int CurseForgeTotal = -1;

        public int ModrinthOffset = 0;
        public int ModrinthTotal = -1;

        // 结果列表

        /// <summary>
        /// 可供展示的所有工程的列表。
        /// </summary>
        public List<ResourceProject> Results = new List<ResourceProject>();
        /// <summary>
        /// 当前的错误信息。如果没有则为 Nothing。
        /// </summary>
        public string ErrorMessage = null;

    }

    /// <summary>
    /// 根据搜索请求获取一系列的工程列表。
    /// </summary>
    public static LoaderTask<SearchRequest, int> GetLoader(Func<SearchRequest> InputDelegate) {
        return new LoaderTask<SearchRequest, int>("社区资源获取", Run, InputDelegate) { ReloadTimeout = 60 * 1000 };
    }

    private static void Run(LoaderTask<SearchRequest, int> Task) {
        SearchRequest Request = Task.Input;
        SearchResult Storage = Request.Storage; // 避免其他线程对 Request.Storage 重新进行了赋值

        #region 前置检查

        if (Storage.Results.Count >= Request.TargetResultCount) {
            Logger.Info($"已有 {Storage.Results.Count} 个结果，多于所需的 {Request.TargetResultCount} 个结果，结束处理");
            return;
        } else if (!Request.CanContinue) {
            if (!Storage.Results.Any()) {
                throw new Exception("没有符合条件的结果");
            } else {
                Logger.Info($"已有 {Storage.Results.Count} 个结果，少于所需的 {Request.TargetResultCount} 个结果，但无法继续获取，结束处理");
                return;
            }
        }

        #endregion

        #region 中文搜索

        string RawSearchText = (Request.SearchText ?? "").Trim();
        RawSearchText = RawSearchText.Lower();
        Logger.Info($"工程列表搜索原始文本：{RawSearchText}");
        RawSearchText = Strings.StrConv(RawSearchText, VbStrConv.SimplifiedChinese); // 繁体转简体

        bool IsChineseSearch =
            RawSearchText.RegexCheck("[\u4e00-\u9fbb]") && !string.IsNullOrEmpty(RawSearchText) &&
            (Request.Type == ResourceTypes.Mod || Request.Type == ResourceTypes.DataPack); // 目前仅对 Mod 和数据包进行中文搜索，注意整合包的名称可能已经有中文了
        string CurseForgeAltSearchText = null;
        string ModrinthAltSearchText = null;
        List<string> ModrinthSlugs = new List<string>(); // 从中文转为英文的替代搜索内容
        if (IsChineseSearch) {
            // 帮助方法：从搜索项提取可能的英文单词
            IEnumerable<string> ExtractWords(SearchEntry<WikiEntry> Result, ResourcePlatforms Source) {
                // 从各个可能的来源提取候选
                List<string> Candidates = new List<string>();
                if (Result.Item.Slugs.ContainsKey(Source)) {
                    Candidates.Add(Result.Item.Slugs[Source].
                                   Replace("-", " ").Replace("/", " "));
                }
                if (Result.Item.ChineseName is not null) {
                    Candidates.Add(Result.Item.ChineseName.
                                   AfterLast(" (").TrimEnd(')', ' ').BeforeFirst(" - ").Replace("-", " ").Replace("/", " ").Replace(":", " ").Replace("(", " ").Replace(")", ""));
                }
                // 分词、清洗、去重
                Candidates = Candidates.
                    SelectMany(c => c.Split(' ')).
                    Select(w => w.TrimStart('{', '[', '(').TrimEnd('}', ']', ')').Lower()).
                    Where(
                    w => {
                        if (w.Length <= 1) return false; // 单字或空白
                        if (new[] { "the", "of", "mod", "and", "forge", "fabric", "for", "quilt", "neoforge" }.Contains(w)) return false; // 常见词
                        if (PclCore.Val(w) > 0) return false; // 数字
                        if (!w.IsAsciiOnly()) return false; // 非纯英文
                        return true;
                    }).Distinct().ToList();
                // 如果一个词可以由其他词拼成，则去掉这个词（例如将 ender io enderio 的 enderio 剔除，只保留 ender io）
                Func<string, bool> CanForm = null;
                CanForm = s => Candidates.Contains(s) || Candidates.Any(c => s.StartsWith(c) && CanForm(s.Substring(c.Length)));
                Candidates = Candidates.Where(w => !Candidates.Any(c => c.Length < w.Length && w.StartsWith(c) && CanForm(w.Substring(c.Length)))).ToList();
                return Candidates;
            }
            // CurseForge
            if (Request.Sources.HasFlagF(ResourcePlatforms.CurseForge)) {
                // 数据库搜索
                List<SearchEntry<WikiEntry>> CurseForgeSearchResults = PclCore.Search(CurseForgeSearchEntries, RawSearchText, 100, 0.25);
                if (CurseForgeSearchResults.Any()) {
                    // 选取目标（CurseForge 要求每个词都必须匹配上，所以只能选择一个 Mod 进行搜索）
                    SearchEntry<WikiEntry> CurseForgeTarget =
                        (CurseForgeSearchResults.First().AbsoluteRight ?
                            CurseForgeSearchResults.Where(s => s.AbsoluteRight).ToList() : // 优先使用所有完全匹配的
                            CurseForgeSearchResults.MaxByAll(s => s.Similarity)). // 其次使用所有相似度最高的
                        MaxBy(s => s.Item.Popularity); // 然后从中选择最受欢迎的那一个
                    // 后处理
                    CurseForgeAltSearchText = ExtractWords(CurseForgeTarget, ResourcePlatforms.CurseForge).Join(" ");
                    Logger.Warn($"中文搜索关键词（CurseForge）：{CurseForgeAltSearchText}");
                }
            }
            // Modrinth
            if (Request.Sources.HasFlagF(ResourcePlatforms.Modrinth)) {
                // 数据库搜索
                List<SearchEntry<WikiEntry>> ModrinthSearchResults = PclCore.Search(ModrinthSearchEntries, RawSearchText, 100, 0.25);
                if (ModrinthSearchResults.Any()) {
                    // 分词
                    Dictionary<string, double> WordWeights = new Dictionary<string, double>(); // 各个单词及其出现的权重
                    foreach (SearchEntry<WikiEntry> Result in ModrinthSearchResults) {
                        foreach (string Word in ExtractWords(Result, ResourcePlatforms.Modrinth)) {
                            if (!WordWeights.ContainsKey(Word)) WordWeights.Add(Word, 0);
                            double Similarity = Result.SearchSource.Any(s => s.Aliases.Contains(RawSearchText)) ? 1000 : Result.Similarity; // 完全匹配为 1000
                            WordWeights[Word] += Similarity * Result.Item.Popularity; // 权重 += 相似度 * 受欢迎程度
                        }
                    }
                    ModrinthAltSearchText = WordWeights.MaxBy(w => w.Value).Key;
                    Logger.Warn($"中文搜索关键词（Modrinth）：{ModrinthAltSearchText}");
                    // 直接请求工程
                    ModrinthSlugs = ModrinthSearchResults.Take(100).Select(r => r.Item.Slugs[ResourcePlatforms.Modrinth]).ToList();
                }
            }
            // 结束
            if (string.IsNullOrEmpty(CurseForgeAltSearchText) && string.IsNullOrEmpty(ModrinthAltSearchText) && !ModrinthSlugs.Any()) {
                throw new Exception(MSG_NO_CHINESE_RESULT);
            }
        }
        Task.Progress = 0.05;

        #endregion

        List<ResourceProject> RealResults = new List<ResourceProject>();
        do {
            List<ResourceProject> RawResults = new List<ResourceProject>();

            #region 从 CurseForge 和 Modrinth 获取结果列表，存储于 RawResults

            List<Thread> WorkThreads = new List<Thread>();
            ConcurrentBag<(Exception Ex, ResourcePlatforms Source)> Errors = new ConcurrentBag<(Exception Ex, ResourcePlatforms Source)>();

            // 在 1.14-，部分老 Mod 没有设置支持的加载器，因此添加 Forge 筛选就会出现遗漏
            // 所以，在发起请求时不筛选加载器，然后在返回的结果中自行筛除不是 Forge 的 Mod
            bool IgnoreModLoaderFilter = Request.ModLoaders == ModLoaders.Forge && McVersion.VersionToDrop(Request.GameVersion) < 140;
            ConcurrentBag<ResourceProject> SearchResults = new ConcurrentBag<ResourceProject>();
            try {

                // CurseForge 搜索
                if (Request.Sources.HasFlagF(ResourcePlatforms.CurseForge) &&
                   !(Storage.CurseForgeTotal > -1 && Storage.CurseForgeTotal <= Storage.CurseForgeOffset) && // 剩余的未显示的搜索结果不足
                   (!IsChineseSearch || (IsChineseSearch && !string.IsNullOrEmpty(CurseForgeAltSearchText)))) { // 如果是中文搜索，就只在有对应搜索关键词的时候才继续
                    WorkThreads.Add(PclCore.RunInNewThread(
                    () => {
                        try {
                            // 获取工程列表
                            string CurseForgeUrl = Request.GetCurseForgeAddress(CurseForgeAltSearchText ?? RawSearchText, IgnoreModLoaderFilter);
                            Logger.Info($"开始 CurseForge 搜索：{CurseForgeUrl}");
                            JObject RequestResult = (JObject)PclNet.DlModRequest(CurseForgeUrl);
                            List<ResourceProject> ProjectList = new List<ResourceProject>();
                            foreach (JToken JsonEntry in RequestResult["data"]) {
                                ResourceProject Project = new ResourceProject((JObject)JsonEntry);
                                if (Request.Type == ResourceTypes.ResourcePack && Project.Tags.Contains("数据包")) continue; // CurseForge 将一些数据包分类成了资源包
                                if (Request.Type != ResourceTypes.Any && !Project.Types.HasFlagF(Request.Type)) continue; // 过滤分区不匹配的搜索结果（#8265）
                                ProjectList.Add(Project);
                            }
                            // 更新结果
                            ProjectList.ForEach(p => SearchResults.Add(p));
                            Storage.CurseForgeOffset += ((JArray)RequestResult["data"]).Count;
                            Storage.CurseForgeTotal = RequestResult["pagination"]["totalCount"]?.ToObject<int>() ?? 0;
                            Logger.Info($"从 CurseForge 搜索到了 {ProjectList.Count} 个工程（总计已获取 {Storage.CurseForgeOffset} 个，共 {Storage.CurseForgeTotal} 个）");
                        } catch (Exception ex) {
                            Logger.Warn(ex, "CurseForge 搜索失败");
                            Storage.CurseForgeTotal = -1;
                            Errors.Add((ex, ResourcePlatforms.CurseForge));
                        }
                        if (Task.Progress < 0.75) Task.Progress += 0.25; // 可能重复加载多页，所以不能直接给够
                    }, "CurseForge Search"));
                }

                // Modrinth 搜索
                if (Request.Sources.HasFlagF(ResourcePlatforms.Modrinth) &&
                   !(Storage.ModrinthTotal > -1 && Storage.ModrinthTotal <= Storage.ModrinthOffset) && // 剩余的未显示的搜索结果不足
                   (!IsChineseSearch || (IsChineseSearch && !string.IsNullOrEmpty(ModrinthAltSearchText)))) { // 如果是中文搜索，就只在有对应搜索关键词的时候才继续
                    WorkThreads.Add(PclCore.RunInNewThread(
                    () => {
                        try {
                            string ModrinthUrl = Request.GetModrinthAddress(ModrinthAltSearchText ?? RawSearchText, IgnoreModLoaderFilter);
                            Logger.Info($"开始 Modrinth 搜索：{ModrinthUrl}");
                            JObject RequestResult = (JObject)PclNet.DlModRequest(ModrinthUrl);
                            List<ResourceProject> ProjectList = new List<ResourceProject>();
                            foreach (JToken JsonEntry in RequestResult["hits"]) {
                                ProjectList.Add(new ResourceProject((JObject)JsonEntry));
                            }
                            // 更新结果
                            ProjectList.ForEach(p => SearchResults.Add(p));
                            Storage.ModrinthOffset += ((JArray)RequestResult["hits"]).Count;
                            Storage.ModrinthTotal = RequestResult["total_hits"]?.ToObject<int>() ?? 0;
                            Logger.Info($"从 Modrinth 搜索到了 {ProjectList.Count} 个工程（总计已获取 {Storage.ModrinthOffset} 个，共 {Storage.ModrinthTotal} 个）");
                        } catch (Exception ex) {
                            Logger.Warn(ex, "Modrinth 搜索失败");
                            Storage.ModrinthTotal = -1;
                            Errors.Add((ex, ResourcePlatforms.Modrinth));
                        }
                        if (Task.Progress < 0.75) Task.Progress += 0.25; // 可能重复加载多页，所以不能直接给够
                    }, "Modrinth Search"));
                }

                // Modrinth 直接获取工程
                if (Request.Sources.HasFlagF(ResourcePlatforms.Modrinth) &&
                   !(Storage.ModrinthTotal > -1 && Storage.ModrinthTotal <= Storage.ModrinthOffset) && // 剩余的未显示的搜索结果不足
                   ModrinthSlugs.Any()) { // 有直接获取的 Slug
                    WorkThreads.Add(PclCore.RunInNewThread(
                    () => {
                        try {
                            string ModrinthUrl = $"https://api.modrinth.com/v2/projects?ids=[\"{ModrinthSlugs.Join("\",\"")}\"]";
                            Logger.Info($"开始 Modrinth 直接获取：{ModrinthUrl}");
                            List<ResourceProject> ProjectList = new List<ResourceProject>();
                            foreach (JToken JsonEntry in (JArray)PclNet.DlModRequest(ModrinthUrl)) {
                                ResourceProject Project = new ResourceProject((JObject)JsonEntry);
                                // 应用筛选
                                if (Request.Type != ResourceTypes.Any && !Project.Types.HasFlagF(Request.Type)) continue;
                                if (!string.IsNullOrEmpty(Request.Tag) &&
                                    !JsonEntry["categories"].Any(c => c.ToString() == Request.Tag.AfterLast("/"))) continue; // Project.Tags 已经转换成中文了，只能从 json 判
                                if (Request.ModLoaders != ModLoaders.None && !IgnoreModLoaderFilter &&
                                    !Project.ModLoaders.Flags().Intersect(Request.ModLoaders.Flags()).Any()) continue;
                                if (!string.IsNullOrEmpty(Request.GameVersion) &&
                                    !Project.UnsafeGameVersions.Any(d => d == Request.GameVersion)) continue;
                                ProjectList.Add(Project);
                            }
                            if (ProjectList.Any() && Request.ModLoaders != ModLoaders.None && !IgnoreModLoaderFilter &&
                                !string.IsNullOrEmpty(Request.GameVersion)) {
                                string ProjectIds = ProjectList.Select(p => p.Id).Join("\",\"project_id:");
                                string Loaders = Request.ModLoaders.Flags().Select(f => f.ToString().Lower()).Join("\",\"categories:");
                                string FilterUrl = $"https://api.modrinth.com/v2/search?limit={ProjectList.Count}&facets=" +
                                    $"[[\"project_id:{ProjectIds}\"],[\"categories:{Loaders}\"],[\"versions:{Request.GameVersion}\"]]";
                                List<string> MatchedIds = PclNet.DlModRequest(FilterUrl)["hits"].Select(h => h["project_id"].ToString()).ToList();
                                ProjectList.KeepIf(p => MatchedIds.Contains(p.Id));
                            }
                            // 更新结果
                            ProjectList.ForEach(p => SearchResults.Add(p));
                            Logger.Info($"从 Modrinth 直接获取到了 {ProjectList.Count} 个工程");
                            ModrinthSlugs.Clear(); // 防止重试/加载下一页时重复获取
                        } catch (Exception ex) {
                            Logger.Warn(ex, "Modrinth 直接获取失败");
                            Errors.Add((ex, ResourcePlatforms.Modrinth));
                        }
                        if (Task.Progress < 0.75) Task.Progress += 0.25; // 可能重复加载多页，所以不能直接给够
                    }, "Modrinth Get"));
                }

                // 等待线程结束
                foreach (Thread Thread in WorkThreads) {
                    Thread.Join();
                    if (Task.IsCanceled) return; // 会自动触发 Finally 以清理线程
                }
                RawResults = new List<ResourceProject>(SearchResults);

                // 仅保留兼容 Forge 的 Mod，或老版本中没有标注任何 Mod Loader 的 Mod
                if (IgnoreModLoaderFilter) {
                    RawResults.KeepIf(p => p.ModLoaders == ModLoaders.None || p.ModLoaders.HasFlagF(ModLoaders.Forge));
                }

                // 确保存在结果
                Storage.ErrorMessage = null;
                if (!RawResults.Any()) {
                    if (Errors.Any()) {
                        throw Errors.First().Ex;
                    } else {
                        if (IsChineseSearch && !(Request.Type == ResourceTypes.Mod || Request.Type == ResourceTypes.DataPack)) {
                            throw new Exception(MSG_NO_CHINESE_RESULT);
                        } else if (Request.Sources == ResourcePlatforms.CurseForge && Request.Tag.StartsWithF("/")) {
                            throw new Exception("CurseForge 不兼容所选的类型");
                        } else if (Request.Sources == ResourcePlatforms.Modrinth && Request.Tag.EndsWithF("/")) {
                            throw new Exception("Modrinth 不兼容所选的类型");
                        } else {
                            throw new Exception("没有搜索结果");
                        }
                    }
                } else if (Errors.Any()) {
                    // 有结果但是有错误
                    if (Errors.Any(e => e.Source == ResourcePlatforms.CurseForge)) {
                        Storage.ErrorMessage = $"无法连接到 CurseForge，所以目前仅显示了来自 Modrinth 的内容，搜索结果可能不全。{Environment.NewLine}请稍后再试，或使用 VPN 改善网络环境。";
                    } else {
                        Storage.ErrorMessage = $"无法连接到 Modrinth，所以目前仅显示了来自 CurseForge 的内容，搜索结果可能不全。{Environment.NewLine}请稍后再试，或使用 VPN 改善网络环境。";
                    }
                }

            } finally {
                foreach (Thread Thread in WorkThreads) {
                    if (Thread.IsAlive) Thread.Interrupt();
                }
            }

            #endregion

            #region 提取非重复项，存储于 RealResults

            // 将 CurseForge 排在 Modrinth 的前面，避免加载结束顺序不同导致排名不同
            RawResults = RawResults.Where(x => x.Platform == ResourcePlatforms.CurseForge).Concat(RawResults.Where(x => x.Platform != ResourcePlatforms.CurseForge)).ToList();
            // 去重
            RawResults = RawResults.Distinct((a, b) => a.IsLike(b)).ToList();
            // 已有内容去重
            RawResults = RawResults.Where(r => !RealResults.Any(b => r.IsLike(b)) &&
                                              !Storage.Results.Any(b => r.IsLike(b))).ToList();
            // 加入列表
            RealResults.AddRange(RawResults);
            Logger.Info($"去重、筛选后累计新增结果 {RealResults.Count} 个（目前已有结果 {Storage.Results.Count} 个）");

            #endregion

            #region 检查结果数量，如果不足且可继续，会继续加载下一页

            if (RealResults.Count + Storage.Results.Count < Request.TargetResultCount) {
                Logger.Info($"总结果数需求最少 {Request.TargetResultCount} 个，仅获得了 {RealResults.Count + Storage.Results.Count} 个");
                if (Request.CanContinue && !Errors.Any()) { // 如果有某个源失败则不再重试，这时候重试可能导致无限循环
                    Logger.Info("将继续尝试加载下一页");
                    continue; // GoTo NextPage
                } else {
                    Logger.Info("无法继续加载，将强制结束");
                }
            }
            break;

            #endregion
        } while (true);

        #region 将结果排序并添加

        Dictionary<ResourceProject, double> Scores = new Dictionary<ResourceProject, double>(); // 排序分
        Func<ResourceProject, double> GetDownloadCountMult =
        Project => {
            switch (Request.Type) {
                case ResourceTypes.Mod:
                case ResourceTypes.ModPack:
                    return Project.Platform == ResourcePlatforms.CurseForge ? 1 : 5;
                case ResourceTypes.DataPack:
                    return Project.Platform == ResourcePlatforms.CurseForge ? 10 : 1;
                case ResourceTypes.ResourcePack:
                case ResourceTypes.Shader:
                    return Project.Platform == ResourcePlatforms.CurseForge ? 1 : 4;
                default:
                    return 1;
            }
        };
        if (string.IsNullOrEmpty(RawSearchText)) {
            // 如果没有搜索文本，按下载量将结果排序
            foreach (ResourceProject Result in RealResults) {
                Scores[Result] = Result.DownloadCount * GetDownloadCountMult(Result);
            }
        } else {
            // 如果有搜索文本，按关联度将结果排序
            // 排序分 = 搜索相对相似度 (1) + 下载量权重 (对数，10 亿时为 1) + 有中文名 (0.2)
            List<SearchEntry<ResourceProject>> SearchEntries = new List<SearchEntry<ResourceProject>>();
            foreach (ResourceProject Result in RealResults) {
                Scores[Result] = (Result.WikiId > 0 ? 0.2 : 0) +
                           Math.Log10(Math.Max(Result.DownloadCount, 1) * GetDownloadCountMult(Result)) / 9;
                SearchEntries.Add(new SearchEntry<ResourceProject> { Item = Result, SearchSource = new List<SearchSource> {
                    new SearchSource((IsChineseSearch ? Result.TranslatedName : Result.RawName).Split('/', true), 1),
                    new SearchSource(Result.Description, 0.05)}});
            }
            List<SearchEntry<ResourceProject>> SearchResult = PclCore.Search(SearchEntries, RawSearchText, 10000, -1);
            foreach (SearchEntry<ResourceProject> OneResult in SearchResult) {
                Scores[OneResult.Item] +=
                    (OneResult.AbsoluteRight ? 10 : OneResult.Similarity) /
                    (SearchResult.First().AbsoluteRight ? 10 : SearchResult.First().Similarity); // 最高 1 分的相似度分
            }
        }
        // 根据排序分得出结果并添加
        if (Task.IsCanceled) throw new OperationCanceledException(); // #8246
        Storage.Results.AddRange(
            Scores.OrderByDescending(s => s.Value).Select(r => r.Key));

        #endregion

    }

    private static readonly List<SearchEntry<WikiEntry>> CurseForgeSearchEntries = GetSearchEntriesCache(ResourcePlatforms.CurseForge).ToList();
    private static readonly List<SearchEntry<WikiEntry>> ModrinthSearchEntries = GetSearchEntriesCache(ResourcePlatforms.Modrinth).ToList();

    /// <summary>
    /// 生成用于中文搜索的数据库条目。
    /// </summary>
    private static IEnumerable<SearchEntry<WikiEntry>> GetSearchEntriesCache(ResourcePlatforms Source) {
        foreach (WikiEntry Entry in WikiEntry.All.Value) {
            if (!Entry.Slugs.ContainsKey(Source)) continue;
            if (Entry.ChineseName is not null) {
                yield return new SearchEntry<WikiEntry> { Item = Entry, SearchSource = new List<SearchSource> {
                    new SearchSource(Strings.StrConv(Entry.ChineseName.BeforeFirst(" ("), VbStrConv.SimplifiedChinese).Split('/', true), 1), // 部分 Mod 有别名
                    new SearchSource(Strings.StrConv(Entry.ChineseName.AfterFirst(" ("), VbStrConv.SimplifiedChinese) + Entry.Slugs[Source], 0.5)
                }};
            } else {
                yield return new SearchEntry<WikiEntry> { Item = Entry, SearchSource = new List<SearchSource> {
                    new SearchSource(Entry.Slugs[Source], 0.5)
                }};
            }
        }
    }

}
