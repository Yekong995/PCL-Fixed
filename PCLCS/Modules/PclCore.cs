// 由 VB Modules\Base\ModBase.vb 的纯逻辑部分迁移而来。
// 依赖 VB UI 的成员（MyColor、DPI、剪贴板、资源释放、网页打开等）保留在 VB ModBase 中，
// VB 侧通过同名成员转发到本类，确保 VB 调用点无需修改。
#nullable disable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Microsoft.VisualBasic;
using MeloongCore.Extensions;

namespace PCLCS.Modules;

public static class PclCore {

    #region 声明

    /// <summary>主窗口句柄。</summary>
    public static IntPtr Handle;

    /// <summary>包含程序名的完整路径。</summary>
    public static string PathExe = Paths.Base + AppDomain.CurrentDomain.SetupInformation.ApplicationName;

    /// <summary>程序内嵌图片文件夹路径，以 / 结尾。</summary>
    public const string PathImage = "pack://application:,,,/Plain Craft Launcher 2;component/Images/";

    /// <summary>程序的缓存文件夹路径，以 \ 结尾。由宿主（VB UI）在模块初始化时按 Settings 填充。</summary>
    public static string PathTemp;

    /// <summary>当前程序的语言。</summary>
    public static string Lang = "zh_CN";

    /// <summary>程序的打开计时。</summary>
    public static long ApplicationStartTick = GetTimeMs();

    /// <summary>程序打开时的时间。</summary>
    public static DateTime ApplicationOpenTime = DateTime.Now;

    /// <summary>程序是否正在结束。</summary>
    public static bool IsProgramEnding = false;

    /// <summary>是否使用 GBK 编码。</summary>
    public static bool IsGBKEncoding = Encoding.Default.CodePage == 936;

    /// <summary>系统盘盘符，以 \ 结尾。例如 "C:\"。</summary>
    public static string OsDrive = Environment.GetLogicalDrives().First(p => Directory.Exists(p));

    /// <summary>获取缓存文件夹路径，未初始化时尝试使用宿主提供的默认值，否则退回系统临时目录。</summary>
    internal static string GetPathTemp() {
        if (PathTemp is null) {
            PathTemp = PathTempDefaultProvider?.Invoke() ?? (Path.GetTempPath() + "PCL\\");
        }
        return PathTemp;
    }

    /// <summary>由宿主（VB UI）设置，用于计算缓存文件夹默认值，避免 C# 反向依赖 Settings。</summary>
    public static Func<string> PathTempDefaultProvider;

    /// <summary>
    /// 由宿主（VB UI）设置，用于读取全局/实例设置项，避免 C# 反向依赖 Settings。
    /// 参数为（键，实例），实例可为 null。
    /// </summary>
    public static Func<string, object, object> SettingProvider;

    /// <summary>读取设置项（经由 VB Settings 实现）。</summary>
    public static T GetSetting<T>(string Key, object Instance = null) {
        return (T)SettingProvider(Key, Instance);
    }

    /// <summary>
    /// 由宿主（VB UI）设置，用于写入全局/实例设置项，避免 C# 反向依赖 Settings。
    /// 参数为（键，值，实例），实例可为 null。
    /// </summary>
    public static Action<string, object, object> SettingSetter;

    /// <summary>写入设置项（经由 VB Settings 实现）。</summary>
    public static void SetSetting(string Key, object Value, object Instance = null) {
        SettingSetter(Key, Value, Instance);
    }

    /// <summary>由宿主（VB UI）设置，用于判断设置项是否已保存。参数为（键，实例）。</summary>
    public static Func<string, object, bool> HasSavedProvider;

    /// <summary>判断设置项是否已保存（经由 VB Settings 实现）。</summary>
    public static bool HasSavedSetting(string Key, object Instance = null) {
        return HasSavedProvider(Key, Instance);
    }

    #endregion

    #region 文件

    // ini

    private static readonly ConcurrentDictionary<string, ConcurrentDictionary<string, string>> IniCache = new();

    /// <summary>
    /// 清除某 ini 文件的运行时缓存。
    /// </summary>
    /// <param name="FileName">文件完整路径或简写文件名。简写将会使用 "ApplicationName\文件名.ini" 作为路径。</param>
    public static void IniClearCache(string FileName) {
        if (!FileName.Contains(":\\")) FileName = $"{Paths.Base}PCL\\{FileName}.ini";
        IniCache.Remove(FileName);
    }

