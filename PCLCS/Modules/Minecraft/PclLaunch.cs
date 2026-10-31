// 由 VB Modules\Minecraft\ModLaunch.vb 的纯逻辑部分迁移而来。
// VB UI 依赖（McLaunchLog、McInstanceSelected、McSkinSex、McJsonRuleCheck、McLibListGet、McAssetsGetIndexName、
// ArgumentReplace、ModeDebug、VersionCode、DPI、PageInstanceSetup.GetRam、McLoginLoader.Output、McLoginRequestLogin、
// FrmMain.PanForm 的窗口尺寸）通过 PclUiBridge 桥接，由 VB 宿主在启动时注册。
// 登录与启动流程（McLoginAble、McLoginInput、McLoginStart、McLoginMsStart、McLoginRequestLogin、MsLoginStep1New、
// MsLoginStep1Refresh、MsLoginStep3、MsLoginStep4、MsLoginStep5、MsLoginStep6、McLaunchStart、McLaunchState、
// McLaunchMemoryOptimize、McLaunchPrecheck、McLaunchPrerun、McLaunchWait、McLaunchEnd 等）已迁移至 PCLCS.Modules.PclLaunchUi，
// VB 侧通过同名转发壳调用；纯 UI 的启动编排（Private McLaunchStart(Loader)、McLaunchLoader 等模块字段）仍保留在 VB ModLaunch 中。
// McLibEntry 是 VB 类型（ModMinecraft），C# 无法直接引用，故 McLibListGet 以 IEnumerable<object> 桥接，
// 在 C# 侧用 dynamic 访问其成员，行为与 VB 直接访问一致。
#nullable disable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Windows;
using Microsoft.VisualBasic;
using MeloongCore.Extensions;
using Newtonsoft.Json.Linq;

namespace PCLCS.Modules;

/// <summary>
/// 登录方式。
/// </summary>
public enum McLoginType {
    /// <summary>离线登录。</summary>
    Legacy = 0,
    /// <summary>统一通行证登录。</summary>
    Nide = 2,
    /// <summary>Authlib-Injector 登录。</summary>
    Auth = 3,
    /// <summary>微软登录。</summary>
    Ms = 5,
}

/// <summary>
/// 各个登录方式的对应数据。
/// </summary>
public abstract class McLoginData {
    /// <summary>
    /// 登录方式。
    /// </summary>
    public McLoginType Type;
    public override bool Equals(object obj) {
        return obj is not null && obj.GetHashCode() == GetHashCode();
    }
    // VB 编译器在仅重写 Equals 时会自动生成 GetHashCode（调用 RuntimeHelpers.GetHashCode），此处保持一致。
    public override int GetHashCode() {
        return System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this);
    }
}

public class McLoginServer : McLoginData {

    /// <summary>
    /// 登录用户名。
    /// </summary>
    public string UserName;
    /// <summary>
    /// 登录密码。
    /// </summary>
    public string Password;
    /// <summary>
    /// 登录服务器基础地址。
    /// </summary>
    public string BaseUrl;
    /// <summary>
    /// 登录所使用的标识符，目前只可能为 “Auth” 或 “Nide”，用于存储缓存等。
    /// </summary>
    public string Token;
    /// <summary>
    /// 登录方式的描述字符串，如 “正版”、“统一通行证”。
    /// </summary>
    public string Description;
    /// <summary>
    /// 是否在本次登录中强制要求玩家重新选择角色，目前仅对 Authlib-Injector 生效。
    /// </summary>
    public bool ForceReselectProfile = false;

    public McLoginServer(McLoginType Type) {
        this.Type = Type;
    }
    public override int GetHashCode() {
        return (int)((UserName + Password + BaseUrl + Token + Type).GetStableHashCode() % int.MaxValue);
    }

}

public class McLoginMs : McLoginData {

    /// <summary>
    /// 缓存的 OAuth Refresh Token。若没有则为空字符串。
    /// </summary>
    public string OAuthRefreshToken = "";
    public string AccessToken = "";
    public string Uuid = "";
    public string UserName = "";
    public string ProfileJson = "";

    public McLoginMs() {
        Type = McLoginType.Ms;
    }
    public override int GetHashCode() {
        // 不能使用全部信息，这俩就足以在登录输入时确保是同一个用户了
        return (int)((OAuthRefreshToken + UserName).GetStableHashCode() % int.MaxValue);
    }
}

public class McLoginLegacy : McLoginData {
    /// <summary>
    /// 登录用户名。
    /// </summary>
    public string UserName;
    /// <summary>
    /// 皮肤种类。
    /// </summary>
    public int SkinType;
    /// <summary>
    /// 若采用正版皮肤，则为该皮肤名。
    /// </summary>
    public string SkinName;

    public McLoginLegacy() {
        Type = McLoginType.Legacy;
    }
    public override int GetHashCode() {
        return (int)((UserName + SkinType + SkinName + Type).GetStableHashCode() % int.MaxValue);
    }
}

/// <summary>
/// 登录返回结果。
/// </summary>
public struct McLoginResult {
    public string Name;
    public string Uuid;
    public string AccessToken;
    public string Type;
    public string ClientToken;
    /// <summary>
    /// 进行微软登录时返回的 profile 信息。
    /// </summary>
    public string ProfileJson;
}

public class McLaunchOptions {
    /// <summary>
    /// 强制指定在启动后进入的服务器 IP。
    /// 默认值：Nothing。使用版本设置的值。
    /// </summary>
    public string ServerIp = null;
    /// <summary>
    /// 将启动脚本保存到该地址，然后取消启动。这同时会改变启动时的提示等。
    /// 默认值：Nothing。不保存。
    /// </summary>
    public string SaveBatch = null;
    /// <summary>
    /// 强行指定启动的 MC 版本。
    /// 默认值：Nothing。使用 McInstanceCurrent。
    /// </summary>
    public McInstance Instance = null;
    /// <summary>
    /// 额外的启动参数。
    /// </summary>
    public List<string> ExtraGameArgs = new List<string>();
}

public static class PclLaunch {

    #region 声明

    /// <summary>
    /// 当前正在使用的启动选项。VB 侧通过 CurrentLaunchOptions 属性转发。
    /// </summary>
    public static McLaunchOptions CurrentLaunchOptions = null;

    /// <summary>
    /// 本次启动所选择的 Java。VB 侧通过 McLaunchJavaSelected 属性转发。
    /// </summary>
    public static Java McLaunchJavaSelected = null;

    /// <summary>
    /// 当前启动的游戏进程。VB 侧通过 McLaunchProcess 属性转发。
    /// </summary>
    public static Process McLaunchProcess = null;

    /// <summary>
    /// 指定启动中断时的提示文本。若不为 Nothing 则会显示为绿色。VB 侧通过 CanceledHint 属性转发。
    /// </summary>
    public static string CanceledHint = null;

    private static string McLaunchArgument;
    private static Encoding McLaunchEncoding;

    /// <summary>
    /// 记录启动日志。
    /// </summary>
    public static void McLaunchLog(string Text, LogLevel Level = LogLevel.Info) {
        Text = PclMc.FilterUserName(PclMc.FilterAccessToken(Text, '*'), '*');
        if (PclUiBridge.ModeDebugGet()) {
            PclCore.RunInUi(
            () => PclUiBridge.AppendLaunchLog($"[{DateTime.Now:HH':'mm':'ss'.'fff'}] {Text}{Environment.NewLine}"));
        }
        Logger.Log(Text, Level);
    }

    #endregion

    #region 主登录模块

    /// <summary>
    /// 根据登录信息获取玩家的 MC 用户名。如果无法获取则返回 Nothing。
    /// </summary>
    public static string McLoginName() {
        // 根据当前登录方式优先返回
        switch (PclCore.GetSetting<McLoginType>("LoginType")) {
            case McLoginType.Ms:
                if (PclCore.GetSetting<string>("CacheMsV2Name") != "") return PclCore.GetSetting<string>("CacheMsV2Name");
                break;
            case McLoginType.Legacy:
                if (PclCore.GetSetting<string>("LoginLegacyName") != "") return PclCore.GetSetting<string>("LoginLegacyName").ToString().BeforeFirst("¨");
                break;
            case McLoginType.Nide:
                if (PclCore.GetSetting<string>("CacheNideName") != "") return PclCore.GetSetting<string>("CacheNideName");
                break;
            case McLoginType.Auth:
                if (PclCore.GetSetting<string>("CacheAuthName") != "") return PclCore.GetSetting<string>("CacheAuthName");
                break;
        }
        // 查找所有可能的项
        if (PclCore.GetSetting<string>("CacheMsV2Name") != "") return PclCore.GetSetting<string>("CacheMsV2Name");
        if (PclCore.GetSetting<string>("CacheNideName") != "") return PclCore.GetSetting<string>("CacheNideName");
        if (PclCore.GetSetting<string>("CacheAuthName") != "") return PclCore.GetSetting<string>("CacheAuthName");
        if (PclCore.GetSetting<string>("LoginLegacyName") != "") return PclCore.GetSetting<string>("LoginLegacyName").ToString().BeforeFirst("¨");
        return null;
    }

