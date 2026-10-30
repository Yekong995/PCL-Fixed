// 由 VB Modules\Minecraft\ModMinecraft.vb 的 McInstance / McInstanceLock / McInstanceCardType 迁移而来。
// McFolderSelected、GetMcFoolName 经 PclUiBridge 桥接；设置读写经 PclCore.SettingProvider/SettingSetter/HasSavedProvider 桥接；
// 版本独立设置缓存清理经 PclUiBridge.ResetInstanceSettingsCache 桥接。
// 依赖 VB UI 类型（MyListItem、MyVirtualizingElement、MyColor）的成员 ToListItem 保留在 VB ModMinecraft 中，以扩展方法形式提供，调用点无需改动。
// McInstanceLock.IsAnyLocked 原为 VB Friend（internal），因 PCLCS 与 VB 主程序分属不同程序集，提升为 public。
#nullable disable

using System;
using System.Collections.Concurrent;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using MeloongCore;
using MeloongCore.Extensions;
using Newtonsoft.Json.Linq;

namespace PCLCS.Modules;

public class McInstance {

    public enum McInstanceState {
        Error,
        Original,
        Snapshot,
        Fool,
        OptiFine,
        Old,
        Forge,
        NeoForge,
        LiteLoader,
        Fabric
    }

    /// <summary>
    /// 版本文件夹路径，以 \ 结尾。
    /// </summary>
    public string PathVersion { get; }

    /// <summary>
    /// 应用版本隔离后，该版本所对应的 Minecraft 根文件夹，以 \ 结尾。
    /// </summary>
    public string PathIndie {
        get {
            if (!PclCore.HasSavedSetting("VersionArgumentIndieV2", this)) {
                if (!IsLoaded) Load();
                // 决定该版本是否应该被隔离
                bool ShouldBeIndie() {
                    // 从老的版本独立设置中迁移：-1 未决定，0 使用全局设置，1 手动开启，2 手动关闭
                    if (PclCore.HasSavedSetting("VersionArgumentIndie", this) && PclCore.GetSetting<int>("VersionArgumentIndie", this) > 0) {
                        Logger.Info($"版本隔离初始化（{Name}）：从老的版本独立设置中迁移");
                        return PclCore.GetSetting<int>("VersionArgumentIndie", this) == 1;
                    }
                    // 若版本文件夹下包含 mods 或 saves 文件夹，则自动开启版本隔离
                    DirectoryInfo ModFolder = DirectoryUtils.GetInfo(PathVersion + "mods\\");
                    DirectoryInfo SaveFolder = DirectoryUtils.GetInfo(PathVersion + "saves\\");
                    if ((ModFolder.Exists && ModFolder.EnumerateFiles().Any()) || (SaveFolder.Exists && SaveFolder.EnumerateDirectories().Any())) {
                        Logger.Info($"版本隔离初始化（{Name}）：版本文件夹下存在 mods 或 saves 文件夹，自动开启");
                        return true;
                    }
                    // 根据全局的默认设置决定是否隔离
                    bool IsRelease = State != McInstanceState.Fool && State != McInstanceState.Old && State != McInstanceState.Snapshot;
                    Logger.Info($"版本隔离初始化（{Name}）：从全局默认设置中（{PclCore.GetSetting<int>("LaunchArgumentIndieV2")}）判断，State {State}，IsRelease {IsRelease}，Modable {Modable}");
                    switch (PclCore.GetSetting<int>("LaunchArgumentIndieV2")) {
                        case 0: // 关闭
                            return false;
                        case 1: // 仅隔离可安装 Mod 的版本
                            return Modable;
                        case 2: // 仅隔离非正式版
                            return !IsRelease;
                        case 3: // 隔离非正式版与可安装 Mod 的版本
                            return !IsRelease || Modable;
                        default: // 隔离所有版本
                            return true;
                    }
                }
                PclCore.SetSetting("VersionArgumentIndieV2", ShouldBeIndie(), this);
            }
            return PclCore.GetSetting<bool>("VersionArgumentIndieV2", this) ? PathVersion : PclUiBridge.McFolderSelectedGet();
        }
    }

