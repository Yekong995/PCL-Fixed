// 由 VB Modules\Base\ModNet.vb 迁移而来。
// VB UI 依赖（SecretCdnSign、SecretHeadersSign、Telemetry、RequestTaskTempFolder、LoaderTaskbar、McFolderList）
// 通过 PclUiBridge 桥接，VB 侧 ModNet 保留同名转发壳。
#nullable disable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CacheCow.Client;
using CacheCow.Client.FileCacheStore;
using MeloongCore;
using MeloongCore.Extensions;
using ThrottleDebounce;

namespace PCLCS.Modules;

public static class NetCore {

    #region 网络请求

    /// <summary>
    /// 发送一次网络请求并获取返回内容。
    /// </summary>
    public static string NetRequestByClient(string Url, HttpMethod Method = null,
            object Content = null, string ContentType = null, string Accept = "*/*",
            int Timeout = 25000, string[,] Headers = null, bool RequireJson = false,
            Encoding Encoding = null, bool SimulateBrowserHeaders = false, bool MakeLog = true) {
        if (Method is null) Method = HttpMethod.Get;
        if (MakeLog) Logger.Info($"发起网络请求（{Method.Method}，{Url}），最大超时 {Timeout}");
        try {
            Dictionary<string, string> HeaderDictionary = (Headers ?? new string[0, 0]).ToDictionary();
            if (ContentType is not null) HeaderDictionary["Content-Type"] = ContentType;
            if (Accept is not null) HeaderDictionary["Accept"] = Accept;
            string Result = SendRequest(Url, Method, Content, HeaderDictionary, SimulateBrowserHeaders: SimulateBrowserHeaders, Timeout: Timeout, Encoding: Encoding).GetString(Encoding);
            // 检查结果是否为完整 JSON（在 #6683 中可能返回空，在被 GFW 截断时可能不完整）
            // 在此处检查并报错，可以让 NetRequestByClientRetry 等方法进行重试
            if (RequireJson) {
                Result = Result.Trim((Environment.NewLine + " ").ToCharArray()); // Authlib-Injector 的结果会在末尾附带一个空行
                if (!(Result.StartsWithF("{") && Result.EndsWithF("}")) &&
                   !(Result.StartsWithF("[") && Result.EndsWithF("]"))) {
                    throw new FormatException("返回结果并非 JSON 格式：" +
                    (Result.Length > 2000 ? Result.Substring(0, 1000) + "..." + Result.Substring(Result.Length - 1000) : Result));
                }
            }
            return Result;
        } catch (FormatException) {
            throw;
        } catch (Exception ex) {
            if (MakeLog) Logger.Warn(ex, $"网络请求失败（{Method.Method}，{Url}）");
            throw;
        }
    }
    /// <summary>
    /// 发送网络请求并获取返回内容。
    /// 会进行至多 45 秒 3 次的尝试，允许最长 30s 的超时。
    /// </summary>
    /// <param name="BackupUrl">如果第一次尝试失败，换用的备用 URL。</param>
    public static string NetRequestByClientRetry(string Url, HttpMethod Method = null, string BackupUrl = null,
            object Content = null, string ContentType = null, string Accept = "*/*",
            string[,] Headers = null, bool RequireJson = false,
            Encoding Encoding = null, bool SimulateBrowserHeaders = false) {
        int RetryCount = 0;
        Exception RetryException = null;
        long StartTime = PclCore.GetTimeMs();
        // 原 VB 使用 GoTo Retry 从 Catch 跳回 Try 内部，C# 不允许跨 try 边界跳转，改为循环重试
        while (true) {
            try {
                switch (RetryCount) {
                    case 0: // 正常尝试
                        return NetRequestByClient(Url, Method, Content, ContentType, Accept, 10000, Headers, RequireJson, Encoding, SimulateBrowserHeaders);
                    case 1: // 慢速重试
                        Thread.Sleep(500);
                        return NetRequestByClient(Url, Method, Content, ContentType, Accept, 30000, Headers, RequireJson, Encoding, SimulateBrowserHeaders);
                    default: // 快速重试
                        if (PclCore.GetTimeMs() - StartTime > 5500) {
                            // 若前两次加载耗费 5 秒以上，才进行重试
                            Thread.Sleep(500);
                            return NetRequestByClient(Url, Method, Content, ContentType, Accept, 4000, Headers, RequireJson, Encoding, SimulateBrowserHeaders);
                        } else {
                            throw RetryException;
                        }
                }
            } catch (Exception ex) {
                ex.ThrowIfCanceled();
                if (ex is HttpRequestCodeException reqEx) {
                    if (reqEx.StatusCode == HttpStatusCode.Forbidden) throw;
                    if (reqEx.StatusCode == HttpStatusCode.NotFound) throw;
                    if (reqEx.StatusCode == (HttpStatusCode)429) Thread.Sleep(10000); // Too Many Requests
                }
                // 重试
                switch (RetryCount) {
                    case 0:
                        RetryException = ex;
                        RetryCount += 1;
                        break;
                    case 1:
                        RetryCount += 1;
                        break;
                    default:
                        throw;
                }
            }
        }
    }
    /// <summary>
    /// 生成数个线程，同时发送网络请求并获取返回内容。
    /// 每个线程开始之前会略有延迟。
    /// </summary>
    public static string NetRequestByClientMultiple(string Url, HttpMethod Method = null, int ThreadCount = 3,
            object Content = null, string ContentType = null, string Accept = "*/*",
            string[,] Headers = null, bool RequireJson = false, int Timeout = 25000,
            Encoding Encoding = null, bool SimulateBrowserHeaders = false) {
        List<Thread> Threads = new List<Thread>();
        string RequestResult = null;
        Exception RequestEx = null;
        int FailCount = 0;
        for (int i = 1; i <= ThreadCount; i++) {
            Thread th = new Thread(
            () => {
                try {
                    RequestResult = NetRequestByClient(Url, Method, Content, ContentType, Accept, Timeout, Headers, RequireJson, Encoding, SimulateBrowserHeaders);
                } catch (Exception ex) {
                    FailCount += 1;
                    RequestEx = ex;
                }
            });
            th.Start();
            Threads.Add(th);
            Thread.Sleep(i * 250);
            if (RequestResult is not null) goto RequestFinished;
        }
        while (true) {
            if (RequestResult is not null) goto RequestFinished;
            if (FailCount == ThreadCount) {
                try {
                    foreach (Thread th in Threads) {
                        if (th.IsAlive) th.Interrupt();
                    }
                } catch {
                }
                throw RequestEx;
            }
            Thread.Sleep(20);
        }
        // C# 标签作用域为所在块，无法像 VB 一样将标签放在 If 内供 For 循环跳入，故移到方法级
        RequestFinished:
        try {
            foreach (Thread th in Threads) {
                if (th.IsAlive) th.Interrupt();
            }
        } catch {
        }
        return RequestResult;
    }

    /// <summary>
    /// 以多线程下载网页文件的方式获取内容。
    /// 不支持缓存协商；若需缓存协商，需要换用 NetRequestByClient。
    /// </summary>
    public static string NetRequestByLoader(string Url, int Timeout = 45000, bool IsJson = false, bool SimulateBrowserHeaders = false) {
        return NetRequestByLoader(new[] { Url }, Timeout, IsJson, SimulateBrowserHeaders);
    }
    /// <summary>
    /// 以多线程下载网页文件的方式获取内容。
    /// 不支持缓存协商；若需缓存协商，需要换用 NetRequestByClient。
    /// </summary>
    public static string NetRequestByLoader(IEnumerable<string> Urls, int Timeout = 45000, bool IsJson = false, bool SimulateBrowserHeaders = false) {
        string Temp = PclUiBridge.RequestTaskTempFolder() + "download.txt";
        LoaderDownload NewTask = new LoaderDownload("源码获取 " + PclCore.GetUuid() + "#", new List<NetFile> { new NetFile(Urls, Temp, new FileChecker { IsJson = IsJson }, SimulateBrowserHeaders) });
        try {
            NewTask.WaitForExitTime(Timeout, TimeoutMessage: "连接服务器超时（第一下载源：" + Urls.First() + "）");
            string Result = FileUtils.ReadAsString(Temp);
            FileUtils.Delete(Temp);
            return Result;
        } finally {
            NewTask.Cancel();
        }
    }

    #endregion

    #region 文件下载

    /// <summary>
    /// 通过网络请求直接下载小文件，若文件已存在将被覆盖。
    /// 不建议用于下载大文件。
    /// </summary>
    public static void NetDownloadByClient(string Url, string LocalPath, bool SimulateBrowserHeaders = false) {
        Logger.Info($"通过网络请求直接下载小文件：{Url} → {LocalPath}");
        int RetryCount = 0;
        // 原 VB 使用 GoTo Retry 从 Catch 跳回 Try 前，C# 不允许跨 try 边界跳转，改为循环重试
        while (true) {
            try {
                FileUtils.Write(LocalPath, SendRequest(Url, HttpMethod.Get, SimulateBrowserHeaders: SimulateBrowserHeaders));
                break;
            } catch (Exception ex) {
                FileUtils.Delete(LocalPath);
                ex.ThrowIfCanceled();
                if (RetryCount < 2) {
                    RetryCount += 1;
                    continue;
                }
                throw new HttpRequestException($"通过网络请求直接下载小文件失败（{LocalPath}）", ex);
            }
        }
    }

    /// <summary>
    /// 通过多线程下载引擎下载文件。
    /// </summary>
    public static void NetDownloadByLoader(string Url, string LocalPath, LoaderBase LoaderToSyncProgress = null, FileChecker Check = null, bool SimulateBrowserHeaders = false) {
        NetDownloadByLoader(new[] { Url }, LocalPath, LoaderToSyncProgress, Check, SimulateBrowserHeaders);
    }
    /// <summary>
    /// 通过多线程下载引擎下载文件。
    /// </summary>
    /// <param name="Urls">文件的 Url 列表。</param>
    /// <param name="LocalPath">下载的本地地址。</param>
    public static void NetDownloadByLoader(IEnumerable<string> Urls, string LocalPath, LoaderBase LoaderToSyncProgress = null, FileChecker Check = null, bool SimulateBrowserHeaders = false) {
        LoaderDownload NewTask = new LoaderDownload($"文件下载 {PclCore.GetUuid()}#", new List<NetFile> { new NetFile(Urls, LocalPath, Check, SimulateBrowserHeaders) });
        try {
            NewTask.WaitForExit(LoaderToSyncProgress: LoaderToSyncProgress);
        } catch (Exception ex) {
            throw new WebException($"多线程直接下载文件失败（第一下载源：" + Urls.First() + "）", ex);
        } finally {
            NewTask.Cancel();
        }
    }

    #endregion

    #region 基础请求引擎

