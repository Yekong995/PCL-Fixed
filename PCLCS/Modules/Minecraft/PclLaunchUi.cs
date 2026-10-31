// 由 VB Modules\Minecraft\ModLaunch.vb 的登录与启动流程（UI 桥接较多的部分）迁移而来。
// 本类中的函数原先依赖大量 VB UI（登录页面 IsVaild/GetLoginData、MyMsgBox/MyMsgBoxSelect、WaitingMyMsgBox、
// FrmLaunchLeft/FrmLaunchRight/FrmMain、PageOtherTest.MemoryOptimize、ThemeUnlock、SetGPUPreference 等），
// 这些依赖均经 PclUiBridge 桥接，由 VB 宿主在启动时注册（见 ModBase.InitBridges）。
// VB 侧（ModLaunch.vb）对每个迁移函数保留同名转发壳；纯 UI 的启动编排
// （Private McLaunchStart(Loader)、McLaunchLoader 等模块字段）仍保留在 VB ModLaunch 中。
#nullable disable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using MeloongCore;
using MeloongCore.Extensions;
using Newtonsoft.Json.Linq;

namespace PCLCS.Modules;

/// <summary>
/// 由 VB Modules\Minecraft\ModLaunch.vb 迁移而来的登录与启动流程函数。
/// </summary>
public static class PclLaunchUi {

    #region 主登录模块

    /// <summary>
    /// 当前是否可以进行登录。若不可以则会返回错误原因。
    /// </summary>
    public static string McLoginAble() {
        switch (PclCore.GetSetting<McLoginType>("LoginType")) {
            case McLoginType.Ms:
                if (PclCore.GetSetting<string>("CacheMsV2OAuthRefresh") == "") {
                    return PclUiBridge.LoginPageValidate(McLoginType.Ms, null);
                } else {
                    return "";
                }
            case McLoginType.Legacy:
                return PclUiBridge.LoginPageValidate(McLoginType.Legacy, null);
            case McLoginType.Nide:
                if (PclCore.GetSetting<string>("CacheNideAccess") == "") {
                    return PclUiBridge.LoginPageValidate(McLoginType.Nide, null);
                } else {
                    return "";
                }
            case McLoginType.Auth:
                if (PclCore.GetSetting<string>("CacheAuthAccess") == "") {
                    return PclUiBridge.LoginPageValidate(McLoginType.Auth, null);
                } else {
                    return "";
                }
            default:
                return "未知的登录方式";
        }
    }
    /// <summary>
    /// 登录输入是否可以进行登录。若不可以则会返回错误原因。
    /// </summary>
    public static string McLoginAble(McLoginData LoginData) {
        switch (LoginData.Type) {
            case McLoginType.Ms:
                return PclUiBridge.LoginPageValidate(McLoginType.Ms, LoginData);
            case McLoginType.Legacy:
                return PclUiBridge.LoginPageValidate(McLoginType.Legacy, LoginData);
            case McLoginType.Nide:
                return PclUiBridge.LoginPageValidate(McLoginType.Nide, LoginData);
            case McLoginType.Auth:
                return PclUiBridge.LoginPageValidate(McLoginType.Auth, LoginData);
            default:
                return "未知的登录方式";
        }
    }

    // 从 UI 获取登录输入（按登录方式与缓存状态选择对应页面，页面调用在 VB 注册的 LoginPageGetData 桥中完成）
    public static McLoginData McLoginInput() {
        McLoginType LoginType = PclCore.GetSetting<McLoginType>("LoginType");
        try {
            return PclUiBridge.LoginPageGetData(LoginType);
        } catch (Exception ex) {
            throw new Exception($"获取登录输入信息失败（{LoginType}）", ex);
        }
    }
    // 登录主模块的编排：校验输入，选择对应登录方式的加载器并等待其完成
    public static void McLoginStart(LoaderTask<McLoginData, McLoginResult> Data) {
        PclLaunch.McLaunchLog("登录加载已开始");
        // 校验登录信息
        string CheckResult = McLoginAble(Data.Input);
        if (CheckResult != "") throw new ArgumentException(CheckResult);
        // 获取对应加载器
        LoaderBase Loader = PclUiBridge.McLoginSubLoaderGet(Data.Input.Type);
        // 尝试加载
        Loader.WaitForExit(Data.Input, PclUiBridge.McLoginLoaderGet(), Data.IsForceRestarting);
        Data.Output = ((dynamic)Loader).Output;
        PclCore.RunInUi(() => PclUiBridge.LaunchLeftRefreshPage(true, false)); // 刷新自动填充列表
        PclLaunch.McLaunchLog("登录加载已结束");
    }

    #endregion

    #region 分方式登录模块