    /// <summary>
    /// 该版本的版本文件夹名称。
    /// </summary>
    public string Name {
        get {
            if (_Name is null && PathVersion != "") _Name = PathUtils.GetLastPart(PathVersion);
            return _Name;
        }
    }
    private string _Name = null;

    /// <summary>
    /// 显示的描述文本。
    /// </summary>
    public string Info = "该版本未被加载，请向作者反馈此问题";

    /// <summary>
    /// 该版本的列表检查原始结果，不受自定义影响。
    /// </summary>
    public McInstanceState State = McInstanceState.Error;
    /// <summary>
    /// 显示的版本图标。
    /// </summary>
    public string Logo;
    /// <summary>
    /// 是否为收藏的版本。
    /// </summary>
    public bool IsStar = false;
    /// <summary>
    /// 强制版本分类，0 为未启用，1 为隐藏，2 及以上为其他普通分类。
    /// </summary>
    public McInstanceCardType DisplayType = McInstanceCardType.Auto;
    /// <summary>
    /// 该版本是否可以安装 Mod。
    /// </summary>
    public bool Modable {
        get {
            if (!IsLoaded) Load();
            return Version.ModLoaders.Flags().Any() || DisplayType == McInstanceCardType.API; // #223
        }
    }
    /// <summary>
    /// 版本信息。
    /// </summary>
    public McVersion Version {
        get {
            if (_Version is not null) return _Version;
            try {
                McVersion Result = new McVersion(Name, JsonObject, JsonVersion);
                _Version = Result;
                if (Result.VanillaName == "Unknown") Info = "PCL 无法识别该版本的 MC 版本号";
            } catch (Exception ex) {
                Logger.Warn(ex, "识别 Minecraft 版本时出错");
                _Version = new McVersion("Unknown");
                Info = "无法识别：" + ex.Message;
            }
            return _Version;
        }
        set {
            _Version = value;
        }
    }
    private McVersion _Version = null;

    /// <summary>
    /// 版本的发布时间。
    /// </summary>
    public DateTime ReleaseTime {
        get {
            return Version.ReleaseTime;
        }
    }