    // 基础请求函数
    private static byte[] SendRequest(string Url, HttpMethod Method,
            object Content = null, Dictionary<string, string> Headers = null,
            bool SimulateBrowserHeaders = false, int Timeout = 25000, Encoding Encoding = null) {
        if (PclCore.RunInUi() && !Url.Contains("//127.")) throw new Exception("在 UI 线程执行了网络请求");
        HttpRequestMessage Request = null; CancellationTokenSource CancelToken = null; HttpResponseMessage Response = null;
        try {
            // 构建 RequestClient
            Directory.CreateDirectory(PclCore.GetPathTemp() + "Cache\\Http\\");
            lock (RequestClientLock) {
                if (RequestClient is null) { // 延迟初始化，以避免在程序启动前加载 CacheCow 导致 DLL 加载失败
                    RequestClient = CacheCow.Client.ClientExtensions.CreateClient(new CacheCow.Client.FileCacheStore.FileStore(PclCore.GetPathTemp() + "Cache/Http/"), new HttpClientHandler {
                        AutomaticDecompression = DecompressionMethods.Deflate | DecompressionMethods.GZip,
                        UseCookies = false // 不设为 False 就不能从 Header 手动传入 Cookies
                    });
                }
            }
            // 构建请求
            Url = PclUiBridge.SecretCdnSign(Url);
            Request = new HttpRequestMessage(Method, Url);
            if (Content is not null && Request.Method != HttpMethod.Get && Request.Method != HttpMethod.Head) { // 写入 Content
                if (Content is HttpContent) {
                    Request.Content = ((HttpContent)Content).Clone();
                } else if (Content is byte[]) {
                    Request.Content = new ByteArrayContent((byte[])Content);
                } else {
                    Request.Content = new ByteArrayContent((Encoding ?? System.Text.Encoding.UTF8).GetBytes(Content.ToString()));
                }
            }
            foreach (KeyValuePair<string, string> Header in Headers ?? new Dictionary<string, string>()) { // 写入 Headers
                if (Header.Key.Lower() == "content-type") {
                    Request.Content?.Headers.TryAddWithoutValidation(Header.Key, Header.Value);
                } else {
                    Request.Headers.TryAddWithoutValidation(Header.Key, Header.Value);
                }
            }
            PclUiBridge.SecretHeadersSign(Url, Request, SimulateBrowserHeaders);
            // 发送请求
            // CacheCow 读取缓存时若文件被占用，会调用不存在的 System.Net.Http.Formatting.resources，抛出异常（#8150）
            Retrier.Attempt(delay: _ => TimeSpan.FromMilliseconds(200), isRetryAllowed: ex => ex is FileNotFoundException || ex is IOException, action:
            Attempt => {
                CancelToken?.Dispose(); CancelToken = new CancellationTokenSource(Timeout);
                HttpRequestMessage ClonedRequest = Request.Clone(); // HttpRequestMessage 只能发送一次，重试时需要克隆一个新的
                try {
                    Response = RequestClient.SendAsync(ClonedRequest, HttpCompletionOption.ResponseHeadersRead, CancelToken.Token).GetResultWithTimeout(CancelToken, Timeout);
                } finally {
                    ClonedRequest.Dispose();
                }
            });
            // 读取响应
            byte[] ResponseBytes;
            CancelToken.Dispose(); CancelToken = new CancellationTokenSource(Timeout);
            using (Stream ResponseStream = Response.Content.ReadAsStreamAsync().GetResultWithTimeout(CancelToken, Timeout)) {
                using (MemoryStream Stream = new MemoryStream()) {
                    ResponseStream.CopyToAsync(Stream, 81920, CancelToken.Token).GetResultWithTimeout(CancelToken, Timeout);
                    ResponseBytes = Stream.ToArray();
                }
            }
            if (!Response.IsSuccessStatusCode) {
                string ResponseMessage = ResponseBytes.GetString(Encoding);
                throw new HttpRequestCodeException(
                    $"错误码 {Response.StatusCode} ({(int)Response.StatusCode})，{Method}，{Url}" +
                    (string.IsNullOrEmpty(ResponseMessage) ? "" : Environment.NewLine + ResponseMessage), Response.StatusCode, ResponseMessage);
            }
            return ResponseBytes;
        } catch (HttpRequestException) {
            throw;
        } catch (TimeoutException) {
            throw new WebException($"连接服务器超时，请稍后再试，或使用 VPN 改善网络环境（{Method}, {Url}）", WebExceptionStatus.Timeout);
        } catch (Exception ex) {
            if (ex is not TaskCanceledException) ex.ThrowIfCanceled(); // GetResultWithTimeout 会抛出 TaskCanceledException
            if (ex.IsBadNetwork()) {
                throw new WebException($"网络请求失败，请稍后再试，或使用 VPN 改善网络环境（{Method}, {Url}）", WebExceptionStatus.Timeout);
            } else {
                throw new Exception($"网络请求出现意外异常（{Method}, {Url}）", ex);
            }
        } finally {
            Request?.Dispose();
            CancelToken?.Dispose();
            Response?.Dispose();
        }
    }
    private static HttpClient RequestClient = null;
    private static object RequestClientLock = new object();

    #endregion

    #region 多线程下载引擎

    // VB 中为模块级 Private 字段，模块嵌套类型（NetFile 等）可直接访问；
    // C# 顶层类型无法访问 NetCore 的 private 成员，故改为 internal。
    internal static HttpClient ThreadClient = new HttpClient(new HttpClientHandler {
        AutomaticDecompression = DecompressionMethods.Deflate | DecompressionMethods.GZip
    });

    /// <summary>
    /// 最大线程数。
    /// </summary>
    public static int NetTaskThreadLimit {
        get {
            return PclCore.GetSetting<int>("ToolDownloadThread") + 1;
        }
    }
    /// <summary>
    /// 速度下限。
    /// </summary>
    public static long NetTaskSpeedLimitLow = 256 * 1024L; // 256K/s
    /// <summary>
    /// 速度上限。若无限制则为 -1。
    /// </summary>
    public static long NetTaskSpeedLimitHigh {
        get {
            if (_NetTaskSpeedLimitHigh is null) {
                int Setting = PclCore.GetSetting<int>("ToolDownloadSpeed");
                if (Setting <= 14) {
                    _NetTaskSpeedLimitHigh = Convert.ToInt64((Setting + 1) * 0.1 * 1024 * 1024L);
                } else if (Setting <= 31) {
                    _NetTaskSpeedLimitHigh = Convert.ToInt64((Setting - 11) * 0.5 * 1024 * 1024L);
                } else if (Setting <= 41) {
                    _NetTaskSpeedLimitHigh = Convert.ToInt64((Setting - 21) * 1024 * 1024L);
                } else {
                    _NetTaskSpeedLimitHigh = -1;
                }
            }
            return _NetTaskSpeedLimitHigh.Value;
        }
    }
    private static long? _NetTaskSpeedLimitHigh = null;
    public static void UpdateNetTaskSpeedLimitHigh(int Value) {
        _NetTaskSpeedLimitHigh = null;
    }
    /// <summary>
    /// 基于限速，当前可以下载的剩余量。
    /// </summary>
    public static long NetTaskSpeedLimitLeft = -1;
    /// <summary>
    /// 正在运行中的线程数。
    /// </summary>
    public static int NetTaskThreadCount = 0;

    public static readonly NetManagerClass NetManager = new NetManagerClass();

    /// <summary>
    /// 是否有正在进行中、需要在任务管理页面显示的下载任务？
    /// </summary>
    public static bool HasDownloadingTask(bool IgnoreCustomDownload = false) {
        return PclUiBridge.LoaderTaskbar().Any(
        Task => Task.Show && Task.State == LoadState.Loading &&
                       (!IgnoreCustomDownload || !Task.Name.ToString().Contains("自定义下载")));
    }

    /// <summary>
    /// VB 中为模块内 Private 类，仅模块内使用。
    /// </summary>
    internal class RangeNotSupportedException : HttpRequestException {
        public RangeNotSupportedException(string Message) : base(Message) {
        }
    }

    #endregion

    #region 端口

    /// <summary>
    /// 随机获取单个可用的端口。
    /// </summary>
    public static int FindFreePort() {
        TcpListener Listener = new TcpListener(IPAddress.Loopback, 0);
        Listener.Start();
        int port = ((IPEndPoint)Listener.LocalEndpoint).Port;
        Listener.Stop();
        return port;
    }

    /// <summary>
    /// 获取当前已被占用的端口列表。
    /// </summary>
    public static List<int> GetUsedPorts() {
        System.Net.NetworkInformation.IPGlobalProperties IPProperties = System.Net.NetworkInformation.IPGlobalProperties.GetIPGlobalProperties();
        List<int> UsedPorts = new List<int>();
        UsedPorts.AddRange(IPProperties.GetActiveTcpListeners().Select(ep => ep.Port));
        UsedPorts.AddRange(IPProperties.GetActiveUdpListeners().Select(ep => ep.Port));
        UsedPorts.AddRange(IPProperties.GetActiveTcpConnections().Select(conn => conn.LocalEndPoint.Port));
        return UsedPorts.Distinct().ToList();
    }

    /// <summary>
    /// 寻找数个连续编号的可用端口。
    /// </summary>
    public static List<int> FindFreePorts(int ConsecutiveCount, params int[] ExtraBlackLists) {
        IEnumerable<int> UsedPorts = GetUsedPorts().Concat(ExtraBlackLists);
        for (int port = 12000 + PclCore.RandomInteger(0, 1000); port <= 65000 - ConsecutiveCount; port++) {
            IEnumerable<int> Range = Enumerable.Range(port, ConsecutiveCount);
            if (!Range.Any(p => UsedPorts.Contains(p))) return Range.ToList();
        }
        throw new Exception("未能找到可用的端口！");
    }

    #endregion

    /// <summary>
    /// 测试 Ping。失败则返回 -1。
    /// </summary>
    public static int Ping(string Ip, int Timeout = 10000, bool MakeLog = true) {
        System.Net.NetworkInformation.PingReply PingResult;
        try {
            PingResult = new System.Net.NetworkInformation.Ping().Send(Ip);
        } catch (Exception ex) {
            if (MakeLog) Logger.Info($"Ping {Ip} 失败：{ex.Message}");
            return -1;
        }
        if (PingResult.Status == System.Net.NetworkInformation.IPStatus.Success) {
            if (MakeLog) Logger.Info($"Ping {Ip} 结束：{PingResult.RoundtripTime}ms");
            return (int)PingResult.RoundtripTime;
        } else {
            if (MakeLog) Logger.Info($"Ping {Ip} 失败");
            return -1;
        }
    }

