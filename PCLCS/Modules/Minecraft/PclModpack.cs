// 由 VB Modules\Minecraft\ModModpack.vb 迁移而来。
// UI 依赖（Hint、MyMsgBox、MyMsgBoxInput、任务栏、安装加载器构造、窗体操作等）通过 PclUiBridge 桥接，
// VB 侧 ModModpack 保留同名转发壳。
#nullable disable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using MeloongCore;
using MeloongCore.Extensions;
using Newtonsoft.Json.Linq;

namespace PCLCS.Modules;

public static class PclModpack {

    // 触发整合包安装的外部接口
    /// <summary>
    /// 弹窗要求选择一个整合包文件并进行安装。
    /// </summary>
    public static void ModpackInstall() {
        string File = Dialogs.SelectFile("选择整合包压缩文件", false, filter: new[] { (new[] { "rar", "zip", "mrpack" }, "整合包文件") }).FirstOrDefault(); // 选择整合包文件
        if (string.IsNullOrEmpty(File)) return;
        PclCore.RunInThread(
        () => {
            try {
                ModpackInstall(File);
            } catch (Exception ex) {
                ex.ThrowIfCanceled();
                Logger.Error(ex, "手动安装整合包失败", LogBehavior.Alert);
            }
        });
    }
    /// <summary>
    /// 构建并启动安装给定的整合包文件的加载器，并返回该加载器。若失败则抛出异常。
    /// 必须在工作线程执行。
    /// </summary>
    public static LoaderCombo<string> ModpackInstall(string File, string InstanceName = null, string Logo = null) {
        Logger.Info($"整合包安装请求：{File ?? "null"}");
        ZipArchive Archive = null;
        string ArchiveBaseFolder = "";
        try {
            // 字符校验
            string TargetFolder = $"{PclUiBridge.McFolderSelectedGet()}versions\\{InstanceName}\\";
            if (TargetFolder.Contains("!") || TargetFolder.Contains(";")) { PclUiBridge.ShowHint?.Invoke("游戏路径中不能含有感叹号或分号：" + TargetFolder, HintType.Red); throw new OperationCanceledException(); }
            // 获取整合包种类与关键 Json
            int PackType = -1;
            try {
                Archive = FileUtils.OpenZip(File);
                // 从根目录判断整合包类型
                bool Found = false;
                if (Archive.GetEntry("mcbbs.packmeta") is not null) { PackType = 3; Found = true; } // MCBBS 整合包（优先于 manifest.json 判断）
                if (!Found && Archive.GetEntry("mmc-pack.json") is not null) { PackType = 2; Found = true; } // MMC 整合包（优先于 manifest.json 判断，#4194）
                if (!Found && Archive.GetEntry("modrinth.index.json") is not null) { PackType = 4; Found = true; } // Modrinth 整合包
                if (!Found && Archive.GetEntry("manifest.json") is not null) {
                    JObject Json = (JObject)Archive.GetEntry("manifest.json").Open().ReadString(Encoding.UTF8).DeserializeJson();
                    if (Json["addons"] is null) {
                        PackType = 0; Found = true; // CurseForge 整合包
                    } else {
                        PackType = 3; Found = true; // MCBBS 整合包
                    }
                }
                if (!Found && Archive.GetEntry("modpack.json") is not null) { PackType = 1; Found = true; } // HMCL 整合包
                if (!Found && (Archive.GetEntry("modpack.zip") is not null || Archive.GetEntry("modpack.mrpack") is not null)) { PackType = 9; Found = true; } // 带启动器的压缩包
                if (!Found) {
                    // 从一级目录判断整合包类型
                    foreach (ZipArchiveEntry Entry in Archive.Entries) {
                        string[] PathSplits = Entry.FullName.Split('/');
                        ArchiveBaseFolder = PathSplits[0] + "/";
                        // 确定为一级目录下
                        if (PathSplits.Length != 2) continue;
                        // 判断是否为关键文件
                        if (PathSplits[1] == "mcbbs.packmeta") { PackType = 3; break; } // MCBBS 整合包（优先于 manifest.json 判断）
                        if (PathSplits[1] == "mmc-pack.json") { PackType = 2; break; } // MMC 整合包（优先于 manifest.json 判断，#4194）
                        if (PathSplits[1] == "modrinth.index.json") { PackType = 4; break; } // Modrinth 整合包
                        if (PathSplits[1] == "manifest.json") {
                            JObject Json = (JObject)Entry.Open().ReadString(Encoding.UTF8).DeserializeJson();
                            if (Json["addons"] is null) {
                                PackType = 0; break; // CurseForge 整合包
                            } else {
                                PackType = 3; ArchiveBaseFolder = "overrides/"; break; // MCBBS 整合包
                            }
                        }
                        if (PathSplits[1] == "modpack.json") { PackType = 1; break; } // HMCL 整合包
                        if (PathSplits[1] == "modpack.zip" || PathSplits[1] == "modpack.mrpack") { PackType = 9; break; } // 带启动器的压缩包
                    }
                }
            } catch (Exception ex) {
                if (ex.GetDisplay(true).Contains("Error.WinIOError")) {
                    throw new Exception("打开整合包文件失败", ex);
                } else if (PathUtils.GetExtension(File) == "rar") {
                    throw new Exception("PCL 无法处理 rar 格式的压缩包，请在解压后重新压缩为 zip 格式再试", ex);
                } else {
                    throw new Exception("打开整合包文件失败，文件可能损坏或为不支持的压缩包格式", ex);
                }
            }
            // 执行对应的安装方法
            switch (PackType) {
                case 0:
                    Logger.Info("整合包种类：CurseForge");
                    return InstallPackCurseForge(File, Archive, ArchiveBaseFolder, InstanceName, Logo);
                case 1:
                    Logger.Info("整合包种类：HMCL");
                    return InstallPackHMCL(File, Archive, ArchiveBaseFolder);
                case 2:
                    Logger.Info("整合包种类：MMC");
                    return InstallPackMMC(File, Archive, ArchiveBaseFolder);
                case 3:
                    Logger.Info("整合包种类：MCBBS");
                    return InstallPackMCBBS(File, Archive, ArchiveBaseFolder, InstanceName);
                case 4:
                    Logger.Info("整合包种类：Modrinth");
                    return InstallPackModrinth(File, Archive, ArchiveBaseFolder, InstanceName, Logo);
                case 9:
                    Logger.Info("整合包种类：带启动器的压缩包");
                    return InstallPackLauncherPack(File, Archive, ArchiveBaseFolder);
                default:
                    Logger.Info("整合包种类：未能识别，假定为压缩包");
                    return InstallPackCompress(File, Archive);
            }
        } finally {
            if (Archive is not null) Archive.Dispose();
        }
    }