    /// <summary>
    /// 获取该版本的 JSON 路径。
    /// </summary>
    public string GetJsonPath() {
        string JsonPath = PathVersion + Name + ".json";
        if (!FileUtils.Exists(JsonPath)) {
            // 尝试寻找 JSON 文件
            JsonPath = null;
            foreach (string JsonCandidatePath in DirectoryUtils.EnumerateFiles(PathVersion, searchPattern: "*.json")) {
                try {
                    JObject JsonCandidate = (JObject)FileUtils.ReadAsJson(JsonCandidatePath);
                    if (!JsonCandidate.ContainsKey("mainClass")) continue;
                    if (!JsonCandidate.ContainsKey("type")) continue;
                    if (!JsonCandidate.ContainsKey("id")) continue;
                } catch {
                    continue;
                }
                JsonPath = JsonCandidatePath;
                break;
            }
            if (JsonPath is null) {
                throw new Exception($"未找到版本 JSON 文件：{PathVersion}{Name}.json");
            } else {
                Logger.Warn($"未找到同名版本 JSON，自动换用 {JsonPath}");
            }
        }
        return JsonPath;
    }
    /// <summary>
    /// 该版本的 Json 文本。会抛出异常。
    /// </summary>
    public string JsonText {
        get {
            // 快速检查 JSON 是否以 { 开头、} 结尾；忽略空白字符
            bool FastJsonCheck(string Json) {
                string TrimedJson = Json.Trim();
                return TrimedJson.StartsWithF("{") && TrimedJson.EndsWithF("}");
            }
            if (_JsonText is null) {
                string JsonPath = GetJsonPath();
                _JsonText = FileUtils.TryReadAsString(JsonPath) ?? "";
                // 如果 ReadFile 失败会返回空字符串；这可能是由于文件被临时占用，故延时后重试
                if (!FastJsonCheck(_JsonText)) {
                    if (PclCore.RunInUi()) {
                        Logger.Warn("版本 JSON 文件为空或有误，由于代码在主线程运行，将不再进行重试");
                        _JsonText.DeserializeJson(); // 触发异常
                    } else {
                        Logger.Warn($"版本 JSON 文件为空或有误，将在 2s 后重试读取（{JsonPath}）");
                        Thread.Sleep(2000);
                        _JsonText = FileUtils.ReadAsString(JsonPath);
                        if (!FastJsonCheck(_JsonText)) _JsonText.DeserializeJson(); // 触发异常
                    }
                }
            }
            return _JsonText;
        }
        set {
            _JsonText = value;
        }
    }
    private string _JsonText = null;
    /// <summary>
    /// 该版本的 Json 对象。
    /// 若 Json 存在问题，在获取该属性时即会抛出异常。
    /// </summary>
    public JObject JsonObject {
        get {
            if (_JsonObject is null) {
                string Text = JsonText; // 触发 JsonText 的 Get 事件
                try {
                    _JsonObject = (JObject)Text.DeserializeJson();
                    // 转换 HMCL 关键项
                    if (_JsonObject.ContainsKey("patches") && !_JsonObject.ContainsKey("time")) {
                        IsHmclFormatJson = true;
                        // 合并 Json
                        // Dim HasOptiFine As Boolean = False, HasForge As Boolean = False
                        JObject CurrentObject = null;
                        List<JObject> SubjsonList = new List<JObject>();
                        foreach (JObject Subjson in _JsonObject["patches"]) {
                            SubjsonList.Add(Subjson);
                        }
                        SubjsonList = SubjsonList.SortByComparison(
                            (Left, Right) => PclCore.Val(((object)Left["priority"] ?? "0").ToString()) < PclCore.Val(((object)Right["priority"] ?? "0").ToString()));
                        foreach (JObject Subjson in SubjsonList) {
                            string Id = (string)Subjson["id"];
                            if (Id is not null) {
                                // 合并 Json
                                Logger.Info($"合并 HMCL 分支项：{Id}");
                                if (CurrentObject is not null) {
                                    CurrentObject.Merge(Subjson);
                                } else {
                                    CurrentObject = Subjson;
                                }
                            } else {
                                Logger.Info("存在为空的 HMCL 分支项");
                            }
                        }
                        _JsonObject = CurrentObject;
                        // 修改附加项
                        _JsonObject["id"] = Name;
                        if (_JsonObject.ContainsKey("inheritsFrom")) _JsonObject.Remove("inheritsFrom");
                    }
                    // 与继承版本合并
                    string InstanceNameInheritFrom = null;
                    try {
                        InstanceNameInheritFrom = _JsonObject["inheritsFrom"] is null ? "" : _JsonObject["inheritsFrom"].ToString();
                        if (InstanceNameInheritFrom == Name) {
                            Logger.Warn($"自引用的继承版本：{Name}");
                            InstanceNameInheritFrom = "";
                            goto ExitTry;
                        }
Recheck:
                        if (InstanceNameInheritFrom != "") {
                            McInstance InheritInstance = new McInstance(InstanceNameInheritFrom);
                            // 继续循环
                            if (InheritInstance.InheritName == InstanceNameInheritFrom) throw new Exception("版本依赖项出现嵌套：" + InstanceNameInheritFrom);
                            InstanceNameInheritFrom = InheritInstance.InheritName;
                            // 合并 Libraries 项：子版本放在前面，父版本放在后面（5978#：如果多个 jar 包中含有相同的类，Java 8 和之前的版本按照 -cp 指定的顺序选择第一个）
                            JArray CurrentLib = (JArray)_JsonObject["libraries"].DeepClone();
                            foreach (JToken LibToken in InheritInstance.JsonObject["libraries"]) {
                                CurrentLib.Add(LibToken);
                            }
                            // 合并其他项：子版本优先于父版本
                            InheritInstance.JsonObject.Merge(_JsonObject);
                            _JsonObject = InheritInstance.JsonObject;
                            _JsonObject["libraries"] = CurrentLib;
                            goto Recheck;
                        }
ExitTry:;
                    } catch (Exception ex) {
                        Logger.Warn(ex, $"合并版本依赖项 JSON 失败（{InstanceNameInheritFrom ?? "null"}）");
                    }
                } catch (Exception ex) {
                    throw new Exception("初始化版本 JSON 时失败（" + (Name ?? "null") + "）", ex);
                }
            }
            return _JsonObject;
        }
        set {
            _JsonObject = value;
        }
    }
    private JObject _JsonObject = null;
    /// <summary>
    /// 是否为旧版 Json 格式。
    /// </summary>
    public bool IsOldJson {
        get {
            // VB 中 JToken <> "" 经 Option Strict Off 按字符串内容比较（JToken 无 == 运算符）
            return JsonObject["minecraftArguments"] is not null && (string)JsonObject["minecraftArguments"] != "";
        }
    }
    /// <summary>
    /// Json 是否为 HMCL 格式。
    /// </summary>
    public bool IsHmclFormatJson = false;