    /// <summary>
    /// 设置是否应当在正版登录时校验 SSL 证书。
    /// </summary>
    public static void ShouldValidateSslCertificateOnLogin(bool Enabled) {
        ServicePointManager.ServerCertificateValidationCallback =
        (Sender, Certificate, Chain, Failure) => {
            HttpWebRequest Request = Sender as HttpWebRequest;
            if (Failure == System.Net.Security.SslPolicyErrors.None) return true; // 已通过验证
            // 基于 #3018 和 #5879，只在访问正版登录 API 时跳过证书验证
            Logger.Warn($"未通过 SSL 证书验证（{Failure}），提供的证书为 {Certificate?.Subject}，URL：{Request?.Address}");
            if (Request is null) {
                return !Enabled;
            } else if (Request.Address.Host.Contains("xboxlive") || Request.Address.Host.Contains("minecraftservices")) {
                return !Enabled; // 根据设置决定是否忽略错误
            } else {
                return false;
            }
        };
    }

}

/// <summary>
/// 下载源。
/// </summary>
public class NetSource {
    public int Id;
    public string Url;
    public int FailCount;
    public Exception Ex;
    /// <summary>
    /// 若该下载源正在进行强制单线程下载，标记这个唯一的线程。
    /// </summary>
    public NetThread SingleThread;
    public bool IsFailed;
    public override string ToString() {
        return Url;
    }
}
/// <summary>
/// 下载进度标示。
/// </summary>
public enum NetState {
    /// <summary>
    /// 尚未进行已存在检查。
    /// </summary>
    WaitingToCheck = -1,
    /// <summary>
    /// 尚未开始。
    /// </summary>
    WaitingToDownload = 0,
    /// <summary>
    /// 正在连接，尚未获取文件大小。
    /// </summary>
    Connecting = 1,
    /// <summary>
    /// 已连接，正在尝试接收首个下载数据包。
    /// </summary>
    Reading = 2,
    /// <summary>
    /// 正在下载。
    /// </summary>
    Downloading = 3,
    /// <summary>
    /// 正在合并文件。
    /// </summary>
    Merging = 4,
    /// <summary>
    /// 已完成。
    /// </summary>
    Finished = 5,
    /// <summary>
    /// 已失败或中断。
    /// </summary>
    Canceled = 6,
}
/// <summary>
/// 预下载检查行为。
/// </summary>
public enum NetPreDownloadBehaviour {
    /// <summary>
    /// 当文件已存在时，显示提示以提醒用户是否继续下载。
    /// </summary>
    HintWhileExists,
    /// <summary>
    /// 当文件已存在或正在下载时，直接退出下载函数执行，不对用户进行提示。
    /// </summary>
    ExitWhileExistsOrDownloading,
    /// <summary>
    /// 不进行已存在检查。
    /// </summary>
    IgnoreCheck,
}

/// <summary>
/// 下载线程。
/// </summary>
public class NetThread : IEnumerable<NetThread>, IEquatable<NetThread> {

    /// <summary>
    /// 对应的下载任务。
    /// </summary>
    public NetFile Task;
    /// <summary>
    /// 对应的线程。
    /// </summary>
    public Thread Thread;
    /// <summary>
    /// 链表中的下一个线程。
    /// </summary>
    public NetThread NextThread;
    // VB 原为 Private ReadOnly Iterator Property [Next]，GetEnumerator 依赖其实现
    public IEnumerator<NetThread> GetEnumerator() {
        NetThread CurrentChain = this;
        while (CurrentChain is not null) {
            yield return CurrentChain;
            CurrentChain = CurrentChain.NextThread;
        }
    }
    IEnumerator IEnumerable.GetEnumerator() {
        return GetEnumerator();
    }

    /// <summary>
    /// 分配给任务中每个线程（无论其是否失败）的编号。
    /// </summary>
    public int Uuid;
    /// <summary>
    /// 是否为第一个线程。
    /// </summary>
    public bool IsFirstThread {
        get {
            return DownloadStart == 0 && Task.FileSize == -2;
        }
    }
    /// <summary>
    /// 该线程的缓存文件。
    /// </summary>
    public string Temp;

    /// <summary>
    /// 线程下载起始位置。
    /// </summary>
    public long DownloadStart;
    /// <summary>
    /// 线程下载结束位置。
    /// </summary>
    public long DownloadEnd {
        get {
            lock (Task.LockChain) {
                if (NextThread is null) {
                    if (Task.IsUnknownSize) {
                        return 1000 * 1000 * 1000 * 1000L; // 约 1T
                    } else {
                        return Task.FileSize - 1;
                    }
                } else {
                    return NextThread.DownloadStart - 1;
                }
            }
        }
    }
    /// <summary>
    /// 线程未下载的文件大小。
    /// </summary>
    public long DownloadUndone {
        get {
            return DownloadEnd - (DownloadStart + DownloadDone) + 1;
        }
    }
    /// <summary>
    /// 线程已下载的文件大小。
    /// </summary>
    public long DownloadDone = 0;

    /// <summary>
    /// 上次记速时的时间。
    /// </summary>
    private long SpeedLastTime = PclCore.GetTimeMs();
    /// <summary>
    /// 上次记速时的已下载大小。
    /// </summary>
    private long SpeedLastDone = 0;
    /// <summary>
    /// 当前的下载速度，单位为 Byte / 秒。
    /// </summary>
    public long Speed {
        get {
            if (PclCore.GetTimeMs() - SpeedLastTime > 200) {
                long DeltaTime = PclCore.GetTimeMs() - SpeedLastTime;
                _Speed = Convert.ToInt64((DownloadDone - SpeedLastDone) / (DeltaTime / 1000d));
                SpeedLastDone = DownloadDone;
                SpeedLastTime += DeltaTime;
            }
            return _Speed;
        }
    }
    private long _Speed = 0;

    /// <summary>
    /// 线程初始化时的时间。
    /// </summary>
    public long InitTime = PclCore.GetTimeMs();
    /// <summary>
    /// 上次接受到有效数据的时间，-1 表示尚未有有效数据。
    /// </summary>
    public long LastReceiveTime = -1;

    /// <summary>
    /// 当前线程的状态。
    /// </summary>
    public NetState State = NetState.WaitingToDownload;
    /// <summary>
    /// 是否已经结束。
    /// </summary>
    public bool IsEnded {
        get {
            return State == NetState.Finished || State == NetState.Canceled;
        }
    }

    /// <summary>
    /// 当前选取的是哪一个 Url。
    /// </summary>
    public NetSource Source;

    // 允许进行 UUID 比较
    public bool Equals(NetThread other) {
        return other is not null && Uuid == other.Uuid;
    }
    public override bool Equals(object obj) {
        return Equals(obj as NetThread);
    }
    public override int GetHashCode() { // 新增：与 Equals 保持一致（VB 未重写）
        return Uuid;
    }
    public static bool operator ==(NetThread left, NetThread right) {
        return EqualityComparer<NetThread>.Default.Equals(left, right);
    }
    public static bool operator !=(NetThread left, NetThread right) {
        return !(left == right);
    }
}
/// <summary>
/// 下载单个文件。
/// </summary>
public class NetFile {

    #region 属性

    /// <summary>
    /// 所属的文件列表任务。
    /// </summary>
    public ConcurrentList<LoaderDownload> Tasks = new ConcurrentList<LoaderDownload>();
    /// <summary>
    /// 所有下载源。
    /// </summary>
    public List<NetSource> Sources;
    /// <summary>
    /// 用于在第一个线程出错时切换下载源。
    /// </summary>
    private int FirstThreadSource = 0;
    /// <summary>
    /// 所有已经被标记为失败的，但未完整尝试过的，不允许断点续传的下载源。
    /// </summary>
    public ConcurrentList<NetSource> SourcesOnce = new ConcurrentList<NetSource>();
    /// <summary>
    /// 仅当合并失败或首次下载失败时，会将所有下载源重新标记为不允许断点续传的下载源，逐个重新尝试下载。
    /// 这一策略可以兼容多个下载源中的一部分返回错误的文件的情况，以及部分在多线程下载时会抽风的源。
    /// </summary>
    private bool Retried = false;
    /// <summary>
    /// 获取从某个源开始，第一个可用的源。
    /// </summary>
    private NetSource GetSource(int Id = 0) {
        if (Id >= Sources.Count || Id < 0) Id = 0;
        lock (LockSource) {
            if (HasAvailableSource(false)) {
                // 存在多线程可用源
                NetSource CurrentSource = Sources[Id];
                while (CurrentSource.IsFailed) {
                    Id += 1;
                    if (Id >= Sources.Count) Id = 0;
                    CurrentSource = Sources[Id];
                }
                return CurrentSource;
            } else if (SourcesOnce.Any()) {
                // 仅存在单线程可用源
                return SourcesOnce.First();
            } else {
                // 没有可用源
                return null;
            }
        }
    }
    /// <summary>
    /// 是否存在可用源。
    /// </summary>
    public bool HasAvailableSource(bool AllowOnceSource = true) {
        lock (LockSource) {
            if (Sources.Any(s => !s.IsFailed)) return true; // 存在多线程可用源
            if (AllowOnceSource && SourcesOnce.Any()) return true; // 存在单线程可用源
        }
        return false;
    }

    /// <summary>
    /// 存储在本地的带文件名的地址。
    /// </summary>
    public string LocalPath = null;
    /// <summary>
    /// 存储在本地的文件名。
    /// </summary>
    public string LocalName = null;

    /// <summary>
    /// 当前的下载状态。
    /// </summary>
    public NetState State = NetState.WaitingToCheck;
    /// <summary>
    /// 导致下载失败的原因。
    /// </summary>
    public List<Exception> Ex = new List<Exception>();

    /// <summary>
    /// 作为文件组成部分的线程链表。
    /// 如果没有线程，可以为 Nothing。
    /// </summary>
    public NetThread Threads;

    /// <summary>
    /// 文件的总大小。若为 -2 则为未获取，若为 -1 则为无法获取准确大小。
    /// </summary>
    public long FileSize = -2;
    /// <summary>
    /// 该文件是否无法获取准确大小。
    /// </summary>
    public bool IsUnknownSize = false;
    /// <summary>
    /// 该文件是否不需要分割。
    /// </summary>
    public bool IsNoSplit {
        get {
            return IsUnknownSize || FileSize < 1024 * 1024; // 小于 1MB 的文件不分割
        }
    }
    /// <summary>
    /// 为不需要分割的小文件在内存中提供临时存储。
    /// </summary>
    private MemoryStream Cache;

    /// <summary>
    /// 文件的已下载大小。
    /// </summary>
    public long DownloadDone = 0;
    /// <summary>
    /// 文件的校验规则。
    /// </summary>
    public FileChecker Check;
    /// <summary>
    /// 是否模拟浏览器的 UserAgent 和 Referer。
    /// </summary>
    public bool SimulateBrowserHeaders;