    // 微软登录的主加载函数，返回所有需要的登录信息
    public static void McLoginMsStart(LoaderTask<McLoginMs, McLoginResult> Data) {
        McLoginMs Input = Data.Input;
        string LogUsername = Input.UserName;
        PclLaunch.McLaunchLog("登录方式：正版（" + (LogUsername == "" ? "尚未登录" : LogUsername) + "）");
        Data.Progress = 0.05;
        // 检查是否已经登录完成
        long ExpiresAt = PclCore.GetSetting<long>("CacheMsV2Expires");
        if (!Data.IsForceRestarting && // 不要求强行重启
           ExpiresAt > 0 && ExpiresAt > PclCore.ToUnixTimestampUtc(DateTime.UtcNow) && // AccessToken 尚未过期
           Input.UserName == PclCore.GetSetting<string>("CacheMsV2Name")) {
            Data.Output = new McLoginResult {
                Name = Input.UserName, Type = "Microsoft",
                AccessToken = PclCore.GetSetting<string>("CacheMsV2Access"),
                Uuid = PclCore.GetSetting<string>("CacheMsV2Uuid"),
                ClientToken = PclCore.GetSetting<string>("CacheMsV2Uuid"),
                ProfileJson = PclCore.GetSetting<string>("CacheMsV2ProfileJson")
            };
            PclLaunch.McLaunchLog("无需登录，AccessToken 尚未过期");
            goto SkipLogin;
        }
        // 尝试登录
        (string OAuthAccessToken, string OAuthRefreshToken) OAuthTokens = default;
        // 原 VB 的 GoTo Relogin 从“有 RefreshToken”分支跳入“无 RefreshToken”分支（跨块跳转），改为循环 + 标志位重构
        bool NeedNewLogin = Input.OAuthRefreshToken == "";
        while (true) {
            if (NeedNewLogin) {
                // 无 RefreshToken（或被要求重新登录）
                OAuthTokens = MsLoginStep1New(Data);
                break;
            } else {
                // 有 RefreshToken
                OAuthTokens = MsLoginStep1Refresh(Input.OAuthRefreshToken);
                if (OAuthTokens.OAuthAccessToken == "Relogin") {
                    NeedNewLogin = true; // 要求重新打开登录网页认证
                    continue;
                }
                break;
            }
        }
        if (Data.IsCanceled) throw new OperationCanceledException();
        Data.Progress = 0.25;
        if (Data.IsCanceled) throw new OperationCanceledException();
        string OAuthAccessToken = OAuthTokens.OAuthAccessToken;
        string OAuthRefreshToken = OAuthTokens.OAuthRefreshToken;
        string XBLToken = PclLaunch.MsLoginStep2(OAuthAccessToken);
        Data.Progress = 0.4;
        if (Data.IsCanceled) throw new OperationCanceledException();
        (string XSTSToken, string UHS) Tokens = MsLoginStep3(XBLToken);
        Data.Progress = 0.55;
        if (Data.IsCanceled) throw new OperationCanceledException();
        (string AccessToken, int ExpiresAt2) LoginResult = MsLoginStep4(Tokens);
        ExpiresAt = LoginResult.ExpiresAt2;
        Data.Progress = 0.7;
        if (Data.IsCanceled) throw new OperationCanceledException();
        MsLoginStep5(LoginResult.AccessToken);
        Data.Progress = 0.85;
        if (Data.IsCanceled) throw new OperationCanceledException();
        (string UUID, string UserName, string ProfileJson) Result = MsLoginStep6(LoginResult.AccessToken);
        Data.Progress = 0.98;
        // 输出登录结果
        PclCore.SetSetting("CacheMsV2OAuthRefresh", OAuthRefreshToken);
        PclCore.SetSetting("CacheMsV2Access", LoginResult.AccessToken);
        PclCore.SetSetting("CacheMsV2Uuid", Result.UUID);
        PclCore.SetSetting("CacheMsV2Name", Result.UserName);
        PclCore.SetSetting("CacheMsV2ProfileJson", Result.ProfileJson);
        PclCore.SetSetting("CacheMsV2Expires", LoginResult.ExpiresAt2);
        JObject MsJson = (JObject)PclCore.GetSetting<string>("LoginMsJson").DeserializeJson();
        MsJson.Remove(Input.UserName); // 如果更改了玩家名……
        MsJson[Result.UserName] = OAuthRefreshToken;
        PclCore.SetSetting("LoginMsJson", MsJson.ToString(Newtonsoft.Json.Formatting.None));
        Data.Output = new McLoginResult { AccessToken = LoginResult.AccessToken, Name = Result.UserName, Uuid = Result.UUID, Type = "Microsoft", ClientToken = Result.UUID, ProfileJson = Result.ProfileJson };
        // 结束
    SkipLogin:
        PclLaunch.McLaunchLog($"微软登录结束，AccessToken 将在 {DateTimeOffset.FromUnixTimeSeconds(ExpiresAt).LocalDateTime} 过期");
        PclCore.SetSetting("HintBuy", true); // 关闭正版购买提示
        if (PclUiBridge.ThemeUnlock(10, false)) PclUiBridge.ShowModpackMsgBox("感谢你对正版游戏的支持！" + Environment.NewLine + "隐藏主题 跳票红 已解锁！", "提示", null, null, null, false, false);
    }
    // Server 登录（Nide/Auth）的普通登录请求。原 VB 为 Friend（仅用于桥注册），迁移后改为 public 供 VB AddressOf 注册
    public static bool McLoginRequestLogin(LoaderTask<McLoginServer, McLoginResult> Data) {
        try {
            bool NeedRefresh = false;
            PclLaunch.McLaunchLog("登录开始（Login, " + Data.Input.Token + "）");
            JObject RequestData = new JObject(
                new JProperty("agent", new JObject(new JProperty("name", "Minecraft"), new JProperty("version", 1))),
                new JProperty("username", Data.Input.UserName),
                new JProperty("password", Data.Input.Password),
                new JProperty("requestUser", true));
            JObject LoginJson = (JObject)NetCore.NetRequestByClientRetry(
                Data.Input.BaseUrl + "/authenticate", HttpMethod.Post,
                Content: RequestData.ToString(Newtonsoft.Json.Formatting.None),
                Headers: new[,] { { "Accept-Language", "zh-CN" } },
                ContentType: "application/json; charset=utf-8", RequireJson: true).DeserializeJson();
            // 检查登录结果
            if (LoginJson["availableProfiles"].Count() == 0) {
                if (Data.Input.ForceReselectProfile) PclUiBridge.ShowHint("你还没有创建角色，无法更换！", HintType.Red);
                throw new Exception("$你还没有创建角色，请在创建角色后再试！");
            } else if (Data.Input.ForceReselectProfile && LoginJson["availableProfiles"].IsSingle()) {
                PclUiBridge.ShowHint("你的账户中只有一个角色，无法更换！", HintType.Red);
            }
            string SelectedName = null;
            string SelectedId = null;
            if ((LoginJson["selectedProfile"] is null || Data.Input.ForceReselectProfile) && LoginJson["availableProfiles"].Count() > 1) {
                // 要求选择档案；优先从缓存读取
                NeedRefresh = true;
                string CacheId = PclCore.GetSetting<string>("Cache" + Data.Input.Token + "Uuid");
                foreach (JToken Profile in LoginJson["availableProfiles"]) {
                    if (Profile["id"].ToString() == CacheId) {
                        SelectedName = Profile["name"].ToString();
                        SelectedId = Profile["id"].ToString();
                        PclLaunch.McLaunchLog("根据缓存选择的角色：" + SelectedName);
                    }
                }
                // 缓存无效，要求玩家选择
                if (SelectedName is null) {
                    PclLaunch.McLaunchLog("要求玩家选择角色");
                    PclCore.RunInUiWait(
                    () => {
                        // 原 VB 在此构造 List(Of IMyRadio)（MyRadioBox 为 VB UI 控件，C# 无法直接引用），
                        // 现将各角色的名称构造成文本列表传入 VB，由 VB 注册的桥构造选择控件
                        List<string> SelectionControl = new List<string>();
                        List<JToken> SelectionJson = new List<JToken>();
                        foreach (JToken Profile in LoginJson["availableProfiles"]) {
                            SelectionControl.Add(Profile["name"].ToString());
                            SelectionJson.Add(Profile);
                        }
                        int SelectedIndex = PclUiBridge.ShowMsgBoxSelect(SelectionControl, "选择使用的角色");
                        SelectedName = SelectionJson[SelectedIndex]["name"].ToString();
                        SelectedId = SelectionJson[SelectedIndex]["id"].ToString();
                    });
                    PclLaunch.McLaunchLog("玩家选择的角色：" + SelectedName);
                }
            } else {
                SelectedName = LoginJson["selectedProfile"]["name"].ToString();
                SelectedId = LoginJson["selectedProfile"]["id"].ToString();
            }
            // 将登录结果输出
            Data.Output.AccessToken = LoginJson["accessToken"].ToString();
            Data.Output.ClientToken = LoginJson["clientToken"].ToString();
            Data.Output.Name = SelectedName;
            Data.Output.Uuid = SelectedId;
            Data.Output.Type = Data.Input.Token;
            // 保存缓存
            PclCore.SetSetting("Cache" + Data.Input.Token + "Access", Data.Output.AccessToken);
            PclCore.SetSetting("Cache" + Data.Input.Token + "Client", Data.Output.ClientToken);
            PclCore.SetSetting("Cache" + Data.Input.Token + "Uuid", Data.Output.Uuid);
            PclCore.SetSetting("Cache" + Data.Input.Token + "Name", Data.Output.Name);
            PclCore.SetSetting("Cache" + Data.Input.Token + "Username", Data.Input.UserName);
            PclCore.SetSetting("Cache" + Data.Input.Token + "Pass", Data.Input.Password);
            PclLaunch.McLaunchLog("登录成功（Login, " + Data.Input.Token + "）");
            return NeedRefresh;
        } catch (Exception ex) {
            string AllMessage = ex.GetDisplay(false);
            Logger.Info(ex, "登录失败原始错误信息");
            // 读取服务器返回的错误
            if (ex is HttpRequestCodeException) {
                string ErrorMessage = null;
                try {
                    string Response = ((HttpRequestCodeException)ex).Response;
                    if (Response.ContainsIgnoreCase("errorMessage")) ErrorMessage = Response.DeserializeJson()["errorMessage"].ToString();
                } catch {
                }
                if (!string.IsNullOrWhiteSpace(ErrorMessage)) {
                    if (ErrorMessage.Contains("密码错误") || ErrorMessage.ContainsIgnoreCase("Incorrect username or password")) {
                        // 密码错误，退出登录 (#5090)
                        PclLaunch.McLaunchLog("密码错误，退出登录");
                        PclUiBridge.LoginPageExitLogin(Data.Input.Type);
                    }
                    throw new Exception("$登录失败：" + ErrorMessage);
                }
            }
            // 通用关键字检测
            if (AllMessage.Contains("(403)")) {
                switch (Data.Input.Type) {
                    case McLoginType.Auth:
                        throw new Exception("$登录失败，以下为可能的原因：" + Environment.NewLine +
                                            " - 输入的账号或密码错误。" + Environment.NewLine +
                                            " - 登录尝试过于频繁，导致被暂时屏蔽。请不要操作，等待 10 分钟后再试。" + Environment.NewLine +
                                            " - 只注册了账号，但没有在皮肤站新建角色。");
                    case McLoginType.Nide:
                        throw new Exception("$登录失败，以下为可能的原因：" + Environment.NewLine +
                                            " - 输入的账号或密码错误。" + Environment.NewLine +
                                            " - 密码错误次数过多，导致被暂时屏蔽。请不要操作，等待 10 分钟后再试。" + Environment.NewLine +
                                            (Data.Input.UserName.Contains("@") ? "" : " - 登录账号应为邮箱或统一通行证账号，而非游戏角色 ID。" + Environment.NewLine) +
                                            " - 只注册了账号，但没有加入对应服务器。");
                }
            } else if (ex.IsBadNetwork()) {
                throw new Exception("$登录失败：你的网络环境不佳，导致难以连接到海外服务器。" + Environment.NewLine + "请稍后再试，或使用加速器、VPN 改善网络环境。");
            } else if (ex.Message.StartsWithF("$")) {
                throw;
            } else {
                throw new Exception("登录失败：" + ex.Message, ex);
            }
            return false;
        }
    }