    /// <summary>
    /// 获取 ini 文件缓存。如果没有，则新读取 ini 文件内容。
    /// 在文件不存在或读取失败时返回 Nothing。
    /// </summary>
    /// <param name="FileName">文件完整路径或简写文件名。简写将会使用 "ApplicationName\文件名.ini" 作为路径。</param>
    private static ConcurrentDictionary<string, string> IniGetContent(string FileName) {
        try {
            // 还原文件路径
            if (!FileName.Contains(":\\")) FileName = $"{Paths.Base}PCL\\{FileName}.ini";
            // 检索缓存
            if (IniCache.TryGetValue(FileName, out var Cache)) return Cache;
            // 读取文件
            if (!FileUtils.Exists(FileName)) return null;
            ConcurrentDictionary<string, string> Ini = new ConcurrentDictionary<string, string>();
            foreach (string Line in FileUtils.ReadAsLines(FileName, true)) {
                int Index = Line.IndexOfF(":");
                if (Index > 0) Ini[Line.Substring(0, Index)] = Line.Substring(Index + 1); // 可能会有重复键，见 #3616
            }
            IniCache[FileName] = Ini;
            return Ini;
        } catch (Exception ex) {
            Logger.Error(ex, $"生成 ini 文件缓存失败（{FileName}）", LogBehavior.Toast);
            return null;
        }
    }

    /// <summary>
    /// 读取 ini 文件。这可能会使用到缓存。
    /// </summary>
    /// <param name="FileName">文件完整路径或简写文件名。简写将会使用 "ApplicationName\文件名.ini" 作为路径。</param>
    /// <param name="Key">键。</param>
    /// <param name="DefaultValue">没有找到键时返回的默认值。</param>
    public static string ReadIni(string FileName, string Key, string DefaultValue = "") {
        var Content = IniGetContent(FileName);
        if (Content is null) return DefaultValue;
        return Content.GetOrDefault(Key, DefaultValue);
    }

    /// <summary>
    /// 判断 ini 文件中是否包含某个键。这可能会使用到缓存。
    /// </summary>
    public static bool HasIniKey(string FileName, string Key) {
        var Content = IniGetContent(FileName);
        return Content is not null && Content.ContainsKey(Key);
    }

    /// <summary>
    /// 从 ini 文件中移除某个键。这会更新缓存。
    /// </summary>
    public static void DeleteIniKey(string FileName, string Key) {
        WriteIni(FileName, Key, null);
    }

    /// <summary>
    /// 写入 ini 文件，这会更新缓存。
    /// 若 Value 为 Nothing，则删除该键。
    /// </summary>
    /// <param name="FileName">文件完整路径或简写文件名。简写将会使用 "ApplicationName\文件名.ini" 作为路径。</param>
    /// <param name="Key">键。</param>
    /// <param name="Value">值。</param>
    public static void WriteIni(string FileName, string Key, string Value) {
        try {
            // 预处理
            if (Key.Contains(":")) throw new Exception($"尝试写入 ini 文件 {FileName} 的键名中包含了冒号：{Key}");
            Key = Key.ReplaceLineEndings("");
            Value = Value?.ReplaceLineEndings("");
            // 获取目前文件
            var Content = IniGetContent(FileName) ?? new ConcurrentDictionary<string, string>();
            // 更新值
            if (Value is null) {
                if (!Content.Remove(Key)) return; // 无需处理
            } else {
                if (Content.TryGetValue(Key, out var ExistingValue) && ExistingValue == Value) return; // 无需处理
                Content[Key] = Value;
            }
            // 写入文件
            StringBuilder FileContent = new StringBuilder();
            foreach (var Pair in Content) {
                FileContent.Append(Pair.Key);
                FileContent.Append(":");
                FileContent.Append(Pair.Value);
                FileContent.Append("\r\n");
            }
            if (!FileName.Contains(":\\")) FileName = $"{Paths.Base}PCL\\{FileName}.ini";
            // 处理相对路径
            FileName = FileName.Contains(":\\") ? FileName : Paths.Base + FileName;
            FileUtils.Write(FileName, FileContent.ToString());
        } catch (Exception ex) {
            Logger.Error(ex, $"写入文件失败（{FileName} → {Key}:{Value}）", LogBehavior.Toast);
        }
    }