    /// <summary>
    /// 版本 jar 中的 version.json 文件对象。
    /// 若没有则返回 Nothing。
    /// </summary>
    public JObject JsonVersion {
        get {
            if (!JsonVersionInited) {
                JsonVersionInited = true;
                try {
                    if (!FileUtils.Exists($"{PathVersion}{Name}.jar")) goto ExitTry;
                    using (ZipArchive JarArchive = FileUtils.OpenZip(PathVersion + Name + ".jar")) {
                        ZipArchiveEntry VersionJson = JarArchive.GetEntry("version.json");
                        if (VersionJson is null) goto ExitTry;
                        using (StreamReader VersionJsonStream = new StreamReader(VersionJson.Open())) {
                            _JsonVersion = (JObject)VersionJsonStream.ReadToEnd().DeserializeJson();
                        }
                    }
                } catch (Exception ex) {
                    Logger.Warn(ex, $"从版本 jar 中读取 version.json 失败（{PathVersion}{Name}.jar）");
                }
            ExitTry:;
            }
            return _JsonVersion;
        }
    }
    private JObject _JsonVersion = null;
    // Static JsonVersionInited As Boolean = False（JsonVersion 的 Static 局部变量；VB Static 在所有实例间共享，故译作类静态字段）
    private static bool JsonVersionInited = false;

    /// <summary>
    /// 该版本的依赖版本。若无依赖版本则为空字符串。
    /// </summary>
    public string InheritName {
        get {
            if (_InheritName is null) {
                _InheritName = ((object)JsonObject["inheritsFrom"] ?? "").ToString();
                // 由于过老的 LiteLoader 中没有 Inherits（例如 1.5.2），需要手动判断以获取真实继承版本
                // 此外，由于这里的加载早于版本种类判断，所以需要手动判断是否为 LiteLoader
                // 如果版本提供了不同的 Jar，代表所需的 Jar 可能已被更改，则跳过 Inherit 替换
                if (JsonText.Contains("liteloader") && Version.VanillaName != Name && !JsonText.Contains("logging")) {
                    if (((object)JsonObject["jar"] ?? Version.VanillaName).ToString() == Version.VanillaName) _InheritName = Version.VanillaName;
                }
                // HMCL 版本无 Json
                if (IsHmclFormatJson) _InheritName = "";
            }
            return _InheritName;
        }
    }
    private string _InheritName = null;

    public JsonConfigProvider Config;
    public void ResetSettingsCache() {
        PclCore.IniClearCache(PathIndie + "options.txt");
        PclCore.IniClearCache(PathVersion + "PCL\\Setup.ini");
        Config.DiscardCache();
        PclUiBridge.ResetInstanceSettingsCache(PathVersion);
    }

    /// <summary>
    /// 从版本名，或版本文件夹的完整路径初始化。
    /// </summary>
    public McInstance(string NameOrFullPath) {
        NameOrFullPath = PathUtils.RemoveExtendedPrefix(NameOrFullPath);
        PathVersion = PathUtils.AddSlashSuffix((NameOrFullPath.Contains(":") ? "" : PclUiBridge.McFolderSelectedGet() + "versions\\") + NameOrFullPath);
        Config = new JsonConfigProvider(Path.Combine(PathVersion, "PCL\\config.json"));
    }