    #endregion

    #region 分方式登录模块

    // 主加载函数，返回所有需要的登录信息
    public static void McLoginServerStart(LoaderTask<McLoginServer, McLoginResult> Data) {
        McLoginServer Input = Data.Input;
        bool NeedRefresh = false, WasRefreshed = false;
        McLaunchLog("登录方式：" + Input.Description);
        Data.Progress = 0.05;
        // 原 VB 的 GoTo Refresh 位于 If 块内、且可从块外跳入，C# 不允许跨块跳转，改为 DoRefresh 标志位重构
        bool DoRefresh = false;
        // 尝试登录
        if (!Data.Input.ForceReselectProfile &&
            PclCore.GetSetting<string>("Cache" + Input.Token + "Username") == Data.Input.UserName &&
            PclCore.GetSetting<string>("Cache" + Input.Token + "Pass") == Data.Input.Password &&
            PclCore.GetSetting<string>("Cache" + Input.Token + "Access") != "" &&
            PclCore.GetSetting<string>("Cache" + Input.Token + "Client") != "" &&
            PclCore.GetSetting<string>("Cache" + Input.Token + "Uuid") != "" &&
            PclCore.GetSetting<string>("Cache" + Input.Token + "Name") != "") {
            // 尝试验证登录
            try {
                if (Data.IsCanceled) throw new OperationCanceledException();
                McLoginRequestValidate(Data);
                goto LoginFinish;
            } catch (Exception ex) {
                Logger.Warn(ex, "验证登录失败");
                if (ex.IsBadNetwork()) throw new Exception("$登录失败：你的网络环境不佳，导致难以连接到海外服务器。" + Environment.NewLine + "请稍后再试，或使用加速器、VPN 改善网络环境。");
            }
            Data.Progress = 0.25;
            DoRefresh = true; // 验证失败，落入 Refresh 标签
        }
    Refresh:
        // 尝试刷新登录
        if (DoRefresh) {
            try {
                if (Data.IsCanceled) throw new OperationCanceledException();
                McLoginRequestRefresh(Data, NeedRefresh);
                goto LoginFinish;
            } catch (Exception ex) {
                McLaunchLog("刷新登录失败：" + ex.GetDisplay(true));
                if (WasRefreshed) throw new Exception("二轮刷新登录失败", ex);
            }
            Data.Progress = NeedRefresh ? 0.85 : 0.45;
            DoRefresh = false;
        }
        // 尝试普通登录
        try {
            if (Data.IsCanceled) throw new OperationCanceledException();
            NeedRefresh = PclUiBridge.McLoginRequestLogin(Data);
        } catch (Exception ex) {
            McLaunchLog("登录失败：" + ex.GetDisplay(true));
            throw;
        }
        if (NeedRefresh) {
            McLaunchLog("重新进行刷新登录");
            WasRefreshed = true;
            Data.Progress = 0.65;
            DoRefresh = true;
            goto Refresh;
        }
    LoginFinish:
        Data.Progress = 0.95;
        // 保存启动记录
        Dictionary<string, string> Dict = new Dictionary<string, string>();
        List<string> Emails = new List<string>();
        List<string> Passwords = new List<string>();
        try {
            if (PclCore.GetSetting<string>("Login" + Input.Token + "Email") != "") Emails.AddRange(PclCore.GetSetting<string>("Login" + Input.Token + "Email").Split('¨'));
            if (PclCore.GetSetting<string>("Login" + Input.Token + "Pass") != "") Passwords.AddRange(PclCore.GetSetting<string>("Login" + Input.Token + "Pass").Split('¨'));
            for (int i = 0; i <= Emails.Count - 1; i++) {
                Dict.Add(Emails[i], Passwords[i]);
            }
            Dict.Remove(Input.UserName);
            Emails = new List<string>(Dict.Keys);
            Emails.Insert(0, Input.UserName);
            Passwords = new List<string>(Dict.Values);
            Passwords.Insert(0, Input.Password);
            PclCore.SetSetting("Login" + Input.Token + "Email", Emails.Join('¨'));
            PclCore.SetSetting("Login" + Input.Token + "Pass", Passwords.Join('¨'));
        } catch (Exception ex) {
            Logger.Error(ex, "保存启动记录失败", LogBehavior.Toast);
            PclCore.SetSetting("Login" + Input.Token + "Email", "");
            PclCore.SetSetting("Login" + Input.Token + "Pass", "");
        }
    }
    public static void McLoginLegacyStart(LoaderTask<McLoginLegacy, McLoginResult> Data) {
        McLoginLegacy Input = Data.Input;
        McLaunchLog("登录方式：离线（" + Input.UserName + "）");
        Data.Progress = 0.1;
        // VB With Data.Output：值类型会先复制，结束时写回
        McLoginResult Output = Data.Output;
        Output.Name = Input.UserName;
        Output.Uuid = McLoginLegacyUuidWithCustomSkin(Input.UserName, Input.SkinType, Input.SkinName);
        Output.Type = "Legacy";
        Data.Output = Output;
        // 将结果扩展到所有项目中
        Output = Data.Output;
        Output.AccessToken = Output.Uuid;
        Output.ClientToken = Output.Uuid;
        Data.Output = Output;
        // 保存启动记录
        List<string> Names = new List<string>();
        if (PclCore.GetSetting<string>("LoginLegacyName") != "") Names.AddRange(PclCore.GetSetting<string>("LoginLegacyName").ToString().Split('¨'));
        Names.Remove(Input.UserName);
        Names.Insert(0, Input.UserName);
        PclCore.SetSetting("LoginLegacyName", Names.Join('¨'));
    }