    // 权限检查

    /// <summary>
    /// 检查是否拥有某一文件夹的 I/O 权限。如果文件夹不存在，会返回 False。
    /// </summary>
    public static bool CheckPermission(string Folder) {
        try {
            if (string.IsNullOrEmpty(Folder)) return false;
            Folder = PathUtils.AddSlashSuffix(Folder);
            if (Folder.EndsWithF(":\\System Volume Information\\", true) || Folder.EndsWithF(":\\$RECYCLE.BIN\\", true)) return false;
            if (!DirectoryUtils.Exists(Folder)) return false;
            string TestFilePath = $"{Folder}CheckPermission{GetUuid()}.txt";
            FileUtils.Write(TestFilePath, "临时文件，用于检测该文件夹的权限是否正常。");
            FileUtils.Delete(TestFilePath);
            return true;
        } catch (Exception ex) {
            Logger.Warn(ex, $"没有对文件夹 {Folder} 的权限，请尝试以管理员权限运行 PCL");
            return false;
        }
    }

    /// <summary>
    /// 检查是否拥有某一文件夹的 I/O 权限。如果出错，则抛出异常。
    /// </summary>
    public static void CheckPermissionWithException(string Folder) {
        if (string.IsNullOrWhiteSpace(Folder)) throw new ArgumentNullException("文件夹名不能为空！");
        Folder = PathUtils.AddSlashSuffix(Folder);
        if (Folder.EndsWithF(":\\System Volume Information\\", true) || Folder.EndsWithF(":\\$RECYCLE.BIN\\", true)) throw new UnauthorizedAccessException("没有对系统文件夹的权限！");
        if (!DirectoryUtils.Exists(Folder)) throw new DirectoryNotFoundException("文件夹不存在！");
        string TestFilePath = $"{Folder}CheckPermission{GetUuid()}.txt";
        FileUtils.Write(TestFilePath, "临时文件，用于检测该文件夹的权限是否正常。");
        FileUtils.Delete(TestFilePath);
    }

    #endregion

    #region 文本

    public static char vbLQ = Convert.ToChar(8220);
    public static char vbRQ = Convert.ToChar(8221);

    /// <summary>
    /// 不会报错的 Val。
    /// 如果输入有误，返回 0。
    /// </summary>
    public static double Val(object Str) {
        if (Str is null) return 0;
        try {
            return (Str is string s && s == "&") ? 0 : Conversion.Val(Str);
        } catch {
            return 0;
        }
    }

    #endregion

    #region 搜索

    /// <summary>
    /// 获取搜索文本的相似度。
    /// </summary>
    /// <param name="Source">被搜索的长内容。</param>
    /// <param name="Query">用户输入的搜索文本。</param>
    private static double SearchSimilarity(string Source, string Query) {
        if (string.IsNullOrEmpty(Source) || string.IsNullOrEmpty(Query)) return 0;
        int qp = 0;
        double lenSum = 0;
        StringBuilder str = new StringBuilder(Source.Length);
        str.Append(Source.Lower().Replace(" ", ""));
        Query = Query.Lower().Replace(" ", "");
        int sourceLength = str.Length, queryLength = Query.Length; // 用于计算最后因数的长度缓存
        if (queryLength == 0) return 0;
        do {
            // 对 qp 作为开始位置计算
            int sp = 0, lenMax = 0, spMax = 0;
            int currentSourceLength = str.Length;
            do {
                int len = 0;
                while ((qp + len) < queryLength && (sp + len) < currentSourceLength && str[sp + len] == Query[qp + len]) {
                    len += 1;
                }
                // 存储 len
                if (len > lenMax) {
                    lenMax = len;
                    spMax = sp;
                }
                // 根据结果增加 sp
                sp += (len > 0) ? len : 1;
            } while (sp < currentSourceLength);
            if (lenMax > 0) {
                str.Remove(spMax, lenMax); // 将源中的对应字段移除
                // 存储 lenSum
                double IncWeight = Math.Pow(1.4, 3 + lenMax) - 3.6; // 根据长度加成
                IncWeight *= 1 + 0.3 * Math.Max(0, 3 - Math.Abs(qp - spMax)); // 根据位置加成
                lenSum += IncWeight;
            }
            qp += (lenMax > 0) ? lenMax : 1;
        } while (qp < queryLength);
        // 计算结果：重复字段量 × 源长度影响比例
        return (lenSum / queryLength) * (3 / Math.Sqrt(sourceLength + 15)) * ((queryLength <= 2) ? (3 - queryLength) : 1);
    }

