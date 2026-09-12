namespace ServiceLib.Services;

public partial class UpdateService(Config config, Func<bool, string, Task> updateFunc)
{
    private readonly Config? _config = config;
    private readonly Func<bool, string, Task>? _updateFunc = updateFunc;
    private readonly int _timeout = 30;

    public async Task UpdateGeoFileAll(bool blProxy = true)
    {
        var requests = new List<FileDownloadRequest>();
        requests.AddRange(GetGeoFilesRequest());
        requests.AddRange(GetOtherFilesRequest());
        requests.AddRange(await GetSrsFileAllRequest());
        // NOTE: srs files are more small, so we reverse the order to ensure a good download experience for the user.
        requests.Reverse();
        await DownloadGeoFiles(requests, blProxy);
        await UpdateFunc(true, string.Format(ResUI.MsgDownloadGeoFileSuccessfully, "geo"));
    }

    /// <summary>
    /// 启动自愈：本地缺少 geosite.dat / geoip.dat 时（典型场景是全新解压的发布包，
    /// publish 不会带上这两个文件，它们平时是运行时按需下载的）立即补下载。
    ///
    /// 缺文件时 xray 会在启动阶段直接报 `failed to open file: geosite.dat` 并退出，
    /// 用户看到的现象是「有节点、有延迟，但内核起不来 / 一个都连不上」，
    /// 而且报错藏在日志里，很难自己定位到是数据文件缺失。
    ///
    /// 文件都在时只做两次 File.Exists，不产生任何网络开销；
    /// 只在缺失时才走下载（此时本来也起不来，多等一会儿远好于静默失败）。
    /// 代理此时尚未启动，只能直连，失败不阻断启动。
    /// </summary>
    public async Task EnsureGeoFilesAsync()
    {
        var missing = new List<string>();
        foreach (var name in new[] { "geosite.dat", "geoip.dat" })
        {
            try
            {
                var path = Utils.GetBinPath(name);
                if (!File.Exists(path) || new FileInfo(path).Length == 0)
                {
                    missing.Add(name);
                }
            }
            catch
            {
                missing.Add(name);
            }
        }

        if (missing.Count == 0)
        {
            return;
        }

        await UpdateFunc(false, string.Format(ResUI.MsgDownloadGeoFileSuccessfully, string.Join(", ", missing)));
        await DownloadGeoFiles(GetGeoFilesRequest(), blProxy: false);
    }

    #region Geo private

    private List<FileDownloadRequest> GetGeoFilesRequest()
    {
        var geoUrl = string.IsNullOrEmpty(_config?.ConstItem.GeoSourceUrl)
            ? Global.GeoUrl
            : _config.ConstItem.GeoSourceUrl;

        List<string> files = ["geosite", "geoip"];
        return
        [
            .. from geoName in files
            let fileName = $"{geoName}.dat"
            let targetPath = Utils.GetBinPath($"{fileName}")
            let url = string.Format(geoUrl, geoName)
            select new FileDownloadRequest()
            {
                FileUrl = url,
                FilePath = targetPath,
                DisplayFileName = fileName,
            },
        ];
    }

    private List<FileDownloadRequest> GetOtherFilesRequest()
    {
        //If it is not in China area, no update is required
        if (_config.ConstItem.GeoSourceUrl.IsNotEmpty())
        {
            return [];
        }

        return
        [
            .. Global.OtherGeoUrls.Select(url =>
            {
                var fileName = Path.GetFileName(url);
                var targetPath = Utils.GetBinPath($"{fileName}");
                return new FileDownloadRequest()
                {
                    FileUrl = url,
                    FilePath = targetPath,
                    DisplayFileName = fileName,
                };
            }),
        ];
    }

    private async Task<List<FileDownloadRequest>> GetSrsFileAllRequest()
    {
        var geoipFiles = new List<string>();
        var geoSiteFiles = new List<string>();

        // Collect from routing rules
        var routingItems = await AppManager.Instance.RoutingItems();
        foreach (var routing in routingItems)
        {
            var rules = JsonUtils.Deserialize<List<RulesItem>>(routing.RuleSet);
            foreach (var item in rules ?? [])
            {
                AddPrefixedItems(item.Ip, Global.GeoIPPrefix, geoipFiles);
                AddPrefixedItems(item.Domain, Global.GeoSitePrefix, geoSiteFiles);
            }
        }

        // Collect from DNS configuration
        var dnsItem = await AppManager.Instance.GetDNSItem(ECoreType.sing_box);
        if (dnsItem != null)
        {
            ExtractDnsRuleSets(dnsItem.NormalDNS, geoipFiles, geoSiteFiles);
            ExtractDnsRuleSets(dnsItem.TunDNS, geoipFiles, geoSiteFiles);
        }

        // Append default items
        geoSiteFiles.AddRange(["google", "cn", "geolocation-cn", "category-ads-all"]);

        // Download files
        var path = Utils.GetBinPath("srss");
        if (!Directory.Exists(path))
        {
            Directory.CreateDirectory(path);
        }

        return
        [
            .. geoipFiles.Distinct().Select(f => (type: "geoip", file: f))
                .Concat(geoSiteFiles.Distinct().Select(f => (type: "geosite", file: f)))
                .Select(item => GetSrsFileRequest(item.type, item.file)),
        ];
    }