    /// <summary>
    /// 检查 Minecraft 版本，若检查通过 State 则为 Original 且返回 True。
    /// </summary>
    public bool Check() {

        // 检查文件夹
        if (!DirectoryUtils.Exists(PathVersion)) {
            State = McInstanceState.Error;
            Info = "未找到版本 " + Name;
            return false;
        }
        // 确认 Json 可用性
        try {
            if (JsonObject["mainClass"] is null) throw new Exception("版本 JSON 中没有 mainClass 项！"); // 这里也会触发 JsonObject 的 Get 事件
        } catch (Exception ex) {
            Logger.Warn(ex, $"版本 JSON 可用性检查失败（{PathVersion}）");
            JsonText = "";
            JsonObject = null;
            Info = ex.Message;
            State = McInstanceState.Error;
            return false;
        }
        // 检查版本号获取
        try {
            if (string.IsNullOrEmpty(Version.VanillaName)) throw new Exception("无法获取版本号，结果为空");
        } catch (Exception ex) {
            Logger.Warn(ex, $"版本号获取失败（{Name}）");
            State = McInstanceState.Error;
            Info = "版本号获取失败：" + ex.GetDisplay(false);
            return false;
        }
        // 检查依赖版本
        try {
            if (InheritName != "") {
                if (!FileUtils.Exists($"{PathUtils.RemoveLastPart(PathVersion)}\\{InheritName}\\{InheritName}.json")) {
                    State = McInstanceState.Error;
                    Info = "需要安装 " + InheritName + " 作为前置版本";
                    return false;
                }
            }
        } catch (Exception ex) {
            Logger.Warn(ex, $"依赖版本检查出错（{Name}）");
            State = McInstanceState.Error;
            Info = "未知错误：" + ex.GetDisplay(false);
            return false;
        }

        State = McInstanceState.Original;
        return true;
    }
    /// <summary>
    /// 加载 Minecraft 版本的详细信息。不使用其缓存，且会更新缓存。
    /// </summary>
    public McInstance Load() {
        try {
            // 检查版本，若出错则跳过数据确定阶段
            if (!Check()) goto ExitDataLoad;
#region 确定版本分类
            switch (Version.VanillaName) { // 在获取 Version.Original 对象时会完成它的加载
                case "Unknown":
                    State = McInstanceState.Error;
                    break;
                case "Old":
                    State = McInstanceState.Old;
                    break;
                default:
                    // 愚人节与快照版本
                    if (Version.IsFool) {
                        State = McInstanceState.Fool;
                    } else if (Version.IsSnapshot) {
                        State = McInstanceState.Snapshot;
                    }
                    // OptiFine
                    if (Version.HasOptiFine) {
                        State = McInstanceState.OptiFine;
                    }
                    // LiteLoader
                    if (Version.ModLoaders.HasFlagF(ModLoaders.LiteLoader)) {
                        State = McInstanceState.LiteLoader;
                    }
                    // Fabric、Forge、NeoForge
                    if (Version.ModLoaders.HasFlagF(ModLoaders.Fabric)) {
                        State = McInstanceState.Fabric;
                    } else if (Version.ModLoaders.HasFlagF(ModLoaders.Forge)) {
                        State = McInstanceState.Forge;
                    } else if (Version.ModLoaders.HasFlagF(ModLoaders.NeoForge)) {
                        State = McInstanceState.NeoForge;
                    }
                    break;
            }
#endregion
ExitDataLoad:
            // 确定版本图标
            Logo = PclCore.ReadIni(PathVersion + "PCL\\Setup.ini", "Logo", "");
            if (Logo == "" || !Convert.ToBoolean(PclCore.ReadIni(PathVersion + "PCL\\Setup.ini", "LogoCustom", "False"))) {
                switch (State) {
                    case McInstanceState.Original:
                        Logo = PclCore.PathImage + "Blocks/Grass.png";
                        break;
                    case McInstanceState.Snapshot:
                        Logo = PclCore.PathImage + "Blocks/CommandBlock.png";
                        break;
                    case McInstanceState.Old:
                        Logo = PclCore.PathImage + "Blocks/CobbleStone.png";
                        break;
                    case McInstanceState.Forge:
                        Logo = PclCore.PathImage + "Blocks/Anvil.png";
                        break;
                    case McInstanceState.NeoForge:
                        Logo = PclCore.PathImage + "Blocks/NeoForge.png";
                        break;
                    case McInstanceState.Fabric:
                        Logo = PclCore.PathImage + "Blocks/Fabric.png";
                        break;
                    case McInstanceState.OptiFine:
                        Logo = PclCore.PathImage + "Blocks/GrassPath.png";
                        break;
                    case McInstanceState.LiteLoader:
                        Logo = PclCore.PathImage + "Blocks/Egg.png";
                        break;
                    case McInstanceState.Fool:
                        Logo = PclCore.PathImage + "Blocks/GoldBlock.png";
                        break;
                    default:
                        Logo = PclCore.PathImage + "Blocks/RedstoneBlock.png";
                        break;
                }
            }
            // 确定版本描述
            RefreshInfo();
            // 确定版本收藏状态
            IsStar = Convert.ToBoolean(PclCore.ReadIni(PathVersion + "PCL\\Setup.ini", "IsStar", "False"));
            // 确定版本显示种类
            DisplayType = (McInstanceCardType)Enum.Parse(typeof(McInstanceCardType), PclCore.ReadIni(PathVersion + "PCL\\Setup.ini", "DisplayType", McInstanceCardType.Auto.ToString()));
            // 写入缓存
            if (DirectoryUtils.Exists(PathVersion)) {
                PclCore.WriteIni(PathVersion + "PCL\\Setup.ini", "State", State.ToString());
                PclCore.WriteIni(PathVersion + "PCL\\Setup.ini", "Info", Info);
                PclCore.WriteIni(PathVersion + "PCL\\Setup.ini", "Logo", Logo);
            }
            if (State != McInstanceState.Error && Version.Reliable) {
                PclCore.WriteIni(PathVersion + "PCL\\Setup.ini", "ReleaseTime", ReleaseTime.ToString("yyyy'-'MM'-'dd HH':'mm"));
                PclCore.WriteIni(PathVersion + "PCL\\Setup.ini", "VersionFabric", Version.ModLoaders.HasFlagF(ModLoaders.Fabric) ? Version.ModLoaderVersions[ModLoaders.Fabric] : null);
                PclCore.WriteIni(PathVersion + "PCL\\Setup.ini", "VersionOptiFine", Version.OptiFineAsMod ? null : Version.OptiFine);
                PclCore.WriteIni(PathVersion + "PCL\\Setup.ini", "VersionLiteLoader", Version.ModLoaders.HasFlagF(ModLoaders.LiteLoader).ToString());
                PclCore.WriteIni(PathVersion + "PCL\\Setup.ini", "VersionForge", Version.ModLoaders.HasFlagF(ModLoaders.Forge) ? Version.ModLoaderVersions[ModLoaders.Forge] : null);
                PclCore.WriteIni(PathVersion + "PCL\\Setup.ini", "VersionNeoForge", Version.ModLoaders.HasFlagF(ModLoaders.NeoForge) ? Version.ModLoaderVersions[ModLoaders.NeoForge] : null);
                PclCore.WriteIni(PathVersion + "PCL\\Setup.ini", "VersionVanillaName", Version.VanillaName);
                PclCore.WriteIni(PathVersion + "PCL\\Setup.ini", "VersionVanilla", Version.Vanilla.ToString());
            }
        } catch (Exception ex) {
            Info = ex.GetDisplay(false);
            Logo = PclCore.PathImage + "Blocks/RedstoneBlock.png";
            State = McInstanceState.Error;
            Logger.Warn(ex, $"加载版本失败（{Name}）");
        } finally {
            IsLoaded = true;
        }
        return this;
    }
    public bool IsLoaded = false;
    /// <summary>
    /// 根据当前组件信息刷新版本描述。
    /// </summary>
    public void RefreshInfo() {
        string CustomInfo = PclCore.ReadIni(PathVersion + "PCL\\Setup.ini", "CustomInfo");
        if (CustomInfo != "") {
            Info = CustomInfo;
        } else {
            Info = VersionDisplayName();
            if (State == McInstanceState.Fool) {
                Info = PclUiBridge.GetMcFoolName(Version.VanillaName);
            } else if (State != McInstanceState.Error) {
                if (PclCore.GetSetting<int>("VersionServerLogin", this) == 3) Info += ", 统一通行证验证";
                if (PclCore.GetSetting<int>("VersionServerLogin", this) == 4) Info += ", Authlib 验证";
            }
        }
    }
    /// <summary>
    /// 获取对版本信息的简短描述。
    /// 例如 “快照 16w01a”、“原版 1.12.2, Forge 1.2.3”、“愚人节版本 2.0”。
    /// </summary>
    public string VersionDisplayName() {
        // Mod Loader 信息
        string ModLoaderInfo = "";
        if (Version.ModLoaders.HasFlagF(ModLoaders.Forge)) ModLoaderInfo += ", Forge" + (Version.ModLoaderVersions[ModLoaders.Forge] == "未知版本" ? "" : " " + Version.ModLoaderVersions[ModLoaders.Forge]);
        if (Version.ModLoaders.HasFlagF(ModLoaders.NeoForge)) ModLoaderInfo += ", NeoForge" + (Version.ModLoaderVersions[ModLoaders.NeoForge] == "未知版本" ? "" : " " + Version.ModLoaderVersions[ModLoaders.NeoForge]);
        if (Version.ModLoaders.HasFlagF(ModLoaders.Fabric)) ModLoaderInfo += ", Fabric" + (Version.ModLoaderVersions[ModLoaders.Fabric] == "未知版本" ? "" : " " + Version.ModLoaderVersions[ModLoaders.Fabric]);
        if (Version.HasOptiFine) ModLoaderInfo += ", OptiFine" + (Version.OptiFine == "未知版本" ? "" : " " + Version.OptiFine.Replace("-", " ").Replace("_", " "));
        if (Version.ModLoaders.HasFlagF(ModLoaders.LiteLoader)) ModLoaderInfo += ", LiteLoader";
        // 基础信息
        string Info;
        switch (State) {
            case McInstanceState.Snapshot:
            case McInstanceState.Original:
            case McInstanceState.Forge:
            case McInstanceState.NeoForge:
            case McInstanceState.Fabric:
            case McInstanceState.OptiFine:
            case McInstanceState.LiteLoader:
                if (Version.VanillaName.ContainsIgnoreCase("pre")) {
                    Info = "预发布版 " + Version.VanillaName;
                } else if (Version.VanillaName.ContainsIgnoreCase("rc")) {
                    Info = "发布候选 " + Version.VanillaName;
                } else if (Version.VanillaName.ContainsIgnoreCase("experimental")) {
                    Info = "实验性快照" + Version.VanillaName;
                } else if (Version.VanillaName == "pending") {
                    Info = "实验性快照";
                } else if (Version.IsSnapshot) {
                    Info = Version.Reliable ? "快照版 " + Version.VanillaName.Replace("-snapshot", "") : "快照版";
                } else {
                    Info = Version.Reliable ? "正式版 " + Version.VanillaName : "正式版";
                }
                break;
            case McInstanceState.Old:
                Info = "远古版本";
                break;
            case McInstanceState.Fool:
                Info = "愚人节版本 " + Version.VanillaName;
                break;
            case McInstanceState.Error:
                return this.Info; // 已有错误信息
            default:
                return "发生了未知错误，请向作者反馈此问题";
        }
        return (Info + ModLoaderInfo).Replace("_", "-");
    }

