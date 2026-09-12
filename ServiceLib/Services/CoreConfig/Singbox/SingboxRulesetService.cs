namespace ServiceLib.Services.CoreConfig;

public partial class CoreConfigSingboxService
{
    private void ConvertGeo2Ruleset()
    {
        static void AddRuleSets(List<string> ruleSets, List<string>? rule_set)
        {
            if (rule_set != null)
            {
                ruleSets.AddRange(rule_set);
            }
        }
        var geosite = "geosite";
        var geoip = "geoip";
        var ruleSets = new List<string>();

        //convert route geosite & geoip to ruleset
        foreach (var rule in _coreConfig.route.rules.Where(t => t.geosite?.Count > 0).ToList() ?? [])
        {
            rule.rule_set ??= [];
            rule.rule_set.AddRange(rule?.geosite?.Select(t => $"{geosite}-{t}").ToList() ?? []);
            rule.geosite = null;
            AddRuleSets(ruleSets, rule.rule_set);
        }
        foreach (var rule in _coreConfig.route.rules.Where(t => t.geoip?.Count > 0).ToList() ?? [])
        {
            rule.rule_set ??= [];
            rule.rule_set.AddRange(rule?.geoip?.Select(t => $"{geoip}-{t}").ToList() ?? []);
            rule.geoip = null;
            AddRuleSets(ruleSets, rule.rule_set);
        }

        //convert dns geosite & geoip to ruleset
        foreach (var rule in _coreConfig.dns?.rules.Where(t => t.geosite?.Count > 0).ToList() ?? [])
        {
            rule.rule_set ??= [];
            rule.rule_set.AddRange(rule?.geosite?.Select(t => $"{geosite}-{t}").ToList() ?? []);
            rule.geosite = null;
        }
        foreach (var rule in _coreConfig.dns?.rules.Where(t => t.geoip?.Count > 0).ToList() ?? [])
        {
            rule.rule_set ??= [];
            rule.rule_set.AddRange(rule?.geoip?.Select(t => $"{geoip}-{t}").ToList() ?? []);
            rule.geoip = null;
        }
        foreach (var dnsRule in _coreConfig.dns?.rules.Where(t => t.rule_set?.Count > 0).ToList() ?? [])
        {
            AddRuleSets(ruleSets, dnsRule.rule_set);
        }
        //rules in rules
        foreach (var item in _coreConfig.dns?.rules.Where(t => t.rules?.Count > 0).Select(t => t.rules).ToList() ?? [])
        {
            foreach (var item2 in item ?? [])
            {
                AddRuleSets(ruleSets, item2.rule_set);
            }
        }

        //load custom ruleset file
        List<Ruleset4Sbox> customRulesets = [];

        var routing = context.RoutingItem;
        if (routing.CustomRulesetPath4Singbox.IsNotEmpty())
        {
            var result = EmbedUtils.LoadResource(routing.CustomRulesetPath4Singbox);
            if (result.IsNotEmpty())
            {
                customRulesets = (JsonUtils.Deserialize<List<Ruleset4Sbox>>(result) ?? [])
                    .Where(t => t.tag != null)
                    .Where(t => t.type != null)
                    .Where(t => t.format != null)
                    .ToList();
            }
        }

        //Local srs files address
        var localSrss = Utils.GetBinPath("srss");

        //Add ruleset srs
        _coreConfig.route.rule_set = [];
        foreach (var item in new HashSet<string>(ruleSets))
        {
            if (item.IsNullOrEmpty())
            { continue; }
            var customRuleset = customRulesets.FirstOrDefault(t => t.tag != null && t.tag.Equals(item));
            if (customRuleset is null)
            {
                var pathSrs = Path.Combine(localSrss, $"{item}.srs");
                if (File.Exists(pathSrs))
                {
                    customRuleset = new()
                    {
                        type = "local",
                        format = "binary",
                        tag = item,
                        path = pathSrs,
                    };
                }
                else
                {
                    // 本地没有该规则集时不再生成 remote：核心启动时通过代理下载
                    // 规则集（download_detour）在代理链路建立之前必然失败，会直接
                    // 让核心起不来。改为跳过该规则集并移除规则中对它的引用，保证
                    // 核心能正常启动；用户可通过“更新规则集”功能走代理补齐 srs。
                    RemoveRuleSetRefs(_coreConfig, item);
                    continue;
                }
            }
            _coreConfig.route.rule_set.Add(customRuleset);
        }

        // 摘掉引用的规则如果已经没有其它匹配条件，必须整条删掉（否则变成全匹配）。
        DropDegenerateRules(_coreConfig);
    }

