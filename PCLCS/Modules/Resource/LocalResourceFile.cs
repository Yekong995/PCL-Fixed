// 由 VB Modules\Resource\LocalResourceFile.vb 迁移而来。
#nullable disable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace PCLCS.Modules;

/// <summary>
/// 本地的社区资源文件。
/// </summary>
public class LocalResourceFile {

    #region 文件信息

    /// <summary>
    /// 该文件的文件信息。
    /// </summary>
    public readonly FileInfo File;
    public LocalResourceFile(FileInfo FileInfo) {
        File = FileInfo;
    }
    public LocalResourceFile(string FullName)
        : this(FileUtils.GetInfo(FullName)) {
    }

    /// <summary>
    /// 该文件是否没有被禁用。
    /// </summary>
    public bool IsEnabled {
        get {
            return new[] { ".jar", ".zip", ".litemod" }.Contains(File.Extension.Lower());
        }
    }
    /// <summary>
    /// 假若该资源未被禁用时，文件的文件名。包含 .jar/.litemod 等后缀。
    /// </summary>
    public string EnabledName {
        get {
            return File.Name.Replace(".disabled", "").Replace(".old", "");
        }
    }
    /// <summary>
    /// 假若该资源未被禁用时，文件的完整路径。
    /// </summary>
    public string EnabledFullName {
        get {
            return $"{File.DirectoryName}\\{EnabledName}";
        }
    }

    #endregion

    #region 信息项

    /// <summary>
    /// 在初始化结束之后，任何信息被后续更新时触发此事件。
    /// </summary>
    public event Action<LocalResourceFile> OnUpdate;

    /// <summary>
    /// 可读名称。
    /// 可能为无扩展的文件名，但不会是 Nothing。
    /// </summary>
    public string Display {
        get {
            if (_Display is null) _Display = ProjectVersion?.Display;
            if (_Display is null) _Display = PathUtils.GetFileNameWithoutExtension(EnabledName);
            return _Display;
        }
        set {
            if (_Display is null && value is not null && !value.Contains("modname") &&
               value.Lower() != "name" && value.Length > 1 && PclCore.Val(value).ToString() != value) {
                _Display = value;
            }
        }
    }
    private string _Display = null;

    /// <summary>
    /// Mod 的描述信息。
    /// 可能为 Nothing。
    /// </summary>
    public string Description {
        get {
            return _Description;
        }
        set {
            if (_Description is null && value is not null && value.Length > 2) {
                _Description = value.ToString().Trim('\n');
                // 优化显示：若以 [a-zA-Z0-9] 结尾，加上小数点句号
                if (_Description.Lower().LastIndexOfAny("qwertyuiopasdfghjklzxcvbnm0123456789".ToCharArray()) == _Description.Length - 1) _Description += ".";
            }
        }
    }
    private string _Description = null;

    /// <summary>
    /// Mod 的版本。
    /// 不保证符合版本格式规范，且可能为 Nothing。
    /// </summary>
    public string Version {
        get {
            if (_Version is null) _Version = ProjectVersion?.Version;
            return _Version;
        }
        set {
            if (_Version is not null && _Version.RegexCheck("[0-9.\\-]+")) return;
            if (value is not null && value.ContainsIgnoreCase("version")) value = "version"; // 需要修改的标识
            _Version = value;
        }
    }
    /// <summary>
    /// 版本缓存字段。原 VB 中为公开字段，外部代码（MyLocalModItem）会直接读写。
    /// </summary>
    public string _Version = null;

    #endregion

    #region 网络信息

    /// <summary>
    /// 该 Mod 关联的网络项目。
    /// </summary>
    public ResourceProject Project {
        get {
            return _Project;
        }
        set {
            _Project = value;
            OnUpdate?.Invoke(this);
        }
    }
    private ResourceProject _Project;

    /// <summary>
    /// 本地文件对应的联网文件信息。
    /// </summary>
    public ResourceVersion ProjectVersion;

    /// <summary>
    /// 该 Mod 对应的联网最新版本。
    /// </summary>
    public ResourceVersion UpdateFile {
        get {
            return _UpdateFile;
        }
        set {
            _UpdateFile = value;
            OnUpdate?.Invoke(this);
        }
    }
    private ResourceVersion _UpdateFile;

    /// <summary>
    /// 该 Mod 的更新日志网址。
    /// </summary>
    public List<string> ChangelogUrls = new List<string>();
    /// <summary>
    /// 所有网络信息是否已成功加载。
    /// </summary>
    public bool OnlineDataLoaded = false;