    private void AddPrefixedItems(List<string>? items, string prefix, List<string> output)
    {
        if (items == null)
        {
            return;
        }

        foreach (var item in items)
        {
            if (item.StartsWith(prefix))
            {
                output.Add(item.Substring(prefix.Length));
            }
        }
    }

    private void ExtractDnsRuleSets(string? dnsJson, List<string> geoipFiles, List<string> geoSiteFiles)
    {
        if (string.IsNullOrEmpty(dnsJson))
        {
            return;
        }

        try
        {
            var dns = JsonUtils.Deserialize<Dns4Sbox>(dnsJson);
            if (dns?.rules != null)
            {
                foreach (var rule in dns.rules)
                {
                    ExtractSrsRuleSets(rule, geoipFiles, geoSiteFiles);
                }
            }
        }
        catch { }
    }

    private void ExtractSrsRuleSets(Rule4Sbox? rule, List<string> geoipFiles, List<string> geoSiteFiles)
    {
        if (rule == null)
        {
            return;
        }

        AddPrefixedItems(rule.rule_set, "geosite-", geoSiteFiles);
        AddPrefixedItems(rule.rule_set, "geoip-", geoipFiles);

        // Handle nested rules recursively
        if (rule.rules != null)
        {
            foreach (var nestedRule in rule.rules)
            {
                ExtractSrsRuleSets(nestedRule, geoipFiles, geoSiteFiles);
            }
        }
    }

    private FileDownloadRequest GetSrsFileRequest(string type, string srsName)
    {
        var srsUrl = string.IsNullOrEmpty(_config.ConstItem.SrsSourceUrl)
                        ? Global.SingboxRulesetUrl
                        : _config.ConstItem.SrsSourceUrl;

        var fileName = $"{type}-{srsName}.srs";
        var targetPath = Path.Combine(Utils.GetBinPath("srss"), fileName);
        var url = string.Format(srsUrl, type, $"{type}-{srsName}", srsName);

        return new FileDownloadRequest()
        {
            FileUrl = url,
            FilePath = targetPath,
            DisplayFileName = fileName,
        };
    }

    private async Task DownloadGeoFiles(List<FileDownloadRequest> requests, bool blProxy)
    {
        var tmpFilePathDict = new Dictionary<string, string>();
        var tmpFileRequests = new List<FileDownloadRequest>();
        foreach (var request in requests)
        {
            var tmpFilePath = Utils.GetTempPath(Utils.GetGuid());
            tmpFilePathDict[request.FilePath] = tmpFilePath;
            tmpFileRequests.Add(request with
            {
                FilePath = tmpFilePath,
            });
        }

        DownloadService downloadHandle = new();
        downloadHandle.UpdateCompleted += (sender2, args) =>
        {
            if (args.Success)
            {
                //_ = UpdateFunc(false, string.Format(ResUI.MsgDownloadGeoFileSuccessfully, fileName));

                foreach (var request in requests)
                {
                    try
                    {
                        //if (File.Exists(tmpFileName))
                        //{
                        //    File.Copy(tmpFileName, targetPath, true);

                        //    File.Delete(tmpFileName);
                        //    //await    UpdateFunc(true, "");
                        //}
                        var tmpFileName = tmpFilePathDict[request.FilePath];
                        if (File.Exists(tmpFileName))
                        {
                            File.Copy(tmpFileName, request.FilePath, true);
                            File.Delete(tmpFileName);
                        }
                    }
                    catch (Exception ex)
                    {
                        _ = UpdateFunc(false, ex.Message);
                    }
                }
            }
            else
            {
                _ = UpdateFunc(false, args.Msg);
            }
        };
        downloadHandle.Error += (sender2, args) =>
        {
            _ = UpdateFunc(false, args.GetException().Message);
        };

        await downloadHandle.DownloadSmallFilesAsync(tmpFileRequests, blProxy, TimeSpan.FromSeconds(_timeout));
    }

    #endregion Geo private

    private async Task UpdateFunc(bool notify, string msg)
    {
        await _updateFunc?.Invoke(notify, msg);
    }
}