    private static void ExtractModpackFiles(string InstallTemp, string FileAddress, CancellationToken c = default, ProgressProvider p = null) {
        DirectoryUtils.Delete(InstallTemp);
        c.ThrowIfCancellationRequested();
        FileUtils.ExtractToDirectory(FileAddress, InstallTemp, c, p);
    }
    /// <summary>
    /// 从整合包的 override 目录复制文件，同时设置 PCL 的配置文件与版本隔离。
    /// 对路径末尾是否为 \ 没有要求。
    /// </summary>
    private static void CopyOverrideDirectory(string OverridesFolder, string VersionFolder, LoaderBase Loader) {
        OverridesFolder = PathUtils.AddSlashSuffix(OverridesFolder);
        VersionFolder = PathUtils.AddSlashSuffix(VersionFolder);
        // 复制文件
        if (DirectoryUtils.Exists(OverridesFolder)) {
            Logger.Info($"处理整合包覆写文件夹：{OverridesFolder} → {VersionFolder}");
            DirectoryUtils.Copy(OverridesFolder, VersionFolder);
        } else {
            Logger.Info($"整合包中没有覆写文件夹：{OverridesFolder}");
        }
        // 设置 ini
        string OverridesIni = $"{OverridesFolder}PCL\\Setup.ini";
        string VersionIni = $"{VersionFolder}PCL\\Setup.ini";
        if (FileUtils.Exists(OverridesIni)) {
            PclCore.WriteIni(OverridesIni, "VersionArgumentIndie", "1"); // 开启版本隔离
            PclCore.WriteIni(OverridesIni, "VersionArgumentIndieV2", "True");
            PclCore.WriteIni(OverridesIni, "IsStar", "False");
            FileUtils.Copy(OverridesIni, VersionIni); // 覆写已有的 ini
        } else {
            PclCore.WriteIni(VersionIni, "VersionArgumentIndie", "1"); // 开启版本隔离
            PclCore.WriteIni(VersionIni, "VersionArgumentIndieV2", "True");
        }
        PclCore.IniClearCache(VersionIni); // 重置缓存，避免被安装过程中写入的 ini 覆盖
    }
    /// <summary>
    /// 弹窗提示整合包中存在不兼容的加载器。
    /// 如果取消，则抛出 CancelledException。
    /// </summary>
    private static void NotifyIncompatibleLoader(string LoaderName) {
        if (PclUiBridge.ShowModpackMsgBox?.Invoke($"整合包中存在不兼容的加载器：{LoaderName}{Environment.NewLine}如果你知道如何手动安装它，可以先选择跳过，然后在整合包下载结束后手动安装。",
            "不兼容的加载器", "取消", $"不安装 {LoaderName} 并继续", null, false, false) == 1) throw new OperationCanceledException();
    }

#region 不同类型整合包的安装方法