    /// <summary>
    /// 获取多段文本加权后的相似度。
    /// </summary>
    private static double SearchSimilarityWeighted(List<SearchSource> Source, string Query) {
        double TotalWeight = 0;
        double Sum = 0;
        foreach (SearchSource Pair in Source) {
            if (Pair.Aliases.Any()) {
                Sum += Pair.Aliases.Max(a => SearchSimilarity(a, Query)) * Pair.Weight;
            }
            TotalWeight += Pair.Weight;
        }
        return Sum / TotalWeight;
    }

    /// <summary>
    /// 进行多段文本加权搜索，获取相似度较高的数项结果。
    /// 在搜索时，会对每个文本源单独加权，但单个文本源内的多个别名只取最高的一个的相似度。
    /// 这会修改 Entries 中每项的 Similarity 与 AbsoluteRight 字段。
    /// </summary>
    /// <param name="MaxBlurCount">返回的最大模糊结果数。</param>
    /// <param name="MinBlurSimilarity">返回结果要求的最低相似度。</param>
    public static List<SearchEntry<T>> Search<T>(List<SearchEntry<T>> Entries, string Query, int MaxBlurCount = 5, double MinBlurSimilarity = 0.1) {
        // 初始化
        List<SearchEntry<T>> ResultList = new List<SearchEntry<T>>();
        if (!Entries.Any()) return ResultList;
        // 进行搜索，获取相似信息
        string[] QueryParts = Query.Split(' ', true);
        List<SearchEntry<T>> Candidates = new List<SearchEntry<T>>();
        foreach (SearchEntry<T> Entry in Entries) {
            Entry.Similarity = SearchSimilarityWeighted(Entry.SearchSource, Query);
            Entry.AbsoluteRight =
                QueryParts.All( // 对于按空格分割的每一段
                QueryPart => Entry.SearchSource.Any( // 若与任意一个搜索源完全匹配，则标记为完全匹配项
                Source => Source.Aliases.Any(
                Alias => Alias.Replace(" ", "").ContainsIgnoreCase(QueryPart))));
            if (Entry.AbsoluteRight || Entry.Similarity >= MinBlurSimilarity) Candidates.Add(Entry);
        }
        // 按照相似度进行排序
        Candidates = Candidates.SortByComparison((Left, Right) => Left.AbsoluteRight != Right.AbsoluteRight ? Left.AbsoluteRight : Left.Similarity > Right.Similarity);
        // 返回结果
        int BlurCount = 0;
        foreach (SearchEntry<T> Entry in Candidates) {
            if (Entry.AbsoluteRight) {
                ResultList.Add(Entry); // 完全匹配，直接加入
            } else {
                if (BlurCount == MaxBlurCount) break;
                ResultList.Add(Entry);
                BlurCount += 1; // 模糊结果计数
            }
        }
        return ResultList;
    }

    #endregion

    #region 系统

    /// <summary>
    /// 为 Task 设置超时，在超时时抛出 TimeoutException。
    /// </summary>
    public static T GetResultWithTimeout<T>(this Task<T> TargetTask, CancellationTokenSource TokenSource, int TimeoutMs) {
        Task DelayTask = Task.Delay(TimeoutMs);
        if (Task.WhenAny(TargetTask, DelayTask).ConfigureAwait(false).GetAwaiter().GetResult() == DelayTask) {
            TokenSource.Cancel();
            throw new TimeoutException($"任务超时（{TimeoutMs} ms）");
        }
        return TargetTask.Run();
    }

    /// <summary>
    /// 为 Task 设置超时，在超时时抛出 TimeoutException。
    /// </summary>
    public static void GetResultWithTimeout(this Task TargetTask, CancellationTokenSource TokenSource, int TimeoutMs) {
        Task DelayTask = Task.Delay(TimeoutMs);
        if (Task.WhenAny(TargetTask, DelayTask).ConfigureAwait(false).GetAwaiter().GetResult() == DelayTask) {
            TokenSource.Cancel();
            throw new TimeoutException($"任务超时（{TimeoutMs} ms）");
        }
        TargetTask.Run();
    }