    /// <summary>
    /// 上次记速时的时间。
    /// </summary>
    private long SpeedLastTime = PclCore.GetTimeMs();
    /// <summary>
    /// 上次记速时的已下载大小。
    /// </summary>
    private long SpeedLastDone = 0;
    /// <summary>
    /// 当前的下载速度，单位为 Byte / 秒。
    /// </summary>
    public long Speed {
        get {
            if (PclCore.GetTimeMs() - SpeedLastTime > 200) {
                long DeltaTime = PclCore.GetTimeMs() - SpeedLastTime;
                _Speed = Convert.ToInt64((DownloadDone - SpeedLastDone) / (DeltaTime / 1000d));
                SpeedLastDone = DownloadDone;
                SpeedLastTime += DeltaTime;
            }
            return _Speed;
        }
    }
    private long _Speed = 0;

    /// <summary>
    /// 该文件是否由本地文件直接复制完成。
    /// </summary>
    public bool IsCopy = false;
    /// <summary>
    /// 本文件的显示进度。
    /// </summary>
    public double Progress {
        get {
            switch (State) {
                case NetState.WaitingToCheck:
                    return 0;
                case NetState.WaitingToDownload:
                    return 0.01;
                case NetState.Connecting:
                    return 0.02;
                case NetState.Reading:
                    return 0.04;
                case NetState.Downloading:
                    // 正在下载中，对应 5% ~ 98%
                    double OriginalProgress = IsUnknownSize ? 0.5 : DownloadDone / (double)Math.Max(FileSize, 1);
                    OriginalProgress = 1 - Math.Pow(1 - OriginalProgress, 0.9);
                    return OriginalProgress * 0.93 + 0.05;
                case NetState.Merging:
                    return 0.99;
                case NetState.Finished:
                case NetState.Canceled:
                    return 1;
                default:
                    throw new ArgumentOutOfRangeException("文件状态未知：" + State);
            }
        }
    }

    /// <summary>
    /// 各个线程建立连接成功的总次数。
    /// </summary>
    private int ConnectCount = 0;
    /// <summary>
    /// 各个线程建立连接成功的总时间。
    /// </summary>
    private long ConnectTime = 0;
    /// <summary>
    /// 各个线程建立连接成功的平均时间，单位为毫秒，-1 代表尚未有成功连接。
    /// </summary>
    private int ConnectAverage {
        get {
            int CurrentConnectCount = ConnectCount; // 防止竞态条件
            return CurrentConnectCount == 0 ? -1 : Convert.ToInt32(ConnectTime / (double)CurrentConnectCount);
        }
    }

    private const long FilePieceLimit = 256 * 1024;
    public readonly object LockState = new object();
    public readonly object LockChain = new object();
    public readonly object LockSource = new object();

    public readonly int Uuid = PclCore.GetUuid();
    public override bool Equals(object obj) {
        NetFile file = obj as NetFile;
        return file is not null && Uuid == file.Uuid;
    }
    public override int GetHashCode() { // 新增：与 Equals 保持一致（VB 未重写）
        return Uuid;
    }

    #endregion

    /// <summary>
    /// 新建一个需要下载的文件。
    /// </summary>
    /// <param name="LocalPath">包含文件名的本地地址。</param>
    public NetFile(IEnumerable<string> Urls, string LocalPath, FileChecker Checker = null, bool SimulateBrowserHeaders = false) {
        List<NetSource> Sources = new List<NetSource>();
        int Count = 0;
        Urls = Urls.Distinct().ToArray();
        foreach (string Source in Urls) {
            Sources.Add(new NetSource { FailCount = 0, Url = PclUiBridge.SecretCdnSign(Source.ReplaceLineEndings("").Trim()), Id = Count, IsFailed = false, Ex = null });
            Count += 1;
        }
        this.Sources = Sources;
        this.LocalPath = LocalPath;
        Check = Checker;
        this.SimulateBrowserHeaders = SimulateBrowserHeaders;
        LocalName = PathUtils.GetLastPart(LocalPath);
    }