    // CurseForge
    private static LoaderCombo<string> InstallPackCurseForge(string FileAddress, ZipArchive Archive, string ArchiveBaseFolder,
            string InstanceName = null, string Logo = null) {

        // 读取 Json 文件
        JObject Json;
        try {
            Json = (JObject)Archive.GetEntry(ArchiveBaseFolder + "manifest.json").Open().ReadString(Encoding.UTF8).DeserializeJson();
        } catch (Exception ex) {
            throw new Exception("CurseForge 整合包安装信息存在问题", ex);
        }
        if (Json["minecraft"] is null || Json["minecraft"]["version"] is null) throw new Exception("CurseForge 整合包未提供 Minecraft 版本信息");

        // 获取版本名
        if (InstanceName is null) {
            InstanceName = Json["name"]?.ToString() ?? "";
            ValidateFolderName Validate = new ValidateFolderName(PclUiBridge.McFolderSelectedGet() + "versions");
            if (Validate.Validate(InstanceName) != "") InstanceName = "";
            if (InstanceName == "") InstanceName = PclUiBridge.ModpackInput?.Invoke("输入版本名称", "", "", new ValidateBase[] { Validate }, "");
            if (string.IsNullOrEmpty(InstanceName)) throw new OperationCanceledException();
        }
        string VersionFolder = PathUtils.AddSlashSuffix(Path.Combine(PclUiBridge.McFolderSelectedGet(), "versions", InstanceName) + "\\");

        // 获取 Mod API 版本信息
        McVersion TargetVersion = new McVersion(Json["minecraft"]["version"].ToString());
        foreach (JToken Entry in Json["minecraft"]?["modLoaders"] ?? new JArray()) {
            string Id = (Entry["id"]?.ToString() ?? "").ToString().Lower();
            if (Id.StartsWithF("forge-")) {
                // Forge 指定
                if (Id.Contains("recommended")) throw new Exception("该整合包版本过老，已不支持进行安装！");
                Logger.Info($"整合包 Forge 版本：{Id}");
                TargetVersion.ModLoaderVersions[ModLoaders.Forge] = Id.Replace("forge-", "");
            } else if (Id.StartsWithF("neoforge-")) {
                // NeoForge 指定
                Logger.Info($"整合包 NeoForge 版本：{Id}");
                TargetVersion.ModLoaderVersions[ModLoaders.NeoForge] = Id.Replace("neoforge-", "");
            } else if (Id.StartsWithF("fabric-")) {
                // Fabric 指定
                Logger.Info($"整合包 Fabric 版本：{Id}");
                TargetVersion.ModLoaderVersions[ModLoaders.Fabric] = Id.Replace("fabric-", "");
            } else {
                NotifyIncompatibleLoader(Id);
            }
        }
        // 解压
        string InstallTemp = PclUiBridge.RequestTaskTempFolder();
        List<LoaderBase> InstallLoaders = new List<LoaderBase>();
        string OverrideHome = Json["overrides"]?.ToString() ?? "";
        if (OverrideHome != "") {
            string OverridesFolder = InstallTemp + ArchiveBaseFolder + (OverrideHome == "." || OverrideHome == "./" ? "" : OverrideHome); // #5613
            if (!PathUtils.IsParentOf(InstallTemp, OverridesFolder)) throw new Exception("整合包的 overrides 路径超出了解压目录，请向整合包作者反馈此问题！");
            InstallLoaders.Add(new LoaderTask<string, int>("解压整合包文件",
            Task => {
                ExtractModpackFiles(InstallTemp, FileAddress, Task.CreateCancellationToken(), Task.CreateSyncProgressProvider(ToPercentage: 0.6));
                CopyOverrideDirectory(OverridesFolder, VersionFolder, Task);
            }) {
                ProgressWeight = FileUtils.GetInfo(FileAddress).Length / 1024d / 1024d / 6, Block = false }); // 每 6M 需要 1s
        }
        // 获取 Mod 列表
        List<int> ModList = new List<int>();
        List<int> ModOptionalList = new List<int>();
        foreach (JToken ModEntry in Json["files"] ?? new JArray()) {
            if (ModEntry["projectID"] is null || ModEntry["fileID"] is null) {
                PclUiBridge.ShowHint?.Invoke("某项 Mod 缺少必要信息，已跳过：" + ModEntry.ToString(), HintType.Blue);
                continue;
            }
            ModList.Add(ModEntry["fileID"].ToObject<int>());
            if (ModEntry["required"] is not null && !ModEntry["required"].ToObject<bool>()) ModOptionalList.Add(ModEntry["fileID"].ToObject<int>());
        }
        if (ModList.Any()) {
            List<LoaderBase> ModDownloadLoaders = new List<LoaderBase>();
            // 获取 Mod 下载信息
            ModDownloadLoaders.Add(new LoaderTask<int, JArray>("获取 Mod 下载信息",
            Task => {
                Task.Output = (JArray)PclNet.DlModRequest("https://api.curseforge.com/v1/mods/files", HttpMethod.Post, "{\"fileIds\": [" + ModList.Join(",") + "]}", "application/json")["data"];
                if (ModList.Count == Task.Output.Count) return;
                // 如果文件已被删除，则 API 会跳过那一项，导致返回的列表数量小于请求的数量。后面的都是对此的错误处理。
                // 获取缺失的 fileId 列表
                List<int> MissingIds = ModList.Except(Task.Output.Select(j => j["id"].ToObject<int>())).ToList();
                Logger.Info($"缺失的 fileIds：{MissingIds.Join("、")}");
                // 将 fileId 映射到 projectId
                List<(int ProjectId, int FileId)> Missings = MissingIds.Select(
                    id => (((JObject)Json["files"].AsEnumerable().First(f => f["fileID"].ToObject<int>() == id))["projectID"].ToObject<int>(), id)).ToList();
                Logger.Info($"缺失项：{Missings.Join("、")}");
                // 尝试获取对应的工程信息
                List<ResourceProject> Projects = new List<ResourceProject>();
                try {
                    Projects = ResourceProject.FromProjectIds(Missings.Select(e => e.ProjectId.ToString()), ResourcePlatforms.CurseForge).ToList();
                } catch (Exception ex) {
                    Logger.Warn(ex, "获取缺失的工程信息失败");
                }
                List<string> MissingDesc = new List<string>();
                foreach ((int ProjectId, int FileId) Missing in Missings) {
                    ResourceProject Project = Projects.FirstOrDefault(p => p.Id == Missing.ProjectId.ToString());
                    if (Project is null) {
                        MissingDesc.Add($"未知 CurseForge 项目 (项目 ID {Missing.ProjectId}，文件 ID {Missing.FileId})");
                    } else {
                        MissingDesc.Add($"{Project.TranslatedName} (文件 ID {Missing.FileId})");
                    }
                }
                if (PclUiBridge.ShowModpackMsgBox?.Invoke($"未找到该整合包所需的以下内容，它们可能已被原作者删除：{Environment.NewLine}- {MissingDesc.Join(Environment.NewLine + "- ")}{Environment.NewLine}{Environment.NewLine}是否继续安装？缺失这些内容可能导致整合包出现异常！",
                    "整合包内容缺失", "继续", "取消", null, true, false) == 2) {
                    Task.Cancel();
                }
            }) { ProgressWeight = ModList.Count / 10d }); // 每 10 Mod 需要 1s
            // 构造 NetFile
            ModDownloadLoaders.Add(new LoaderTask<JArray, List<NetFile>>("构造 Mod 下载信息",
            Task => {
                Dictionary<int, NetFile> FileList = new Dictionary<int, NetFile>();
                foreach (JToken ModJson in Task.Input) {
                    int Id = ModJson["id"].ToObject<int>();
                    // 跳过重复的 Mod（疑似 CurseForge Bug）
                    if (FileList.ContainsKey(Id)) continue;
                    // 可选 Mod 提示
                    if (ModOptionalList.Contains(Id)) {
                        if (PclUiBridge.ShowModpackMsgBox?.Invoke("是否要下载整合包中的可选文件 " + ModJson["displayName"].ToString() + "？", "下载可选文件", "是", "否", null, false, false) == 2) {
                            continue;
                        }
                    }
                    // 根据 modules 和文件名后缀判断资源类型
                    string TargetFolder; ResourceTypes Type;
                    if (ModJson["modules"].Any()) { // modules 可能返回 null（#1006）
                        List<string> ModuleNames = ((JArray)ModJson["modules"]).Select(l => l["name"].ToString()).ToList();
                        if (ModuleNames.Contains("META-INF") || ModuleNames.Contains("mcmod.info") || ModuleNames.Contains("fabric.mod.json") ||
                            (ModJson?["FileName"]?.ToString() ?? "").EndsWithF(".jar", true)) {
                            TargetFolder = "mods"; Type = ResourceTypes.Mod;
                        } else if (ModuleNames.Contains("pack.mcmeta")) {
                            TargetFolder = "resourcepacks"; Type = ResourceTypes.ResourcePack;
                        } else if (ModuleNames.Contains("shaders")) {
                            TargetFolder = "shaderpacks"; Type = ResourceTypes.Shader;
                        } else {
                            TargetFolder = "saves"; Type = ResourceTypes.Map;
                        }
                    } else {
                        TargetFolder = "mods"; Type = ResourceTypes.Mod;
                    }
                    // 建立 CompFile
                    ResourceVersion File = ResourceVersion.FromPlatformJson((JObject)ModJson, Type);
                    if (!File.DownloadAvailable) continue;
                    // 实际的添加
                    FileList.Add(Id, new NetFile(
                        ResourceVersion.ParseModrinthTrackArguments(File.DownloadUrls, ResourceVersion.DownloadReason.ModPack, TargetVersion.VanillaName, TargetVersion.ModLoaders),
                        PathUtils.AddSlashSuffix(Path.Combine(VersionFolder, TargetFolder)) + File.FileName,
                        new FileChecker { Hash = File.Hash, ActualSize = File.Size },
                        SimulateBrowserHeaders: true));
                    Task.Progress += 1d / (1 + ModList.Count);
                }
                Task.Output = FileList.Values.ToList();
            }) { ProgressWeight = ModList.Count / 200d, Show = false }); // 每 200 Mod 需要 1s
            // 下载 Mod 文件
            ModDownloadLoaders.Add(new LoaderDownload("下载 Mod", new List<NetFile>()) { ProgressWeight = ModList.Count * 1.5 }); // 每个 Mod 需要 1.5s
            // 构造加载器
            InstallLoaders.Add(new LoaderCombo<int>("下载 Mod（主加载器）", ModDownloadLoaders) {
                Show = false, ProgressWeight = ModDownloadLoaders.Sum(l => l.ProgressWeight)
            });
        }

        // 构造加载器
        McComponents Target = new McComponents(TargetVersion.VanillaName, TargetVersion.ModLoaderVersions);
        List<LoaderBase> MergeLoaders = PclUiBridge.McInstallLoadersGet(Target, InstanceName, VersionFolder);
        // 构造总加载器
        List<LoaderBase> Loaders = new List<LoaderBase>();
        Loaders.Add(new LoaderCombo<string>("整合包安装", InstallLoaders) { Show = false, Block = false, ProgressWeight = InstallLoaders.Sum(l => l.ProgressWeight) });
        Loaders.Add(new LoaderCombo<string>("游戏安装", MergeLoaders) { Show = false, ProgressWeight = MergeLoaders.Sum(l => l.ProgressWeight) });
        Loaders.Add(new LoaderTask<string, string>("最终整理文件",
        Task => {
            // 设置图标
            if (Logo is not null && FileUtils.Exists(Logo)) {
                FileUtils.Copy(Logo, VersionFolder + "PCL\\Logo.png");
                PclCore.WriteIni(VersionFolder + "PCL\\Setup.ini", "Logo", "PCL\\Logo.png");
                PclCore.WriteIni(VersionFolder + "PCL\\Setup.ini", "LogoCustom", "True");
                Logger.Info($"已设置整合包 Logo：{Logo}");
            }
            // 解压地图文件
            string SaveFolder = $"{VersionFolder}saves\\";
            foreach (string MapFile in DirectoryUtils.EnumerateFiles(SaveFolder, searchPattern: "*.zip")) {
                FileUtils.ExtractToDirectory(MapFile, SaveFolder, Task.CreateCancellationToken());
                FileUtils.Delete(MapFile);
            }
            // 删除原始整合包文件
            foreach (string FileToDelete in new[] { VersionFolder + "原始整合包.zip", VersionFolder + "原始整合包.mrpack" }) {
                if (FileUtils.Exists(FileToDelete)) {
                    Logger.Info($"删除原始整合包文件：{FileToDelete}");
                    FileUtils.Delete(FileToDelete);
                }
            }
            if (FileUtils.Exists(FileAddress) && PathUtils.GetFileNameWithoutExtension(FileAddress) == "modpack") {
                Logger.Info($"删除安装整合包文件：{FileAddress}");
                FileUtils.Delete(FileAddress);
            }
        }) { ProgressWeight = 0.1, Show = false });

        // 重复任务检查
        string LoaderName = "CurseForge 整合包安装：" + InstanceName + " ";
        if (PclUiBridge.LoaderTaskbar().Any(l => l.Name == LoaderName)) {
            PclUiBridge.ShowHint?.Invoke("该整合包正在安装中！", HintType.Red);
            throw new OperationCanceledException();
        }

        // 启动
        LoaderCombo<string> Loader = new LoaderCombo<string>(LoaderName, Loaders) { OnStateChanged = PclUiBridge.McInstallState };
        Loader.Start(VersionFolder);
        PclUiBridge.LoaderTaskbarAdd(Loader);
        PclUiBridge.DownloadExtraRefresh?.Invoke();
        return Loader;
    }