    /// <summary>
    /// 可用于临时存放文件的，不含任何特殊字符的文件夹路径，以 "\" 结尾。
    /// </summary>
    public static readonly Lazy<string> PathPure = new Lazy<string>(
    () => {
        if (Paths.Base.IsAsciiOnly()) {
            return Paths.Base + "PCL\\";
        } else if (Paths.AppDataThenName.IsAsciiOnly()) {
            return Paths.AppDataThenName;
        } else if (GetPathTemp().IsAsciiOnly()) {
            return GetPathTemp();
        } else {
            return OsDrive + "ProgramData\\PCL\\";
        }
    });

    /// <summary>
    /// 以管理员权限运行当前程序，并等待程序运行结束。
    /// 返回程序的返回代码，如果运行失败将抛出异常。
    /// </summary>
    public static int RunAsAdmin(string Argument) {
        Process NewProcess = StartProcess(new ProcessStartInfo(PathExe) { Verb = "runas", Arguments = Argument });
        NewProcess.WaitForExit();
        return NewProcess.ExitCode;
    }

    private static int Uuid = 1;
    private static readonly object UuidLock = new object();

    /// <summary>
    /// 获取一个全程序内不会重复的数字（伪 Uuid）。
    /// </summary>
    public static int GetUuid() {
        lock (UuidLock) {
            Uuid += 1;
            return Uuid;
        }
    }

    // 时间相关

    /// <summary>
    /// 获取一个单调递增时间值（毫秒）。
    /// </summary>
    public static long GetTimeMs() {
        return Stopwatch.GetTimestamp() / (Stopwatch.Frequency / 1000L);
    }

    /// <summary>
    /// 获取十进制 Unix 时间戳。
    /// </summary>
    public static long GetUnixTimestampUtc() {
        return (DateTime.Now.ToUniversalTime().Ticks - 621355968000000000) / 10000000;
    }

    /// <summary>
    /// 获取十进制 Unix 时间戳。
    /// </summary>
    public static long ToUnixTimestampUtc(DateTime Time) {
        return (Time.ToUniversalTime().Ticks - 621355968000000000) / 10000000;
    }

    /// <summary>
    /// 时间戳转化为日期。
    /// </summary>
    public static DateTime GetDate(int timeStamp) {
        return DateTimeOffset.FromUnixTimeSeconds(timeStamp).LocalDateTime;
    }

    /// <summary>
    /// 将 UTC 时间转化为当前时区的时间。
    /// </summary>
    public static DateTime GetLocalTime(DateTime UtcDate) {
        return DateTime.SpecifyKind(UtcDate, DateTimeKind.Utc).ToLocalTime();
    }

    /// <summary>
    /// 启动进程并返回进程实例，若失败则抛出异常。
    /// 会自动进行长路径处理。
    /// </summary>
    public static Process StartProcess(string FileName, string Arguments = "") {
        return StartProcess(new ProcessStartInfo { FileName = FileName, Arguments = Arguments });
    }

    /// <summary>
    /// 启动进程并返回进程实例，若失败则抛出异常。
    /// 会自动进行长路径处理。
    /// </summary>
    public static Process StartProcess(ProcessStartInfo StartInfo) {
        StartInfo.FileName = PathUtils.ToShortPath(StartInfo.FileName);
        if (!string.IsNullOrEmpty(StartInfo.WorkingDirectory)) StartInfo.WorkingDirectory = PathUtils.ToShortPath(StartInfo.WorkingDirectory);
        Logger.Info($"启动进程：{StartInfo.FileName} {StartInfo.Arguments}");
        return Process.Start(StartInfo);
    }

    /// <summary>
    /// 在新的工作线程中执行代码。
    /// </summary>
    public static Thread RunInNewThread(Action Action, string Name = null, ThreadPriority Priority = ThreadPriority.Normal) {
        Thread th = new Thread(
        () => {
            try {
                Action();
            } catch (Exception ex) {
                Logger.Error(ex, $"{Name}：线程执行失败");
            }
        }) { Name = Name ?? "Runtime New Invoke " + GetUuid() + "#", Priority = Priority };
        th.Start();
        return th;
    }