    /// <summary>
    /// 尝试开始一个新的下载线程。
    /// 如果失败，返回 Nothing。
    /// </summary>
    public NetThread TryBeginThread() {
        try {

            long StartPosition; NetSource StartSource = null;
            Thread Th; NetThread ThreadInfo;
            // 条件检测
            lock (LockSource) {
                if (NetCore.NetTaskThreadCount >= NetCore.NetTaskThreadLimit || !HasAvailableSource() ||
                    (IsNoSplit && Threads is not null && Threads.State != NetState.Canceled)) return null;
                if (State >= NetState.Merging || State == NetState.WaitingToCheck) return null;
                lock (LockState) {
                    if (State < NetState.Connecting) State = NetState.Connecting;
                }
                // 初始化参数
                // 获取线程起点与下载源
                // 不分割
                // C# 标签作用域为所在块，VB 的 GoTo StartSingleThreadDownload 跳入 If 块内，改为等价的条件分支
                if (IsNoSplit) {
                    Reset();
                    State = NetState.Reading;
                } else if (!HasAvailableSource(false)) {
                    // 只剩下单线程可用点
                    if (SourcesOnce.First().SingleThread is not null && SourcesOnce.First().SingleThread.State != NetState.Canceled) return null;
                    Reset();
                    State = NetState.Reading;
                }
            }
            lock (LockChain) {
                // 首个开始点
                if (Threads is null) {
                    StartPosition = 0;
                    StartSource = GetSource(FirstThreadSource);
                    FirstThreadSource = StartSource.Id + 1;
                    goto StartThread;
                }
                // 寻找失败点
                foreach (NetThread Thread in Threads) {
                    if (Thread.State == NetState.Canceled && Thread.DownloadUndone > 0) {
                        StartPosition = Thread.DownloadStart + Thread.DownloadDone;
                        StartSource = GetSource(Thread.Source.Id + 1);
                        goto StartThread;
                    }
                }
                // 是否禁用多线程，以及规定碎片大小
                NetSource Source = GetSource();
                if (Source is null) return null;
                string TargetUrl = Source.Url;
                if (TargetUrl.Contains("pcl2-server") || TargetUrl.Contains("meloong.com") || TargetUrl.Contains("bmclapi") || TargetUrl.Contains("github.com") ||
                   TargetUrl.Contains("optifine.net") || TargetUrl.Contains("momot.rs")) return null;
                // 寻找最大碎片
                // FUTURE: 下载引擎重做，计算下载源平均链接时间和线程下载速度，按最高时间节省来开启多线程
                NetThread FilePieceMax = Threads;
                foreach (NetThread Thread in Threads) {
                    if (Thread.DownloadUndone > FilePieceMax.DownloadUndone) FilePieceMax = Thread;
                }
                if (FilePieceMax is null || FilePieceMax.DownloadUndone < FilePieceLimit) return null;
                StartPosition = Convert.ToInt64(FilePieceMax.DownloadEnd - FilePieceMax.DownloadUndone * 0.4);
                StartSource = GetSource();

                // 开始线程
            StartThread:
                if ((StartPosition > FileSize && FileSize >= 0 && !IsUnknownSize) || StartPosition < 0 || StartSource is null) return null;
                // 构建线程
                int ThreadUuid = PclCore.GetUuid();
                if (!Tasks.Any()) return null; // 由于中断，已没有可用任务
                // VB 经宽松委托转换将 Thread 方法转为 ParameterizedThreadStart，C# 需显式转换参数类型
                Th = new Thread(o => Thread((NetThread)o)) { Name = $"下载 {Tasks[0].Uuid}/{Uuid}/{ThreadUuid}#", Priority = ThreadPriority.BelowNormal };
                ThreadInfo = new NetThread { Uuid = ThreadUuid, DownloadStart = StartPosition, Thread = Th, Source = StartSource, Task = this, State = NetState.WaitingToDownload };
                // 链表处理
                if (ThreadInfo.IsFirstThread || Threads is null) {
                    Threads = ThreadInfo;
                } else {
                    NetThread CurrentChain = Threads;
                    while (CurrentChain.DownloadEnd <= StartPosition) {
                        CurrentChain = CurrentChain.NextThread;
                    }
                    ThreadInfo.NextThread = CurrentChain.NextThread;
                    CurrentChain.NextThread = ThreadInfo;
                }

            }
            // 开始线程
            Interlocked.Increment(ref NetCore.NetTaskThreadCount);
            lock (LockSource) {
                if (!HasAvailableSource(false)) SourcesOnce.First().SingleThread = ThreadInfo;
            }
            Th.Start(ThreadInfo);
            return ThreadInfo;

        } catch (Exception ex) {
            Fail(new Exception($"尝试开始下载线程失败（{LocalName}）", ex));
            return null;
        }
    }
    /// <summary>
    /// 每个下载线程执行的代码。
    /// </summary>
    private void Thread(NetThread Th) {
        Logger.Log($"{LocalName}：开始，起始点 {Th.DownloadStart}，{Th.Source.Url}", Th.DownloadStart == 0 ? LogLevel.Info : LogLevel.Trace);
        Stream ResultStream = null; HttpRequestMessage HttpRequest = null;
        HttpResponseMessage Response = null; Stream ResponseStream = null;
        CancellationTokenSource CancelToken = null;
        // 部分下载源真的特别慢，并且只需要一个请求，例如 Ping 为 20s，如果增长太慢，就会造成类似 2.5s 5s 7.5s 10s 12.5s... 的极大延迟
        // 延迟过长会导致某些特别慢的链接迟迟不被掐死
        int Timeout = Math.Min(Math.Max(ConnectAverage, 15000) * (1 + Th.Source.FailCount), 30000);
        Th.State = NetState.Connecting;
        try {
            int HttpDataCount = 0;
            long ContentLength = 0; // VB 声明位置靠后，但 C# 中 goto SourceBreak 会跳过声明导致未赋值，故提前声明（VB 跳过时值为 0，语义一致）
            // C# 标签作用域为所在块，VB 的 GoTo NotSupportRange 跨 if 分支跳转，改为标志位重构
            bool ForceNotSupportRange = false;
            if (SourcesOnce.Contains(Th.Source) && Th != Th.Source.SingleThread) goto SourceBreak;
            HttpRequest = new HttpRequestMessage(HttpMethod.Get, Th.Source.Url);
            PclUiBridge.SecretHeadersSign(Th.Source.Url, HttpRequest, SimulateBrowserHeaders);
            CancelToken = new CancellationTokenSource(Timeout);
            if (!Th.IsFirstThread) HttpRequest.Headers.Range = new RangeHeaderValue(Th.DownloadStart, null);
            Response = NetCore.ThreadClient.SendAsync(HttpRequest, HttpCompletionOption.ResponseHeadersRead, CancelToken.Token).GetResultWithTimeout(CancelToken, Timeout);
            if (!Response.IsSuccessStatusCode) { // 状态码检查
                throw new HttpRequestCodeException($"错误码 {Response.StatusCode} ({(int)Response.StatusCode})，{Th.Source.Url}", Response.StatusCode, null);
            }
            if (State == NetState.Canceled) goto SourceBreak; // 快速中断
            if (Response.RequestMessage.RequestUri.ToString() != Th.Source.Url) Logger.Trace(() => $"{LocalName}：重定向至 {Response.RequestMessage.RequestUri}");
            // 文件大小校验
            ContentLength = Response.Content.Headers.ContentLength.GetValueOrDefault(-1);
            if (ContentLength < 0) {
                if (FileSize > 1) {
                    if (Th.DownloadStart == 0) {
                        Logger.Info($"{LocalName}：文件大小未知，但已从其他下载源获取，不作处理");
                    } else {
                        Logger.Info($"{LocalName}：ContentLength 返回了 {ContentLength}，无法确定是否支持分段下载，视作不支持");
                        ForceNotSupportRange = true;
                    }
                } else {
                    FileSize = -1; IsUnknownSize = true;
                    Logger.Info($"{LocalName}：文件大小未知");
                }
            } else if (Th.IsFirstThread) {
                if (Check is not null) {
                    if (ContentLength < Check.MinSize && Check.MinSize > 0) {
                        throw new Exception($"文件大小不足，获取结果为 {ContentLength}，要求至少为 {Check.MinSize}。");
                    }
                    if (ContentLength != Check.ActualSize && Check.ActualSize > 0) {
                        throw new Exception($"文件大小不一致，获取结果为 {ContentLength}，要求必须为 {Check.ActualSize}。");
                    }
                }
                FileSize = ContentLength; IsUnknownSize = false;
                Logger.Info($"{LocalName}：文件大小 {ContentLength}（{StringUtils.FormatByteSize(ContentLength)}）");
                // 若文件大小大于 50 M，进行剩余磁盘空间校验
                if (ContentLength > 50 * 1024 * 1024) {
                    foreach (DriveInfo Drive in DriveInfo.GetDrives()) {
                        string DriveName = Drive.Name.First().ToString();
                        double RequiredSpace = (PclCore.GetPathTemp().StartsWithF(DriveName) ? ContentLength * 1.1 : 0) +
                                                (LocalPath.StartsWithF(DriveName) ? ContentLength + 5 * 1024 * 1024 : 0);
                        if (RequiredSpace > 0 && Drive.TotalFreeSpace < RequiredSpace) {
                            throw new Exception(DriveName + " 盘空间不足，无法进行下载。" + Environment.NewLine + "需要至少 " + StringUtils.FormatByteSize(Convert.ToInt64(RequiredSpace)) + " 空间，但当前仅剩余 " + StringUtils.FormatByteSize(Drive.TotalFreeSpace) + "。" +
                                                (PclCore.GetPathTemp().StartsWithF(DriveName) ? Environment.NewLine + Environment.NewLine + "下载时需要与文件同等大小的空间存放缓存，你可以在设置中调整缓存文件夹的位置。" : ""));
                        }
                    }
                }
            } else if (FileSize < 0) {
                throw new Exception("非首线程运行时，尚未获取文件大小");
            } else if (ForceNotSupportRange || (Th.DownloadStart > 0 && ContentLength >= FileSize)) {
                lock (LockSource) {
                    if (SourcesOnce.Contains(Th.Source)) goto SourceBreak;
                }
                throw new NetCore.RangeNotSupportedException($"该下载源不支持分段下载：Range 起始于 {Th.DownloadStart}，预期 ContentLength 为 {FileSize - Th.DownloadStart}，返回 ContentLength 为 {ContentLength}，总文件大小 {FileSize}");
            } else if (!(FileSize - Th.DownloadStart == ContentLength)) {
                throw new NetCore.RangeNotSupportedException($"获取到的分段大小不一致：Range 起始于 {Th.DownloadStart}，预期 ContentLength 为 {FileSize - Th.DownloadStart}，返回 ContentLength 为 {ContentLength}，总文件大小 {FileSize}");
            }
            // Log($"[Download] {LocalName} {Info.Uuid}#：通过大小检查，文件大小 {FileSize}，起始点 {Info.DownloadStart}，ContentLength {ContentLength}")
            Th.State = NetState.Reading;
            lock (LockState) {
                if (State < NetState.Reading) State = NetState.Reading;
            }
            // 创建缓存文件
            if (IsNoSplit) {
                Th.Temp = null;
                Cache = new MemoryStream();
                ResultStream = Cache;
            } else {
                Th.Temp = $"{PclCore.GetPathTemp()}Download\\{Uuid}_{Th.Uuid}_{PclCore.RandomInteger(0, 999999)}.tmp";
                ResultStream = FileUtils.CreateAsStream(Th.Temp);
            }
            // 开始下载
            CancelToken.Dispose(); CancelToken = new CancellationTokenSource(); // 重置超时，不再让 CancellationToken 在固定时间后触发（#8488）
            ResponseStream = Response.Content.ReadAsStreamAsync().GetAwaiter().GetResult();
            if (PclCore.GetSetting<bool>("SystemDebugDelay")) System.Threading.Thread.Sleep(PclCore.RandomInteger(50, 3000));
            byte[] ResponseBytes = new byte[16385];
            HttpDataCount = ResponseStream.ReadAsync(ResponseBytes, 0, 16384, CancelToken.Token).GetResultWithTimeout(CancelToken, Timeout);
            while ((IsUnknownSize || Th.DownloadUndone > 0) && // 判断是否下载完成
                        HttpDataCount > 0 && !PclCore.IsProgramEnding && State < NetState.Merging && (!Th.Source.IsFailed || Th.Source.SingleThread == Th)) {
                // 限速
                while (NetCore.NetTaskSpeedLimitHigh > 0 && NetCore.NetTaskSpeedLimitLeft <= 0) {
                    System.Threading.Thread.Sleep(16);
                }
                int RealDataCount = IsUnknownSize ? HttpDataCount : (int)Math.Min(HttpDataCount, Th.DownloadUndone);
                if (NetCore.NetTaskSpeedLimitHigh > 0) Interlocked.Add(ref NetCore.NetTaskSpeedLimitLeft, -RealDataCount);
                long DeltaTime = PclCore.GetTimeMs() - Th.LastReceiveTime;
                if (DeltaTime > 100000000) DeltaTime = 1; // 避免时间刻反转导致出现极大值
                if (RealDataCount > 0) {
                    // 有数据
                    if (Th.DownloadDone == 0) {
                        // 第一次接受到数据
                        Th.State = NetState.Downloading;
                        lock (LockState) {
                            if (State < NetState.Downloading) State = NetState.Downloading;
                        }
                        Interlocked.Increment(ref ConnectCount);
                        Interlocked.Add(ref ConnectTime, PclCore.GetTimeMs() - Th.InitTime);
                    }
                    Th.Source.FailCount = 0;
                    foreach (LoaderDownload Task in Tasks) {
                        Task.FailCount = 0;
                    }
                    Interlocked.Add(ref NetCore.NetManager.DownloadDone, RealDataCount);
                    Interlocked.Add(ref DownloadDone, RealDataCount);
                    Th.DownloadDone += RealDataCount;
                    ResultStream.Write(ResponseBytes, 0, RealDataCount);
                    // 已完成
                    if (Th.DownloadUndone == 0 && !IsUnknownSize) break;
                    // 检查速度是否过慢
                    if (Th.LastReceiveTime > 0 && DeltaTime > 5000 && DeltaTime > RealDataCount && // 数据包间隔大于 5s，且速度小于 1K/s
                                Th.Source.SingleThread is null) { // 且并非单线程下载
                        throw new TimeoutException("由于速度过慢断开链接，下载 " + RealDataCount + " B，消耗 " + DeltaTime + " ms。");
                    }
                    Th.LastReceiveTime = PclCore.GetTimeMs();
                } else if (Th.LastReceiveTime > 0 && DeltaTime > Timeout) {
                    // 无数据，且已超时
                    throw new TimeoutException("操作超时，无数据。");
                }
                HttpDataCount = ResponseStream.ReadAsync(ResponseBytes, 0, 16384, CancelToken.Token).GetResultWithTimeout(CancelToken, Timeout);
            }
        SourceBreak:
            if (State == NetState.Canceled || (Th.Source.IsFailed && Th.Source.SingleThread != Th) || (Th.DownloadUndone > 0 && !IsUnknownSize)) {
                // 被外部中断
                Th.State = NetState.Canceled;
                Logger.Info($"{LocalName}：中断");
            } else if (HttpDataCount == 0 && Th.DownloadUndone > 0 && !IsUnknownSize) {
                // 服务器无返回数据
                throw new Exception($"返回的 ContentLength 过多：ContentLength 为 {ContentLength}，但获取到的总数据量仅为 {Th.DownloadDone}（全文件总数据量 {DownloadDone}）");
            } else {
                // 本线程完成
                Th.State = NetState.Finished;
                Logger.Trace(() => $"{LocalName}：完成，已下载 {Th.DownloadDone}（{Th.DownloadStart}~{Th.DownloadEnd}）");
            }
        } catch (Exception ex) {
            if (ex.IsBadNetwork() || ex is TaskCanceledException) {
                Logger.Info(new TimeoutException($"{LocalName}：超时（{Timeout}ms），{ex.Message}"));
            } else {
                Logger.Info(ex, $"{LocalName}：出错");
            }
            SourceFail(Th, ex, false);
        } finally {
            // 释放资源
            HttpRequest?.Dispose();
            Response?.Dispose();
            ResponseStream?.Dispose();
            CancelToken?.Dispose();
            if (!IsNoSplit) ResultStream?.Dispose();
            // 改变计数
            Interlocked.Decrement(ref NetCore.NetTaskThreadCount);
            // 合并
            if (((FileSize >= 0 && DownloadDone >= FileSize) || (FileSize == -1 && DownloadDone > 0)) &&
                State < NetState.Merging && Th.State != NetState.Canceled) Merge(Th);
        }
    }
    private void SourceFail(NetThread Th, Exception ex, bool IsMergeFailure) {
        // 状态变更
        Interlocked.Increment(ref Th.Source.FailCount);
        foreach (LoaderDownload Task in Tasks) {
            Task.FailCount += 1; // VB 原为 Interlocked.Increment(Task.FailCount)，属性无法按引用传递
        }
        Th.State = NetState.Canceled;
        Th.Source.Ex = ex;
        // 根据情况判断，是否在多线程下禁用下载源（连续错误过多，或不支持断点续传）
        bool IsRangeNotSupported = ex is NetCore.RangeNotSupportedException || ex.Message.Contains("(416)");
        if (IsMergeFailure || IsRangeNotSupported ||
           ex.Message.Contains("(502)") || ex.Message.Contains("(404)") ||
           ex.Message.Contains("未能解析") || ex.Message.Contains("无返回数据") || ex.Message.Contains("空间不足") ||
           ((ex.Message.Contains("(403)") || ex.Message.Contains("(429)")) && !Th.Source.Url.Contains("bmclapi")) || // BMCLAPI 的部分源在高频率请求下会返回 403/429，所以不应因此禁用下载源
           (Th.Source.FailCount >= NetCore.NetTaskThreadLimit.Clamp(5, 30) && DownloadDone < 1) || Th.Source.FailCount > NetCore.NetTaskThreadLimit + 2) {
            // 当一个下载源有多个线程在下载时，只选择其中一个线程进行后续处理
            bool IsThisFail = false;
            lock (LockSource) {
                if (!Th.Source.IsFailed || Th.Source.SingleThread == Th) {
                    IsThisFail = true;
                    Th.Source.IsFailed = true;
                }
            }
            // ……后续处理
            if (IsThisFail) {
                Logger.Info($"{LocalName}：下载源被禁用（{Th.Source.Id}，Range 问题：{IsRangeNotSupported}）：{Th.Source.Url}");
                Logger.Warn(ex, $"{(SourcesOnce.FirstOrDefault()?.SingleThread is null ? "" : "单线程")}下载源 {Th.Source.Id} 已被禁用");
                lock (LockSource) {
                    SourcesOnce.Remove(Th.Source);
                }
                if (ex.Message.Contains("空间不足")) {
                    // 硬盘空间不足：强制失败
                    Fail(ex);
                } else if (HasAvailableSource() && !IsMergeFailure) {
                    // 当前源失败，但还有下载源：正常地继续执行
                } else if (!Retried) {
                    // 合并失败或首次下载失败，未重试：将所有下载源重新标记为不允许断点续传的下载源，逐个重新尝试下载
                    // 若所有源均不支持 Range，也会走到这里重试
                    if (!IsRangeNotSupported) Logger.Warn($"{LocalName}：文件下载失败，正在自动重试……");
                    Retried = true;
                    lock (LockSource) {
                        SourcesOnce.Clear();
                        foreach (NetSource Source in Sources) {
                            SourcesOnce.Add(Source);
                            Source.IsFailed = true;
                        }
                    }
                    Reset();
                    lock (LockState) {
                        State = NetState.WaitingToDownload;
                    }
                } else if (HasAvailableSource() && IsMergeFailure) {
                    // 合并失败且单个源失败：继续下一个源
                    Reset();
                    lock (LockState) {
                        State = NetState.WaitingToDownload;
                    }
                } else {
                    // 失败
                    Logger.Info($"{LocalName}：已无可用下载源，下载失败");
                    Exception ExampleEx = null;
                    lock (LockSource) {
                        foreach (NetSource Source in Sources) {
                            Logger.Info($"已禁用的下载源：{Source.Url}");
                            if (Source.Ex is not null) {
                                ExampleEx = Source.Ex;
                                Logger.Warn(Source.Ex, "下载源禁用原因");
                            }
                        }
                    }
                    Fail(ExampleEx);
                }
            }
        }
        // 清理当前已下载的内容
        if (FileSize == -2) Reset();
    }
    private void Reset() {
        FileSize = -2;
        Cache?.Dispose(); Cache = null;
        lock (LockChain) {
            Threads = null;
        }
        Interlocked.Add(ref NetCore.NetManager.DownloadDone, -DownloadDone);
        DownloadDone = 0;
        SpeedLastDone = 0;
    }

