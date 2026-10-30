// 由 VB Modules\Base\ModLoader.vb 的文件夹加载器部分迁移而来。
#nullable disable

using System;
using System.Collections.Generic;
using System.IO;
using MeloongCore;

namespace PCLCS.Modules;

public enum LoaderFolderRunType {
    RunOnUpdated,
    ForceRun,
    UpdateOnly,
}

public static class PclLoader {

    // 文件夹刷新类委托
    private static readonly Dictionary<LoaderBase, LoaderFolderDictionaryEntry> LoaderFolderDictionary = new Dictionary<LoaderBase, LoaderFolderDictionaryEntry>();
    private struct LoaderFolderDictionaryEntry {
        public DateTime? LastCheckTime;
        public string FolderPath;
        public override bool Equals(object obj) {
            if (obj is not LoaderFolderDictionaryEntry) return false;
            LoaderFolderDictionaryEntry entry = (LoaderFolderDictionaryEntry)obj;
            return EqualityComparer<DateTime?>.Default.Equals(LastCheckTime, entry.LastCheckTime) &&
                   FolderPath == entry.FolderPath;
        }
        public override int GetHashCode() {
            int hashCode = -1383909793;
            hashCode = hashCode * -1521134295 + LastCheckTime.GetHashCode();
            hashCode = hashCode * -1521134295 + EqualityComparer<string>.Default.GetHashCode(FolderPath);
            return hashCode;
        }
    }

    /// <summary>
    /// 执行以文件夹检测作为输入的加载器。加载器需以文件夹路径为输入值。
    /// 返回是否执行了加载器。
    /// </summary>
    /// <param name="ExtraPath">用于检查文件夹修改的额外路径。该路径不会传入加载器。</param>
    public static bool LoaderFolderRun(LoaderBase Loader, string FolderPath, LoaderFolderRunType Type, int MaxDepth = 0, string ExtraPath = "", bool WaitForExit = false) {
        LoaderFolderDictionaryEntry Value = new LoaderFolderDictionaryEntry { FolderPath = FolderPath + ExtraPath, LastCheckTime = null };
        try {
            // 获取数据
            DirectoryInfo FolderInfo = DirectoryUtils.GetInfo(FolderPath + ExtraPath);
            Value.LastCheckTime = FolderInfo.Exists ? GetActualLastWriteTimeUtc(FolderInfo, MaxDepth) : (DateTime?)null;
            // 如果已经检查过，则跳过
            if (Type == LoaderFolderRunType.RunOnUpdated && LoaderFolderDictionary.ContainsKey(Loader)) {
                if (FolderInfo.Exists) {
                    if (LoaderFolderDictionary[Loader].LastCheckTime is not null &&
                       Value.Equals(LoaderFolderDictionary[Loader])) return false;
                } else {
                    if (LoaderFolderDictionary[Loader].LastCheckTime is null) return false;
                }
            }
        } catch (Exception ex) {
            Logger.Warn(ex, "文件夹加载器启动检测出错");
        }
        // 写入检查数据
        LoaderFolderDictionary[Loader] = Value;
        // 开始检查
        if (Type == LoaderFolderRunType.UpdateOnly) return false;
        if (WaitForExit) {
            Loader.WaitForExit(FolderPath, IsForceRestart: true);
        } else {
            Loader.Start(FolderPath, IsForceRestart: true);
        }
        return true;
    }
    private static DateTime GetActualLastWriteTimeUtc(DirectoryInfo FolderInfo, int MaxDepth) {
        DateTime Time = FolderInfo.LastWriteTimeUtc;
        if (MaxDepth > 0) {
            foreach (DirectoryInfo Folder in FolderInfo.EnumerateDirectories()) {
                DateTime FolderTime = GetActualLastWriteTimeUtc(Folder, MaxDepth - 1);
                if (FolderTime > Time) Time = FolderTime;
            }
        }
        return Time;
    }

}
