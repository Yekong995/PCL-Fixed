// ═══════════════════════════════════════════════════════════════════════════
// 密钥与安全信息入口（由 VB Modules\ModSecret.vb 迁移而来）
//
// 注入方式（优先级从高到低）：
//   1. 编译期注入：msbuild ... -p:PclMsClientId=xxx -p:PclCurseForgeApiKey=yyy
//      （或设置环境变量 PCL_MS_CLIENT_ID / PCL_CURSEFORGE_API_KEY 后编译，
//        由 PCLCS.csproj 的 GenerateEmbeddedSecrets 目标生成 EmbeddedSecrets.g.cs 写入程序集）
//   2. 运行时环境变量：PCL_MS_CLIENT_ID / PCL_CURSEFORGE_API_KEY
//
// 开源版默认留空。请勿将真实密钥提交到公共仓库！
// ═══════════════════════════════════════════════════════════════════════════
#nullable disable

using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Windows;
using System.Windows.Media;

namespace PCLCS.Modules;

public static partial class ModSecret {

    /// <summary>
    /// 微软正版登录使用的 ClientId。
    /// 编译期注入的 EmbeddedOAuthClientId 非空时优先使用，否则回退到运行时环境变量 PCL_MS_CLIENT_ID。
    /// </summary>
    public static string OAuthClientId = string.IsNullOrEmpty(EmbeddedOAuthClientId)
        ? Environment.GetEnvironmentVariable("PCL_MS_CLIENT_ID") ?? ""
        : EmbeddedOAuthClientId;

    /// <summary>
    /// CurseForge API Key。
    /// 编译期注入的 EmbeddedCurseForgeApiKey 非空时优先使用，否则回退到运行时环境变量 PCL_CURSEFORGE_API_KEY。
    /// </summary>
    public static string CurseForgeAPIKey = string.IsNullOrEmpty(EmbeddedCurseForgeApiKey)
        ? Environment.GetEnvironmentVariable("PCL_CURSEFORGE_API_KEY") ?? ""
        : EmbeddedCurseForgeApiKey;

    // 标注 PCL 的不同分支，仅用于替换标记
    public const string VersionBranchMain = "OpenSource";
    // 在开源版的注册表与常规版的注册表隔离，以防数据冲突
    public const string RegFolder = "PCLDebug";
    // 用于匿名数据收集的腾讯云日志服务上报 URL，形如 https://{region}.cls.tencentcs.com/track?topic_id={topic_id}
    public const string ClsBaseUrl = "";

    #region 网络鉴权

    public static string SecretCdnSign(string UrlWithMark) {
        if (!UrlWithMark.EndsWithF("{CDN}")) return UrlWithMark;
        return UrlWithMark.Replace("{CDN}", "").Replace(" ", "%20");
    }
    /// <summary>
    /// 设置 Headers 的 UA、Referer。
    /// </summary>
    public static void SecretHeadersSign(string Url, HttpRequestMessage Req, bool SimulateBrowserHeaders = false) {
        if (!Req.Headers.UserAgent.Any()) {
            if (Url.Contains("baidupcs.com") || Url.Contains("baidu.com")) {
                Req.Headers.Add("User-Agent", "LogStatistic"); // #4951
            } else if (SimulateBrowserHeaders) {
                Req.Headers.Add("User-Agent", $"PCL2/{PclUiBridge.VersionBaseNameGet()}.{PclUiBridge.BuildTypeNumberGet()} Mozilla/5.0 AppleWebKit/537.36 Chrome/63.0.3239.132 Safari/537.36");
            } else {
                Req.Headers.Add("User-Agent", $"PCL2/{PclUiBridge.VersionBaseNameGet()}.{PclUiBridge.BuildTypeNumberGet()}");
            }
        }
        if (!SimulateBrowserHeaders) Req.Headers.Add("Referer", $"http://{PclUiBridge.VersionCodeGet()}.open.pcl2.server/");
        if (Url.Contains("api.curseforge.com") || Url.Contains("forgecdn.net")) Req.Headers.Add("x-api-key", CurseForgeAPIKey);
    }

    #endregion

    #region 主题

    public static MyColor Color1 = new MyColor(52, 61, 74);
    public static MyColor Color2 = new MyColor(11, 91, 203);
    public static MyColor Color3 = new MyColor(19, 112, 243);
    public static MyColor Color4 = new MyColor(72, 144, 245);
    public static MyColor Color5 = new MyColor(150, 192, 249);
    public static MyColor Color6 = new MyColor(213, 230, 253);
    public static MyColor Color7 = new MyColor(222, 236, 253);
    public static MyColor Color8 = new MyColor(234, 242, 254);
    public static MyColor ColorBg0 = new MyColor(150, 192, 249);
    public static MyColor ColorBg1 = new MyColor(190, Color7);
    public static MyColor ColorGray1 = new MyColor(64, 64, 64);
    public static MyColor ColorGray2 = new MyColor(115, 115, 115);
    public static MyColor ColorGray3 = new MyColor(140, 140, 140);
    public static MyColor ColorGray4 = new MyColor(166, 166, 166);
    public static MyColor ColorGray5 = new MyColor(204, 204, 204);
    public static MyColor ColorGray6 = new MyColor(235, 235, 235);
    public static MyColor ColorGray7 = new MyColor(240, 240, 240);
    public static MyColor ColorGray8 = new MyColor(245, 245, 245);
    public static MyColor ColorSemiTransparent = new MyColor(1, Color8);