    // Modrinth
    private static LoaderCombo<string> InstallPackModrinth(string FileAddress, ZipArchive Archive, string ArchiveBaseFolder, string InstanceName = null, string Logo = null) {

        // 读取 Json 文件
        JObject Json;
        try {
            Json = (JObject)Archive.GetEntry(ArchiveBaseFolder + "modrinth.index.json").Open().ReadString().DeserializeJson();
        } catch (Exception ex) {
            throw new Exception("Modrinth 整合包安装信息存在问题", ex);
        }
        if (Json["dependencies"] is null || Json["dependencies"]["minecraft"] is null) throw new Exception("Modrinth 整合包未提供 Minecraft 版本信息");
        // 获取 Mod API 版本信息
        // TODO: 改为使用类似 InstallPackMMC 中，一个版本名一个字典的方式来存储它，不要用 Unknown
        McVersion TargetVersion = new McVersion("Unknown");
        foreach (JProperty Entry in Json["dependencies"] ?? new JArray()) {
            switch (Entry.Name.Lower()) {
                case "minecraft":
                    TargetVersion.VanillaName = Entry.Value.ToString();
                    break;
                case "forge": // eg. 14.23.5.2859 / 1.19-41.1.0
                    TargetVersion.ModLoaderVersions[ModLoaders.Forge] = Entry.Value.ToString();
                    Logger.Info($"整合包 Forge 版本：{TargetVersion.ModLoaderVersions[ModLoaders.Forge]}");
                    break;
                case "neoforge":
                case "neo-forge": // eg. 20.6.98-beta
                    TargetVersion.ModLoaderVersions[ModLoaders.NeoForge] = Entry.Value.ToString();
                    Logger.Info($"整合包 NeoForge 版本：{TargetVersion.ModLoaderVersions[ModLoaders.NeoForge]}");
                    break;
                case "fabric-loader": // eg. 0.14.14
                    TargetVersion.ModLoaderVersions[ModLoaders.Fabric] = Entry.Value.ToString();
                    Logger.Info($"整合包 Fabric 版本：{TargetVersion.ModLoaderVersions[ModLoaders.Fabric]}");
                    break;
                default:
                    NotifyIncompatibleLoader(Entry.Name);
                    break;
            }
        }
        // 获取版本名
        if (InstanceName is null) {
            InstanceName = Json["name"]?.ToString() ?? "";
            ValidateFolderName Validate = new ValidateFolderName(PclUiBridge.McFolderSelectedGet() + "versions");
            if (Validate.Validate(InstanceName) != "") InstanceName = "";
            if (InstanceName == "") InstanceName = PclUiBridge.ModpackInput?.Invoke("输入版本名称", "", "", new ValidateBase[] { Validate }, "");
            if (string.IsNullOrEmpty(InstanceName)) throw new OperationCanceledException();
        }
        string VersionFolder = Path.Combine(PclUiBridge.McFolderSelectedGet(), "versions", InstanceName) + "\\";
        // 解压
        string InstallTemp = PclUiBridge.RequestTaskTempFolder();
        List<LoaderBase> InstallLoaders = new List<LoaderBase>();
        InstallLoaders.Add(new LoaderTask<string, int>("解压整合包文件",
        Task => {
            ExtractModpackFiles(InstallTemp, FileAddress, Task.CreateCancellationToken(), Task.CreateSyncProgressProvider(ToPercentage: 0.5));
            CopyOverrideDirectory(InstallTemp + ArchiveBaseFolder + "overrides", VersionFolder, Task);
            CopyOverrideDirectory(InstallTemp + ArchiveBaseFolder + "client-overrides", VersionFolder, Task);
        }) { ProgressWeight = FileUtils.GetInfo(FileAddress).Length / 1024d / 1024d / 6, Block = false }); // 每 6M 需要 1s
        // 获取下载文件列表
        List<NetFile> FileList = new List<NetFile>();
        foreach (JToken File in Json["files"] ?? new JArray()) {
            // 检查是否需要该文件
            if (File["env"] is not null) {
                switch (File["env"]["client"].ToString()) {
                    case "optional":
                        if (PclUiBridge.ShowModpackMsgBox?.Invoke("是否要下载可选文件 " + PathUtils.GetLastPart(File["path"].ToString()) + "？",
                                    "下载可选文件", "是", "否", null, false, false) == 2) {
                            continue;
                        }
                        break;
                    case "unsupported":
                        continue;
                }
            }
            // 添加下载文件
            List<string> Urls = File["downloads"].SelectMany(t => ResourceVersion.ParseCurseForgeDownloadUrls(t.ToString())).ToList();
            Urls.AddRange(Urls.Select(u => PclNet.DlSourceModGet(u)).ToList());
            Urls = ResourceVersion.ParseModrinthTrackArguments(Urls,
                ResourceVersion.DownloadReason.ModPack, TargetVersion.VanillaName, TargetVersion.ModLoaders).ToList(); // Modrinth 来源追踪信息
            Urls = Urls.Distinct().ToList();
            string TargetPath = Path.Combine(VersionFolder, File["path"].ToString().TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (!PathUtils.IsParentOf(VersionFolder, TargetPath)) {
                PclUiBridge.ShowModpackMsgBox?.Invoke($"整合包的文件路径超出了版本文件夹，请向整合包作者反馈此问题！{Environment.NewLine}目标：{Path.GetFullPath(TargetPath)}{Environment.NewLine}错误的文件：{TargetPath}", "文件路径校验失败", null, null, null, true, false);
                throw new OperationCanceledException();
            }
            FileList.Add(new NetFile(Urls, TargetPath,
                new FileChecker { Hash = File["hashes"]["sha1"].ToString() }, true)); // Modrinth 整合包不应校验文件大小（他们官方自己说的）
        }
        if (FileList.Any()) {
            InstallLoaders.Add(new LoaderDownload("下载额外文件", FileList) { ProgressWeight = FileList.Count * 1.5 }); // 每个 Mod 需要 1.5s
        }

        // 构造加载器
        McComponents Target = new McComponents(TargetVersion.VanillaName, TargetVersion.ModLoaderVersions);
        List<LoaderBase> MergeLoaders = PclUiBridge.McInstallLoadersGet(Target, InstanceName, VersionFolder);
        // 构造总加载器
        List<LoaderBase> Loaders = new List<LoaderBase>();
        Loaders.Add(new LoaderCombo<string>("整合包安装", InstallLoaders) { Show = false, Block = false, ProgressWeight = InstallLoaders.Sum(l => l.ProgressWeight) });
        Loaders.Add(new LoaderCombo<string>("游戏安装", MergeLoaders) { Show = false, ProgressWeight = MergeLoaders.Sum(l => l.ProgressWeight) });
        Loaders.Add(new LoaderTask<string, string>("最终整理文件",
        Task => {
            // 设置图标
            if (Logo is not null && FileUtils.Exists(Logo)) {
                FileUtils.Copy(Logo, VersionFolder + "PCL\\Logo.png");
                PclCore.WriteIni(VersionFolder + "PCL\\Setup.ini", "Logo", "PCL\\Logo.png");
                PclCore.WriteIni(VersionFolder + "PCL\\Setup.ini", "LogoCustom", "True");
                Logger.Info($"已设置整合包 Logo：{Logo}");
            }
            // 删除原始整合包文件
            foreach (string FileToDelete in new[] { VersionFolder + "原始整合包.zip", VersionFolder + "原始整合包.mrpack" }) {
                if (FileUtils.Exists(FileToDelete)) {
                    Logger.Info($"删除原始整合包文件：{FileToDelete}");
                    FileUtils.Delete(FileToDelete);
                }
            }
            if (FileUtils.Exists(FileAddress) && PathUtils.GetFileNameWithoutExtension(FileAddress) == "modpack") {
                Logger.Info($"删除安装整合包文件：{FileAddress}");
                FileUtils.Delete(FileAddress);
            }
        }) { ProgressWeight = 0.1, Show = false });

        // 重复任务检查
        string LoaderName = $"Modrinth 整合包安装：{InstanceName} ";
        if (PclUiBridge.LoaderTaskbar().Any(l => l.Name == LoaderName)) {
            PclUiBridge.ShowHint?.Invoke("该整合包正在安装中！", HintType.Red);
            throw new OperationCanceledException();
        }

        // 启动
        LoaderCombo<string> Loader = new LoaderCombo<string>(LoaderName, Loaders) { OnStateChanged = PclUiBridge.McInstallState };
        Loader.Start(VersionFolder);
        PclUiBridge.LoaderTaskbarAdd(Loader);
        PclUiBridge.DownloadExtraRefresh?.Invoke();
        return Loader;
    }

    // HMCL
    private static LoaderCombo<string> InstallPackHMCL(string FileAddress, ZipArchive Archive, string ArchiveBaseFolder) {
        // 读取 Json 文件
        JObject Json;
        try {
            Json = (JObject)Archive.GetEntry(ArchiveBaseFolder + "modpack.json").Open().ReadString(Encoding.UTF8).DeserializeJson();
        } catch (Exception ex) {
            throw new Exception("HMCL 整合包安装信息存在问题", ex);
        }
        // 获取版本名
        string InstanceName = Json["name"]?.ToString() ?? "";
        ValidateFolderName Validate = new ValidateFolderName(PclUiBridge.McFolderSelectedGet() + "versions");
        if (Validate.Validate(InstanceName) != "") InstanceName = "";
        if (InstanceName == "") InstanceName = PclUiBridge.ModpackInput?.Invoke("输入版本名称", "", "", new ValidateBase[] { Validate }, "");
        if (string.IsNullOrEmpty(InstanceName)) throw new OperationCanceledException();
        string VersionFolder = Path.Combine(PclUiBridge.McFolderSelectedGet(), "versions", InstanceName) + "\\";
        // 解压
        string InstallTemp = PclUiBridge.RequestTaskTempFolder();
        List<LoaderBase> InstallLoaders = new List<LoaderBase>();
        InstallLoaders.Add(new LoaderTask<string, int>("解压整合包文件",
        Task => {
            ExtractModpackFiles(InstallTemp, FileAddress, Task.CreateCancellationToken(), Task.CreateSyncProgressProvider(ToPercentage: 0.6));
            CopyOverrideDirectory(InstallTemp + ArchiveBaseFolder + "minecraft", VersionFolder, Task);
        }) { ProgressWeight = FileUtils.GetInfo(FileAddress).Length / 1024d / 1024d / 6, Block = false }); // 每 6M 需要 1s
        // 构造游戏本体安装加载器
        if (Json["gameVersion"] is null) throw new Exception("该 HMCL 整合包未提供游戏版本信息，无法安装！");
        McComponents Target = new McComponents(Json["gameVersion"].ToString(), new Dictionary<ModLoaders, string>());
        List<LoaderBase> MergeLoaders = PclUiBridge.McInstallLoadersGet(Target, InstanceName, VersionFolder);
        // 构造总加载器
        List<LoaderBase> Loaders = new List<LoaderBase> {
            new LoaderCombo<string>("整合包安装", InstallLoaders) { Show = false, Block = false, ProgressWeight = InstallLoaders.Sum(l => l.ProgressWeight) },
            new LoaderCombo<string>("游戏安装", MergeLoaders) { Show = false, ProgressWeight = MergeLoaders.Sum(l => l.ProgressWeight) }
        };
        // 重复任务检查
        string LoaderName = "HMCL 整合包安装：" + InstanceName + " ";
        if (PclUiBridge.LoaderTaskbar().Any(l => l.Name == LoaderName)) {
            PclUiBridge.ShowHint?.Invoke("该整合包正在安装中！", HintType.Red);
            throw new OperationCanceledException();
        }
        // 启动
        LoaderCombo<string> Loader = new LoaderCombo<string>(LoaderName, Loaders) { OnStateChanged = PclUiBridge.McInstallState };
        Loader.Start(VersionFolder);
        PclUiBridge.LoaderTaskbarAdd(Loader);
        PclUiBridge.DownloadExtraRefresh?.Invoke();
        return Loader;
    }

    // MMC
    private static LoaderCombo<string> InstallPackMMC(string FileAddress, ZipArchive Archive, string ArchiveBaseFolder) {
        // 读取 Json 文件
        JObject PackJson; string PackInstance;
        try {
            PackJson = (JObject)Archive.GetEntry(ArchiveBaseFolder + "mmc-pack.json").Open().ReadString(Encoding.UTF8).DeserializeJson();
            PackInstance = Archive.GetEntry(ArchiveBaseFolder + "instance.cfg").Open().ReadString(Encoding.UTF8);
        } catch (Exception ex) {
            throw new Exception("MMC 整合包安装信息存在问题", ex);
        }
        // 获取版本名
        string InstanceName = PackInstance.RegexSeek("(?<=\\nname\\=)[^\\n]+") ?? "";
        ValidateFolderName Validate = new ValidateFolderName(PclUiBridge.McFolderSelectedGet() + "versions");
        if (Validate.Validate(InstanceName) != "") InstanceName = "";
        if (InstanceName == "") InstanceName = PclUiBridge.ModpackInput?.Invoke("输入版本名称", "", "", new ValidateBase[] { Validate }, "");
        if (string.IsNullOrEmpty(InstanceName)) throw new OperationCanceledException();
        string VersionFolder = Path.Combine(PclUiBridge.McFolderSelectedGet(), "versions", InstanceName) + "\\";
        // 解压
        string InstallTemp = PclUiBridge.RequestTaskTempFolder();
        string SetupFile = VersionFolder + "PCL\\Setup.ini";
        List<LoaderBase> InstallLoaders = new List<LoaderBase>();
        InstallLoaders.Add(new LoaderTask<string, int>("解压整合包文件",
        Task => {
            ExtractModpackFiles(InstallTemp, FileAddress, Task.CreateCancellationToken(), Task.CreateSyncProgressProvider(ToPercentage: 0.55));
            // Prism 优先使用 minecraft/ 而非 .minecraft/（#9272）
            string MmcGameRoot;
            if (DirectoryUtils.Exists(InstallTemp + ArchiveBaseFolder + ".minecraft") && !DirectoryUtils.Exists(InstallTemp + ArchiveBaseFolder + "minecraft")) {
                MmcGameRoot = InstallTemp + ArchiveBaseFolder + ".minecraft";
            } else {
                MmcGameRoot = InstallTemp + ArchiveBaseFolder + "minecraft";
            }
            CopyOverrideDirectory(MmcGameRoot, VersionFolder, Task);
            // 读取 MMC 设置文件（#2655）
            try {
                string MmcSetupFile = InstallTemp + ArchiveBaseFolder + "instance.cfg";
                if (FileUtils.Exists(MmcSetupFile)) {
                    // 将其中的等号替换为冒号，以符合 ini 文件格式
                    List<string> Lines = new List<string>();
                    foreach (string Line in FileUtils.ReadAsLines(MmcSetupFile, true)) {
                        if (!Line.Contains("=")) continue;
                        Lines.Add(Line.BeforeFirst("=") + ":" + Line.AfterFirst("="));
                    }
                    FileUtils.Write(MmcSetupFile, Lines.Join(Environment.NewLine));
                    // 读取文件
                    if (Convert.ToBoolean(PclCore.ReadIni(MmcSetupFile, "OverrideCommands", "False"))) {
                        string PreLaunchCommand = PclCore.ReadIni(MmcSetupFile, "PreLaunchCommand");
                        if (PreLaunchCommand != "") {
                            PreLaunchCommand = PreLaunchCommand.Replace("\\\"", "\"").
                                Replace("$INST_JAVA", "{java}java.exe").
                                Replace("$INST_MC_DIR\\", "{minecraft}").Replace("$INST_MC_DIR", "{minecraft}").
                                Replace("$INST_DIR\\", "{verpath}").Replace("$INST_DIR", "{verpath}").
                                Replace("$INST_ID", "{name}").Replace("$INST_NAME", "{name}");
                            PclCore.WriteIni(SetupFile, "VersionAdvanceRun", PreLaunchCommand);
                            Logger.Info($"迁移 MultiMC 版本独立设置：启动前执行命令：{PreLaunchCommand}");
                        }
                    }
                    if (Convert.ToBoolean(PclCore.ReadIni(MmcSetupFile, "JoinServerOnLaunch", "False"))) {
                        string ServerAddress = PclCore.ReadIni(MmcSetupFile, "JoinServerOnLaunchAddress").Replace("\\\"", "\"");
                        PclCore.WriteIni(SetupFile, "VersionServerEnter", ServerAddress);
                        Logger.Info($"迁移 MultiMC 版本独立设置：自动进入服务器：{ServerAddress}");
                    }
                    string Logo = PclCore.ReadIni(MmcSetupFile, "iconKey", "");
                    if (Logo != "" && FileUtils.Exists($"{InstallTemp}{ArchiveBaseFolder}{Logo}.png")) {
                        PclCore.WriteIni(SetupFile, "LogoCustom", "True");
                        PclCore.WriteIni(SetupFile, "Logo", "PCL\\Logo.png");
                        FileUtils.Copy($"{InstallTemp}{ArchiveBaseFolder}{Logo}.png", VersionFolder + "PCL\\Logo.png");
                        Logger.Info($"迁移 MultiMC 版本独立设置：版本图标（{Logo}.png）");
                    }
                    // JVM 参数
                    string JvmArgs = PclCore.ReadIni(MmcSetupFile, "JvmArgs", "");
                    if (JvmArgs != "") {
                        if (Convert.ToBoolean(PclCore.ReadIni(MmcSetupFile, "OverrideJavaArgs", "False"))) {
                            PclCore.WriteIni(SetupFile, "VersionAdvanceJvm", JvmArgs);
                            Logger.Info($"迁移 MultiMC 版本独立设置：JVM 参数（覆盖）：{JvmArgs}");
                        } else {
                            JvmArgs += " " + PclCore.GetSetting<string>("LaunchAdvanceJvm");
                            PclCore.WriteIni(SetupFile, "VersionAdvanceJvm", JvmArgs);
                            Logger.Info($"迁移 MultiMC 版本独立设置：JVM 参数（追加）：{JvmArgs}");
                        }
                    }
                }
            } catch (Exception ex) {
                Logger.Warn(ex, $"读取 MMC 配置文件失败（{InstallTemp}{ArchiveBaseFolder}instance.cfg）");
            }
        }) { ProgressWeight = FileUtils.GetInfo(FileAddress).Length / 1024d / 1024d / 6, Block = false }); // 每 6M 需要 1s
        // 构造版本安装请求
        if (PackJson["components"] is null) throw new Exception("该 MMC 整合包未提供游戏版本信息，无法安装！");
        string MinecraftVersion = null;
        Dictionary<ModLoaders, string> ModLoaderVersions = new Dictionary<ModLoaders, string>();
        foreach (JObject Component in PackJson["components"]) {
            if (!Component.ContainsKey("uid")) continue;
            string UID = Component["uid"].ToString();
            switch (UID) {
                case "net.minecraft":
                    MinecraftVersion = Component["version"].ToString();
                    break;
                case "net.minecraftforge":
                    ModLoaderVersions[ModLoaders.Forge] = Component["version"].ToString();
                    break;
                case "net.neoforged":
                    ModLoaderVersions[ModLoaders.NeoForge] = Component["version"].ToString();
                    break;
                case "net.fabricmc.fabric-loader":
                    ModLoaderVersions[ModLoaders.Fabric] = Component["version"].ToString();
                    break;
                default:
                    if (UID.StartsWithF("org.lwjgl")) { // #8210
                        Logger.Info($"已跳过 LWJGL 项：{UID}");
                    } else {
                        NotifyIncompatibleLoader(UID);
                    }
                    break;
            }
        }
        // 构造加载器
        McComponents Target = new McComponents(MinecraftVersion, ModLoaderVersions);
        List<LoaderBase> MergeLoaders = PclUiBridge.McInstallLoadersGet(Target, InstanceName, VersionFolder);
        // 构造总加载器
        List<LoaderBase> Loaders = new List<LoaderBase>();
        Loaders.Add(new LoaderCombo<string>("整合包安装", InstallLoaders) { Show = false, Block = false, ProgressWeight = InstallLoaders.Sum(l => l.ProgressWeight) });
        Loaders.Add(new LoaderCombo<string>("游戏安装", MergeLoaders) { Show = false, ProgressWeight = MergeLoaders.Sum(l => l.ProgressWeight) });

        // 重复任务检查
        string LoaderName = "MMC 整合包安装：" + InstanceName + " ";
        if (PclUiBridge.LoaderTaskbar().Any(l => l.Name == LoaderName)) {
            PclUiBridge.ShowHint?.Invoke("该整合包正在安装中！", HintType.Red);
            throw new OperationCanceledException();
        }

        // 启动
        LoaderCombo<string> Loader = new LoaderCombo<string>(LoaderName, Loaders) { OnStateChanged = PclUiBridge.McInstallState };
        Loader.Start(VersionFolder);
        PclUiBridge.LoaderTaskbarAdd(Loader);
        PclUiBridge.DownloadExtraRefresh?.Invoke();
        return Loader;
    }

    // MCBBS
    private static LoaderCombo<string> InstallPackMCBBS(string FileAddress, ZipArchive Archive, string ArchiveBaseFolder,
            string InstanceName = null) {
        // 读取 Json 文件
        JObject Json;
        try {
            ZipArchiveEntry Entry = Archive.GetEntry(ArchiveBaseFolder + "mcbbs.packmeta") ?? Archive.GetEntry(ArchiveBaseFolder + "manifest.json");
            Json = (JObject)Entry.Open().ReadString(Encoding.UTF8).DeserializeJson();
        } catch (Exception ex) {
            throw new Exception("MCBBS 整合包安装信息存在问题", ex);
        }
        // 获取版本名
        if (InstanceName is null) {
            InstanceName = Json["name"]?.ToString() ?? "";
            ValidateFolderName Validate = new ValidateFolderName(PclUiBridge.McFolderSelectedGet() + "versions");
            if (Validate.Validate(InstanceName) != "") InstanceName = "";
            if (InstanceName == "") InstanceName = PclUiBridge.ModpackInput?.Invoke("输入版本名称", "", "", new ValidateBase[] { Validate }, "");
            if (string.IsNullOrEmpty(InstanceName)) throw new OperationCanceledException();
        }
        string VersionFolder = Path.Combine(PclUiBridge.McFolderSelectedGet(), "versions", InstanceName) + "\\";
        // 解压
        string InstallTemp = PclUiBridge.RequestTaskTempFolder();
        string SetupFile = VersionFolder + "PCL\\Setup.ini";
        List<LoaderBase> InstallLoaders = new List<LoaderBase>();
        InstallLoaders.Add(new LoaderTask<string, int>("解压整合包文件",
        Task => {
            ExtractModpackFiles(InstallTemp, FileAddress, Task.CreateCancellationToken(), Task.CreateSyncProgressProvider(ToPercentage: 0.6));
            CopyOverrideDirectory(InstallTemp + ArchiveBaseFolder + "overrides", VersionFolder, Task);
            // JVM 参数
            if (Json["launchInfo"] is not null) {
                JObject LaunchInfo = (JObject)Json["launchInfo"];
                if (LaunchInfo.ContainsKey("javaArgument")) PclCore.WriteIni(SetupFile, "VersionAdvanceJvm", string.Join(" ", LaunchInfo["javaArgument"]));
                if (LaunchInfo.ContainsKey("launchArgument")) PclCore.WriteIni(SetupFile, "VersionAdvanceGame", string.Join(" ", LaunchInfo["launchArgument"]));
            }
        }) { ProgressWeight = FileUtils.GetInfo(FileAddress).Length / 1024d / 1024d / 6, Block = false }); // 每 6M 需要 1s
        // 构造加载器
        if (Json["addons"] is null) throw new Exception("该 MCBBS 整合包未提供游戏版本附加信息，无法安装！");
        Dictionary<string, string> Addons = new Dictionary<string, string>();
        foreach (JToken Entry in Json["addons"]) {
            Addons.Add(Entry["id"].ToString(), Entry["version"].ToString());
        }
        if (!Addons.ContainsKey("game")) throw new Exception("该 MCBBS 整合包未提供游戏版本信息，无法安装！");
        if (Addons.ContainsKey("quilt")) {
            PclUiBridge.ShowHint?.Invoke("PCL 不支持安装 Quilt 整合包！", HintType.Red);
            throw new OperationCanceledException();
        }
        Dictionary<ModLoaders, string> ModLoaderVersions = new Dictionary<ModLoaders, string>();
        if (Addons.ContainsKey("forge")) ModLoaderVersions[ModLoaders.Forge] = Addons["forge"];
        if (Addons.ContainsKey("neoforge")) ModLoaderVersions[ModLoaders.NeoForge] = Addons["neoforge"];
        if (Addons.ContainsKey("fabric")) ModLoaderVersions[ModLoaders.Fabric] = Addons["fabric"];
        JObject OptiFineEntry = null;
        if (Addons.ContainsKey("optifine")) {
            string MinecraftVersion = Addons["game"];
            if (MinecraftVersion.EndsWithF(".0")) MinecraftVersion = MinecraftVersion.Substring(0, MinecraftVersion.Length - 2);
            string OptiFineFileSuffix = "_HD_" + Addons["optifine"].AfterLast("HD_") + ".jar";
            // 从镜像源拉取 OptiFine 列表
            if (PclDownload.DlOptiFineListBmclapiLoader.State != LoadState.Finished) PclDownload.DlOptiFineListBmclapiLoader.WaitForExit();
            DlOptiFineListEntry Entry = PclDownload.DlOptiFineListBmclapiLoader.Output.Value.FirstOrDefault(
                v => v.Inherit == MinecraftVersion && v.FileName.EndsWithF(OptiFineFileSuffix));
            if (Entry is null) throw new Exception($"OptiFine 列表中没有整合包所需的 OptiFine {Addons["optifine"]}（Minecraft {Addons["game"]}）");
            OptiFineEntry = JObject.FromObject(Entry);
        }
        McComponents Target = new McComponents(Addons["game"], ModLoaderVersions, OptiFineEntry);
        List<LoaderBase> MergeLoaders = PclUiBridge.McInstallLoadersGet(Target, InstanceName, VersionFolder);
        // 构造总加载器
        List<LoaderBase> Loaders = new List<LoaderBase>();
        Loaders.Add(new LoaderCombo<string>("整合包安装", InstallLoaders) { Show = false, Block = false, ProgressWeight = InstallLoaders.Sum(l => l.ProgressWeight) });
        Loaders.Add(new LoaderCombo<string>("游戏安装", MergeLoaders) { Show = false, ProgressWeight = MergeLoaders.Sum(l => l.ProgressWeight) });

        // 重复任务检查
        string LoaderName = "MCBBS 整合包安装：" + InstanceName + " ";
        if (PclUiBridge.LoaderTaskbar().Any(l => l.Name == LoaderName)) {
            PclUiBridge.ShowHint?.Invoke("该整合包正在安装中！", HintType.Red);
            throw new OperationCanceledException();
        }

        // 启动
        LoaderCombo<string> Loader = new LoaderCombo<string>(LoaderName, Loaders) { OnStateChanged = PclUiBridge.McInstallState };
        // If Archive IsNot Nothing Then Archive.Dispose() '解除占用，以免在加载器中触发 “正由另一进程使用，因此该进程无法访问此文件”
        Loader.Start(VersionFolder);
        PclUiBridge.LoaderTaskbarAdd(Loader);
        PclUiBridge.DownloadExtraRefresh?.Invoke();
        return Loader;
    }

    // 带启动器的压缩包
    private static LoaderCombo<string> InstallPackLauncherPack(string FileAddress, ZipArchive Archive, string ArchiveBaseFolder) {
        // 获取解压路径
        PclUiBridge.ShowModpackMsgBox?.Invoke("接下来请选择一个空文件夹，它会被安装到这个文件夹里。", "安装", "继续", null, null, false, true);
        string TargetFolder = Dialogs.SelectFolder("选择安装目标（必须是一个空文件夹）", false).FirstOrDefault();
        if (TargetFolder is null) throw new OperationCanceledException();
        if (!DirectoryUtils.IsEmpty(TargetFolder)) { PclUiBridge.ShowHint?.Invoke("请选择一个空文件夹作为安装目标！", HintType.Red); throw new OperationCanceledException(); }
        // 解压
        LoaderCombo<string> Loader = new LoaderCombo<string>("解压压缩包", new List<LoaderBase> {
            new LoaderTask<string, int>("解压压缩包",
            Task => {
                ExtractModpackFiles(TargetFolder, FileAddress, Task.CreateCancellationToken(), Task.CreateSyncProgressProvider(ToPercentage: 0.9));
                PclCore.OpenExplorer(TargetFolder);
                Thread.Sleep(400); // 避免文件争用
                // 查找解压后的 exe 文件
                string Launcher = null;
                foreach (string ExeFile in DirectoryUtils.EnumerateFiles(TargetFolder, searchPattern: "*.exe")) {
                    FileVersionInfo Info = FileVersionInfo.GetVersionInfo(ExeFile);
                    Logger.Info($"文件 {ExeFile} 的产品名标识为 {Info.ProductName}");
                    if (Info.ProductName == "Plain Craft Launcher") {
                        Launcher = ExeFile;
                        Logger.Info($"发现整合包附带的 PCL 启动器：{ExeFile}");
                    } else if ((Info.ProductName.ContainsIgnoreCase("Launcher") || Info.ProductName.Contains("启动")) &&
                        !(Info.ProductName == "Plain Craft Launcher Admin Manager")) {
                        if (Launcher is null) {
                            Launcher = ExeFile;
                            Logger.Info($"发现整合包附带的疑似第三方启动器：{ExeFile}");
                        }
                    }
                }
                Task.Progress = 0.95;
                // 尝试使用附带的启动器打开
                if (Launcher is not null) {
                    Logger.Info($"找到压缩包中附带的启动器：{Launcher}");
                    if (PclUiBridge.ShowModpackMsgBox?.Invoke($"整合包里似乎自带了启动器，是否换用它继续安装？{Environment.NewLine}即将打开：{Launcher}", "换用整合包启动器？", "换用", "不换用", null, false, false) == 1) {
                        PclCore.StartProcess(Launcher, "--wait"); // 要求等待已有的 PCL 退出
                        Logger.Info("为换用整合包中的启动器启动，强制结束程序");
                        PclUiBridge.EndProgram?.Invoke(false);
                        return;
                    }
                } else {
                    Logger.Info("未找到压缩包中附带的启动器");
                }
                // 加入文件夹列表
                string InstanceName = PathUtils.GetLastPart(TargetFolder);
                DirectoryUtils.Create(TargetFolder + ".minecraft\\");
                PclUiBridge.PageSelectLeftAddFolder?.Invoke(
                    TargetFolder + ".minecraft\\" + ArchiveBaseFolder.Replace("/", "\\").TrimStart('\\'), // 格式例如：包裹文件夹\.minecraft\（最短为空字符串）
                    InstanceName, false);
                // 调用 modpack 文件进行安装
                string ModpackFile = DirectoryUtils.EnumerateFiles(TargetFolder, true, "modpack.*").First();
                Logger.Info($"调用 modpack 文件继续安装：{ModpackFile}");
                ModpackInstall(ModpackFile);
            })
        });
        Loader.Start(TargetFolder);
        PclUiBridge.LoaderTaskbarAdd(Loader);
        PclUiBridge.DownloadExtraRefresh?.Invoke();
        PclUiBridge.DownloadExtraRibble?.Invoke();
        return Loader;
    }

    // 普通压缩包
    private static LoaderCombo<string> InstallPackCompress(string FileAddress, ZipArchive Archive) {
        // 尝试定位 .minecraft 文件夹：寻找形如 “/versions/XXX/XXX.json” 的路径
        Match Match = null;
        Regex Regex = new Regex(@"^.*\/(?=versions\/(?<ver>[^\/]+)\/(\k<ver>)\.json$)", RegexOptions.IgnoreCase);
        foreach (ZipArchiveEntry Entry in Archive.Entries) {
            Match EntryMatch = Regex.Match("/" + Entry.FullName);
            if (EntryMatch.Success) {
                Match = EntryMatch;
                break;
            }
        }
        if (Match is null) throw new Exception("文件结构不匹配，这可能不是 Minecraft 客户端压缩包？"); // 没有匹配
        string ArchiveBaseFolder = Match.Value.Replace("/", "\\").TrimStart('\\'); // 格式例如：包裹文件夹\.minecraft\（最短为空字符串）
        Logger.Info($"检测到压缩包的 .minecraft 根目录：{ArchiveBaseFolder}，命中的版本名：{Match.Groups[1].Value}");
        // 获取解压路径
        PclUiBridge.ShowModpackMsgBox?.Invoke("接下来请选择一个空文件夹，它会被安装到这个文件夹里。", "安装", "继续", null, null, false, true);
        string TargetFolder = Dialogs.SelectFolder("选择安装目标（必须是一个空文件夹）", false).FirstOrDefault();
        if (TargetFolder is null) throw new OperationCanceledException();
        if (TargetFolder.Contains("!") || TargetFolder.Contains(";")) { PclUiBridge.ShowHint?.Invoke("Minecraft 文件夹路径中不能含有感叹号或分号！", HintType.Red); throw new OperationCanceledException(); }
        if (!DirectoryUtils.IsEmpty(TargetFolder)) { PclUiBridge.ShowHint?.Invoke("请选择一个空文件夹作为安装目标！", HintType.Red); throw new OperationCanceledException(); }
        // 解压
        LoaderCombo<string> Loader = new LoaderCombo<string>("解压压缩包", new List<LoaderBase> {
            new LoaderTask<string, int>("解压压缩包",
            Task => {
                ExtractModpackFiles(TargetFolder, FileAddress, Task.CreateCancellationToken(), Task.CreateSyncProgressProvider(ToPercentage: 0.95));
                // 加入文件夹列表
                PclUiBridge.PageSelectLeftAddFolder?.Invoke(TargetFolder + ArchiveBaseFolder, PathUtils.GetLastPart(TargetFolder), false);
                Thread.Sleep(400); // 避免文件争用
                PclCore.RunInUi(() => PclUiBridge.PageChangeInstanceSelect?.Invoke());
            })
        }) { OnStateChanged = PclUiBridge.McInstallState };
        Loader.Start(TargetFolder);
        PclUiBridge.LoaderTaskbarAdd(Loader);
        PclUiBridge.DownloadExtraRefresh?.Invoke();
        PclUiBridge.DownloadExtraRibble?.Invoke();
        return Loader;
    }

#endregion

}