    /// <summary>
    /// 将网络信息保存为 JSON。
    /// </summary>
    public JObject ToJson() {
        JObject Json = new JObject();
        if (Project is not null) Json.Add("Project", Project.ToJson());
        Json.Add("ChangelogUrls", new JArray(ChangelogUrls));
        Json.Add("OnlineDataLoaded", OnlineDataLoaded);
        if (ProjectVersion is not null) Json.Add("Version", ProjectVersion.ToCacheJson());
        if (UpdateFile is not null) Json.Add("UpdateFile", UpdateFile.ToCacheJson());
        return Json;
    }
    /// <summary>
    /// 从 JSON 中读取网络信息。
    /// </summary>
    public void FromJson(JObject Json) {
        OnlineDataLoaded = Json["OnlineDataLoaded"]?.ToObject<bool>() ?? false;
        if (Json.ContainsKey("Project")) Project = new ResourceProject((JObject)Json["Project"]);
        if (Json.ContainsKey("ChangelogUrls")) ChangelogUrls = Json["ChangelogUrls"].ToObject<List<string>>();
        if (Json.ContainsKey("Version")) ProjectVersion = ResourceVersion.FromCacheJson((JObject)Json["Version"]);
        if (Json.ContainsKey("UpdateFile")) UpdateFile = ResourceVersion.FromCacheJson((JObject)Json["UpdateFile"]);
    }

    /// <summary>
    /// 该文件是否可以更新。
    /// </summary>
    public bool HasUpdate {
        get {
            return !PclCore.GetSetting<bool>("UiHiddenFunctionModUpdate") &&
                   !PclCore.GetSetting<bool>("VersionAdvanceDisableModUpdate", PclUiBridge.CurrentVersionInstance?.Invoke()) &&
                   UpdateFile is not null;
        }
    }

    /// <summary>
    /// 获取用于 CurseForge 信息获取的 Hash 值（MurmurHash2）。
    /// </summary>
    public uint CurseForgeHash {
        get {
            if (_CurseForgeHash is null) {
                // 读取缓存
                string CacheKey = $"{EnabledFullName}-{File.LastWriteTime.ToLongTimeString()}-{File.Length}-C".GetStableHashCode().ToString();
                string Cached = PclCore.ReadIni(PclCore.GetPathTemp() + "Cache\\ModHash.ini", CacheKey);
                if (Cached != "" && Cached.RegexCheck("^\\d+$")) { // #5062
                    _CurseForgeHash = uint.Parse(Cached);
                    return _CurseForgeHash.Value;
                }
                // 读取文件
                List<byte> data = new List<byte>();
                foreach (byte b in FileUtils.ReadAsBytes(File.FullName)) {
                    if (b == 9 || b == 10 || b == 13 || b == 32) continue;
                    data.Add(b);
                }
                // 计算 MurmurHash2
                int length = data.Count;
                uint h = (uint)(1 ^ length); // 1 是种子
                int i;
                for (i = 0; i <= length - 4; i += 4) {
                    uint k = data[i] | ((uint)data[i + 1] << 8) | ((uint)data[i + 2] << 16) | ((uint)data[i + 3] << 24);
                    k = k * 0x5BD1E995u;
                    k = k ^ (k >> 24);
                    k = k * 0x5BD1E995u;
                    h = h * 0x5BD1E995u;
                    h = h ^ k;
                }
                switch (length - i) {
                    case 3:
                        h = h ^ (data[i] | ((uint)data[i + 1] << 8));
                        h = h ^ ((uint)data[i + 2] << 16);
                        h = h * 0x5BD1E995u;
                        break;
                    case 2:
                        h = h ^ (data[i] | ((uint)data[i + 1] << 8));
                        h = h * 0x5BD1E995u;
                        break;
                    case 1:
                        h = h ^ data[i];
                        h = h * 0x5BD1E995u;
                        break;
                }
                h = h ^ (h >> 13);
                h = h * 0x5BD1E995u;
                h = h ^ (h >> 15);
                _CurseForgeHash = h;
                // 写入缓存
                PclCore.WriteIni(PclCore.GetPathTemp() + "Cache\\ModHash.ini", CacheKey, h.ToString());
            }
            return _CurseForgeHash.Value;
        }
    }
    private uint? _CurseForgeHash;

    /// <summary>
    /// 获取用于 Modrinth 信息获取的 Hash 值（SHA1）。固定为小写。
    /// </summary>
    public string ModrinthHash {
        get {
            if (_ModrinthHash is null) {
                // 读取缓存
                string CacheKey = $"{EnabledFullName}-{File.LastWriteTime.ToLongTimeString()}-{File.Length}-M".GetStableHashCode().ToString();
                string Cached = PclCore.ReadIni(PclCore.GetPathTemp() + "Cache\\ModHash.ini", CacheKey);
                if (Cached != "") {
                    _ModrinthHash = Cached;
                    return _ModrinthHash;
                }
                // 计算 SHA1
                _ModrinthHash = CryptographyUtils.ComputeFileHash(File.FullName, CryptographyUtils.HashMethod.Sha1);
                // 写入缓存
                PclCore.WriteIni(PclCore.GetPathTemp() + "Cache\\ModHash.ini", CacheKey, _ModrinthHash);
            }
            return _ModrinthHash;
        }
    }
    private string _ModrinthHash;

    #endregion

    #region API

    public override string ToString() {
        return File.FullName;
    }
    public override bool Equals(object obj) {
        LocalResourceFile Target = obj as LocalResourceFile;
        return Target is not null && PathUtils.IsSame(File.FullName, Target.File.FullName);
    }
    public override int GetHashCode() {
        return File.FullName?.GetHashCode() ?? 0;
    }

    #endregion

}