    // 最终收束事件

    /// <summary>
    /// 下载完成。合并文件。
    /// </summary>
    private void Merge(NetThread Th) {
        // 状态判断
        lock (LockState) {
            if (State < NetState.Merging) {
                State = NetState.Merging;
            } else {
                return;
            }
        }
        int RetryCount = 0;
        // 原 VB 使用 GoTo Retry 从 Catch 跳回 Try 内部，C# 不允许跨 try 边界跳转，改为循环重试；
        // Finally 中的 Cache?.Dispose() 以 try/finally 包裹保持等效。
        try {
            while (true) {
                try {
                    // 创建文件夹
                    DirectoryUtils.Create(PathUtils.RemoveLastPart(LocalPath));
                    FileUtils.Delete(LocalPath);
                    lock (LockChain) {
                        // 合并文件
                        if (IsNoSplit) {
                            // 仅有一个线程，从内存中输出
                            Logger.Trace(() => $"{LocalName}：下载结束，从内存输出文件");
                            FileUtils.Write(LocalPath, Cache);
                        } else if (Threads.DownloadDone == DownloadDone && Threads.Temp is not null) {
                            // 仅有一个文件，直接复制
                            Logger.Trace(() => $"{LocalName}：下载结束，仅有一个文件，无需合并");
                            FileUtils.Copy(Threads.Temp, LocalPath);
                        } else {
                            // 有多个线程，合并
                            Logger.Trace(() => $"{LocalName}：下载结束，开始合并文件");
                            using (FileStream MergeFile = FileUtils.CreateAsStream(LocalPath)) {
                                using (BinaryWriter AddWriter = new BinaryWriter(MergeFile)) {
                                    foreach (NetThread Thread in Threads) {
                                        if (Thread.DownloadDone == 0 || Thread.Temp is null) continue;
                                        using (Stream fs = FileUtils.ReadAsStream(Thread.Temp)) {
                                            using (BinaryReader TempReader = new BinaryReader(fs)) {
                                                AddWriter.Write(TempReader.ReadBytes((int)Thread.DownloadDone));
                                            }
                                        }
                                    }
                                }
                            }
                        }
                        // 检查文件
                        string CheckResult = Check?.Check(LocalPath);
                        if (CheckResult is null && !IsUnknownSize && Check is not null) {
                            if (Check.ActualSize == -1) {
                                CheckResult = new FileChecker { ActualSize = FileSize }.Check(LocalPath); // 不修改原始的 Checker，以免污染原始实例
                            } else if (Check.ActualSize != FileSize) {
                                CheckResult = $"文件大小不一致：任务校验要求为 {Check.ActualSize}，请求结果为 {FileSize}";
                            }
                        }
                        if (CheckResult is not null) {
                            Logger.Info($"{LocalName} 文件校验失败，下载线程细节：");
                            foreach (NetThread T in Threads) {
                                Logger.Info($"- {T.Uuid}#，状态 {T.State}，范围 {T.DownloadStart}~{T.DownloadEnd}，已下载 {T.DownloadDone}，未下载 {T.DownloadUndone}");
                            }
                            throw new Exception(CheckResult);
                        }
                        // 后处理
                        if (!IsNoSplit) {
                            foreach (NetThread Thread in Threads) {
                                if (Thread.Temp is not null) FileUtils.Delete(Thread.Temp);
                            }
                        }
                        Finish();
                    }
                    break;
                } catch (Exception ex) {
                    RetryCount += 1;
                    if (State > NetState.Merging) return; // 通常是因为在合并时被中断了
                    Logger.Warn(ex, $"合并文件出错，第 {RetryCount} 次尝试（{LocalName}）");
                    // 重新尝试合并
                    if (RetryCount < 3) {
                        System.Threading.Thread.Sleep(500 * RetryCount);
                        continue;
                    }
                    // 失败，禁用当前下载源并重启下载
                    try {
                        FileUtils.Delete(LocalPath);
                    } catch (Exception) {
                        Logger.Warn(ex, $"删除合并失败的文件出错（{LocalPath}）");
                    }
                    SourceFail(Th, ex, true);
                    break;
                }
            }
        } finally {
            Cache?.Dispose();
        }
    }
    /// <summary>
    /// 下载失败。
    /// </summary>
    private void Fail(Exception RaiseEx = null) {
        lock (LockState) {
            if (State >= NetState.Finished) return;
            if (RaiseEx is not null) Ex.Add(RaiseEx);
            // 凉凉
            Logger.Info($"{LocalName}：已失败，当前状态 {State}");
            State = NetState.Canceled;
        }
        CancelInternal();
        foreach (LoaderDownload Task in Tasks) {
            Task.OnFileFail(this);
        }
    }
    /// <summary>
    /// 取消下载。
    /// </summary>
    public void Cancel(LoaderDownload CanceledTask) {
        // 从特定任务中移除，如果它还属于其他任务，则继续下载
        Tasks.Remove(CanceledTask);
        if (Tasks.Any()) return;
        // 确认中断
        lock (LockState) {
            if (State >= NetState.Finished) return;
            Logger.Info($"{LocalName}：已中断，当前状态 {State}");
            State = NetState.Canceled;
        }
        CancelInternal();
    }
    private void CancelInternal() {
        // 原 VB 使用 On Error Resume Next
        try {
            Reset();
            Interlocked.Decrement(ref NetCore.NetManager.FileRemain);
        } catch {
        }
    }

    // 状态改变接口
    /// <summary>
    /// 将该文件设置为已下载完成。
    /// </summary>
    public void Finish(bool PrintLog = true) {
        lock (LockState) {
            if (State >= NetState.Finished) return;
            State = NetState.Finished;
        }
        Interlocked.Decrement(ref NetCore.NetManager.FileRemain);
        if (PrintLog) Logger.Info($"{LocalName}：已完成");
        foreach (LoaderDownload Task in Tasks) {
            Task.OnFileFinish(this);
        }
    }

}
/// <summary>
/// 下载一系列文件的加载器。
/// </summary>
public class LoaderDownload : LoaderBase, IDownloadLoader {

    /// <summary>
    /// 供 C# 侧 LoaderCombo 调用的入口，避免 C# 反向依赖 VB 的 NetFile 类型。
    /// </summary>
    public void StartDownload(object Input, bool IsForceRestart) {
        Start(Input is List<NetFile> ? Input : null, IsForceRestart);
    }

    #region 属性

    /// <summary>
    /// 需要下载的文件。
    /// </summary>
    public ConcurrentQueue<NetFile> Files;
    /// <summary>
    /// 剩余未完成的文件数。（用于减轻 FilesLock 的占用）
    /// </summary>
    private int FileRemain;
    private readonly object FileRemainLock = new object();

    /// <summary>
    /// 用于显示的百分比进度。
    /// </summary>
    public override double Progress {
        get {
            if (State >= LoadState.Finished) return 1;
            if (!Files.Any()) return 0; // 必须返回 0，否则在获取列表的时候会错觉已经下载完了
            return _Progress;
        }
        set {
            throw new Exception("文件下载不允许指定进度");
        }
    }
    private double _Progress = 0;

    /// <summary>
    /// 任务中的文件的连续失败计数。
    /// </summary>
    public int FailCount {
        get {
            return _FailCount;
        }
        set {
            _FailCount = value;
            if (State == LoadState.Loading && value >= Math.Min(10000, Math.Max(FileRemain * 8, NetCore.NetTaskThreadLimit * 8 + 3))) {
                Logger.Error($"由于同加载器中失败次数过多引发强制失败：连续失败了 {value} 次", LogBehavior.ToastIfDebug);
                // 原 VB 使用 On Error Resume Next
                try {
                    List<Exception> ExList = new List<Exception>();
                    foreach (NetFile File in Files) {
                        foreach (NetSource Source in File.Sources) {
                            if (Source.Ex is not null) {
                                ExList.Add(Source.Ex);
                                if (ExList.Count > 10) goto FinishExCatch;
                            }
                        }
                    }
                FinishExCatch:
                    OnFail(ExList);
                } catch {
                }
            }
        }
    }
    private int _FailCount = 0;