    // Server 登录：三种验证方式的请求
    internal static void McLoginRequestValidate(LoaderTask<McLoginServer, McLoginResult> Data) {
        McLaunchLog("验证登录开始（Validate, " + Data.Input.Token + "）");
        // 提前缓存信息，否则如果在登录请求过程中退出登录，设置项目会被清空，导致输出存在空值
        string AccessToken = PclCore.GetSetting<string>("Cache" + Data.Input.Token + "Access");
        string ClientToken = PclCore.GetSetting<string>("Cache" + Data.Input.Token + "Client");
        string Uuid = PclCore.GetSetting<string>("Cache" + Data.Input.Token + "Uuid");
        string Name = PclCore.GetSetting<string>("Cache" + Data.Input.Token + "Name");
        // 发送登录请求
        JObject RequestData = new JObject(
            new JProperty("accessToken", AccessToken), new JProperty("clientToken", ClientToken), new JProperty("requestUser", true));
        NetCore.NetRequestByClientRetry(
            Data.Input.BaseUrl + "/validate", HttpMethod.Post,
            Content: RequestData.ToString(Formatting.None),
            Headers: new[,] { { "Accept-Language", "zh-CN" } },
            ContentType: "application/json; charset=utf-8"); // 没有返回值的
        // 将登录结果输出
        McLoginResult Output = Data.Output;
        Output.AccessToken = AccessToken;
        Output.ClientToken = ClientToken;
        Output.Uuid = Uuid;
        Output.Name = Name;
        Output.Type = Data.Input.Token;
        Data.Output = Output;
        // 不更改缓存，直接结束
        McLaunchLog("验证登录成功（Validate, " + Data.Input.Token + "）");
    }
    internal static void McLoginRequestRefresh(LoaderTask<McLoginServer, McLoginResult> Data, bool RequestUser) {
        McLaunchLog("刷新登录开始（Refresh, " + Data.Input.Token + "）");
        JObject LoginJson = (JObject)NetCore.NetRequestByClientRetry(
               Data.Input.BaseUrl + "/refresh", HttpMethod.Post,
               Content: new JObject(
                   new JProperty("selectedProfile", new JObject(
                       new JProperty("name", PclCore.GetSetting<string>($"Cache{Data.Input.Token}Name")),
                       new JProperty("id", PclCore.GetSetting<string>($"Cache{Data.Input.Token}Uuid"))
                   )),
                   new JProperty("accessToken", PclCore.GetSetting<string>($"Cache{Data.Input.Token}Access")),
                   new JProperty("requestUser", true)
               ).ToString(Formatting.None),
               Headers: new[,] { { "Accept-Language", "zh-CN" } },
               ContentType: "application/json; charset=utf-8", RequireJson: true).DeserializeJson();
        // 将登录结果输出
        if (LoginJson["selectedProfile"] is null) throw new Exception("选择的角色 " + PclCore.GetSetting<string>("Cache" + Data.Input.Token + "Name") + " 无效！");
        McLoginResult Output = Data.Output;
        Output.AccessToken = LoginJson["accessToken"].ToString();
        Output.ClientToken = LoginJson["clientToken"].ToString();
        Output.Uuid = LoginJson["selectedProfile"]["id"].ToString();
        Output.Name = LoginJson["selectedProfile"]["name"].ToString();
        Output.Type = Data.Input.Token;
        Data.Output = Output;
        // 保存缓存
        PclCore.SetSetting("Cache" + Data.Input.Token + "Access", Data.Output.AccessToken);
        PclCore.SetSetting("Cache" + Data.Input.Token + "Client", Data.Output.ClientToken);
        PclCore.SetSetting("Cache" + Data.Input.Token + "Uuid", Data.Output.Uuid);
        PclCore.SetSetting("Cache" + Data.Input.Token + "Name", Data.Output.Name);
        PclCore.SetSetting("Cache" + Data.Input.Token + "Username", Data.Input.UserName);
        PclCore.SetSetting("Cache" + Data.Input.Token + "Pass", Data.Input.Password);
        McLaunchLog("刷新登录成功（Refresh, " + Data.Input.Token + "）");
    }

