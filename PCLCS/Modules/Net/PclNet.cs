// 由 VB Modules\Minecraft\ModDownload.vb 的 Mod 镜像源请求部分迁移而来。
// 基础 HTTP 请求经 RawRequestProvider 由 VB ModNet 提供，避免 C# 反向依赖。
#nullable disable

using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using Newtonsoft.Json.Linq;

namespace PCLCS.Modules;

public static class PclNet {

    /// <summary>
    /// 由 VB ModNet 提供的基础网络请求实现。
    /// 参数为（Url, Method, Content, ContentType, TimeoutMs, Encoding, RequireJson），返回响应文本。
    /// </summary>
    public static Func<string, HttpMethod, object, string, int, Encoding, bool, string> RawRequestProvider;

    /// <summary>
    /// 对可能涉及 Mod 镜像源的请求进行处理，返回 JToken。
    /// 调用 NetRequest，会进行重试。
    /// </summary>
    public static JToken DlModRequest(string Url, HttpMethod Method = null, string Content = null, string ContentType = null) {
        List<(string Url, int Timeout)> Urls = new List<(string Url, int Timeout)>();
        string McimUrl = DlSourceModGet(Url);
        if (McimUrl != Url) {
            switch (PclCore.GetSetting<int>("ToolDownloadMod")) {
                case 0:
                    Urls.Add((McimUrl, 10));
                    Urls.Add((McimUrl, 10));
                    Urls.Add((Url, 30));
                    break;
                case 1:
                    Urls.Add((Url, Url.Contains("modrinth") ? 20 : 10)); // 至少 20s，要不然 Modrinth 返回不过来
                    Urls.Add((McimUrl, 10));
                    Urls.Add((Url, 30));
                    Urls.Add((McimUrl, 30));
                    break;
                default:
                    Urls.Add((Url, Url.Contains("modrinth") ? 20 : 10));
                    Urls.Add((Url, 30));
                    Urls.Add((McimUrl, 30));
                    break;
            }
        } else {
            Urls.Add((Url, Url.Contains("modrinth") ? 20 : 10));
            Urls.Add((Url, 30));
        }
        List<Exception> Errors = new List<Exception>();
        foreach ((string SourceUrl, int Timeout) in Urls) {
            try {
                return RawRequestProvider(SourceUrl, Method, Content, ContentType, Timeout * 1000, Encoding.UTF8, true).DeserializeJson();
            } catch (Exception ex) {
                Errors.Add(ex);
            }
        }
        throw new AggregateException(Errors);
    }

    /// <summary>
    /// 获取 Mod 下载镜像源地址。
    /// </summary>
    public static string DlSourceModGet(string Original) {
        return Original.
            Replace("api.modrinth.com", "mod.mcimirror.top/modrinth").
            Replace("staging-api.modrinth.com", "mod.mcimirror.top/modrinth").
            Replace("cdn.modrinth.com", "mod.mcimirror.top").
            Replace("api.curseforge.com", "mod.mcimirror.top/curseforge").
            Replace("edge.forgecdn.net", "mod.mcimirror.top").
            Replace("mediafilez.forgecdn.net", "mod.mcimirror.top").
            Replace("media.forgecdn.net", "mod.mcimirror.top");
    }

}