    /// <summary>
    /// 确保在 UI 线程中执行代码。
    /// 如果当前并非 UI 线程，则会阻断当前线程，直至 UI 线程执行完毕。
    /// 为防止线程互锁，请仅在开始加载动画、从 UI 获取输入时使用！
    /// </summary>
    public static TOutput RunInUiWait<TOutput>(Func<TOutput> Action) {
        if (RunInUi()) {
            return Action();
        } else {
            return Application.Current.Dispatcher.Invoke(Action);
        }
    }

    /// <summary>
    /// 确保在 UI 线程中执行代码。
    /// 如果当前并非 UI 线程，则会阻断当前线程，直至 UI 线程执行完毕。
    /// 为防止线程互锁，请仅在开始加载动画、从 UI 获取输入时使用！
    /// </summary>
    public static void RunInUiWait(Action Action) {
        if (RunInUi()) {
            Action();
        } else {
            Application.Current.Dispatcher.Invoke(Action);
        }
    }

    /// <summary>
    /// 确保在 UI 线程中执行代码，代码按触发顺序执行。
    /// 如果当前并非 UI 线程，也不阻断当前线程的执行。
    /// </summary>
    public static void RunInUi(Action Action, bool ForceWaitUntilLoaded = false) {
        if (ForceWaitUntilLoaded) {
            Application.Current.Dispatcher.InvokeAsync(Action, DispatcherPriority.Loaded);
        } else if (RunInUi()) {
            Action();
        } else {
            Application.Current.Dispatcher.InvokeAsync(Action);
        }
    }

    /// <summary>
    /// 确保在工作线程中执行代码。
    /// </summary>
    public static void RunInThread(Action Action) {
        if (RunInUi()) {
            RunInNewThread(Action, "Invoke " + GetUuid());
        } else {
            Action();
        }
    }

    /// <summary>
    /// 按照既定的函数进行选择排序。
    /// 返回第一个对象是否应该排在前面（a > b）。
    /// </summary>
    public static List<T> SortByComparison<T>(this IList<T> List, Func<T, T, bool> SortRule) {
        List<T> NewList = new List<T>();
        while (List.Any()) {
            T Highest = List[0];
            for (int i = 1; i < List.Count; i++) {
                if (SortRule(List[i], Highest)) Highest = List[i];
            }
            List.Remove(Highest);
            NewList.Add(Highest);
        }
        return NewList;
    }

    /// <summary>
    /// 获取程序启动参数。
    /// </summary>
    /// <param name="Name">参数名。</param>
    /// <param name="DefaultValue">默认值。</param>
    public static object GetProgramArgument(string Name, object DefaultValue = null) {
        string[] AllArguments = Command.Split(' ');
        for (int i = 0; i < AllArguments.Length; i++) {
            if (AllArguments[i] == "-" + Name) {
                if (AllArguments.Length == i + 1 || AllArguments[i + 1].StartsWithF("-")) return true;
                return AllArguments[i + 1];
            }
        }
        return DefaultValue;
    }

    /// <summary>
    /// 获取程序启动参数部分（等价于 VB 的 Command()）。
    /// </summary>
    private static string Command {
        get {
            string cmd = Environment.CommandLine;
            int start;
            if (cmd.StartsWith("\"", StringComparison.Ordinal)) {
                int end = cmd.IndexOf('"', 1);
                start = end < 0 ? 0 : end + 1;
            } else {
                int sp = cmd.IndexOf(' ');
                start = sp < 0 ? cmd.Length : sp;
            }
            return cmd.Substring(start).TrimStart(' ');
        }
    }

    /// <summary>
    /// 打开 explorer。
    /// 若不以 \ 结尾，则将视作文件路径，打开并选中此文件。
    /// </summary>
    public static void OpenExplorer(string Location) {
        try {
            Location = Location.Replace("/", "\\").Trim(' ', '"');
            Logger.Info($"正在打开资源管理器：{Location}");
            if (Location.EndsWithF("\\")) {
                StartProcess(Location);
            } else {
                StartProcess("explorer", $"/select,\"{Location}\"");
            }
        } catch (Exception ex) {
            Logger.Error(ex, "打开资源管理器失败，请尝试关闭安全软件（如 360 安全卫士）", LogBehavior.Alert);
        }
    }

    private static readonly int UiThreadId = Thread.CurrentThread.ManagedThreadId;

    /// <summary>
    /// 当前线程是否为主线程。
    /// </summary>
    public static bool RunInUi() {
        return Thread.CurrentThread.ManagedThreadId == UiThreadId;
    }