    // 微软登录步骤 1，原始登录：获取 DeviceCode 并开启登录网页
    private static (string OAuthAccessToken, string OAuthRefreshToken) MsLoginStep1New(LoaderTask<McLoginMs, McLoginResult> Data) {
        // 参考：https://learn.microsoft.com/zh-cn/entra/identity-platform/v2-oauth2-device-code

        // 初始请求。原 VB 的 GoTo Retry 从弹窗分支跳回函数开头（跨块跳转），改为 while 循环重构
        while (true) {
            PclLaunch.McLaunchLog("开始微软登录步骤 1/6（原始登录）");
            JObject PrepareJson = (JObject)NetCore.NetRequestByClientRetry("https://login.microsoftonline.com/consumers/oauth2/v2.0/devicecode", HttpMethod.Post,
                Content: $"client_id={ModSecret.OAuthClientId}&tenant=/consumers&scope=XboxLive.signin%20offline_access",
                ContentType: "application/x-www-form-urlencoded", RequireJson: true).DeserializeJson();
            PclLaunch.McLaunchLog("网页登录地址：" + PrepareJson["verification_uri"].ToString());

            // 弹窗（VB 注册的桥：反序列化 JSON、以 MyMsgBoxType.Login 入队并返回 Converter，C# 侧轮询其 Result）
            object Converter = PclUiBridge.EnqueueWaitingMsgBox(PrepareJson.ToString(Newtonsoft.Json.Formatting.None), true);
            while (((dynamic)Converter).Result is null) {
                Thread.Sleep(100);
            }
            if (((dynamic)Converter).Result is RestartException) {
                if (PclUiBridge.ShowMsgBoxActions($"请在登录时选择 {PclCore.vbLQ}其他登录方法{PclCore.vbRQ}，然后选择 {PclCore.vbLQ}使用我的密码{PclCore.vbRQ}。{Environment.NewLine}如果没有该选项，请选择 {PclCore.vbLQ}设置密码{PclCore.vbRQ}，设置完毕后再登录。",
                    "需要使用密码登录", "重新登录", "设置密码", "取消",
                    null, () => PclUiBridge.OpenWebsite("https://account.live.com/password/Change")) == 1) {
                    continue; // GoTo Retry
                } else {
                    throw new OperationCanceledException();
                }
            } else if (((dynamic)Converter).Result is Exception Ex) {
                throw Ex;
            } else {
                string[] Result = (string[])((dynamic)Converter).Result;
                return (Result[0], Result[1]);
            }
        }
    }
    // 微软登录步骤 1，刷新登录：从 OAuth Code 或 OAuth RefreshToken 获取 {OAuth AccessToken, OAuth RefreshToken}
    private static (string OAuthAccessToken, string OAuthRefreshToken) MsLoginStep1Refresh(string Code) {
        PclLaunch.McLaunchLog("开始微软登录步骤 1/6（刷新登录）");

        string Result;
        try {
            Result = NetCore.NetRequestByClientMultiple("https://login.live.com/oauth20_desktop.srf", HttpMethod.Post,
                Content: $"client_id={ModSecret.OAuthClientId}&refresh_token={StringUtils.FormUrlEscape(Code)}&grant_type=refresh_token&scope=XboxLive.signin%20offline_access",
                ContentType: "application/x-www-form-urlencoded",
                Headers: new[,] { { "Accept-Language", "en-US,en;q=0.5" }, { "X-Requested-With", "XMLHttpRequest" } },
                ThreadCount: 2);
        } catch (HttpRequestCodeException ex) {
            // 修改错误列表时，同时检查 MyMsgLogin.xaml.vb 中的对应代码
            string Response = ex.Response;
            if (Response.ContainsIgnoreCase("must sign in again") || Response.ContainsIgnoreCase("password expired") ||
               (Response.Contains("refresh_token") && Response.ContainsIgnoreCase("is not valid")) || // #269
               Response.ContainsIgnoreCase("expired")) { // #8611
                return ("Relogin", "");
            } else if (Response.Contains("Account security interrupt")) {
                PclUiBridge.ShowModpackMsgBox("该账号由于安全问题无法登陆，请前往微软账户页获取更多信息。", "登录失败", "我知道了", null, null, true, false);
                throw new OperationCanceledException();
            } else if (Response.Contains("service abuse")) {
                PclUiBridge.ShowModpackMsgBox("非常抱歉，该账号已被微软封禁，无法登录。", "登录失败", "我知道了", null, null, true, false);
                throw new OperationCanceledException();
            } else {
                throw;
            }
        }

        JObject ResultJson = (JObject)Result.DeserializeJson();
        string AccessToken = ResultJson["access_token"].ToString();
        string RefreshToken = ResultJson["refresh_token"].ToString();
        return (AccessToken, RefreshToken);
    }
    // 微软登录步骤 3：从 XBLToken 获取 {XSTSToken, UHS}
    private static (string XSTSToken, string UHS) MsLoginStep3(string XBLToken) {
        PclLaunch.McLaunchLog("开始微软登录步骤 3/6");

        string Request = @"{
           ""Properties"": {
               ""SandboxId"": ""RETAIL"",
               ""UserTokens"": [""" + XBLToken + @"""]
           },
           ""RelyingParty"": ""rp://api.minecraftservices.com/"",
           ""TokenType"": ""JWT""
        }";
        string Result;
        try {
            Result = NetCore.NetRequestByClientMultiple("https://xsts.auth.xboxlive.com/xsts/authorize", HttpMethod.Post,
                Content: Request, ContentType: "application/json", RequireJson: true);
        } catch (HttpRequestCodeException ex) {
            // 参考 https://github.com/PrismarineJS/prismarine-auth/blob/master/src/common/Constants.js
            if (ex.Response.Contains("2148916227")) {
                PclUiBridge.ShowModpackMsgBox("该账号似乎已被微软封禁，无法登录。", "登录失败", "我知道了", null, null, true, false);
                throw new OperationCanceledException();
            } else if (ex.Response.Contains("2148916233")) {
                if (PclUiBridge.ShowModpackMsgBox("你尚未注册 Xbox 账户，请在注册后再登录。", "登录提示", "注册", "取消", null, false, false) == 1) {
                    PclUiBridge.OpenWebsite("https://signup.live.com/signup");
                }
                throw new OperationCanceledException();
            } else if (ex.Response.Contains("2148916235")) {
                PclUiBridge.ShowModpackMsgBox($"你的网络所在的国家或地区无法登录微软账号。{Environment.NewLine}请使用加速器或 VPN，然后再试。", "登录失败", "我知道了", null, null, false, false);
                throw new OperationCanceledException();
            } else if (ex.Response.Contains("2148916238")) {
                if (PclUiBridge.ShowModpackMsgBox("该账号年龄不足，你需要先修改出生日期，然后才能登录。" + Environment.NewLine +
                            "该账号目前填写的年龄是否在 13 岁以上？", "登录提示", "13 岁以上", "12 岁以下", "我不知道", false, false) == 1) {
                    PclUiBridge.OpenWebsite("https://account.live.com/editprof.aspx");
                    PclUiBridge.ShowModpackMsgBox("请在打开的网页中修改账号的出生日期（至少改为 18 岁以上）。" + Environment.NewLine +
                             "在修改成功后等待一分钟，然后再回到 PCL，就可以正常登录了！", "登录提示", null, null, null, false, false);
                } else {
                    PclUiBridge.OpenWebsite("https://support.microsoft.com/zh-cn/account-billing/如何更改-microsoft-帐户上的出生日期-837badbc-999e-54d2-2617-d19206b9540a");
                    PclUiBridge.ShowModpackMsgBox("请根据打开的网页的说明，修改账号的出生日期（至少改为 18 岁以上）。" + Environment.NewLine +
                             "在修改成功后等待一分钟，然后再回到 PCL，就可以正常登录了！", "登录提示", null, null, null, false, false);
                }
                throw new OperationCanceledException();
            } else {
                throw;
            }
        }