    #endregion

    /// <summary>
    /// 刷新公开属性。由 NetManager 每 0.1 秒调用一次。
    /// </summary>
    public void RefreshStat() {
        // 计算进度
        double NewProgress = 0;
        double TotalProgress = 0;
        foreach (NetFile File in Files) {
            if (File.IsCopy) {
                NewProgress += File.Progress * 0.2;
                TotalProgress += 0.2;
            } else {
                NewProgress += File.Progress;
                TotalProgress += 1;
            }
        }
        if (TotalProgress > 0 && !double.IsNaN(TotalProgress)) NewProgress /= TotalProgress;
        // 刷新进度
        _Progress = NewProgress;
    }

    public LoaderDownload(string Name, IEnumerable<NetFile> FileTasks) {
        this.Name = Name;
        Files = new ConcurrentQueue<NetFile>(FileTasks);
    }
    public override void Start(object Input = null, bool IsForceRestart = false) {
        if (Input is not null) Files = new ConcurrentQueue<NetFile>((IEnumerable<NetFile>)Input);
        // 去重
        Files = new ConcurrentQueue<NetFile>(Files.DistinctBy(a => PathUtils.ForCompare(a.LocalPath).Lower()));
        // 设置剩余文件数
        lock (FileRemainLock) {
            FileRemain += Files.Where(f => f.State != NetState.Finished).Count();
        }
        State = LoadState.Loading;
        // 开始执行
        PclCore.RunInNewThread(
        () => {
            try {
                // 输入检测
                if (!Files.Any()) {
                    OnFinish();
                    return;
                }
                foreach (NetFile File in Files) {
                    if (File is null) throw new ArgumentException("存在空文件请求！");
                    foreach (NetSource Source in File.Sources) {
                        if (!(Source.Url.StartsWithF("https://", true) || Source.Url.StartsWithF("http://", true))) {
                            Source.Ex = new ArgumentException("输入的下载链接不正确：" + Source.Url);
                            Source.IsFailed = true;
                        }
                    }
                    if (!File.HasAvailableSource()) throw new ArgumentException("输入的下载链接不正确！");
                    File.LocalPath = File.LocalPath.Replace("/", "\\");
                    if (!File.LocalPath.Contains(":\\")) throw new ArgumentException("输入的本地文件地址不正确：" + File.LocalPath);
                    if (File.LocalPath.EndsWithF("\\")) throw new ArgumentException("请输入含文件名的完整文件路径：" + File.LocalPath);
                    DirectoryUtils.Create(PathUtils.RemoveLastPart(File.LocalPath)); // 创建目标文件夹
                }
                // 接入下载管理器，获取新开始下载的文件列表
                List<NetFile> NewFiles = NetCore.NetManager.Start(this);


                // ====================================
                // 已存在文件查找
                // ====================================

                // 整理允许进行查找的文件
                List<NetFile> FilesToCheck = new List<NetFile>();
                foreach (NetFile File in NewFiles) {
                    if (File.Check is not null) {
                        FilesToCheck.Add(File);
                    } else { // 没有校验信息，直接开始下载
                        lock (LockState) {
                            File.State = NetState.WaitingToDownload;
                            File.IsCopy = false;
                        }
                    }
                }
                if (!FilesToCheck.Any()) return;
                // 获取 MC 文件夹列表
                List<string> Folders = new List<string>();
                Folders.Add(Paths.AppData + ".minecraft\\"); // 总是添加官启文件夹，因为 HMCL 会把所有文件存在这里
                Folders.AddRange(PclUiBridge.McFolderListProvider?.Invoke() ?? new List<string>());
                Folders = Folders.Distinct().Where(f => DirectoryUtils.Exists(f)).ToList();
                // 平均分配到多个检查线程
                int ThreadCount = (FilesToCheck.Count / 40).Clamp(1, 8); // 每个线程至少 40 个文件，最多 8 线程
                if (ThreadCount == 1) { // 只有一个线程，直接执行
                    CheckExistingFiles(FilesToCheck, Folders);
                } else {
                    int BaseSize = FilesToCheck.Count / ThreadCount;
                    int Remainder = FilesToCheck.Count % ThreadCount;
                    int Index = 0;
                    for (int i = 0; i <= ThreadCount - 1; i++) {
                        int Size = BaseSize + (i < Remainder ? 1 : 0);
                        List<NetFile> ThreadFiles = FilesToCheck.GetRange(Index, Size);
                        Index += Size;
                        PclCore.RunInNewThread(() => CheckExistingFiles(ThreadFiles, Folders), $"下载 文件复制 {Uuid}/{PclCore.GetUuid()}");
                    }
                }
            } catch (Exception ex) {
                OnFail(new List<Exception> { new Exception("下载初始化失败", ex) });
            }
        }, "L/下载 " + Uuid);
    }
    private void CheckExistingFiles(List<NetFile> Files, List<string> FolderList) {
        try {
            Logger.Trace(() => $"文件检查开始，本线程负责 {Files.Count} 个文件，首个文件为 {Files.FirstOrDefault()?.LocalName}");
            // 列出 MC 文件夹中的各个版本文件夹
            List<string> VersionFolders = new List<string>();
            foreach (string McFolder in FolderList) {
                foreach (string VersionFolder in DirectoryUtils.EnumerateDirectories(McFolder + "versions\\")) {
                    VersionFolders.Add(PathUtils.AddSlashSuffix(VersionFolder));
                }
            }
            // 处理每个文件
            foreach (NetFile File in Files) {
                string Target = CheckExistingFile(FolderList, VersionFolders, File);
                Logger.Trace(() => $"本地文件匹配：{File.LocalName} → {(Target ?? "无匹配")}，当前状态 {File.State}");
                if (File.State >= NetState.WaitingToDownload) continue; // 中断
                // 已找到相同文件
                if (Target is not null) {
                    File.IsCopy = true;
                    try {
                        FileUtils.Copy(Target, File.LocalPath);
                        File.Finish(false);
                        continue;
                    } catch {
                    }
                }
                // 回退到下载
                lock (LockState) {
                    File.State = NetState.WaitingToDownload;
                    File.IsCopy = false;
                }
            }
        } catch (Exception ex) {
            OnFail(new List<Exception> { new Exception("下载已存在文件查找失败", ex) });
        }
    }
    private string CheckExistingFile(List<string> FolderList, List<string> VersionFolders, NetFile File) {
        // 目标文件已存在
        if (File.Check.Check(File.LocalPath) is null) return File.LocalPath;
        // 在设置中禁用了复制
        if (PclCore.GetSetting<bool>("SystemDebugSkipCopy")) return null;
        // 没有可用的检查规则，只能开始下载
        if (File.Check.Hash is null && File.Check.ActualSize < 0) return null;
        // 大致判断文件类别
        List<(string FolderName, int Index)> TypeIndexes =
            new[] { "\\assets\\", "\\libraries\\", "\\versions\\", "\\mods\\", "\\coremods\\", "\\lib\\", "\\resourcepacks\\", "\\texturepacks\\", "\\shaderpacks\\" }.
            Select(FolderName => (FolderName, File.LocalPath.IndexOfF(FolderName, true))).
            Where(kv => kv.Item2 >= 0).ToList();
        if (!TypeIndexes.Any()) {
            if (PathUtils.GetExtension(File.LocalName) == "jar") {
                TypeIndexes.Add(("\\versions\\", 1)); // 总是对 jar 进行版本文件检查，以包括另存为 jar 的情况
            } else {
                return null;
            }
        }
        string Type = TypeIndexes.MaxBy(kv => kv.Item2).FolderName.TrimStart('\\');
        // 根据类别进行查找
        // 原 VB Static 局部变量
        switch (Type) {
            case "\\assets\\":
            case "\\libraries\\":
                // assets/libraries：查找 MC 文件夹下的相同路径
                foreach (string Folder in FolderList) {
                    string Candidate = Folder + Type + File.LocalPath.AfterFirst(Type);
                    if (CheckCandidate(Candidate, File.Check)) return Candidate;
                }
                return null;
            case "\\versions\\":
                // 版本 jar 或 json：查找 MC 文件夹下的各个版本文件夹
                foreach (string VersionFolder in VersionFolders) {
                    foreach (string Candidate in DirectoryUtils.EnumerateFiles(VersionFolder, searchPattern: "*." + PathUtils.GetExtension(File.LocalPath))) {
                        if (CheckCandidate(Candidate, File.Check)) return Candidate;
                    }
                }
                break;
            default:
                // 社区资源
                if (File.Check.ActualSize < 0 || File.Check.Hash is null) return null; // 必须要求指定了文件大小和 Hash
                foreach (string Folder in FolderList.Concat(VersionFolders)) {
                    foreach (string Candidate in DirectoryUtils.EnumerateFiles(Folder + Type)) {
                        if (CheckCandidate(Candidate, File.Check)) return Candidate;
                    }
                }
                break;
        }
        return null;
    }
    // 原 VB Static 局部变量
    private static ConcurrentDictionary<string, long> Sizes = new ConcurrentDictionary<string, long>();
    private static Func<string, FileChecker, bool> CheckCandidate =
    (Candidate, Checker) => {
        // 快速进行大小校验
        long Length = Sizes.GetOrAdd(Candidate, c => FileUtils.Exists(c) ? FileUtils.GetInfo(c).Length : -1);
        if (Checker.ActualSize != Length) return false;
        // 进一步进行 Hash 校验
        return Checker.Check(Candidate) is null;
    };