    private static void RemoveRuleSetRefs(SingboxConfig config, string tag)
    {
        static void RemoveFrom(List<Rule4Sbox>? rules, string tag)
        {
            foreach (var rule in rules ?? [])
            {
                rule.rule_set?.RemoveAll(t => t == tag);
            }
        }
        RemoveFrom(config.route?.rules, tag);
        RemoveFrom(config.dns?.rules, tag);
        foreach (var rule in config.dns?.rules ?? [])
        {
            RemoveFrom(rule.rules, tag);
        }
    }

    /// <summary>
    /// 规则集在本地缺失时，RemoveRuleSetRefs 会把 rule_set 引用摘掉。
    /// 但如果某条规则的匹配条件**只有** rule_set，摘完它就变成了「匹配一切」的空规则
    /// （sing-box 里没有条件即全匹配），会把所有流量劫持到该规则的出口 ——
    /// 表现就是「能连上但什么都走不通」。这里把这类退化规则连同引用它的 respond 规则一起删掉。
    /// 注意：样例配置里本来就没有 rule_set，所以「rule_set 非 null 但为空」只可能是被摘过引用。
    /// </summary>
    private static void DropDegenerateRules(SingboxConfig config)
    {
        static void Drop(List<Rule4Sbox>? rules)
        {
            if (rules is null || rules.Count == 0)
            {
                return;
            }

            var droppedTags = new List<string>();
            for (var i = rules.Count - 1; i >= 0; i--)
            {
                var rule = rules[i];
                // respond / match_response 规则本来就没有匹配条件，不能当成退化规则删掉。
                if (rule.match_response.IsNotEmpty() || rule.action == "respond")
                {
                    continue;
                }
                if (rule.rule_set is null || rule.rule_set.Count > 0)
                {
                    continue;
                }
                if (rule.rules?.Count > 0 || HasMatchCondition(rule))
                {
                    continue;
                }

                rules.RemoveAt(i);
                if (rule.tag.IsNotEmpty())
                {
                    droppedTags.Add(rule.tag);
                }
            }

            // 与已删除规则成对的 respond 规则（match_response 指向被删掉的 tag）也要一起删，
            // 否则 sing-box 会因为引用了不存在的 tag 而拒绝配置。
            if (droppedTags.Count > 0)
            {
                rules.RemoveAll(r => r.match_response.IsNotEmpty() && droppedTags.Contains(r.match_response));
            }
        }

        Drop(config.route?.rules);
        Drop(config.dns?.rules);
        foreach (var rule in config.dns?.rules ?? [])
        {
            Drop(rule.rules);
        }
    }

    /// <summary>
    /// 除 rule_set 外是否还有其它匹配条件（outbound/server/action/tag 这些不是匹配条件）。
    /// </summary>
    private static bool HasMatchCondition(Rule4Sbox rule)
    {
        return rule.ip_is_private is not null
            || rule.source_ip_is_private is not null
            || rule.ip_accept_any is not null
            || rule.tls_record_fragment is not null
            || rule.network_is_expensive is not null
            || rule.network_is_constrained is not null
            || rule.clash_mode.IsNotEmpty()
            || rule.inbound?.Count > 0
            || rule.protocol?.Count > 0
            || rule.network?.Count > 0
            || rule.network_type?.Count > 0
            || rule.port?.Count > 0
            || rule.port_range?.Count > 0
            || rule.source_port is not null
            || rule.source_port_range?.Count > 0
            || rule.geosite?.Count > 0
            || rule.domain?.Count > 0
            || rule.domain_suffix?.Count > 0
            || rule.domain_keyword?.Count > 0
            || rule.domain_regex?.Count > 0
            || rule.geoip?.Count > 0
            || rule.ip_cidr?.Count > 0
            || rule.source_ip_cidr?.Count > 0
            || rule.process_name?.Count > 0
            || rule.process_path?.Count > 0
            || rule.query_type?.Count > 0
            || rule.answer?.Count > 0
            || rule.ns?.Count > 0
            || rule.wifi_ssid?.Count > 0
            || rule.wifi_bssid?.Count > 0
            || rule.extra?.Count > 0;
    }
}
