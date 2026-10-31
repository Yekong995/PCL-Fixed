// 由 VB Modules\Resource\LocalResourceLoaders.vb 迁移而来。
// VB UI 依赖（FrmInstanceMod.Load、FrmInstanceMod.LoaderRun、PageInstanceMod.UpdatingInstanceModFolders、
// PageInstanceLeft.Instance、FrmInstanceMod.Filter/RefreshUI/RefreshBars、ModeDebug）经 PclUiBridge 桥接。
#nullable disable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using Newtonsoft.Json.Linq;

namespace PCLCS.Modules;

public static class LocalResourceLoaders {

    // 加载 Mod 列表
    public static readonly LoaderTask<string, List<LocalResourceFile>> LocalResourceLoader = new LoaderTask<string, List<LocalResourceFile>>("LocalResourceLoader", LocalResourceFileLoad);

    // 原 VB 为 LocalResourceFileLoad 方法内的 Static IsModFile As Func(Of String, Boolean)（VB Static 在多次调用间保持状态，故译作类静态只读字段）
    private static readonly Func<string, bool> IsModFile =
        FullName => {
            if (FullName is null) return false;
            if (new[] { "disabled", "old" }.Contains(PathUtils.GetExtension(FullName))) FullName = PathUtils.GetFileNameWithoutExtension(FullName);
            return new[] { "jar", "zip", "litemod" }.Contains(PathUtils.GetExtension(FullName));
        };

    private static void LocalResourceFileLoad(LoaderTask<string, List<LocalResourceFile>> Loader) {
        try {
            PclCore.RunInUiWait(() => PclUiBridge.ModPageLoadShowProgress?.Invoke(false));

            // 等待 Mod 更新完成
            if (PclUiBridge.ModPageUpdatingFolderContains(Loader.Input)) {
                Logger.Info($"等待 Mod 更新完成后才能继续加载 Mod 列表：{Loader.Input}");
                try {
                    PclCore.RunInUiWait(() => PclUiBridge.ModPageLoadText?.Invoke("正在更新 Mod"));
                    while (PclUiBridge.ModPageUpdatingFolderContains(Loader.Input)) {
                        if (Loader.IsCanceled) return;
                        Thread.Sleep(100);
                    }
                } finally {
                    PclCore.RunInUiWait(() => PclUiBridge.ModPageLoadText?.Invoke("正在加载 Mod 列表"));
                }
                PclUiBridge.ModPageLoaderRun?.Invoke(LoaderFolderRunType.UpdateOnly);
            }

            // 获取 Mod 文件夹下的可用文件列表
            List<string> ModFileList = new List<string>();
            if (DirectoryUtils.Exists(Loader.Input)) {
                foreach (string File in DirectoryUtils.EnumerateFiles(Loader.Input, true)) {
                    if (!PathUtils.IsSame(PathUtils.RemoveLastPart(File), Loader.Input)) {
                        // 仅当 Forge 1.13- 且文件夹名与版本号相同时，才加载该子文件夹下的 Mod
                        McInstance Instance = PclUiBridge.ModPageInstanceGet();
                        if (!(Instance is not null && Instance.Version.ModLoaders.HasFlagF(ModLoaders.Forge) &&
                              Instance.Version.Vanilla.Major < 13 &&
                              PathUtils.GetLastPart(PathUtils.RemoveLastPart(File)) == $"1.{Instance.Version.Vanilla.Major}.{Instance.Version.Vanilla.Build}")) {
                            continue;
                        }
                    }
                    if (IsModFile(File)) ModFileList.Add(File);
                }
            }

            // 获取本地文件缓存
            string CachePath = PclCore.GetPathTemp() + "Cache\\LocalMod.json";
            JObject Cache = new JObject();
            try {
                string CacheContent = FileUtils.TryReadAsString(CachePath);
                if (!string.IsNullOrWhiteSpace(CacheContent)) {
                    Cache = (JObject)CacheContent.DeserializeJson();
                    if (!Cache.ContainsKey("version") || Cache["version"].ToObject<int>() != Versions.LocalModCacheVersion) {
                        Logger.Warn($"本地 Mod 信息缓存版本已过期，将弃用这些缓存信息");
                        Cache = new JObject();
                    }
                }
            } catch (Exception ex) {
                Logger.Warn(ex, "读取本地 Mod 信息缓存失败，已重置");
                Cache = new JObject();
            }
            Cache["version"] = Versions.LocalModCacheVersion;

            // 加载 Mod 列表
            List<LocalResourceFile> ModList = new List<LocalResourceFile>();
            foreach (string ModFile in ModFileList) {
                if (Loader.IsCanceled) return;
                LocalResourceFile ModEntry = new LocalResourceFile(ModFile);
                LocalResourceFile DumpMod = ModList.FirstOrDefault(m => m.EnabledName == ModEntry.EnabledName); // 存在两个文件，名称相同，但一个启用一个禁用
                if (DumpMod is not null) {
                    LocalResourceFile DisabledMod = DumpMod.IsEnabled ? ModEntry : DumpMod;
                    Logger.Warn($"重复的 Mod 文件：{DumpMod.File.Name} 与 {ModEntry.File.Name}，已忽略 {DisabledMod.File.Name}");
                    if (DisabledMod == ModEntry) {
                        continue;
                    } else {
                        ModList.Remove(DisabledMod);
                    }
                }
                ModList.Add(ModEntry);
            }
            Logger.Info($"共发现 {ModList.Count} 个 Mod");

            // 排序
            ModList = ModList.OrderBy(m => m.File.Name).ToList();

            // 回设
            if (Loader.IsCanceled) return;
            Loader.Output = ModList;

            // 开始联网加载
            // TODO: 添加信息获取中提示
            LocalResourceOnlineLoader.Start((ModList, Cache, PclUiBridge.ModPageInstanceGet().Version), IsForceRestart: true);

        } catch (Exception ex) {
            Logger.Warn(ex, "Mod 列表加载失败");
            throw;
        }
    }