    public static int ThemeNow = -1;
    public static int ColorHue = 210, ColorSat = 85, ColorLightAdjust = 0;
    public static OneOf<int, int[]> ColorHueTopbarDelta = 0;
    public static int ThemeDontClick = 0;

    public static void ThemeRefresh(int NewTheme = -1) {
        try {
            if (ThemeNow == NewTheme && NewTheme >= 0) return;
            if (NewTheme >= 0) ThemeNow = NewTheme;

            Color1 = new MyColor().FromHSL2(ColorHue, ColorSat * 0.2, 25 + ColorLightAdjust * 0.3);
            Color2 = new MyColor().FromHSL2(ColorHue, ColorSat, 45 + ColorLightAdjust);
            Color3 = new MyColor().FromHSL2(ColorHue, ColorSat, 55 + ColorLightAdjust);
            Color4 = new MyColor().FromHSL2(ColorHue, ColorSat, 65 + ColorLightAdjust);
            Color5 = new MyColor().FromHSL2(ColorHue, ColorSat, 80 + ColorLightAdjust * 0.4);
            Color6 = new MyColor().FromHSL2(ColorHue, ColorSat, 91 + ColorLightAdjust * 0.1);
            Color7 = new MyColor().FromHSL2(ColorHue, ColorSat, 95);
            Color8 = new MyColor().FromHSL2(ColorHue, ColorSat, 97);
            ColorBg0 = Color4 * 0.4 + Color5 * 0.4 + ColorGray4 * 0.2;
            ColorBg1 = new MyColor(190, Color7);

            ColorSemiTransparent = new MyColor(1, Color8);
            Application.Current.Resources["ColorBrush1"] = new SolidColorBrush(Color1);
            Application.Current.Resources["ColorBrush2"] = new SolidColorBrush(Color2);
            Application.Current.Resources["ColorBrush3"] = new SolidColorBrush(Color3);
            Application.Current.Resources["ColorBrush4"] = new SolidColorBrush(Color4);
            Application.Current.Resources["ColorBrush5"] = new SolidColorBrush(Color5);
            Application.Current.Resources["ColorBrush6"] = new SolidColorBrush(Color6);
            Application.Current.Resources["ColorBrush7"] = new SolidColorBrush(Color7);
            Application.Current.Resources["ColorBrush8"] = new SolidColorBrush(Color8);
            Application.Current.Resources["ColorBrushBg0"] = new SolidColorBrush(ColorBg0);
            Application.Current.Resources["ColorBrushBg1"] = new SolidColorBrush(ColorBg1);
            Application.Current.Resources["ColorObject1"] = (Color)Color1;
            Application.Current.Resources["ColorObject2"] = (Color)Color2;
            Application.Current.Resources["ColorObject3"] = (Color)Color3;
            Application.Current.Resources["ColorObject4"] = (Color)Color4;
            Application.Current.Resources["ColorObject5"] = (Color)Color5;
            Application.Current.Resources["ColorObject6"] = (Color)Color6;
            Application.Current.Resources["ColorObject7"] = (Color)Color7;
            Application.Current.Resources["ColorObject8"] = (Color)Color8;
            Application.Current.Resources["ColorObjectBg0"] = (Color)ColorBg0;
            Application.Current.Resources["ColorObjectBg1"] = (Color)ColorBg1;
            PclUiBridge.ThemeRefreshMain?.Invoke();
        } catch (Exception ex) {
            Logger.Error(ex, "刷新主题颜色失败", LogBehavior.Toast);
        }
    }
    public static void ThemeCheckAll(bool EffectSetup) {
    }
    public static bool ThemeCheckOne(int Id) {
        return true;
    }
    public static bool ThemeUnlock(int Id, bool ShowDoubleHint = true, string UnlockHint = null) {
        return false;
    }

    #endregion

    #region 更新

    public static void UpdateCheckByButton() {
        PclUiBridge.ShowHint("该版本中不包含更新功能……", HintType.Blue);
    }

    public static bool IsUpdateWaitingRestart = false;
    public static void UpdateRestart(bool TriggerRestartAndByEnd) {
    }
    public static void UpdateReplace(int ProcessId, string OldFileName, string NewFileName, bool TriggerRestart) {
    }

    /// <summary>
    /// 确保 PathTemp/Latest.exe 是最新正式版的 PCL，它会被用于整合包打包。
    /// 如果不是，则下载一个。
    /// </summary>
    public static void DownloadLatestPCL(LoaderBase LoaderToSyncProgress = null) {
        // 注意：如果要自行实现这个功能，请换用另一个文件路径，以免与官方版本冲突
    }

    #endregion

    #region 联网配置

    /// <summary>
    /// 联网获取的配置信息。
    /// 若获取失败或仍在获取中，可能为 null。
    /// </summary>
    public static JObject ServerConfig;

    public static LoaderTask<int, int> ServerLoader = new LoaderTask<int, int>("PCL 配置更新", Loader => Logger.Info("该版本中不包含更新通知功能……"), null, ThreadPriority.BelowNormal) {
        ReloadTimeout = 1000 * 60 * 60 // 超时 1 小时
    };

    #endregion

    #region 赞助等级

    public static DonationRank CurrentRank {
        get {
            return DonationRank.None;
        }
    }

    public static void InputPotatoCode(bool IsUpdating) {
    }
    public static void GeneratePotatoCode() {
    }

    /// <summary>
    /// 获取设备识别码。
    /// </summary>
    public static string GetIdentify() {
        return "0000-0000-0000-0000";
    }

    #endregion

}
