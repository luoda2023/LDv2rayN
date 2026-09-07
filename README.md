# LDv2rayN

### LDv2rayN 是面向 Windows、Linux 和 macOS 的图形客户端。

作者：LUODA  
网址：[dicad.cn](https://dicad.cn)

---

## 使用说明与责任声明

**本软件仅限本人内部使用，其它人使用我不承担任何责任。**

使用者须自行确认运行环境、配置内容及相关法律责任。本软件基于开源项目二次开发，作者不对其它人使用本软件产生的任何后果负责。

---

## 安全特性 / Security Features

LDv2rayN 在原版基础上进行了以下安全增强：

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

## 隐私与安全边界

本项目仅提供本地代理配置与连接管理，不承诺对抗 DPI、单位扫描、运营商审计或隐藏真实网络身份。请仅在获得授权的环境中使用，并根据系统管理员要求配置 Windows Defender Firewall；软件不会自动写入不可逆或隐蔽的防火墙规则。

默认配置保持本地监听、关闭 IPv6 TUN 地址，并在 TUN 模式启用严格路由；启用 LAN 共享、TUN 或自定义 DNS 前，请先确认其影响。TLS 证书校验默认保持开启，勿为"加速"关闭校验。

---

## 版权声明

本软件基于开源项目二次开发，遵循 GNU General Public License v3.0。

原作者：2dust  
二次开发：LUODA  
网址：[dicad.cn](https://dicad.cn)