    /// <summary>
    /// 重命名 MC 版本。
    /// </summary>
    public McInstance Rename(string NewName) {
        string OldName = Name;
        string OldPath = PathVersion;
        string NewPath = PathUtils.AddSlashSuffix(Path.Combine(PathUtils.RemoveLastPart(OldPath), NewName));
        // 重新读取版本 JSON 信息，避免 JsonObject 中已被合并的项被重新存储
        JObject JsonObject;
        string OldJsonPath = GetJsonPath();
        try {
            JsonObject = (JObject)FileUtils.ReadAsJson(OldJsonPath);
        } catch (Exception ex) {
            Logger.Warn(ex, "在重命名读取 JSON 时失败");
            JsonObject = this.JsonObject;
        }
        // 清理缓存
        Config.Save();
        ResetSettingsCache();
        // 重命名主文件夹
        DirectoryUtils.Move(OldPath, NewPath);
        // 重命名 jar 文件与 natives 文件夹
        // 不能进行遍历重命名，否则在版本名很短的时候容易误伤其他文件（#6443）
        if (DirectoryUtils.Exists($"{NewPath}{OldName}-natives")) DirectoryUtils.Move($"{NewPath}{OldName}-natives", $"{NewPath}{NewName}-natives");
        if (FileUtils.Exists($"{NewPath}{OldName}.jar")) FileUtils.Move($"{NewPath}{OldName}.jar", $"{NewPath}{NewName}.jar");
        // 替换版本设置文件中的路径
        if (FileUtils.Exists(NewPath + "PCL\\Setup.ini")) {
            FileUtils.Write(NewPath + "PCL\\Setup.ini", FileUtils.ReadAsString(NewPath + "PCL\\Setup.ini").Replace(OldPath, NewPath));
        }
        // 更改已选中的版本
        if (PclCore.ReadIni(PclUiBridge.McFolderSelectedGet() + "PCL.ini", "Version") == OldName) PclCore.WriteIni(PclUiBridge.McFolderSelectedGet() + "PCL.ini", "Version", NewName);
        // 更新版本 Json
        try {
            JsonObject["id"] = NewName;
            FileUtils.Delete(NewPath + PathUtils.GetLastPart(OldJsonPath));
            FileUtils.Write(NewPath + NewName + ".json", JsonObject.ToString());
        } catch (Exception ex) {
            Logger.Warn(ex, "重命名版本 json 失败");
        }
        return new McInstance(NewPath).Load();
    }