        JObject ResultJson = (JObject)Result.DeserializeJson();
        return (
            ResultJson["Token"].ToString(), // XSTSToken
            ResultJson["DisplayClaims"]["xui"][0]["uhs"].ToString() // UHS
        );
    }
    // 微软登录步骤 4：从 {XSTSToken, UHS} 获取 {Minecraft AccessToken, 过期于 (Unix 时间戳)}
    private static (string AccessToken, int ExpiresAt) MsLoginStep4((string XSTSToken, string UHS) Tokens) {
        PclLaunch.McLaunchLog("开始微软登录步骤 4/6");

        string Request = new JObject(new JProperty("identityToken", $"XBL3.0 x={Tokens.UHS};{Tokens.XSTSToken}")).ToString(0);
        string Result;
        try {
            Result = NetCore.NetRequestByClientRetry("https://api.minecraftservices.com/authentication/login_with_xbox", HttpMethod.Post,
                Content: Request, ContentType: "application/json", RequireJson: true);
        } catch (HttpRequestCodeException ex) {
            if (ex.StatusCode == (HttpStatusCode)429) {
                Logger.Warn(ex, "微软登录第 4 步汇报 429");
                throw new Exception("$登录尝试太过频繁，请等待几分钟后再试！");
            } else if (ex.StatusCode == HttpStatusCode.Forbidden) {
                Logger.Warn(ex, "微软登录第 4 步汇报 403");
                throw new Exception("$当前 IP 的登录尝试异常。" + Environment.NewLine + "如果你使用了 VPN 或加速器，请把它们关掉或更换节点后再试！");
            } else if (ex.StatusCode == HttpStatusCode.ServiceUnavailable) {
                Logger.Warn(ex, "微软登录第 4 步汇报 503");
                throw new Exception("$Mojang 服务器出现问题，请稍后再试。" + Environment.NewLine + "你的网络是正常的，PCL 也是正常的，是 Mojang 出问题了……");
            } else if (ex.Response?.Contains("ACCOUNT_SUSPENDED") ?? false) { // #8655
                PclUiBridge.ShowModpackMsgBox("该账号似乎已被微软封禁，无法登录。", "登录失败", "我知道了", null, null, true, false);
                throw new OperationCanceledException();
            } else {
                throw;
            }
        }

        JObject ResultJson = (JObject)Result.DeserializeJson();
        return (
            ResultJson["access_token"].ToString(),
            (int)(ResultJson["expires_in"].ToObject<int>() + PclCore.GetUnixTimestampUtc() - 3600)); // 提前 1 小时视作过期；VB 的返回类型为 Integer，此处与 VB 的隐式窄化转换一致
    }
    // 微软登录步骤 5：验证微软账号是否持有 MC，这也会刷新 XGP
    private static void MsLoginStep5(string AccessToken) {
        PclLaunch.McLaunchLog("开始微软登录步骤 5/6");

        string Result = NetCore.NetRequestByClientMultiple("https://api.minecraftservices.com/entitlements/mcstore",
            ContentType: "application/json", ThreadCount: 2, Headers: new[,] { { "Authorization", "Bearer " + AccessToken } }, RequireJson: true);
        try {
            JObject ResultJson = (JObject)Result.DeserializeJson();
            if (!(ResultJson.ContainsKey("items") && ResultJson["items"].Any())) {
                switch (PclUiBridge.ShowModpackMsgBox("你尚未购买正版 Minecraft，或者 Xbox Game Pass 已到期。", "登录失败", "购买 Minecraft", "取消", null, false, false)) {
                    case 1:
                        PclUiBridge.OpenWebsite("https://www.xbox.com/zh-cn/games/store/minecraft-java-bedrock-edition-for-pc/9nxp44l49shj");
                        break;
                }
                throw new OperationCanceledException();
            }
        } catch (Exception ex) {
            Logger.Warn(ex, $"微软登录第 5 步异常：{Result}");
            throw;
        }
    }
    // 微软登录步骤 6：从 Minecraft AccessToken 获取 {UUID, UserName, ProfileJson}
    private static (string UUID, string UserName, string ProfileJson) MsLoginStep6(string AccessToken) {
        PclLaunch.McLaunchLog("开始微软登录步骤 6/6");

        string Result;
        try {
            Result = NetCore.NetRequestByClientMultiple("https://api.minecraftservices.com/minecraft/profile",
                ContentType: "application/json", ThreadCount: 2, Headers: new[,] { { "Authorization", "Bearer " + AccessToken } }, RequireJson: true);
        } catch (HttpRequestCodeException ex) {
            switch (ex.StatusCode) {
                case (HttpStatusCode)429:
                    Logger.Warn(ex, "微软登录第 6 步汇报 429");
                    throw new Exception("$登录尝试太过频繁，请等待几分钟后再试！");
                case HttpStatusCode.NotFound:
                    Logger.Warn(ex, "微软登录第 6 步汇报 404");
                    PclCore.RunInNewThread(
                    () => {
                        switch (PclUiBridge.ShowModpackMsgBox("请先创建 Minecraft 玩家档案，然后再重新登录。", "登录失败", "创建档案", "取消", null, false, false)) {
                            case 1:
                                PclUiBridge.OpenWebsite("https://www.minecraft.net/zh-hans/msaprofile/mygames/editprofile");
                                break;
                        }
                    }, "Login Failed: Create Profile");
                    throw new OperationCanceledException();
                default:
                    throw;
            }
        }
        JObject ResultJson = (JObject)Result.DeserializeJson();
        string UUID = ResultJson["id"].ToString();
        string UserName = ResultJson["name"].ToString();
        return (UUID, UserName, Result);
    }

    #endregion

    #region 开始

    /// <summary>
    /// 尝试启动 Minecraft。必须在 UI 线程调用。
    /// 返回是否实际开始了启动（如果没有，则一定弹出了错误提示）。
    /// </summary>
    public static bool McLaunchStart(McLaunchOptions Options = null) {
        PclLaunch.CurrentLaunchOptions = Options ?? new McLaunchOptions();
        // 预检查
        if (!PclCore.RunInUi()) throw new Exception("McLaunchStart 必须在 UI 线程调用！");
        if (PclUiBridge.McLaunchLoaderStateGet() == LoadState.Loading) {
            PclUiBridge.ShowHint("已有游戏正在启动中！", HintType.Red);
            return false;
        }
        // 强制切换需要启动的版本
        if (PclLaunch.CurrentLaunchOptions.Instance is not null && PclUiBridge.McInstanceSelectedGet() != PclLaunch.CurrentLaunchOptions.Instance) {
            PclLaunch.McLaunchLog("在启动前切换到版本 " + PclLaunch.CurrentLaunchOptions.Instance.Name);
            // 检查版本是否存在
            try {
                PclLaunch.CurrentLaunchOptions.Instance.GetJsonPath();
            } catch {
                PclUiBridge.ShowHint($"无法启动 {PclLaunch.CurrentLaunchOptions.Instance.Name}：当前 Minecraft 文件夹中尚未安装该版本！", HintType.Red);
                return false;
            }
            // 检查版本
            PclLaunch.CurrentLaunchOptions.Instance.Load();
            if (PclLaunch.CurrentLaunchOptions.Instance.State == McInstance.McInstanceState.Error) {
                PclUiBridge.ShowHint($"无法启动 {PclLaunch.CurrentLaunchOptions.Instance.Name}：{PclLaunch.CurrentLaunchOptions.Instance.Info}", HintType.Red);
                return false;
            }
            // 切换版本
            PclUiBridge.McInstanceSelectedSet(PclLaunch.CurrentLaunchOptions.Instance);
            PclUiBridge.LaunchLeftRefreshButtons();
            PclUiBridge.LaunchLeftRefreshPage(false, false);
        }
        if (PclUiBridge.McInstanceSelectedGet() is not null && McInstanceLock.IsLocked(PclUiBridge.McInstanceSelectedGet())) {
            PclUiBridge.ShowHint($"版本 {PclUiBridge.McInstanceSelectedGet().Name} 正在被修改，请稍后再启动！", HintType.Red);
            return false;
        }
        PclUiBridge.AprilGiveup();
        // 禁止进入版本选择页面（否则就可以在启动中切换 McInstanceSelected 了）
        PclUiBridge.FilterPageStack();
        // 实际启动加载器
        PclUiBridge.McLaunchLoaderStart(Options);
        return true;
    }

    // 启动状态切换
    public static void McLaunchState(LoaderTask<McLaunchOptions, object> Loader) {
        switch (PclUiBridge.McLaunchLoaderStateGet()) {
            case LoadState.Finished:
            case LoadState.Failed:
            case LoadState.Waiting:
            case LoadState.Canceled:
                PclUiBridge.LaunchLeftPageChangeToLogin();
                break;
            case LoadState.Loading:
                // 在预检测结束后再触发动画
                PclUiBridge.ClearLaunchLog();
                break;
        }
    }

    #endregion

    #region 内存优化

    public static void McLaunchMemoryOptimize(LoaderTask<int, int> Loader) {
        PclLaunch.McLaunchLog("内存优化开始");
        bool Finished = false;
        PclCore.RunInNewThread(
        () => {
            PclUiBridge.MemoryOptimizeFull(false);
            Finished = true;
        }, "Launch Memory Optimize");
        while (!Finished && !Loader.IsCanceled) {
            if (Loader.Progress < 0.7) {
                Loader.Progress += 0.007; // 10s
            } else {
                Loader.Progress += (0.95 - Loader.Progress) * 0.02; // 最快 += 0.005
            }
            Thread.Sleep(100);
        }
    }

    #endregion

    #region 预检测

    public static void McLaunchPrecheck() {
        if (PclCore.GetSetting<bool>("SystemDebugDelay")) Thread.Sleep(PclCore.RandomInteger(100, 2000));
        // 检查路径
        if (PclUiBridge.McInstanceSelectedGet().PathIndie.Contains("!") || PclUiBridge.McInstanceSelectedGet().PathIndie.Contains(";")) throw new Exception("游戏路径中不可包含 ! 或 ;（" + PclUiBridge.McInstanceSelectedGet().PathIndie + "）");
        if (PclUiBridge.McInstanceSelectedGet().PathVersion.Contains("!") || PclUiBridge.McInstanceSelectedGet().PathVersion.Contains(";")) throw new Exception("游戏路径中不可包含 ! 或 ;（" + PclUiBridge.McInstanceSelectedGet().PathVersion + "）");
        // 检查版本
        if (PclUiBridge.McInstanceSelectedGet() is null) throw new Exception("未选择 Minecraft 版本！");
        PclUiBridge.McInstanceSelectedGet().Load();
        if (PclUiBridge.McInstanceSelectedGet().State == McInstance.McInstanceState.Error) throw new Exception("Minecraft 存在问题：" + PclUiBridge.McInstanceSelectedGet().Info);
        // 检查输入信息
        string CheckResult = "";
        PclCore.RunInUiWait(() => CheckResult = McLoginAble(McLoginInput()));
        if (CheckResult != "") throw new ArgumentException(CheckResult);
        // 求赞助
        if (PclUiBridge.BuildTypeNumberGet() == 50 && PclLaunch.CurrentLaunchOptions?.SaveBatch is null) { // BuildTypes.Release = 50；保存脚本时不提示
            PclCore.RunInNewThread(
            () => {
                switch (PclCore.GetSetting<int>("SystemLaunchCount")) {
                    case 10: case 20: case 40: case 60: case 80: case 100: case 120: case 150: case 200: case 250: case 300: case 350: case 400: case 500: case 600: case 700: case 800: case 900: case 1000: case 1200: case 1400: case 1600: case 1800: case 2000:
                        if (PclUiBridge.ShowModpackMsgBox("PCL 已经为你启动了 " + PclCore.GetSetting<int>("SystemLaunchCount") + " 次游戏啦！" + Environment.NewLine +
                                    "如果 PCL 还算好用的话，能不能考虑赞助一下 PCL……" + Environment.NewLine +
                                    "如果没有大家的支持，PCL 很难在免费、无任何广告的情况下维持数年的更新（磕头）……！",
                                    PclCore.GetSetting<int>("SystemLaunchCount") + " 次启动！", "支持 PCL！", "但是我拒绝", null, false, false) == 1) {
                            PclUiBridge.OpenWebsite("https://meloong.com/afd/a/LTCat");
                        }
                        break;
                }
            }, "Donate");
        }
        // 正版购买提示
        if (PclLaunch.CurrentLaunchOptions?.SaveBatch is null && // 保存脚本时不提示
           !PclCore.GetSetting<bool>("HintBuy") && PclCore.GetSetting<McLoginType>("LoginType") != McLoginType.Ms) {
            if (CultureInfo.CurrentCulture.Name.StartsWithF("zh") || CultureInfo.CurrentUICulture.Name.StartsWithF("zh")) { // 中文？
                PclCore.RunInNewThread(
                () => {
                    switch (PclCore.GetSetting<int>("SystemLaunchCount")) {
                        case 3: case 8: case 15: case 30: case 50: case 70: case 90: case 110: case 130: case 180: case 220: case 280: case 330: case 380: case 450: case 550: case 660: case 750: case 880: case 950: case 1100: case 1300: case 1500: case 1700: case 1900:
                            if (PclUiBridge.ShowModpackMsgBox("你已经启动了 " + PclCore.GetSetting<int>("SystemLaunchCount") + " 次 Minecraft 啦！" + Environment.NewLine +
                                "如果觉得 Minecraft 还不错，可以购买正版支持一下，毕竟开发游戏也真的很不容易……不要一直白嫖啦。" + Environment.NewLine + Environment.NewLine +
                                "在登录一次正版账号后，就不会再出现这个提示了！",
                                "考虑一下正版？", "支持正版游戏！", "下次一定", null, false, false) == 1) {
                                PclUiBridge.OpenWebsite("https://www.xbox.com/zh-cn/games/store/minecraft-java-bedrock-edition-for-pc/9nxp44l49shj");
                            }
                            break;
                    }
                }, "Buy Minecraft");
            } else if (PclCore.GetSetting<McLoginType>("LoginType") == McLoginType.Legacy) {
                switch (PclUiBridge.ShowMsgBoxActions("你必须先登录正版账号，才能进行离线登录！", "正版验证", "购买正版", "试玩", "返回",
                    () => PclUiBridge.OpenWebsite("https://www.xbox.com/zh-cn/games/store/minecraft-java-bedrock-edition-for-pc/9nxp44l49shj"), null)) {
                    case 2:
                        PclUiBridge.ShowHint("游戏将以试玩模式启动！", HintType.Red);
                        PclLaunch.CurrentLaunchOptions.ExtraGameArgs.Add("--demo");
                        break;
                    case 3:
                        throw new OperationCanceledException();
                }
            }
        }
    }

    #endregion

    #region 启动与前后处理

    public static void McLaunchPrerun() {

        // 使用高性能显卡
        if (PclCore.GetSetting<bool>("LaunchAdvanceGraphicCard")) {
            try {
                PclUiBridge.SetGpuPreference(PclLaunch.McLaunchJavaSelected.JavaExePath);
                PclUiBridge.SetGpuPreference(PclCore.PathExe);
            } catch (Exception ex) {
                if (WindowsUtils.HasAdminRole()) {
                    Logger.Warn(ex, "直接调整显卡设置失败");
                } else {
                    Logger.Warn(ex, "直接调整显卡设置失败，将以管理员权限重启 PCL 再次尝试");
                    try {
                        if (PclCore.RunAsAdmin($"--gpu \"{PclLaunch.McLaunchJavaSelected.JavaExePath}\"") == (int)ProcessReturnValues.TaskDone) {
                            PclLaunch.McLaunchLog("以管理员权限重启 PCL 并调整显卡设置成功");
                        } else {
                            throw new Exception("调整过程中出现异常");
                        }
                    } catch (Exception exx) {
                        Logger.Error(exx, "调整显卡设置失败，Minecraft 可能会使用默认显卡运行", LogBehavior.Toast);
                    }
                }
            }
        }

        // 更新 launcher_profiles.json
        try {
            // 确保可用（原 VB 为 Exit Try，改为条件包裹）
            if (PclUiBridge.McLoginLoaderOutputGet().Type == "Microsoft") {
                PclUiBridge.McFolderLauncherProfilesJsonCreate(PclUiBridge.McFolderSelectedGet());
                // 构建需要替换的 Json 对象
                string ReplaceJsonString = $@"
            {{
              ""authenticationDatabase"": {{
                ""00000111112222233333444445555566"": {{
                  ""username"": ""{PclUiBridge.McLoginLoaderOutputGet().Name.Replace("\"", "-")}"",
                  ""profiles"": {{
                    ""66666555554444433333222221111100"": {{
                        ""displayName"": ""{PclUiBridge.McLoginLoaderOutputGet().Name}""
                    }}
                  }}
                }}
              }},
              ""clientToken"": ""{PclUiBridge.McLoginLoaderOutputGet().ClientToken}"",
              ""selectedUser"": {{
                ""account"": ""00000111112222233333444445555566"",
                ""profile"": ""66666555554444433333222221111100""
              }}
            }}";
                JObject ReplaceJson = (JObject)ReplaceJsonString.DeserializeJson();
                // 更新文件
                JObject Profiles = (JObject)FileUtils.ReadAsJson(PclUiBridge.McFolderSelectedGet() + "launcher_profiles.json");
                Profiles.Merge(ReplaceJson);
                FileUtils.Write(PclUiBridge.McFolderSelectedGet() + "launcher_profiles.json", Profiles.ToString(), encoding: Encoding.GetEncoding("GB18030"));
                PclLaunch.McLaunchLog("已更新 launcher_profiles.json");
            }
        } catch (Exception ex) {
            Logger.Warn(ex, "更新 launcher_profiles.json 失败，将在删除文件后重试");
            try {
                FileUtils.Delete(PclUiBridge.McFolderSelectedGet() + "launcher_profiles.json");
                PclUiBridge.McFolderLauncherProfilesJsonCreate(PclUiBridge.McFolderSelectedGet());
                // 构建需要替换的 Json 对象
                string ReplaceJsonString = $@"
                    {{
                      ""authenticationDatabase"": {{
                        ""00000111112222233333444445555566"": {{
                          ""username"": ""{PclUiBridge.McLoginLoaderOutputGet().Name.Replace("\"", "-")}"",
                          ""profiles"": {{
                            ""66666555554444433333222221111100"": {{
                                ""displayName"": ""{PclUiBridge.McLoginLoaderOutputGet().Name}""
                            }}
                          }}
                        }}
                      }},
                      ""clientToken"": ""{PclUiBridge.McLoginLoaderOutputGet().ClientToken}"",
                      ""selectedUser"": {{
                        ""account"": ""00000111112222233333444445555566"",
                        ""profile"": ""66666555554444433333222221111100""
                      }}
                    }}";
                JObject ReplaceJson = (JObject)ReplaceJsonString.DeserializeJson();
                // 更新文件
                JObject Profiles = (JObject)FileUtils.ReadAsJson(PclUiBridge.McFolderSelectedGet() + "launcher_profiles.json");
                Profiles.Merge(ReplaceJson);
                FileUtils.Write(PclUiBridge.McFolderSelectedGet() + "launcher_profiles.json", Profiles.ToString(), encoding: Encoding.GetEncoding("GB18030"));
                PclLaunch.McLaunchLog("已在删除后更新 launcher_profiles.json");
            } catch (Exception exx) {
                Logger.Error(exx, "更新 launcher_profiles.json 失败");
            }
        }

        // 更新 options.txt
        string SetupFileAddress = PclUiBridge.McInstanceSelectedGet().PathIndie + "options.txt";
        if (!FileUtils.Exists(SetupFileAddress)) {
            // Yosbr Mod 兼容（#2385）：https://www.curseforge.com/minecraft/mc-mods/yosbr
            string YosbrFileAddress = PclUiBridge.McInstanceSelectedGet().PathIndie + "config\\yosbr\\options.txt";
            if (FileUtils.Exists(YosbrFileAddress)) {
                PclLaunch.McLaunchLog("将修改 Yosbr Mod 中的 options.txt");
                SetupFileAddress = YosbrFileAddress;
                PclCore.WriteIni(SetupFileAddress, "lang", "none"); // 忽略默认语言
            }
        }
        try {
            // 语言
            // 1.0-     ：没有语言选项
            // 1.1 ~ 5  ：zh_CN 时正常，zh_cn 时崩溃（最后两位字母必须大写，否则将会 NPE 崩溃）
            // 1.6 ~ 10 ：zh_CN 时正常，zh_cn 时自动切换为英文
            // 1.11 ~ 12：zh_cn 时正常，zh_CN 时虽然显示了中文但语言设置会错误地显示选择英文
            // 1.13+    ：zh_cn 时正常，zh_CN 时自动切换为英文
            string CurrentLang = PclCore.ReadIni(SetupFileAddress, "lang", "none");
            string RequiredLang = CurrentLang == "none" || !DirectoryUtils.Exists(PclUiBridge.McInstanceSelectedGet().PathIndie + "saves") ? // #3844，整合包可能已经自带了 options.txt
                (PclCore.GetSetting<bool>("ToolHelpChinese") ? "zh_cn" : "en_us") : CurrentLang.Lower();
            if (!PclUiBridge.McInstanceSelectedGet().Version.Valid || PclUiBridge.McInstanceSelectedGet().Version.Vanilla.Major <= 10) {
                // 将最后两位改为大写，前面的部分保留
                RequiredLang = RequiredLang.Substring(0, RequiredLang.Length - 2) + RequiredLang.Substring(RequiredLang.Length - 2).Upper();
            }
            if (CurrentLang == RequiredLang) {
                PclLaunch.McLaunchLog($"需要的语言为 {RequiredLang}，当前语言为 {CurrentLang}，无需修改");
            } else {
                PclCore.WriteIni(SetupFileAddress, "lang", "-"); // 触发缓存更改，避免删除后重新下载残留缓存
                PclCore.WriteIni(SetupFileAddress, "lang", RequiredLang);
                PclLaunch.McLaunchLog($"已将语言从 {CurrentLang} 修改为 {RequiredLang}");
            }
            // ''如果是初次设置，一并修改 forceUnicodeFont
            // 'If PclCore.GetSetting(Of Boolean)("ToolHelpChinese") AndAlso (CurrentLang = "none" OrElse Not DirectoryUtils.Exists(McInstanceSelected.PathIndie & "saves")) Then
            // '    PclCore.WriteIni(SetupFileAddress, "forceUnicodeFont", "true")
            // '    McLaunchLog("已开启 forceUnicodeFont")
            // 'End If
            // 窗口
            switch (PclCore.GetSetting<int>("LaunchArgumentWindowType")) {
                case 0: // 全屏
                    PclCore.WriteIni(SetupFileAddress, "fullscreen", "true");
                    break;
                case 1: // 默认
                    break;
                default: // 其他
                    PclCore.WriteIni(SetupFileAddress, "fullscreen", "false");
                    break;
            }
        } catch (Exception ex) {
            Logger.Error(ex, "更新 options.txt 失败", LogBehavior.Toast);
        }

        // 离线皮肤 Alex 警告
        if (PclUiBridge.McInstanceSelectedGet().Version.Vanilla.Major <= 7 && PclUiBridge.McInstanceSelectedGet().Version.Vanilla.Major >= 2 && // 1.2 ~ 1.7
               PclUiBridge.McLoginLoaderInputGet().Type == McLoginType.Legacy && // 离线登录
               (PclCore.GetSetting<int>("LaunchSkinType") == 2 || // 强制 Alex
               (PclCore.GetSetting<int>("LaunchSkinType") == 4 && PclCore.GetSetting<bool>("LaunchSkinSlim")))) { // 或选用 Alex 皮肤
            PclUiBridge.ShowHint("此 Minecraft 版本尚不支持 Alex 皮肤，你的皮肤可能会显示为 Steve！", HintType.Red);
        }

        // 离线皮肤资源包
        try {
            string ZipFileAddress = PclUiBridge.McInstanceSelectedGet().PathIndie + "resourcepacks\\PCL2 Skin.zip";
            bool NewTypeSetup = PclUiBridge.McInstanceSelectedGet().Version.Vanilla.Major >= 13 || PclUiBridge.McInstanceSelectedGet().Version.Vanilla.Major < 6;
            if (PclUiBridge.McLoginLoaderInputGet().Type == McLoginType.Legacy && PclCore.GetSetting<int>("LaunchSkinType") == 4 && FileUtils.Exists(Paths.AppDataThenName + "CustomSkin.png")) {
                string MetaFileAddress = PclCore.GetPathTemp() + "pack.mcmeta";
                string PackPicAddress = PclCore.GetPathTemp() + "pack.png";
                if (PclUiBridge.McInstanceSelectedGet().Version.Vanilla.Major < 6) {
                    PclLaunch.McLaunchLog("Minecraft 版本过老，尚不支持自定义离线皮肤");
                    goto IgnoreCustomSkin;
                }
                JToken PackVersionJToken = PclUiBridge.McInstanceSelectedGet().JsonVersion?["pack_version"];
                if (PackVersionJToken is JObject) PackVersionJToken = ((JObject)PackVersionJToken)["resource"]; // 从 1.17 开始，可以从 pack_version.resource 获取资源包版本
                int PackVersion = Convert.ToInt32(PclCore.Val(PackVersionJToken?.ToString()));
                if (PackVersion <= 0) { // 没有对应字段的老版本 MC，或兜底
                    switch (PclUiBridge.McInstanceSelectedGet().Version.Vanilla.Major) {
                        case 6: case 7: case 8:
                            PackVersion = 1;
                            break;
                        case 9: case 10:
                            PackVersion = 2;
                            break;
                        case 11: case 12:
                            PackVersion = 3;
                            break;
                        case 13: case 14:
                            PackVersion = 4;
                            break;
                        case 15:
                            PackVersion = 5;
                            break;
                        case 16:
                            PackVersion = PclUiBridge.McInstanceSelectedGet().Version.Vanilla.Build <= 1 ? 5 : 6;
                            break;
                        case 17:
                            PackVersion = 7;
                            break;
                        case 18:
                            PackVersion = 8;
                            break;
                        default: // 旧版快照可能没有可用的资源包格式声明
                            PackVersion = 17;
                            break;
                    }
                }
                PclLaunch.McLaunchLog($"正在构建自定义皮肤资源包，版本号 {PackVersion}，MC 版本 {PclUiBridge.McInstanceSelectedGet().Version.Vanilla}");
                // 准备文件
                MyBitmap Bit = new MyBitmap(PclCore.PathImage + "Heads/Logo.png");
                Bit.Save(PackPicAddress);
                FileUtils.Write(MetaFileAddress, "{\"pack\":{\"pack_format\":" + PackVersion + ",\"description\":\"PCL 自定义离线皮肤资源包\"}}");
                MyBitmap Skin = new MyBitmap(Paths.AppDataThenName + "CustomSkin.png");
                if ((PclUiBridge.McInstanceSelectedGet().Version.Vanilla.Major == 6 || PclUiBridge.McInstanceSelectedGet().Version.Vanilla.Major == 7) && Skin.Pic.Height == 64) {
                    PclLaunch.McLaunchLog("该 Minecraft 版本不支持双层皮肤，已进行裁剪");
                    Skin = Skin.Clip(0, 0, 64, 32);
                }
                Skin.Save(Paths.Base + "PCL\\CustomSkin_Cliped.png");
                // 构建压缩文件
                Dictionary<string, string> FilesToCompress = new Dictionary<string, string> {
                    { "pack.mcmeta", MetaFileAddress }, { "pack.png", PackPicAddress } };
                if (PclUiBridge.McInstanceSelectedGet().Version.Vanilla.CompareTo(new Version(19, 0, 3)) >= 0) {
                    // 1.19.3+ 使用复杂版本的替换
                    foreach (string SkinName in new[] { "alex", "ari", "efe", "kai", "makena", "noor", "steve", "sunny", "zuri" }) {
                        FilesToCompress.Add(
                                $"assets/minecraft/textures/entity/player/{(PclCore.GetSetting<bool>("LaunchSkinSlim") ? "slim" : "wide")}/{SkinName}.png",
                                Paths.Base + "PCL\\CustomSkin_Cliped.png");
                    }
                } else {
                    FilesToCompress.Add(
                            $"assets/minecraft/textures/entity/{(PclCore.GetSetting<bool>("LaunchSkinSlim") ? "alex.png" : "steve.png")}",
                            Paths.Base + "PCL\\CustomSkin_Cliped.png");
                }
                FileUtils.CreateZipFromFiles(ZipFileAddress, FilesToCompress);
                FileUtils.Delete(Paths.Base + "PCL\\CustomSkin_Cliped.png");
                // 更改设置文件
                PclCore.IniClearCache(SetupFileAddress);
                string EnabledResourcePack = PclCore.ReadIni(SetupFileAddress, "resourcePacks", "[]").TrimStart('[').TrimEnd(']');
                if (NewTypeSetup) {
                    if (EnabledResourcePack == "") EnabledResourcePack = "\"vanilla\"";
                    List<string> EnabledResourcePacks = new List<string>(EnabledResourcePack.Split(','));
                    List<string> NewResourcePacks = new List<string>();
                    foreach (string Res in EnabledResourcePacks) {
                        if (Res != "\"file/PCL2 Skin.zip\"" && Res != "") NewResourcePacks.Add(Res);
                    }
                    NewResourcePacks.Add("\"file/PCL2 Skin.zip\"");
                    string Result = "[" + NewResourcePacks.Join(',') + "]";
                    PclCore.WriteIni(SetupFileAddress, "resourcePacks", Result);
                } else {
                    List<string> EnabledResourcePacks = new List<string>(EnabledResourcePack.Split(','));
                    List<string> NewResourcePacks = new List<string>();
                    foreach (string Res in EnabledResourcePacks) {
                        if (Res != "\"PCL2 Skin.zip\"" && Res != "") NewResourcePacks.Add(Res);
                    }
                    NewResourcePacks.Add("\"PCL2 Skin.zip\"");
                    string Result = "[" + NewResourcePacks.Join(',') + "]";
                    PclCore.WriteIni(SetupFileAddress, "resourcePacks", Result);
                }
            IgnoreCustomSkin:;
            } else if (FileUtils.Exists(ZipFileAddress)) {
                PclLaunch.McLaunchLog("正在清空自定义皮肤资源包");
                // 删除压缩文件
                FileUtils.Delete(ZipFileAddress);
                // 更改设置文件
                PclCore.IniClearCache(SetupFileAddress);
                string EnabledResourcePack = PclCore.ReadIni(SetupFileAddress, "resourcePacks", "[]").TrimStart('[').TrimEnd(']');
                if (NewTypeSetup) {
                    if (EnabledResourcePack == "") EnabledResourcePack = "\"vanilla\"";
                    List<string> EnabledResourcePacks = new List<string>(EnabledResourcePack.Split(','));
                    EnabledResourcePacks.Remove("\"file/PCL2 Skin.zip\"");
                    string Result = "[" + EnabledResourcePacks.Join(',') + "]";
                    PclCore.WriteIni(SetupFileAddress, "resourcePacks", Result);
                } else {
                    List<string> EnabledResourcePacks = new List<string>(EnabledResourcePack.Split(','));
                    EnabledResourcePacks.Remove("\"PCL2 Skin.zip\"");
                    string Result = "[" + EnabledResourcePacks.Join(',') + "]";
                    PclCore.WriteIni(SetupFileAddress, "resourcePacks", Result);
                }
            }
        } catch (Exception ex) {
            Logger.Error(ex, "离线皮肤资源包设置失败", LogBehavior.Toast);
        }

    }
    public static void McLaunchWait(LoaderTask<Process, int> Loader) {

        // 输出信息
        PclLaunch.McLaunchLog("");
        PclLaunch.McLaunchLog("~ 基础参数 ~");
        PclLaunch.McLaunchLog("PCL 版本：" + PclUiBridge.VersionDisplayGet() + " (" + PclUiBridge.VersionCodeGet() + ")");
        PclLaunch.McLaunchLog($"游戏版本：{PclUiBridge.GetInstanceDisplayName(PclUiBridge.McInstanceSelectedGet())}（{PclUiBridge.McInstanceSelectedGet().Version.Vanilla}，Drop {PclUiBridge.McInstanceSelectedGet().Version.Drop}{(PclUiBridge.McInstanceSelectedGet().Version.Reliable ? "" : "，无法完全确定")}）");
        PclLaunch.McLaunchLog("资源版本：" + PclUiBridge.McAssetsGetIndexName(PclUiBridge.McInstanceSelectedGet()));
        PclLaunch.McLaunchLog("版本继承：" + (PclUiBridge.McInstanceSelectedGet().InheritName == "" ? "无" : PclUiBridge.McInstanceSelectedGet().InheritName));
        PclLaunch.McLaunchLog("分配的内存：" + PclUiBridge.GetRam(PclUiBridge.McInstanceSelectedGet()) + " GB（" + Math.Round(PclUiBridge.GetRam(PclUiBridge.McInstanceSelectedGet()) * 1024) + " MB）");
        PclLaunch.McLaunchLog("MC 文件夹：" + PclUiBridge.McFolderSelectedGet());
        PclLaunch.McLaunchLog("版本文件夹：" + PclUiBridge.McInstanceSelectedGet().PathVersion);
        PclLaunch.McLaunchLog("版本隔离：" + (PclUiBridge.McInstanceSelectedGet().PathIndie == PclUiBridge.McInstanceSelectedGet().PathVersion));
        PclLaunch.McLaunchLog("HMCL 格式：" + PclUiBridge.McInstanceSelectedGet().IsHmclFormatJson);
        PclLaunch.McLaunchLog("Java 信息：" + (PclLaunch.McLaunchJavaSelected is not null ? PclLaunch.McLaunchJavaSelected.ToString() : "无可用 Java"));
        PclLaunch.McLaunchLog("Natives 文件夹：" + PclLaunch.GetNativesFolder());
        PclLaunch.McLaunchLog("");
        PclLaunch.McLaunchLog("~ 登录参数 ~");
        PclLaunch.McLaunchLog("玩家用户名：" + PclUiBridge.McLoginLoaderOutputGet().Name);
        PclLaunch.McLaunchLog("AccessToken：" + PclUiBridge.McLoginLoaderOutputGet().AccessToken);
        PclLaunch.McLaunchLog("ClientToken：" + PclUiBridge.McLoginLoaderOutputGet().ClientToken);
        PclLaunch.McLaunchLog("UUID：" + PclUiBridge.McLoginLoaderOutputGet().Uuid);
        PclLaunch.McLaunchLog("登录方式：" + PclUiBridge.McLoginLoaderOutputGet().Type);
        PclLaunch.McLaunchLog("");

        // 获取窗口标题
        string WindowTitle = PclCore.GetSetting<string>("VersionArgumentTitle", PclUiBridge.McInstanceSelectedGet());
        if (WindowTitle == "") WindowTitle = PclCore.GetSetting<string>("LaunchArgumentTitle");
        WindowTitle = PclUiBridge.ArgumentReplaceNoTime(WindowTitle);

        // 初始化等待
        Watcher GameWatcher = new Watcher(Loader, PclUiBridge.McInstanceSelectedGet(), WindowTitle);
        PclUiBridge.McLaunchWatcherSet(GameWatcher);

        // 等待
        while (GameWatcher.State == Watcher.MinecraftState.Loading) {
            Thread.Sleep(100);
        }
        if (GameWatcher.State == Watcher.MinecraftState.Crashed) {
            throw new OperationCanceledException();
        }

    }
    public static void McLaunchEnd() {
        PclLaunch.McLaunchLog("开始启动结束处理");

        // 暂停或开始音乐播放
        if (PclCore.GetSetting<bool>("UiMusicStop")) {
            PclCore.RunInUi(() => { if (PclUiBridge.MusicPause()) Logger.Info("已根据设置，在启动后暂停音乐播放"); });
        } else if (PclCore.GetSetting<bool>("UiMusicStart")) {
            PclCore.RunInUi(() => { if (PclUiBridge.MusicResume()) Logger.Info("已根据设置，在启动后开始音乐播放"); });
        }

        // 启动器可见性
        PclLaunch.McLaunchLog("启动器可见性：" + PclCore.GetSetting<int>("LaunchArgumentVisible"));
        switch (PclCore.GetSetting<int>("LaunchArgumentVisible")) {
            case 0:
                // 直接关闭
                PclLaunch.McLaunchLog("已根据设置，在启动后关闭启动器");
                PclCore.RunInUi(() => PclUiBridge.EndProgram(false));
                break;
            case 2:
            case 3:
                // 隐藏
                PclLaunch.McLaunchLog("已根据设置，在启动后隐藏启动器");
                PclCore.RunInUi(() => PclUiBridge.MainHiddenSet(true));
                break;
            case 4:
                // 最小化
                PclLaunch.McLaunchLog("已根据设置，在启动后最小化启动器");
                PclCore.RunInUi(() => PclUiBridge.MinimizeMainWindow());
                break;
            case 5:
                // 啥都不干
                break;
        }

        // 启动计数
        PclCore.SetSetting("SystemLaunchCount", PclCore.GetSetting<int>("SystemLaunchCount") + 1);

    }

    #endregion

}
