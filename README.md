# LDv2rayN

### LDv2rayN 是面向 Windows、Linux 和 macOS 的图形客户端。

作者：LUODA  
网址：[dicad.cn](https://dicad.cn)

Support [Xray](https://github.com/XTLS/Xray-core) and [sing-box](https://github.com/SagerNet/sing-box) and [others](https://github.com/2dust/v2rayN/wiki/List-of-supported-cores)

---

## 使用说明与责任声明

**本软件仅限本人内部使用，其它人使用我不承担任何责任。** 使用者须自行确认运行环境、配置内容及相关法律责任。

## Download / 下载

Download the latest release here:

在这里下载最新版本：

[https://github.com/2dust/v2rayN/releases](https://github.com/2dust/v2rayN/releases)

---

## 安全特性 / Security Features

LDv2rayN 在原版 v2rayN 基础上进行了以下安全增强：

### DNS 泄露防护
- 默认启用 `BlockAAAAQuery`，阻止 IPv6 DNS 泄露
- DNS 查询强制走代理隧道
- 内置 FakeIP 模式，防止 DNS 污染

### IPv6 泄露防护
- TUN 模式默认禁用 IPv6 地址分配
- IPv6 路由严格隔离

### 路由安全
- TUN 模式默认启用严格路由 (`StrictRoute = true`)
- 默认启用自动路由 (`AutoRoute = true`)
- 中国域名/IP 直连，海外流量走代理

### 国内网站加速
- 预置国内主流 CDN 域名直连规则（B站、淘宝、京东、微信、抖音等）
- 中国公共 DNS IP 直连
- geosite:cn 和 geoip:cn 规则覆盖

### TLS 指纹伪装
- 支持 uTLS 指纹伪装（Chrome、Firefox、Safari 等）
- 支持 Reality 协议，免证书 SNI 伪装
- 支持 XHTTP 传输层混淆

---

## Documentation / 使用文档

Read the Wiki for usage guides and configuration details.

请阅读 Wiki 获取使用说明和配置教程。

[https://github.com/2dust/v2rayN/wiki](https://github.com/2dust/v2rayN/wiki)

---

## Supported Platforms / 支持平台

| Platform / 平台 | x64 | x86 | arm64 | riscv64 | loong64 |
| --- | --- | --- | --- | --- | --- |
| Windows | ✅ | ✅ | ✅ | - | - |
| Linux | ✅ | - | ✅ | ✅ | ✅ |
| macOS | ✅ | - | ✅ | - | - |

Minimum OS requirements: [Release files introduction](https://github.com/2dust/v2rayN/wiki/Release-files-introduction) / 最低系统要求：[发布文件介绍](https://github.com/2dust/v2rayN/wiki/Release-files-introduction)

---

## 隐私与安全边界

本项目仅提供本地代理配置与连接管理，不承诺对抗 DPI、单位扫描、运营商审计或隐藏真实网络身份。请仅在获得授权的环境中使用，并根据系统管理员要求配置 Windows Defender Firewall；软件不会自动写入不可逆或隐蔽的防火墙规则。

默认配置保持本地监听、关闭 IPv6 TUN 地址，并在 TUN 模式启用严格路由；启用 LAN 共享、TUN 或自定义 DNS 前，请先确认其影响。TLS 证书校验默认保持开启，勿为"加速"关闭校验。

---

## GPG Verification / GPG 签名校验

Release files are signed with GPG to verify authenticity and integrity, helping prevent mirror, ISP, or CDN hijacking.

发布文件已使用 GPG 签名，可用于校验文件真实性与完整性，预防镜像站、运营商或 CDN 劫持。

### Fingerprint / 公钥指纹

```text
7694 5E9F 3E9A 168F 8070 F195 805D 661C
134D FAF6 8903 C199 463C 31E5 AE90 3AE0
```

---

## Community / 社区

Telegram Group / Telegram 群组：

[https://t.me/v2rayN](https://t.me/v2rayN)

Telegram Channel / Telegram 频道：

[https://t.me/github_2dust](https://t.me/github_2dust)