    // 运算符支持
    public override bool Equals(object obj) {
        McInstance version = obj as McInstance;
        return version is not null && PathUtils.IsSame(PathVersion, version.PathVersion);
    }
    // VB 编译器在仅重写 Equals 时会自动生成 GetHashCode（调用 RuntimeHelpers.GetHashCode），此处保持一致。
    public override int GetHashCode() {
        return System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this);
    }
    public static bool operator ==(McInstance a, McInstance b) {
        if (a is null && b is null) return true;
        if (a is null || b is null) return false;
        return PathUtils.IsSame(a.PathVersion, b.PathVersion);
    }
    public static bool operator !=(McInstance a, McInstance b) {
        return !(a == b);
    }

}

/// <summary>
/// MC 版本独占锁。
/// </summary>
public sealed class McInstanceLock {
    private static readonly ConcurrentDictionary<string, IWorker> LockedPaths = new ConcurrentDictionary<string, IWorker>(StringComparer.OrdinalIgnoreCase);
    // 原为 VB Friend Shared ReadOnly Property；因 VB 主程序为独立程序集，Friend（internal）不可跨程序集访问，故提升为 public。
    public static bool IsAnyLocked {
        get {
            return !LockedPaths.IsEmpty;
        }
    }
    /// <summary>
    /// 在 <paramref name="Worker"/> 结束前，锁定指定的 MC 版本。当 <paramref name="Worker"/> 结束时，会自动将锁定释放。
    /// <para/> 仅在该版本已被锁定时返回 false，此时不会改变锁定状态。
    /// </summary>
    public static bool TryAcquire(IWorker Worker, string InstancePath) {
        InstancePath = PathUtils.ForCompare(InstancePath);
        if (!LockedPaths.TryAdd(InstancePath, Worker)) return false;
        Worker.Stopped += () => LockedPaths.TryRemove(InstancePath, out _);
        return true;
    }
    /// <summary>
    /// 检查 MC 版本是否正被锁定。
    /// </summary>
    public static bool IsLocked(McInstance Instance) {
        if (Instance is null) return false;
        return LockedPaths.ContainsKey(PathUtils.ForCompare(Instance.PathVersion));
    }
}

public enum McInstanceCardType {
    Star = -1,
    Auto = 0, // 仅用于强制版本分类的自动
    Hidden = 1,
    API = 2,
    OriginalLike = 3,
    Rubbish = 4,
    Fool = 5,
    Error = 6,
}