    // 微软登录步骤 2：从 OAuth AccessToken 获取 XBLToken
    public static string MsLoginStep2(string AccessToken) {
        McLaunchLog("开始微软登录步骤 2/6");

        string Request = @"{
   ""Properties"": {
       ""AuthMethod"": ""RPS"",
       ""SiteName"": ""user.auth.xboxlive.com"",
       ""RpsTicket"": """ + (AccessToken.StartsWithF("d=") ? "" : "d=") + AccessToken + @"""
   },
   ""RelyingParty"": ""http://auth.xboxlive.com"",
   ""TokenType"": ""JWT""
}";
        return NetCore.NetRequestByClientMultiple("https://user.auth.xboxlive.com/user/authenticate", HttpMethod.Post,
            Content: Request, ContentType: "application/json", RequireJson: true).DeserializeJson()["Token"].ToString();
    }

    // 返回符合离线皮肤设置的 UUID
    internal static string McLoginLegacyUuidWithCustomSkin(string UserName, int SkinType, string SkinName) {
        string Uuid = McLoginLegacyUuid(UserName);
        // 根据离线皮肤获取实际使用的 Uuid
        switch (SkinType) {
            case 0:
                // 默认，不需要处理
                break;
            case 1:
                // Steve
                while (PclUiBridge.McSkinSex(Uuid) != "Steve") {
                    if (Uuid.EndsWithF("FFFFF")) Uuid = Uuid.Substring(0, 27) + "00000";
                    Uuid = Uuid.Substring(0, 27) + (long.Parse(Uuid.Substring(27), NumberStyles.AllowHexSpecifier) + 1).ToString("X").PadLeft(5, '0');
                }
                break;
            case 2:
                // Alex
                while (PclUiBridge.McSkinSex(Uuid) != "Alex") {
                    if (Uuid.EndsWithF("FFFFF")) Uuid = Uuid.Substring(0, 27) + "00000";
                    Uuid = Uuid.Substring(0, 27) + (long.Parse(Uuid.Substring(27), NumberStyles.AllowHexSpecifier) + 1).ToString("X").PadLeft(5, '0');
                }
                break;
            case 3:
                // 使用正版用户名
                try {
                    if (SkinName != "" &&
                        PclUiBridge.McInstanceSelectedGet() is not null && PclUiBridge.McInstanceSelectedGet().Version.Vanilla.Major < 20) { // 1.20+ 或快照版不能使用该项（#3746）
                        Logger.Info($"由于离线皮肤设置，使用正版 UUID：{SkinName}");
                        Uuid = McLoginMojangUuid(SkinName, false);
                    }
                } catch (Exception ex) {
                    Logger.Error(ex, "皮肤信息获取失败，游戏将以无皮肤的方式启动", LogBehavior.Toast);
                }
                break;
            case 4:
                // 自定义
                while (PclUiBridge.McSkinSex(Uuid) != (PclCore.GetSetting<bool>("LaunchSkinSlim") ? "Alex" : "Steve")) {
                    if (Uuid.EndsWithF("FFFFF")) Uuid = Uuid.Substring(0, 27) + "00000";
                    Uuid = Uuid.Substring(0, 27) + (long.Parse(Uuid.Substring(27), NumberStyles.AllowHexSpecifier) + 1).ToString("X").PadLeft(5, '0');
                }
                break;
        }
        return Uuid;
    }
    /// <summary>
    /// 根据用户名返回对应 UUID，需要多线程。
    /// </summary>
    public static string McLoginMojangUuid(string Name, bool ThrowOnNotFound) {
        if (Name.Trim().Length == 0) return new string('0', 32);
        // 从缓存获取
        string Uuid = PclCore.ReadIni(PclCore.GetPathTemp() + "Cache\\Uuid\\Mojang.ini", Name, "");
        if (Uuid.Length == 32) return Uuid;
        // 从官网获取
        try {
            JObject GotJson = (JObject)NetCore.NetRequestByClientRetry("https://api.mojang.com/users/profiles/minecraft/" + Name, RequireJson: true).DeserializeJson();
            if (GotJson is null) throw new FileNotFoundException("正版玩家档案不存在（" + Name + "）");
            Uuid = GotJson["id"] is null ? "" : GotJson["id"].ToString();
        } catch (Exception ex) {
            Logger.Warn(ex, $"从官网获取正版 Uuid 失败（{Name}）");
            if (!ThrowOnNotFound && ex.GetType().Name == "FileNotFoundException") {
                Uuid = McLoginLegacyUuid(Name); // 玩家档案不存在
            } else {
                throw new Exception("从官网获取正版 Uuid 失败", ex);
            }
        }
        // 写入缓存
        if (Uuid.Length != 32) throw new Exception("获取的正版 Uuid 长度不足（" + Uuid + "）");
        PclCore.WriteIni(PclCore.GetPathTemp() + "Cache\\Uuid\\Mojang.ini", Name, Uuid);
        return Uuid;
    }
    public static string McLoginLegacyUuid(string Name) {
        string FullUuid = Name.Length.ToString("X").EnsureLength('0', 16) + Name.GetStableHashCode().ToString("X").EnsureLength('0', 16);
        return FullUuid.Substring(0, 12) + "3" + FullUuid.Substring(13, 3) + "9" + FullUuid.Substring(17, 15);
    }

    #endregion

    #region Java 处理

    public static void McLaunchJava(LoaderTask<int, int> Task) {
        McLaunchJavaSelected = PclJava.SelectOrDownloadJava(PclUiBridge.McInstanceSelectedGet(), true, Task.CreateCancellationToken(), Task.CreateSyncProgressProvider());
        if (Task.IsCanceled) return;
        if (McLaunchJavaSelected is null) throw new OperationCanceledException(); // SelectOrDownloadJava 已经会给予适当的提示
        if (PclUiBridge.ModeDebugGet()) {
            McLaunchLog($"预期 Java 版本范围：{PclJava.GetJavaRequirement(PclUiBridge.McInstanceSelectedGet()).Range}");
            McLaunchLog($"Java 版本设置类别：{PclCore.GetSetting<int>("VersionArgumentJavaV2", PclUiBridge.McInstanceSelectedGet())}");
        }
        McLaunchLog("选择的 Java：" + McLaunchJavaSelected.ToString());
    }

    #endregion

    #region 启动参数

    // 主方法
    public static void McLaunchArgumentMain(LoaderTask<string, int> Loader) {
        // 获取基础参数
        List<string> Arguments = new List<string>();
        McLaunchLog("参数获取开始");
        Arguments.AddRange(McLaunchArgumentsJVM()); // JVM 参数
        Arguments.Add(PclUiBridge.McInstanceSelectedGet().JsonObject["mainClass"].ToString()); // mainClass
        Arguments.AddRange(McLaunchArgumentsGame()); // 游戏参数
        // 替换
        McLaunchLog("=================== 启动参数开始 ===================");
        List<(string Key, string Value)> Replacements = McLaunchArgumentsReplace(Arguments).ToList();
        string Argument = "";
        foreach (string RawArg in Arguments) {
            string Arg = RawArg;
            // 应用替换标记
            foreach ((string Key, string Value) Replacement in Replacements) {
                Arg = Arg.Replace(Replacement.Key, Replacement.Value);
            }
            // 为带特殊字符的参数添加双引号
            if (Arg.IndexOfAny(new[] { '&', '|', '<', '>', '^', ' ' }) >= 0 && !Arg.StartsWithF("\"") && !Arg.EndsWithF("\"")) {
                Arg = $"\"{Arg.Replace("\"", "\\\"")}\""; // 转义在原本参数中间的双引号
            }
            // 输出
            McLaunchLog(Arg);
            Argument += Arg + " ";
        }
        McLaunchLog("=================== 启动参数结束 ===================");
        // 输出
        McLaunchArgument = Argument.TrimEnd();
    }

    // JVM 部分
    internal static List<string> McLaunchArgumentsJVM() {
        List<string> Args = new List<string>();

        // Minecraft 参数
        if (PclUiBridge.McInstanceSelectedGet().JsonObject["arguments"] is not null && PclUiBridge.McInstanceSelectedGet().JsonObject["arguments"]["jvm"] is not null) {
            // 新版 MC：从版本 JSON 获取参数
            McInstance CurrentInstance = PclUiBridge.McInstanceSelectedGet();
            do {
                McLaunchLog($"新版 JVM 参数：从版本 JSON 获取（{CurrentInstance.Name}）");
                if (CurrentInstance.JsonObject["arguments"] is not null && CurrentInstance.JsonObject["arguments"]["jvm"] is not null) {
                    foreach (JToken SubJson in CurrentInstance.JsonObject["arguments"]["jvm"]) {
                        switch (SubJson.Type) {
                            case JTokenType.String:
                                Args.Add(SubJson.ToString().Trim());
                                break;
                            case JTokenType.Object: {
                                JObject Argument = (JObject)SubJson;
                                if (Argument.ContainsKey("rules") && !PclUiBridge.McJsonRuleCheck(Argument["rules"])) continue; // 不满足准则
                                if (!Argument.ContainsKey("value")) continue; // 没有 value 字段
                                if (Argument["value"].Type == JTokenType.String) {
                                    Args.Add(Argument["value"].ToString().Trim());
                                } else {
                                    foreach (JToken value in Argument["value"]) {
                                        Args.Add(value.ToString().Trim());
                                    }
                                }
                                break;
                            }
                        }
                    }
                }
                if (CurrentInstance.InheritName == "") break;
                CurrentInstance = new McInstance(CurrentInstance.InheritName);
            } while (true);
            // 1.17.1 中有个 "-Dos.name=Windows 10"，不立即加双引号会导致 10 被隔开
            Args = Args.Select(a => a.Contains(" ") && !a.StartsWithF("\"") && !a.EndsWithF("\"") ? $"\"{a.Replace("\"", "\\\"")}\"" : a).ToList();
        } else {
            // 旧版 MC：直接添加参数
            McLaunchLog($"旧版 JVM 参数");
            Args.Add("-XX:HeapDumpPath=MojangTricksIntelDriversForPerformance_javaw.exe_minecraft.exe.heapdump");
            Args.Add("-Djava.library.path=${natives_directory}");
            Args.Add("-cp"); Args.Add("${classpath}");
        }

        // 设置里自定义的参数
        string CustomArg = PclCore.GetSetting<string>("VersionAdvanceJvm", PclUiBridge.McInstanceSelectedGet());
        if (CustomArg == "") CustomArg = PclCore.GetSetting<string>("LaunchAdvanceJvm");
        if (CustomArg != "") CustomArg = PclUiBridge.ArgumentReplace(CustomArg);

        // 重新分割字符串并去重
        Args = DeduplicateJavaArguments(SplitJavaArguments(Args.Join(' ').Replace("McEmu= ", "McEmu=") + " " + CustomArg).ToList(), true);

        // 统一通行证
        if (PclUiBridge.McLoginLoaderOutputGet().Type == "Nide") {
            Args.Add("-javaagent:\"${pure_directory}\\nide8auth.jar\"=\"" + PclCore.GetSetting<string>("VersionServerNide", PclUiBridge.McInstanceSelectedGet()) + "\"");
        }
        // Authlib-Injector
        if (PclUiBridge.McLoginLoaderOutputGet().Type == "Auth") {
            if (McLaunchJavaSelected.Version.Major >= 6) Args.Add("-Djavax.net.ssl.trustStoreType=WINDOWS-ROOT"); // 信任系统根证书（5252#）
            string Server = PclCore.GetSetting<string>("VersionServerAuthServer", PclUiBridge.McInstanceSelectedGet());
            try {
                McLaunchLog($"开始 Authlib Injector Prefetch");
                string Response = NetCore.NetRequestByClientRetry(Server, Encoding: Encoding.UTF8);
                Args.Add("-javaagent:\"${pure_directory}\\authlib-injector.jar\"=\"" + Server + "\"");
                Args.Add("-Dauthlibinjector.side=client");
                Args.Add("-Dauthlibinjector.yggdrasil.prefetched=" + Convert.ToBase64String(Encoding.UTF8.GetBytes(Response)));
            } catch (Exception ex) {
                throw new Exception("无法连接到第三方登录服务器（" + (Server ?? "Nothing") + "）", ex);
            }
        }

        // JLW
        // 非 GBK 编码下命令行参数乱码的问题（JDK-8272352）
        bool UseJLW =
            !PclCore.GetSetting<bool>("LaunchAdvanceDisableJLW") && !PclCore.GetSetting<bool>("VersionAdvanceDisableJLW", PclUiBridge.McInstanceSelectedGet()) &&
            !PclCore.IsGBKEncoding; // 即使游戏文件夹名称不含非 ASCII 字符，但玩家名等也可能出现中文，因此依然需要 JLW（#8739）
        if (UseJLW && CustomArg.Contains("-javaagent")) {
            UseJLW = false;
            McLaunchLog("由于使用了自定义 javaagent，已禁用 JLW，这可能导致游戏崩溃！", LogLevel.Warn);
        }
        if (UseJLW) {
            if (McLaunchJavaSelected.Version.Major >= 9) {
                Args.Add("--add-exports"); Args.Add("cpw.mods.bootstraplauncher/cpw.mods.bootstraplauncher=ALL-UNNAMED");
            }
            Args.Add("-Doolloo.jlw.tmpdir=${pure_directory}"); // 这里需要不以 \ 结尾
        }
        McLaunchLog("使用 JLW：" + UseJLW);

        // LUA
        bool UseLUA =
            !PclCore.GetSetting<bool>("LaunchAdvanceDisableLUA") && !PclCore.GetSetting<bool>("VersionAdvanceDisableLUA", PclUiBridge.McInstanceSelectedGet()) &&
            PclUiBridge.McLibListGet(PclUiBridge.McInstanceSelectedGet(), false).Cast<dynamic>().Select(e => (string)e.OriginalName).Contains("org.lwjgl:lwjgl:3.4.1");
        if (UseLUA) Args.Add($"-javaagent:\"{LaunchUtils.ExtractPatch("Launch\\LwjglUnsafeAgent.jar", PclCore.PathPure.Value)}\"");
        McLaunchLog("使用 LUA：" + UseLUA);

        #region 内存管理

        McLaunchLog("当前剩余内存：" + Math.Round((double)new Microsoft.VisualBasic.Devices.ComputerInfo().AvailablePhysicalMemory / 1024 / 1024 / 1024 * 10) / 10 + "G");
        int TargetRam = (int)Math.Floor(PclUiBridge.GetRam(PclUiBridge.McInstanceSelectedGet()) * 1024);
        Args.Add("-Xmx" + TargetRam + "m");

        // 获取设置的管理方式
        // 0：在 Java 21+ 使用分代 ZGC，20~15 使用非分代的 ZGC，14- 使用 G1GC
        // 1：在 Java 21+ 使用分代 ZGC，20- 使用 G1GC
        // 2：仅 G1GC
        // 3：不指定，玩家自定义
        int SetupType = PclCore.GetSetting<int>("LaunchAdvanceGC");
        if (PclUiBridge.McInstanceSelectedGet() is not null && PclCore.GetSetting<int>("VersionAdvanceGC", PclUiBridge.McInstanceSelectedGet()) > 0) SetupType = PclCore.GetSetting<int>("VersionAdvanceGC", PclUiBridge.McInstanceSelectedGet()) - 1; // 去掉默认
        if (SetupType != 3) {
            // 确定是使用 G1GC 还是 ZGC
            bool UseG1GC = false;
            if ((SetupType == 0 && McLaunchJavaSelected.Version.Major < 15) ||
               (SetupType == 1 && McLaunchJavaSelected.Version.Major < 21) ||
               (SetupType == 2 || SetupType == 4)) UseG1GC = true; // 检查 Java 版本，ZGC 在 Java 15+ 可用，分代 ZGC 在 Java 21+ 可用
            if (Environment.OSVersion.Version.Major < 10 || Environment.OSVersion.Version.Build < 17763) UseG1GC = true; // 检查系统兼容性，ZGC 需要 Windows 10 1809+
            McLaunchLog($"GC 设置：{SetupType}，选取 {(UseG1GC ? "G1GC" : "ZGC")}，Java 版本：{McLaunchJavaSelected.Version.Major}");
            // 移除已有的 GC 参数
            Args = Args.Where(a => !a.RegexCheck(
                "( )*-XX:[+-]?(Use\\w+GC|ZGenerational|UseCompactObjectHeaders|G1\\w+Percent|G1\\w+Size|(Max|Min)(GCPauseMillis|HeapFreeRatio))")).ToList();
            // 添加 GC 参数
            Args.Add("-XX:+UnlockExperimentalVMOptions");
            if (McLaunchJavaSelected.Version.Major >= 24) Args.Add("-XX:+UseCompactObjectHeaders");
            if (UseG1GC && SetupType == 4) {
                // 优化的 G1GC
                Args.Add("-XX:+UseG1GC");
                Args.Add("-XX:G1NewSizePercent=20");
                Args.Add("-XX:G1ReservePercent=20");
                Args.Add("-XX:G1HeapRegionSize=32M");
                Args.Add("-XX:MaxGCPauseMillis=50");
                if (!PclUiBridge.ModeDebugGet()) Args.Add("-XX:+PerfDisableSharedMem");
                if (McLaunchJavaSelected.Version.Major <= 7) Args.Add("-XX:MaxPermSize=512m"); // #8286
                if (McLaunchJavaSelected.Version.Major == 8) Args.Add("-XX:+ParallelRefProcEnabled");
                if (McLaunchJavaSelected.Version.Major >= 12) Args.Add("-XX:MinHeapFreeRatio=25"); // 需要比 G1NewSizePercent 大一点
                if (McLaunchJavaSelected.Version.Major >= 12) Args.Add("-XX:MaxHeapFreeRatio=40"); // 需要比 MinHeapFreeRatio 大一些
            } else if (UseG1GC) {
                // Mojang G1GC
                Args.Add("-XX:+UseG1GC");
                Args.Add("-XX:G1NewSizePercent=20");
                Args.Add("-XX:G1ReservePercent=20");
                Args.Add("-XX:G1HeapRegionSize=32M");
                Args.Add("-XX:MaxGCPauseMillis=50");
                if (McLaunchJavaSelected.Version.Major <= 7) Args.Add("-XX:MaxPermSize=512m"); // #8286
            } else {
                // ZGC
                Args.Add("-XX:+UseZGC");
                if (McLaunchJavaSelected.Version.Major == 21 || McLaunchJavaSelected.Version.Major == 22) Args.Add("-XX:+ZGenerational"); // Java 23 起默认启用分代 ZGC，不需要再添加参数
            }
        }

        Args.Remove("-XX:MaxDirectMemorySize=256M"); // #3511 的清理

        #endregion

        // Log4j 漏洞防御参数
        Args.Add("-Dlog4j2.formatMsgNoLookups=true");

        // 编码（#4700、#5892、#5909、#6934、#8950、#9241）
        bool UseCompatEncoding = McLaunchJavaSelected.Version.Major >= 18;
        string EncodingName = UseCompatEncoding ?
                              McLaunchJavaSelected.NativeEncodingName : // Java 18+ 会添加 COMPAT，因此 Java 会回退到 native.encoding
                              McLaunchJavaSelected.FileEncodingName; // Java 17- 直接使用 file.encoding
        try {
            McLaunchEncoding = Encoding.GetEncoding(EncodingName.Equals("GBK", StringComparison.OrdinalIgnoreCase) ? "GB18030" : EncodingName);
        } catch (Exception) { // VB Catch ex As Exception，ex 未使用
            Encoding FallbackEncoding = Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.ANSICodePage); // 老版本 Java 的编码选择行为
            McLaunchLog($"无法识别 Java 编码 {EncodingName ?? "Nothing"}，已回退为 {FallbackEncoding.WebName}", LogLevel.Warn);
            McLaunchEncoding = FallbackEncoding;
        }
        McLaunchLog($"使用编码：{McLaunchEncoding.WebName}");
        string LaunchEncodingName = McLaunchEncoding.CodePage == 54936 ? "GBK" : McLaunchEncoding.WebName;
        string StdoutArgName = McLaunchJavaSelected.Version.Major < 19 ? "sun.stdout.encoding" : "stdout.encoding"; // 老版本 Java 应该设置 sun.std
        string StderrArgName = McLaunchJavaSelected.Version.Major < 19 ? "sun.stderr.encoding" : "stderr.encoding";
        if (!Args.Any(a => a.StartsWithF($"-D{StdoutArgName}="))) Args.Add($"-D{StdoutArgName}={LaunchEncodingName}");
        if (!Args.Any(a => a.StartsWithF($"-D{StderrArgName}="))) Args.Add($"-D{StderrArgName}={LaunchEncodingName}");
        if (UseCompatEncoding && !Args.Any(a => a.StartsWithF("-Dfile.encoding="))) Args.Add("-Dfile.encoding=COMPAT"); // 防止老版本 MC 与部分 Mod 不兼容 UTF-8

        // 为 JLW 添加 -jar，它必须放在最后
        if (UseJLW) {
            Args.Add("-jar"); Args.Add(LaunchUtils.ExtractPatch("Launch\\JavaWrapper.jar", PclCore.PathPure.Value));
        }

        // 再次去重并输出
        return DeduplicateJavaArguments(Args, true);
    }
    // 游戏参数部分
    internal static List<string> McLaunchArgumentsGame() {
        string Arg = "";

        // Minecraft 参数
        // 旧版 MC：直接添加参数
        string OldArgumentsText = PclUiBridge.McInstanceSelectedGet().JsonObject["minecraftArguments"]?.ToString();
        if (OldArgumentsText is not null && OldArgumentsText.Any()) {
            string BasicString = OldArgumentsText;
            McLaunchLog($"旧版游戏参数：" + BasicString);
            BasicString += " --height ${resolution_height} --width ${resolution_width}"; // 总是添加宽高选项，之后去重的时候会覆盖 MC 自带的
            Arg += " " + BasicString;
        }
        // 新版 MC：从版本 JSON 获取参数
        if (PclUiBridge.McInstanceSelectedGet().JsonObject["arguments"] is not null && PclUiBridge.McInstanceSelectedGet().JsonObject["arguments"]["game"] is not null) {
            McInstance CurrentInstance = PclUiBridge.McInstanceSelectedGet();
            Action<string> AppendArg = a => Arg += " " + (a.Contains(" ") && !a.StartsWithF("\"") && !a.EndsWithF("\"") ? $"\"{a.Replace("\"", "\\\"")}\"" : a);
            do {
                McLaunchLog($"新版游戏参数：从版本 JSON 获取（{CurrentInstance.Name}）");
                if (CurrentInstance.JsonObject["arguments"] is not null && CurrentInstance.JsonObject["arguments"]["game"] is not null) {
                    foreach (JToken SubJson in CurrentInstance.JsonObject["arguments"]["game"]) {
                        switch (SubJson.Type) {
                            case JTokenType.String:
                                AppendArg(SubJson.ToString());
                                break;
                            case JTokenType.Object: {
                                JObject Argument = (JObject)SubJson;
                                if (Argument.ContainsKey("rules") && !PclUiBridge.McJsonRuleCheck(Argument["rules"])) continue; // 不满足准则
                                if (!Argument.ContainsKey("value")) continue; // 没有 value 字段
                                if (Argument["value"].Type == JTokenType.String) {
                                    AppendArg(Argument["value"].ToString());
                                } else {
                                    foreach (JToken value in Argument["value"]) {
                                        AppendArg(value.ToString());
                                    }
                                }
                                break;
                            }
                        }
                    }
                }
                if (CurrentInstance.InheritName == "") break;
                CurrentInstance = new McInstance(CurrentInstance.InheritName);
            } while (true);
        }

        // 设置里自定义的参数
        string CustomArg = PclCore.GetSetting<string>("VersionAdvanceGame", PclUiBridge.McInstanceSelectedGet());
        if (CustomArg == "") CustomArg = PclCore.GetSetting<string>("LaunchAdvanceGame");
        if (CustomArg != "") CustomArg = PclUiBridge.ArgumentReplace(CustomArg);
        Arg += " " + CustomArg;

        // 把 OptiFineForgeTweaker 放在参数的末尾
        if ((PclUiBridge.McInstanceSelectedGet().Version.ModLoaders.HasFlagF(ModLoaders.Forge) || PclUiBridge.McInstanceSelectedGet().Version.ModLoaders.HasFlagF(ModLoaders.LiteLoader)) &&
                PclUiBridge.McInstanceSelectedGet().Version.HasOptiFine && !PclUiBridge.McInstanceSelectedGet().Version.OptiFineAsMod) {
            if (Arg.Contains("--tweakClass optifine.OptiFineForgeTweaker")) {
                McLaunchLog("发现正确的 OptiFineForge TweakClass，目前的游戏参数：" + Arg);
                Arg = Arg.Replace(" --tweakClass optifine.OptiFineForgeTweaker", "") + " --tweakClass optifine.OptiFineForgeTweaker";
            }
            if (Arg.Contains("--tweakClass optifine.OptiFineTweaker")) {
                McLaunchLog("发现错误的 OptiFineForge TweakClass，目前的游戏参数：" + Arg);
                Arg = Arg.Replace(" --tweakClass optifine.OptiFineTweaker", "") + " --tweakClass optifine.OptiFineForgeTweaker";
                try {
                    FileUtils.Write(
                        PclUiBridge.McInstanceSelectedGet().GetJsonPath(),
                        FileUtils.ReadAsString(PclUiBridge.McInstanceSelectedGet().GetJsonPath()).Replace("optifine.OptiFineTweaker", "optifine.OptiFineForgeTweaker"));
                } catch (Exception ex) {
                    Logger.Warn(ex, "替换 OptiFineForge TweakClass 失败");
                }
            }
        }

        // 重新分割字符串并去重
        List<string> Args = DeduplicateJavaArguments(SplitJavaArguments(Arg).ToList(), false);

        // 额外传入的参数
        Args.AddRange(CurrentLaunchOptions.ExtraGameArgs);

        // 全屏
        if (PclCore.GetSetting<int>("LaunchArgumentWindowType") == 0) Args.Add("--fullscreen");

        // 自动进入服务器
        string Server = string.IsNullOrEmpty(CurrentLaunchOptions.ServerIp) ? PclCore.GetSetting<string>("VersionServerEnter", PclUiBridge.McInstanceSelectedGet()) : CurrentLaunchOptions.ServerIp;
        if (Server.Any()) {
            if (PclUiBridge.McInstanceSelectedGet().ReleaseTime > new DateTime(2023, 4, 4)) {
                // QuickPlay
                Args.Add($"--quickPlayMultiplayer"); Args.Add(Server);
            } else {
                // 老版本
                if (Server.Contains(":")) {
                    // 包含端口号
                    Args.Add("--server"); Args.Add(Server.Split(':')[0]);
                    Args.Add("--port"); Args.Add(Server.Split(':')[1]);
                } else {
                    // 不包含端口号
                    Args.Add("--server"); Args.Add(Server);
                    Args.Add("--port"); Args.Add("25565");
                }
                if (PclUiBridge.McInstanceSelectedGet().Version.HasOptiFine) PclUiBridge.ShowHint?.Invoke("OptiFine 与自动进入服务器可能不兼容，有概率导致材质丢失甚至游戏崩溃！", HintType.Red);
            }
        }

        // 再次去重并输出
        return DeduplicateJavaArguments(Args, false);
    }
    // 获取替换标记
    internal static IEnumerable<(string Key, string Value)> McLaunchArgumentsReplace(List<string> Arguments) {
        // 基础参数
        yield return ("${classpath_separator}", ";");
        yield return ("${natives_directory}", PathUtils.RemoveSlashSuffix(GetNativesFolder()));
        yield return ("${library_directory}", PathUtils.ToShortPath(PclUiBridge.McFolderSelectedGet() + "libraries"));
        yield return ("${libraries_directory}", PathUtils.ToShortPath(PclUiBridge.McFolderSelectedGet() + "libraries"));
        yield return ("${pure_directory}", PathUtils.RemoveSlashSuffix(PclCore.PathPure.Value)); // 由 PCL 添加，这会允许在分割参数并去重后再替换路径，防止路径中的特殊字符影响参数分割和去重
        yield return ("${launcher_name}", "PCL");
        yield return ("${launcher_version}", PclUiBridge.VersionCodeGet().ToString());
        yield return ("${version_name}", PclUiBridge.McInstanceSelectedGet().Name);
        yield return ("${game_directory}", PathUtils.ToShortPath(PclUiBridge.McInstanceSelectedGet().PathIndie.Substring(0, PclUiBridge.McInstanceSelectedGet().PathIndie.Length - 1))); // VB Left：去掉末尾的 \
        yield return ("${assets_root}", PathUtils.ToShortPath(PclUiBridge.McFolderSelectedGet() + "assets"));
        yield return ("${user_properties}", "{}");
        yield return ("${auth_player_name}", PclUiBridge.McLoginLoaderOutputGet().Name);
        yield return ("${auth_uuid}", PclUiBridge.McLoginLoaderOutputGet().Uuid);
        yield return ("${auth_access_token}", PclUiBridge.McLoginLoaderOutputGet().AccessToken);
        yield return ("${access_token}", PclUiBridge.McLoginLoaderOutputGet().AccessToken);
        yield return ("${auth_session}", PclUiBridge.McLoginLoaderOutputGet().AccessToken);
        yield return ("${user_type}", "msa"); // #1221
        yield return ("${primary_jar}", PathUtils.ToShortPath(PclUiBridge.McInstanceSelectedGet().PathVersion + PclUiBridge.McInstanceSelectedGet().Name + ".jar")); // #6942
        yield return ("${game_assets}", PathUtils.ToShortPath(PclUiBridge.McFolderSelectedGet() + "assets\\virtual\\legacy")); // 1.5.2 的 pre-1.6 资源索引应与 legacy 合并
        yield return ("${assets_index_name}", PclUiBridge.McAssetsGetIndexName(PclUiBridge.McInstanceSelectedGet()));

        // 自定义信息
        string ArgumentInfo;
        ArgumentInfo = PclCore.GetSetting<string>("VersionArgumentInfo", PclUiBridge.McInstanceSelectedGet());
        if (ArgumentInfo == "") ArgumentInfo = PclCore.GetSetting<string>("LaunchArgumentInfo");
        if (ArgumentInfo == "") { // 若自定义信息为空，则必须从参数中去掉该部分，不然 MC 会显示 ${version_type} 或者一个空字符串
            if (Arguments.Contains("--versionType")) {
                int Index = Arguments.IndexOf("--versionType");
                Arguments.RemoveAt(Index);
                if (Index < Arguments.Count) Arguments.RemoveAt(Index);
            }
            yield return ("${version_type}", "\"\"");
        } else {
            yield return ("${version_type}", PclUiBridge.ArgumentReplace(ArgumentInfo));
        }

        // 窗口尺寸
        Size TargetSize;
        switch (PclCore.GetSetting<int>("LaunchArgumentWindowType")) {
            case 2: // 与启动器尺寸一致
                Size Result = default;
                PclCore.RunInUiWait(() => Result = PclUiBridge.LauncherWindowSize());
                TargetSize = Result;
                TargetSize.Height -= 29.5 * PclUiBridge.DpiGet() / 96; // 标题栏高度
                break;
            case 3: // 自定义
                TargetSize = new Size(Math.Max(100, PclCore.GetSetting<int>("LaunchArgumentWindowWidth")), Math.Max(100, PclCore.GetSetting<int>("LaunchArgumentWindowHeight")));
                break;
            default:
                TargetSize = new Size(854, 480);
                break;
        }
        if (PclUiBridge.McInstanceSelectedGet().Version.Vanilla.Major <= 12 && McLaunchJavaSelected.Version >= new Version(8, 0, 200) && McLaunchJavaSelected.Version <= new Version(8, 0, 321) &&
                !PclUiBridge.McInstanceSelectedGet().Version.HasOptiFine && !PclUiBridge.McInstanceSelectedGet().Version.ModLoaders.HasFlagF(ModLoaders.Forge)) { // 修复 1.12.2- 下 JRE 8u200~321 下窗口大小为设置大小的 DPI% 倍（#3463）
            McLaunchLog($"已应用窗口大小过大修复（Java 版本：{McLaunchJavaSelected.Version}）");
            TargetSize.Width /= PclUiBridge.DpiGet() / 96.0;
            TargetSize.Height /= PclUiBridge.DpiGet() / 96.0;
        }
        yield return ("${resolution_width}", Math.Round(TargetSize.Width).ToString());
        yield return ("${resolution_height}", Math.Round(TargetSize.Height).ToString());

        // 支持库
        List<string> Cps = new List<string>();
        string OptiFineCp = null;
        foreach (dynamic Library in PclUiBridge.McLibListGet(PclUiBridge.McInstanceSelectedGet(), true)) {
            if (Library.IsNatives) continue;
            if (Library.Name is not null && Library.Name == "optifine:OptiFine") {
                OptiFineCp = (string)Library.LocalPath;
            } else {
                Cps.Add((string)Library.LocalPath);
            }
        }
        if (OptiFineCp is not null) Cps.Insert(Cps.Count - 2, OptiFineCp); // OptiFine 的总是需要放到倒数第二位
        yield return ("${classpath}", Cps.Select(c => PathUtils.ToShortPath(c)).Join(';'));
    }

    /// <summary>
    /// 分割 Java 参数字符串。
    /// </summary>
    internal static IEnumerable<string> SplitJavaArguments(string Arg) {
        Arg = Arg.ReplaceLineEndings(" ", mergeMultiple: true).Trim(); // 预处理换行符
        if (string.IsNullOrWhiteSpace(Arg)) yield break;
        StringBuilder Current = new StringBuilder();
        bool InQuotes = false;
        int i = 0;
        while (i < Arg.Length) {
            char c = Arg[i];
            if (c == '\\' && i + 1 < Arg.Length && Arg[i + 1] == '"') { // 检测 \"
                // 转义的双引号：保留反斜杠和引号，不切换 InQuotes
                Current.Append("\\\"");
                i += 1; // 额外跳过下一个字符（双引号）
            } else if (c == '"') {
                // 未转义的双引号：切换 InQuotes
                InQuotes = !InQuotes;
                Current.Append(c);
            } else if (c == ' ' && !InQuotes) {
                // 不在双引号内的空格：一个参数结束
                if (Current.Length > 0) {
                    yield return Current.ToString();
                    Current.Clear();
                }
            } else {
                // 普通字符，直接添加
                Current.Append(c);
            }
            i += 1;
        }
        if (Current.Length > 0) yield return Current.ToString(); // 添加剩余的
    }
    /// <summary>
    /// 对 Java 参数进行去重。
    /// </summary>
    internal static List<string> DeduplicateJavaArguments(List<string> Args, bool IsJVMArgs) {
        List<string> Result = new List<string>();
        int i = 0;
        while (i < Args.Count) {
            string Key = Args[i];
            if (!Key.StartsWithF("-") ||
               (i + 1 >= Args.Count || (Args[i + 1].StartsWithF("-") && PclCore.Val(Args[i + 1].AfterFirst("-")) == 0))) { // 避免 -xPos 23 -xPos -50 中后面的 -50 被识别为参数
                // 单参数
                i += 1;
                if (Result.Contains(Key)) {
                    McLaunchLog($"已去重单个{(IsJVMArgs ? " JVM " : "游戏")}参数：{Key}");
                    continue;
                }
                Result.Add(Key);
            } else {
                // 以空格间隔的键值对（例如 --width 233）
                string Value = Args[i + 1];
                i += 2;
                for (int j = 0; j <= Result.Count - 2; j++) { // 向前寻找相同的键
                    if (Result[j] != Key) continue;
                    // 仅当是游戏参数且不是 tweakClass 时，用新的值覆盖旧的值
                    // 据测试，如果包含多个相同 --width 或 --uuid 等参数，两个都会直接失效，如果这发生在关键参数上会导致崩溃
                    if (!IsJVMArgs && Key != "--tweakClass") {
                        McLaunchLog($"已覆盖重复的{(IsJVMArgs ? " JVM " : "游戏")}参数：{Key} {Result[j + 1]} → {Key} {Value}");
                        Result[j + 1] = Value;
                        goto EndWhileIteration; // VB Continue While
                    }
                    // 键和值完全相同，直接抛弃
                    if (Result[j + 1] == Value) {
                        McLaunchLog($"已去重{(IsJVMArgs ? " JVM " : "游戏")}参数对：{Key} {Value}");
                        goto EndWhileIteration; // VB Continue While
                    }
                }
                Result.Add(Key);
                Result.Add(Value);
            }
        EndWhileIteration:;
        }
        return Result;
    }

    #endregion

    #region 解压 Natives

    public static void McLaunchNatives(LoaderTask<int, int> Loader) {

        // 创建文件夹
        string Target = GetNativesFolder();
        DirectoryUtils.Create(Target);

        // 解压文件
        McLaunchLog("正在解压 Natives 文件");
        List<string> ExistFiles = new List<string>();
        foreach (dynamic Native in PclUiBridge.McLibListGet(PclUiBridge.McInstanceSelectedGet(), true)) {
            if (!Native.IsNatives) continue;
            string NativeLocalPath = (string)Native.LocalPath;
            ZipArchive Zip;
            try {
                Zip = FileUtils.OpenZip(NativeLocalPath);
            } catch (InvalidDataException ex) {
                Logger.Warn(ex, $"打开 Natives 文件失败（{NativeLocalPath}）");
                FileUtils.Delete(NativeLocalPath);
                throw new Exception("无法打开 Natives 文件（" + NativeLocalPath + "），该文件可能已损坏，请重新尝试启动游戏");
            }
            foreach (ZipArchiveEntry Entry in Zip.Entries) {
                string FileName = Entry.FullName;
                if (PathUtils.GetExtension(FileName) == "dll") {
                    // 实际解压文件的步骤
                    string FilePath = Target + FileName;
                    ExistFiles.Add(FilePath);
                    FileInfo OriginalFile = FileUtils.GetInfo(FilePath);
                    if (OriginalFile.Exists) {
                        if (OriginalFile.Length == Entry.Length) {
                            if (PclUiBridge.ModeDebugGet()) McLaunchLog("无需解压：" + FilePath);
                            continue;
                        }
                        // 删除原文件
                        try {
                            FileUtils.Delete(FilePath);
                        } catch (UnauthorizedAccessException ex) {
                            McLaunchLog("删除原 dll 访问被拒绝，这通常代表有一个 MC 正在运行，跳过解压：" + FilePath);
                            McLaunchLog("实际的错误信息：" + ex.GetDisplay(true));
                            break; // Exit For
                        }
                    }
                    // 解压新文件
                    FileUtils.Write(FilePath, Entry.Open());
                    McLaunchLog("已解压：" + FilePath);
                }
            }
            Zip?.Dispose();
        }

        // 删除多余文件
        foreach (string FileName in DirectoryUtils.EnumerateFiles(Target)) {
            if (ExistFiles.Contains(FileName)) continue;
            try {
                McLaunchLog("删除：" + FileName);
                FileUtils.Delete(FileName);
            } catch (UnauthorizedAccessException ex) {
                McLaunchLog("删除多余文件访问被拒绝，跳过删除步骤");
                McLaunchLog("实际的错误信息：" + ex.GetDisplay(true));
                return;
            }
        }

    }
    /// <summary>
    /// 获取 Natives 文件夹路径，以 \ 结尾。
    /// </summary>
    public static string GetNativesFolder(McInstance Instance = null) {
        if (Instance is null) Instance = PclUiBridge.McInstanceSelectedGet();
        string Result = PathUtils.AddSlashSuffix(PathUtils.ToShortPath(Instance.PathVersion)) + Instance.Name + "-natives\\";
        if (!(PclCore.IsGBKEncoding || Result.IsAsciiOnly())) {
            Result = Paths.AppData + ".minecraft\\bin\\natives\\";
            if (!Result.IsAsciiOnly()) {
                Result = PclCore.OsDrive + "ProgramData\\PCL\\natives\\";
            }
        }
        DirectoryUtils.Create(Result); // 提前创建，这样 DirectoryUtils.Shorten 才有结果（否则在长路径下首次启动 Forge 会崩溃）
        return PathUtils.AddSlashSuffix(PathUtils.ToShortPath(Result));
    }

    #endregion

    #region 启动与前后处理

    public static void McLaunchCustom(LoaderTask<int, int> Loader) {

        // 获取自定义命令
        string CustomCommandGlobal = PclCore.GetSetting<string>("LaunchAdvanceRun");
        if (CustomCommandGlobal != "") CustomCommandGlobal = PclUiBridge.ArgumentReplace(CustomCommandGlobal);
        string CustomCommandVersion = PclCore.GetSetting<string>("VersionAdvanceRun", PclUiBridge.McInstanceSelectedGet());
        if (CustomCommandVersion != "") CustomCommandVersion = PclUiBridge.ArgumentReplace(CustomCommandVersion);

        // 输出 bat
        try {
            string CmdString =
                $"chcp {(McLaunchEncoding.CodePage == 54936 && PclCore.IsGBKEncoding ? 936 : McLaunchEncoding.CodePage)}>nul" + Environment.NewLine +
                "@echo off" + Environment.NewLine +
                $"title 启动 - {PclUiBridge.McInstanceSelectedGet().Name}" + Environment.NewLine +
                "echo 游戏正在启动，请稍候。" + Environment.NewLine +
                $"cd /D \"{PathUtils.ToShortPath(PclUiBridge.McInstanceSelectedGet().PathIndie)}\"" + Environment.NewLine +
                CustomCommandGlobal + Environment.NewLine +
                CustomCommandVersion + Environment.NewLine +
                $"\"{McLaunchJavaSelected.JavaExePath}\" {McLaunchArgument}" + Environment.NewLine +
                "echo 游戏已退出。" + Environment.NewLine +
                "pause";
            FileUtils.Write(
                filePath: CurrentLaunchOptions.SaveBatch ?? (Paths.Base + "PCL\\LatestLaunch.bat"),
                text: PclMc.FilterAccessToken(CmdString, 'F').Replace("%", "%%"),
                encoding: McLaunchEncoding);
            if (CurrentLaunchOptions.SaveBatch is not null) {
                McLaunchLog("导出启动脚本完成，强制结束启动过程");
                CanceledHint = "导出启动脚本成功！";
                PclCore.OpenExplorer(CurrentLaunchOptions.SaveBatch);
                Loader.Parent.Cancel();
                return; // 导出脚本完成
            }
        } catch (Exception ex) {
            Logger.Warn(ex, "输出启动脚本失败");
            if (CurrentLaunchOptions.SaveBatch is not null) throw; // 直接触发启动失败
        }

        // 执行自定义命令
        if (CustomCommandGlobal != "") {
            McLaunchLog("正在执行全局自定义命令：" + CustomCommandGlobal);
            Process CustomProcess = null;
            try {
                CustomProcess = PclCore.StartProcess(new ProcessStartInfo {
                    FileName = "cmd.exe",
                    Arguments = $"/c \"{CustomCommandGlobal}\"",
                    WorkingDirectory = PclUiBridge.McFolderSelectedGet(),
                    UseShellExecute = false,
                    CreateNoWindow = true
                });
                if (PclCore.GetSetting<bool>("LaunchAdvanceRunWait")) {
                    do {
                        Thread.Sleep(10);
                    } while (!CustomProcess.HasExited && !Loader.IsCanceled);
                }
            } catch (Exception ex) {
                Logger.Error(ex, "执行全局自定义命令失败", LogBehavior.Toast);
            } finally {
                if (CustomProcess is not null && !CustomProcess.HasExited && Loader.IsCanceled) {
                    McLaunchLog("由于取消启动，已强制结束自定义命令 CMD 进程"); // #1183
                    CustomProcess.Kill();
                }
            }
        }
        if (CustomCommandVersion != "") {
            McLaunchLog("正在执行版本自定义命令：" + CustomCommandVersion);
            Process CustomProcess = null;
            try {
                CustomProcess = PclCore.StartProcess(new ProcessStartInfo {
                    FileName = "cmd.exe",
                    Arguments = $"/c \"{CustomCommandVersion}\"",
                    WorkingDirectory = PclUiBridge.McFolderSelectedGet(),
                    UseShellExecute = false,
                    CreateNoWindow = true
                });
                if (PclCore.GetSetting<bool>("VersionAdvanceRunWait", PclUiBridge.McInstanceSelectedGet())) {
                    do {
                        Thread.Sleep(10);
                    } while (!CustomProcess.HasExited && !Loader.IsCanceled);
                }
            } catch (Exception ex) {
                Logger.Error(ex, "执行版本自定义命令失败", LogBehavior.Toast);
            } finally {
                if (CustomProcess is not null && !CustomProcess.HasExited && Loader.IsCanceled) {
                    McLaunchLog("由于取消启动，已强制结束自定义命令 CMD 进程"); // #1183
                    CustomProcess.Kill();
                }
            }
        }

    }
    public static void McLaunchRun(LoaderTask<int, Process> Loader) {

        // 启动信息
        ProcessStartInfo StartInfo = new ProcessStartInfo(McLaunchJavaSelected.JavaExePath); // 使用 javaw.exe 会导致 #6263

        // 设置环境变量
        string PathEnv = StartInfo.EnvironmentVariables["Path"];
        List<string> Paths = new List<string>();
        if (!string.IsNullOrEmpty(PathEnv)) Paths.AddRange(PathEnv.Split(';'));
        Paths.Add(PathUtils.ToShortPath(McLaunchJavaSelected.Folder));
        StartInfo.EnvironmentVariables["Path"] = Paths.Distinct().Join(';');
        StartInfo.EnvironmentVariables["appdata"] = PathUtils.ToShortPath(PclUiBridge.McFolderSelectedGet());

        // 设置其他参数
        StartInfo.WorkingDirectory = PclUiBridge.McInstanceSelectedGet().PathIndie;
        StartInfo.UseShellExecute = false;
        StartInfo.StandardOutputEncoding = McLaunchEncoding;
        StartInfo.StandardErrorEncoding = McLaunchEncoding;
        StartInfo.RedirectStandardOutput = true;
        StartInfo.RedirectStandardError = true;
        StartInfo.CreateNoWindow = true;
        StartInfo.Arguments = McLaunchArgument;

        // 开始进程
        Process GameProcess = PclCore.StartProcess(StartInfo);
        McLaunchLog("已启动游戏进程：" + McLaunchJavaSelected.JavaExePath);
        if (Loader.IsCanceled) {
            McLaunchLog("由于取消启动，已强制结束游戏进程"); // #1631
            GameProcess.Kill();
            return;
        }
        Loader.Output = GameProcess;
        McLaunchProcess = GameProcess;

        // 进程优先级处理
        try {
            GameProcess.PriorityBoostEnabled = true;
            switch (PclCore.GetSetting<int>("LaunchArgumentPriority")) {
                case 0: // 高
                    GameProcess.PriorityClass = ProcessPriorityClass.AboveNormal;
                    break;
                case 2: // 低
                    GameProcess.PriorityClass = ProcessPriorityClass.BelowNormal;
                    break;
                default: // 中
                    break;
            }
        } catch (Exception ex) {
            Logger.Error(ex, "设置进程优先级失败");
        }

    }

    #endregion

}