    // 联网加载 Mod 详情
    public static readonly LoaderTask<(List<LocalResourceFile> ModList, JObject Cache, McVersion Target), int> LocalResourceOnlineLoader = new LoaderTask<(List<LocalResourceFile> ModList, JObject Cache, McVersion Target), int>("LocalResourceOnlineLoader", LocalResourceOnlineLoad);

    private static void LocalResourceOnlineLoad(LoaderTask<(List<LocalResourceFile> ModList, JObject Cache, McVersion Target), int> Loader) {
        List<LocalResourceFile> Mods = new List<LocalResourceFile>();
        JObject Cache = Loader.Input.Cache;
        // VB 中局部变量 ModLoaders 与枚举类型 ModLoaders 同名（VB 允许类型与变量同名，C# 不允许），故译作 TargetModLoaders
        ModLoaders TargetModLoaders = Loader.Input.Target.ModLoaders;
        // 读取缓存，获取需要更新的 Mod 列表
        foreach (LocalResourceFile ModEntry in Loader.Input.ModList) {
            if (Loader.IsCanceled) return;
            string CacheKey = ModEntry.ModrinthHash + Loader.Input.Target.VanillaName + TargetModLoaders;
            if (Cache.ContainsKey(CacheKey)) {
                ModEntry.FromJson((JObject)Cache[CacheKey]);
                // 如果缓存中的信息在 6 小时以内更新过，则无需重新获取
                if (ModEntry.OnlineDataLoaded && DateTime.Now - Cache[CacheKey]["Project"]["CacheTime"].ToObject<DateTime>() < new TimeSpan(6, 0, 0)) continue;
            }
            Mods.Add(ModEntry);
        }
        Logger.Info($"有 {Mods.Where(m => m.Project is null).Count()} 个 Mod 需要联网获取信息，{Mods.Where(m => m.Project is not null).Count()} 个 Mod 需要更新信息");
        if (!Mods.Any()) return;
        // 获取作为检查目标的版本
        // 此处不应向下扩展检查的 MC 小版本，例如 Mod 在更新 1.16.5 后，对早期的 1.16.2 版本发布了修补补丁，这会导致 PCL 将 1.16.5 版本的 Mod 降级到 1.16.2
        McVersion TargetMcVersion = Loader.Input.Target;
        string VanillaVersion = TargetMcVersion.VanillaName;
        // 开始网络获取
        Logger.Info($"正在检查 Mod 信息的版本：{VanillaVersion}，加载器 {TargetModLoaders}");
        int EndedThreadCount = 0;
        bool IsFailed = false;
        Thread CurrentTaskThread = Thread.CurrentThread;
        // 从 Modrinth 获取信息
        PclCore.RunInNewThread(
        () => {
            try {
                // 步骤 1：获取 Hash 与对应的工程 ID
                List<string> ModrinthHashes = Mods.Select(m => m.ModrinthHash).ToList();
                JObject ModrinthVersion = (JObject)PclNet.DlModRequest("https://api.modrinth.com/v2/version_files", HttpMethod.Post,
                    $"{{\"hashes\": [\"{ModrinthHashes.Join("\",\"")}\"], \"algorithm\": \"sha1\"}}", "application/json");
                Logger.Info($"从 Modrinth 获取到 {ModrinthVersion.Count} 个本地 Mod 的对应信息");
                // 步骤 2：尝试读取工程信息缓存，构建其他 Mod 的对应关系
                if (ModrinthVersion.Count == 0) return;
                Dictionary<string, List<LocalResourceFile>> ModrinthMapping = new Dictionary<string, List<LocalResourceFile>>();
                Dictionary<LocalResourceFile, List<string>> ModrinthLoaders = new Dictionary<LocalResourceFile, List<string>>();
                foreach (LocalResourceFile Entry in Mods) {
                    if (!ModrinthVersion.ContainsKey(Entry.ModrinthHash)) continue;
                    if ((string)ModrinthVersion[Entry.ModrinthHash]["files"][0]["hashes"]["sha1"] != Entry.ModrinthHash) continue;
                    string ProjectId = ModrinthVersion[Entry.ModrinthHash]["project_id"].ToString();
                    if (ResourceProject.Cache.ContainsKey(ProjectId) && Entry.Project is null) Entry.Project = ResourceProject.Cache[ProjectId]; // 读取已加载的缓存，加快结果出现速度
                    if (!ModrinthMapping.ContainsKey(ProjectId)) ModrinthMapping[ProjectId] = new List<LocalResourceFile>();
                    ModrinthMapping[ProjectId].Add(Entry);
                    // 记录对应的 ProjectVersion
                    ResourceVersion File = ResourceVersion.FromPlatformJson((JObject)ModrinthVersion[Entry.ModrinthHash], ResourceTypes.Mod);
                    ModrinthLoaders[Entry] = ModrinthVersion[Entry.ModrinthHash]["loaders"].
                        Select(l => l.ToString().Lower()).Distinct().OrderBy(l => l).ToList();
                    if (Entry.ProjectVersion is null || Entry.ProjectVersion.ReleaseDate < File.ReleaseDate) {
                        Entry.ProjectVersion = File;
                    } else {
                        Entry.ProjectVersion.Version = File.Version; // 使用来自 Modrinth 的版本号
                    }
                }
                if (Loader.IsCanceledWithThread(CurrentTaskThread)) return;
                Logger.Info($"需要从 Modrinth 获取 {ModrinthMapping.Count} 个本地 Mod 的工程信息");
                // 步骤 3：获取工程信息
                if (!ModrinthMapping.Any()) return;
                JArray ModrinthProject = (JArray)PclNet.DlModRequest(
                    $"https://api.modrinth.com/v2/projects?ids=[\"{ModrinthMapping.Keys.Join("\",\"")}\"]");
                foreach (JToken ProjectJson in ModrinthProject) {
                    ResourceProject Project = new ResourceProject((JObject)ProjectJson);
                    foreach (LocalResourceFile Entry in ModrinthMapping[Project.Id]) {
                        Entry.Project = Project;
                    }
                }
                Logger.Info($"已从 Modrinth 获取本地 Mod 信息，继续获取更新信息");
                // 步骤 4：获取更新信息
                List<LocalResourceFile> ModsToUpdate = ModrinthMapping.SelectMany(l => l.Value).ToList();
                if (!ModsToUpdate.Any()) return;
                string UpdateData = ModsToUpdate.
                    Select(m => $"{{\"hash\": \"{m.ModrinthHash}\", \"loaders\": [\"{ModrinthLoaders[m].Join("\",\"")}\"], \"game_versions\": [\"{VanillaVersion}\"]}}").
                    Join(",");
                JObject ModrinthUpdate = (JObject)PclNet.DlModRequest("https://api.modrinth.com/v2/version_files/update_individual", HttpMethod.Post,
                    $"{{\"algorithm\": \"sha1\", \"hashes\": [{UpdateData}]}}", "application/json");
                foreach (LocalResourceFile Entry in Mods) {
                    if (!ModrinthUpdate.ContainsKey(Entry.ModrinthHash) || Entry.ProjectVersion is null) continue;
                    ResourceVersion UpdateFile = ResourceVersion.FromPlatformJson((JObject)ModrinthUpdate[Entry.ModrinthHash], ResourceTypes.Mod);
                    if (!UpdateFile.DownloadAvailable) continue;
                    List<string> UpdateLoaders = ModrinthUpdate[Entry.ModrinthHash]["loaders"].
                        Select(l => l.ToString().Lower()).Distinct().OrderBy(l => l).ToList();
                    if (!UpdateLoaders.SequenceEqual(ModrinthLoaders[Entry])) continue;
                    Logger.Trace(() => $"本地文件 {Entry.ProjectVersion.FileName} 在 Modrinth 上的最新版为 {UpdateFile.FileName}");
                    if (Entry.ProjectVersion.ReleaseDate >= UpdateFile.ReleaseDate || Entry.ProjectVersion.Hash == UpdateFile.Hash) continue;
                    // 设置更新日志与更新文件
                    if (Entry.UpdateFile is not null && UpdateFile.Hash == Entry.UpdateFile.Hash) { // 合并
                        Entry.ChangelogUrls.Add($"https://modrinth.com/mod/{ModrinthUpdate[Entry.ModrinthHash]["project_id"]}/changelog?g={VanillaVersion}");
                        UpdateFile.DownloadUrls.AddRange(Entry.UpdateFile.DownloadUrls); // 合并下载源
                        Entry.UpdateFile = UpdateFile; // 优先使用 Modrinth 的文件
                    } else if (Entry.UpdateFile is null || UpdateFile.ReleaseDate >= Entry.UpdateFile.ReleaseDate) { // 替换
                        Entry.ChangelogUrls = new List<string> { $"https://modrinth.com/mod/{ModrinthUpdate[Entry.ModrinthHash]["project_id"]}/changelog?g={VanillaVersion}" };
                        Entry.UpdateFile = UpdateFile;
                    }
                }
                Logger.Info($"从 Modrinth 获取本地 Mod 信息结束");
            } catch (Exception ex) {
                Logger.Warn(ex, "从 Modrinth 获取本地 Mod 信息失败");
                IsFailed = true;
            } finally {
                EndedThreadCount += 1;
            }
        }, "Mod List Detail Loader Modrinth");
        // 从 CurseForge 获取信息
        PclCore.RunInNewThread(
        () => {
            try {
                // 步骤 1：获取 Hash 与对应的工程 ID
                List<uint> Hashes = new List<uint>();
                foreach (LocalResourceFile Entry in Mods) {
                    Hashes.Add(Entry.CurseForgeHash);
                    if (Loader.IsCanceledWithThread(CurrentTaskThread)) return;
                }
                JContainer CurseForgeRaw = (JContainer)PclNet.DlModRequest("https://api.curseforge.com/v1/fingerprints/432", HttpMethod.Post,
                    $"{{\"fingerprints\": [{Hashes.Join(",")}]}}", "application/json")["data"]["exactMatches"];
                Logger.Info($"从 CurseForge 获取到 {CurseForgeRaw.Count} 个本地 Mod 的对应信息");
                if (!CurseForgeRaw.Any()) return;

                // 步骤 2：尝试读取工程信息缓存，构建其他 Mod 的对应关系
                Dictionary<int, List<LocalResourceFile>> ProjectIdToLocalFiles = new Dictionary<int, List<LocalResourceFile>>();
                Dictionary<LocalResourceFile, ModLoaders> CurseForgeLoaders = new Dictionary<LocalResourceFile, ModLoaders>();
                foreach (JToken Project in CurseForgeRaw) {
                    string ProjectId = Project["id"].ToString();
                    uint Hash = Project["file"]["fileFingerprint"].ToObject<uint>();
                    foreach (LocalResourceFile Entry in Mods) {
                        if (Entry.CurseForgeHash != Hash) continue;
                        if (ResourceProject.Cache.ContainsKey(ProjectId) && Entry.Project is null) Entry.Project = ResourceProject.Cache[ProjectId]; // 读取已加载的缓存，加快结果出现速度
                        ProjectIdToLocalFiles.AddIntoValueCollection(int.Parse(ProjectId), Entry);
                        // 记录对应的 ProjectVersion
                        ResourceVersion File = ResourceVersion.FromPlatformJson((JObject)Project["file"], ResourceTypes.Mod);
                        CurseForgeLoaders[Entry] = File.ModLoaders;
                        if (Entry.ProjectVersion is null || Entry.ProjectVersion.ReleaseDate < File.ReleaseDate) Entry.ProjectVersion = File;
                    }
                }
                if (Loader.IsCanceledWithThread(CurrentTaskThread)) return;
                Logger.Info($"需要从 CurseForge 获取 {ProjectIdToLocalFiles.Count} 个本地 Mod 的工程信息");
                if (!ProjectIdToLocalFiles.Any()) return;

                // 步骤 3：获取工程信息
                JToken CurseForgeProject = PclNet.DlModRequest("https://api.curseforge.com/v1/mods", HttpMethod.Post,
                    $"{{\"modIds\": [{ProjectIdToLocalFiles.Keys.Join(",")}]}}", "application/json")["data"];
                Dictionary<int, List<LocalResourceFile>> UpdateFileIds = new Dictionary<int, List<LocalResourceFile>>(); // FileId -> 本地 Mod 文件列表
                Dictionary<int, string> FileIdToProjectSlug = new Dictionary<int, string>();
                foreach (JToken ProjectJson in CurseForgeProject) {
                    if (ProjectJson["isAvailable"] is not null && !ProjectJson["isAvailable"].ToObject<bool>()) continue;
                    // 设置 Entry 中的工程信息
                    ResourceProject Project = new ResourceProject((JObject)ProjectJson);
                    foreach (LocalResourceFile LocalFile in ProjectIdToLocalFiles[int.Parse(Project.Id)]) { // 倒查防止 CurseForge 返回的内容有漏
                        if (LocalFile.Project?.Platform == ResourcePlatforms.Modrinth) {
                            LocalFile.Project = LocalFile.Project; // 再次触发修改事件
                        } else {
                            LocalFile.Project = Project;
                        }
                        // 查找可能有版本更新的文件列表
                        List<JToken> LatestFiles = ProjectJson["latestFilesIndexes"].Where(
                        Entry => {
                            // Mod Loader 匹配
                            ModLoaders EntryLoader = Entry["modLoader"] is null ? ModLoaders.None :
                                Resource.FromCurseForgeModLoaderType(Entry["modLoader"].ToObject<int>());
                            if (CurseForgeLoaders[LocalFile] == ModLoaders.None) {
                                if (EntryLoader != ModLoaders.None) return false;
                            } else if (!CurseForgeLoaders[LocalFile].HasFlagF(EntryLoader)) {
                                return false;
                            }
                            // MC 版本匹配
                            string EntryVersion = (string)Entry["gameVersion"];
                            return EntryVersion is not null && EntryVersion == VanillaVersion;
                        }).
                        MaxByAll(Entry => Entry["gameVersion"].ToString(), Comparer<string>.Create((x, y) => McUtils.CompareVersion(x, y))); // 只保留最新的 MC 版本
                        foreach (JToken Entry in LatestFiles) {
                            int FileId = Entry["fileId"].ToObject<int>();
                            if (!UpdateFileIds.ContainsKey(FileId)) UpdateFileIds[FileId] = new List<LocalResourceFile>();
                            UpdateFileIds[FileId].Add(LocalFile);
                            FileIdToProjectSlug[FileId] = Project.Slug;
                        }
                    }
                }
                Logger.Info($"已从 CurseForge 获取本地 Mod 信息，需要获取 {UpdateFileIds.Count} 个用于检查更新的文件信息");
                if (!UpdateFileIds.Any()) return;

                // 步骤 4：获取更新文件信息
                JToken CurseForgeFiles = PclNet.DlModRequest("https://api.curseforge.com/v1/mods/files", HttpMethod.Post,
                                    $"{{\"fileIds\": [{UpdateFileIds.Keys.Join(",")}]}}", "application/json")["data"];
                Dictionary<LocalResourceFile, ResourceVersion> UpdateFiles = new Dictionary<LocalResourceFile, ResourceVersion>();
                foreach (JToken FileJson in CurseForgeFiles) {
                    ResourceVersion File = ResourceVersion.FromPlatformJson((JObject)FileJson, ResourceTypes.Mod);
                    if (!File.DownloadAvailable) continue;
                    foreach (LocalResourceFile Entry in UpdateFileIds[int.Parse(File.Id)]) {
                        if (File.ModLoaders != CurseForgeLoaders[Entry]) {
                            Logger.Warn($"从 CurseForge 获取到的更新文件 {File.FileName} 加载器为 {File.ModLoaders}，与本地文件 {Entry.File.Name} 的加载器 {CurseForgeLoaders[Entry]} 不一致，已忽略", LogBehavior.None);
                            continue;
                        }
                        if (UpdateFiles.ContainsKey(Entry) && UpdateFiles[Entry].ReleaseDate >= File.ReleaseDate) continue;
                        UpdateFiles[Entry] = File;
                    }
                }
                foreach (KeyValuePair<LocalResourceFile, ResourceVersion> Pair in UpdateFiles) {
                    LocalResourceFile Entry = Pair.Key;
                    ResourceVersion UpdateFile = Pair.Value;
                    Logger.Trace(() => $"本地文件 {Entry.ProjectVersion.FileName} 在 CurseForge 上的最新版为 {UpdateFile.FileName}");
                    if (Entry.ProjectVersion.ReleaseDate >= UpdateFile.ReleaseDate || Entry.ProjectVersion.Hash == UpdateFile.Hash) continue;
                    // 设置更新日志与更新文件
                    if (Entry.UpdateFile is not null && UpdateFile.Hash == Entry.UpdateFile.Hash) { // 合并
                        Entry.ChangelogUrls.Add($"https://www.curseforge.com/minecraft/mc-mods/{FileIdToProjectSlug[int.Parse(UpdateFile.Id)]}/files/{UpdateFile.Id}");
                        Entry.UpdateFile.DownloadUrls.AddRange(UpdateFile.DownloadUrls); // 合并下载源
                    } else if (Entry.UpdateFile is null || UpdateFile.ReleaseDate > Entry.UpdateFile.ReleaseDate) { // 替换
                        Entry.ChangelogUrls = new List<string> { $"https://www.curseforge.com/minecraft/mc-mods/{FileIdToProjectSlug[int.Parse(UpdateFile.Id)]}/files/{UpdateFile.Id}" };
                        Entry.UpdateFile = UpdateFile;
                    }
                }

                Logger.Info($"从 CurseForge 获取 Mod 更新信息结束");
            } catch (Exception ex) {
                Logger.Warn(ex, "从 CurseForge 获取本地 Mod 信息失败");
                IsFailed = true;
            } finally {
                EndedThreadCount += 1;
            }
        }, "Mod List Detail Loader CurseForge");
        // 等待线程结束
        while (EndedThreadCount != 2) {
            Thread.Sleep(10);
            if (Loader.IsCanceled) return;
        }
        // 保存缓存
        Mods = Mods.Where(m => m.Project is not null).ToList();
        Logger.Info($"联网获取本地 Mod 信息完成，为 {Mods.Count} 个 Mod 更新缓存");
        if (!Mods.Any()) return;
        if (Loader.IsCanceled) return;
        foreach (LocalResourceFile Entry in Mods) {
            Entry.OnlineDataLoaded = !IsFailed;
            Cache[Entry.ModrinthHash + VanillaVersion + TargetModLoaders] = Entry.ToJson();
        }
        FileUtils.Write(PclCore.GetPathTemp() + "Cache\\LocalMod.json", Cache.ToString(PclUiBridge.ModeDebugGet() ? Formatting.Indented : Formatting.None));
        // 刷新边栏
        if (Loader.IsCanceled) return;
        if (PclUiBridge.ModPageFilterGet() == 3) { // 3 = PageInstanceMod.FilterType.CanUpdate
            PclCore.RunInUi(() => PclUiBridge.ModPageRefreshUI?.Invoke()); // 同步 “可更新” 列表 (#4677)
        } else {
            PclCore.RunInUi(() => PclUiBridge.ModPageRefreshBars?.Invoke());
        }
    }

}