    public void OnFileFinish(NetFile File) {
        // 要求全部文件完成
        lock (FileRemainLock) {
            FileRemain -= 1;
            if (FileRemain > 0) return;
        }
        OnFinish();
    }
    public void OnFinish() {
        RaisePreviewFinish();
        lock (LockState) {
            if (State > LoadState.Loading) return;
            State = LoadState.Finished;
        }
    }
    public void OnFileFail(NetFile File) {
        // 将下载源的错误加入主错误列表
        foreach (NetSource Source in File.Sources) {
            if (Source.Ex is not null) File.Ex.Add(Source.Ex);
        }
        OnFail(File.Ex);
    }
    public override void Failed(Exception Ex) {
        OnFail(new List<Exception> { Ex });
    }
    public void OnFail(List<Exception> ExList) {
        lock (LockState) {
            if (State >= LoadState.Finished) return;
            if (ExList is null || !ExList.Any()) ExList = new List<Exception> { new Exception("未知错误！") };
            // 寻找有效的错误信息
            List<Exception> UsefulExs = ExList.Where(e => !e.IsCanceled() && e is not TimeoutException).ToList();
            Error = UsefulExs.FirstOrDefault() ?? ExList.FirstOrDefault();
            // 获取实际失败的文件
            foreach (NetFile File in Files) {
                if (File.State != NetState.Canceled) continue;
                if (File.Sources.All(s => s.Ex.IsCanceled() || s.Ex is TimeoutException)) continue;
                string Detail = File.Sources.Select(s => $"{(s.Ex is null ? "无错误信息。" : s.Ex.GetDisplay(false))}（{s.Url}）").Join(Environment.NewLine);
                Error = new Exception("文件下载失败：" + File.LocalPath + Environment.NewLine +
                                        "各下载源的错误如下：" + Environment.NewLine + Detail, Error);
                // 上报
                PclUiBridge.Telemetry?.Invoke("文件下载失败",
                              new[] { "FileName", File.LocalName,
                              "Exception", Detail });
                break;
            }
            // 在设置 Error 对象后再更改为失败，避免 WaitForExit 无法捕获错误
            State = LoadState.Failed;
        }
        // 中断所有文件
        foreach (NetFile TaskFile in Files) {
            TaskFile.Cancel(this);
        }
        // 在退出同步锁后再进行日志输出
        Logger.Error(new AggregateException(ExList), "下载加载器失败", LogBehavior.None);
    }
    public override void Cancel() {
        lock (LockState) {
            if (State >= LoadState.Finished) return;
            State = LoadState.Canceled;
        }
        Logger.Info($"{Name} 已取消！");
        // 中断所有文件
        foreach (NetFile TaskFile in Files) {
            TaskFile.Cancel(this);
        }
    }

}
/// <summary>
/// 下载文件管理。
/// </summary>
public class NetManagerClass {

    #region 属性

    /// <summary>
    /// 需要下载的文件。为“本地地址 - 文件对象”键值对。
    /// </summary>
    public ConcurrentDictionary<string, NetFile> AllFiles = new ConcurrentDictionary<string, NetFile>();

    /// <summary>
    /// 当前的所有下载任务。
    /// </summary>
    public ConcurrentBag<LoaderDownload> Tasks = new ConcurrentBag<LoaderDownload>();

    /// <summary>
    /// 已下载完成的大小。
    /// </summary>
    public long DownloadDone = 0;

    /// <summary>
    /// 尚未完成下载的文件数。
    /// </summary>
    public int FileRemain = 0;

    // 这些属性由 RefreshStat 刷新
    /// <summary>
    /// 当前的全局下载速度，单位为 Byte / 秒。
    /// </summary>
    public long Speed = 0;

    public readonly int Uuid = PclCore.GetUuid();

    #endregion

    /// <summary>
    /// 进度与下载速度由下载管理线程每隔约 0.1 秒刷新一次。
    /// </summary>
    private void RefreshStat() {
        try {
            long DeltaTime = PclCore.GetTimeMs() - RefreshStatLast;
            if (DeltaTime == 0) return;
            RefreshStatLast += DeltaTime;
            #region 刷新整体速度
            // 计算瞬时速度
            double ActualSpeed = Math.Max(0, (DownloadDone - SpeedLastDone) / (DeltaTime / 1000d));
            SpeedLast.Insert(0, Convert.ToInt64(ActualSpeed));
            if (SpeedLast.Count >= 31) SpeedLast.RemoveAt(30);
            SpeedLastDone = DownloadDone;
            // 计算用于显示的速度
            long SpeedSum = 0, SpeedDiv = 0; int Weight = SpeedLast.Count;
            foreach (long SpeedRecord in SpeedLast) {
                SpeedSum += SpeedRecord * Weight;
                SpeedDiv += Weight;
                Weight -= 1;
            }
            Speed = SpeedDiv > 0 ? Convert.ToInt64((double)SpeedSum / SpeedDiv) : 0;
            // 计算新的速度下限
            long Limit = 0;
            if (SpeedLast.Count >= 10) Limit = Convert.ToInt64(SpeedLast.Take(10).Average() * 0.85); // 取近 1 秒的平均速度的 85%
            if (Limit > NetCore.NetTaskSpeedLimitLow) {
                NetCore.NetTaskSpeedLimitLow = Limit;
                Logger.Info($"速度下限已提升到 {StringUtils.FormatByteSize(Limit)}");
            }
            #endregion
            #region 刷新下载任务属性
            foreach (LoaderDownload Task in Tasks) {
                Task.RefreshStat();
            }
            #endregion
        } catch (Exception ex) {
            Logger.Warn(ex, "刷新下载公开属性失败");
        }
    }
    private long RefreshStatLast;
    // 原 VB Static 局部变量
    private static List<long> SpeedLast = new List<long>(); // 记录至多最近 30 次下载速度的记录，较新的在前面
    private static long SpeedLastDone = 0; // 上次记速时的已下载大小

    /// <summary>
    /// 启动监控线程，用于新增下载线程。
    /// </summary>
    private void StartManager() {
        // 原 VB Static 局部变量 IsStarted
        if (IsStarted) return;
        IsStarted = true;
        Action<int> ThreadStarter =
        Id => { // 0 或 1
            try {
                while (true) {
                    Thread.Sleep(20);
                    // 获取文件列表
                    List<NetFile> WaitingFiles = new List<NetFile>();
                    List<NetFile> OngoingFiles = new List<NetFile>();
                    if (Id == 0 && FileRemain == 0) AllFiles.Clear(); // 若已完成，则清空
                    foreach (NetFile File in AllFiles.Values) {
                        if (File.Uuid % 2 == Id) continue;
                        if (File.State == NetState.WaitingToDownload) {
                            WaitingFiles.Add(File);
                        } else if (File.State < NetState.Merging && File.State >= NetState.WaitingToDownload) {
                            OngoingFiles.Add(File);
                        }
                    }
                    // 为等待中的文件开始线程
                    foreach (NetFile File in WaitingFiles) {
                        if (NetCore.NetTaskThreadCount >= NetCore.NetTaskThreadLimit) goto ContinueWhile; // 最大线程数检查
                        NetThread NewThread = File.TryBeginThread();
                        if (NewThread is not null && NewThread.Source.Url.Contains("bmclapi")) Thread.Sleep(100); // 减少 BMCLAPI 请求频率
                    }
                    // 为进行中的文件追加线程
                    if (Speed >= NetCore.NetTaskSpeedLimitLow) goto ContinueWhile; // 下载速度足够，无需新增
                    foreach (NetFile File in OngoingFiles) {
                        if (NetCore.NetTaskThreadCount >= NetCore.NetTaskThreadLimit) goto ContinueWhile; // 最大线程数检查
                        // 线程种类计数
                        int PreparingCount = 0, DownloadingCount = 0;
                        if (File.Threads is not null) {
                            foreach (NetThread Thread in File.Threads.ToList()) {
                                if (Thread.State < NetState.Downloading) {
                                    PreparingCount += 1;
                                } else if (Thread.State == NetState.Downloading) {
                                    DownloadingCount += 1;
                                }
                            }
                        }
                        // 新增线程
                        if (PreparingCount > DownloadingCount) continue; // 准备中的线程已多于下载中的线程，不再新增
                        NetThread NewThread = File.TryBeginThread();
                        if (NewThread is not null && NewThread.Source.Url.Contains("bmclapi")) Thread.Sleep(100); // 减少 BMCLAPI 请求频率
                    }
                ContinueWhile:;
                }
            } catch (Exception ex) {
                Logger.Error(ex, $"下载管理启动线程 {Id} 出错", LogBehavior.AlertThenCrash);
            }
        };
        PclCore.RunInNewThread(() => ThreadStarter(0), "NetManager ThreadStarter 0");
        PclCore.RunInNewThread(() => ThreadStarter(1), "NetManager ThreadStarter 1");
        PclCore.RunInNewThread(
        () => {
            try {
                long NextTick = PclCore.GetTimeMs();
                while (true) {
                    // 增加限速余量
                    if (NetCore.NetTaskSpeedLimitHigh > 0) Interlocked.Add(ref NetCore.NetTaskSpeedLimitLeft, Convert.ToInt64(NetCore.NetTaskSpeedLimitHigh / 10d));
                    // 刷新公开属性
                    RefreshStat();
                    // 等待 100 ms
                    NextTick += 100;
                    long SleepTime = NextTick - PclCore.GetTimeMs();
                    if (SleepTime > 0) {
                        Thread.Sleep((int)SleepTime);
                    } else {
                        NextTick = PclCore.GetTimeMs(); // 超时，直接追帧，不等待
                    }
                }
            } catch (Exception ex) {
                Logger.Error(ex, "下载管理刷新线程出错", LogBehavior.AlertThenCrash);
            }
        }, "NetManager StatRefresher");
    }
    private static bool IsStarted = false; // 原 VB Static 局部变量

    private readonly object DownloadCacheLock = new object();
    private bool IsDownloadCacheCleared = false;
    /// <summary>
    /// 开始一个下载任务。
    /// </summary>
    public List<NetFile> Start(LoaderDownload Task) {
        StartManager();
        // 清理缓存
        lock (DownloadCacheLock) { // 防止同时开启多个下载任务时重复清理
            if (!IsDownloadCacheCleared) {
                try {
                    Logger.Info("开始清理下载缓存");
                    DirectoryUtils.Delete(PclCore.GetPathTemp() + "Download");
                    Logger.Info("下载缓存已清理");
                } catch (Exception ex) {
                    Logger.Warn(ex, "清理下载缓存失败");
                }
                IsDownloadCacheCleared = true;
            }
        }
        // 添加每个文件
        List<NetFile> NewFiles = new List<NetFile>();
        List<NetFile> NewFilesList = new List<NetFile>();
        foreach (NetFile File in Task.Files) {
            NetFile AddedFile = File;
            if (AllFiles.TryGetValue(File.LocalPath, out NetFile OngoingFile) && OngoingFile.State < NetState.Finished) {
                // 该文件正在被另一个任务下载
                Logger.Trace(() => $"{File.LocalName}：无需开始，该文件已在下载中，目标 {File.LocalPath}");
                AddedFile = OngoingFile; // 将列表中的文件替换成下载中的文件，即两个任务指向同一个文件；抛弃现在的 File 对象
            } else {
                // 常规情况（该文件可能已在其他任务中下载完成，但这并不影响）
                Logger.Trace(() => $"{File.LocalName}：开始，目标 {File.LocalPath}");
                AllFiles[File.LocalPath] = File;
                NewFiles.Add(File);
                Interlocked.Increment(ref FileRemain);
            }
            NewFilesList.Add(AddedFile);
            AddedFile.Tasks.Add(Task);
        }
        Task.Files = new ConcurrentQueue<NetFile>(NewFilesList);
        Tasks.Add(Task);
        return NewFiles;
    }

}