    /// <summary>
    /// 将布尔值转换为 Visibility。True 转换为 Visible，False 转换为 Collapsed。
    /// </summary>
    public static Visibility ToVisibility(this bool IsVisible) {
        return IsVisible ? Visibility.Visible : Visibility.Collapsed;
    }

    #endregion

    #region 随机

    private static readonly Random Random = new Random();

    /// <summary>
    /// 随机选择其一。
    /// </summary>
    public static T RandomOne<T>(ICollection<T> objects) {
        return objects.ElementAt(RandomInteger(0, objects.Count - 1));
    }

    /// <summary>
    /// 取随机整数（包含）。
    /// </summary>
    public static int RandomInteger(int min, int max) {
        return (int)Math.Floor((max - min + 1) * Random.NextDouble()) + min;
    }

    /// <summary>
    /// 将数组随机打乱。
    /// </summary>
    public static IEnumerable<T> Shuffle<T>(this IEnumerable<T> Raw) {
        List<T> RawCopy = new List<T>(Raw);
        while (RawCopy.Any()) {
            int i = RandomInteger(0, RawCopy.Count - 1);
            yield return RawCopy[i];
            RawCopy.RemoveAt(i);
        }
    }

    #endregion

}

#region 自定义类

/// <summary>
/// 支持负数与浮点数的矩形。
/// </summary>
public class MyRect {

    // 属性
    public double Width = 0;
    public double Height = 0;
    public double Left = 0;
    public double Top = 0;

    // 构造函数
    public MyRect() {
    }
    public MyRect(double left, double top, double width, double height) {
        Left = left;
        Top = top;
        Width = width;
        Height = height;
    }

}

/// <summary>
/// 模块加载状态枚举。
/// </summary>
public enum LoadState {
    Waiting,
    Loading,
    Finished,
    Failed,
    Canceled,
}

/// <summary>
/// 执行返回值。
/// </summary>
public enum ProcessReturnValues {
    /// <summary>
    /// 执行成功。
    /// </summary>
    Success = 0,
    /// <summary>
    /// 执行失败。
    /// </summary>
    Fail = 1,
    /// <summary>
    /// 执行时出现未经处理的异常。
    /// </summary>
    Exception = 2,
    /// <summary>
    /// 执行超时。
    /// </summary>
    Timeout = 3,
    /// <summary>
    /// 取消执行。可能是由于不满足执行的前置条件。
    /// </summary>
    Cancel = 4,
    /// <summary>
    /// 任务成功完成。
    /// </summary>
    TaskDone = 5,
}

#endregion

#region 搜索

/// <summary>
/// 用于搜索的项目。
/// </summary>
public class SearchEntry<T> {
    /// <summary>
    /// 该项目对应的源数据。
    /// </summary>
    public T Item;
    /// <summary>
    /// 该项目用于搜索的文本源。
    /// 在搜索时，会对每个文本源单独加权，但单个文本源内的多个别名只取最高的一个的相似度。
    /// </summary>
    public List<SearchSource> SearchSource;
    /// <summary>
    /// 相似度。
    /// </summary>
    public double Similarity;
    /// <summary>
    /// 是否完全匹配。
    /// </summary>
    public bool AbsoluteRight;
    public override string ToString() {
        return Math.Round(Similarity, 3) + " - " + Item?.ToString();
    }
}

/// <summary>
/// 单个用于搜索的文本源。
/// </summary>
public class SearchSource {
    public string[] Aliases;
    public double Weight;
    public SearchSource(string[] Aliases, double Weight = 1) {
        this.Aliases = Aliases;
        this.Weight = Weight;
    }
    public SearchSource(string Text, double Weight = 1) {
        Aliases = new[] { Text };
        this.Weight = Weight;
    }
}

#endregion

#region 系统

/// <summary>
/// 指示接取到这个异常的函数进行重试。
/// </summary>
public class RestartException : Exception {
}

/// <summary>
/// 用于储存 RaiseByMouse 的 EventArgs。
/// </summary>
public sealed class RouteEventArgs : EventArgs {
    public bool RaiseByMouse;
    public bool Handled = false;
    public RouteEventArgs(bool RaiseByMouse = false) {
        this.RaiseByMouse = RaiseByMouse;
    }
}

#endregion
