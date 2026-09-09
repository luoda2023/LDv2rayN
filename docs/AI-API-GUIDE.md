# LDv2rayN 内部 AI API 使用指南

LDv2rayN 启动后会在本机 **127.0.0.1:26066** 上启动一个 HTTP 服务，供外部 AI（或你写的小脚本）远程调用。服务是**默认强制开启**的，不需要额外配置。

> 默认绑定 `127.0.0.1`（仅本机）。如果需要远程调用，把 `AIConfigItem.ExternalApi.Host` 改成 `0.0.0.0` 即可。

---

## 完整接口列表

| 方法 | 路径 | 说明 |
|---|---|---|
| `GET`  | `/ai/capabilities` | 列出所有可用接口（含参数 schema） |
| `GET`  | `/ai/status` | 当前运行状态（核心类型、端口、系统代理模式） |
| `GET`  | `/ai/subscriptions`| 列出所有订阅分组 |
| `GET`  | `/ai/servers` | 列出所有节点，可按 `subId` 过滤 |
| `GET`  | `/ai/groups` | 列出所有分组及节点数 |
| `POST` | `/ai/analyzeUrl` | **核心接口**：URL/文本 → 提取节点 → 测试 → 加入分组 |
| `POST` | `/ai/testNode` | 测试一个或多个节点是否可达（TCP + DNS） |
| `POST` | `/ai/addNodes` | 直接把节点链接加入分组（不测试） |
| `POST` | `/ai/deleteNode` | 按 `indexId` 或 `remarks` 删除节点 |
| `POST` | `/ai/selectGroup`  | 切换当前激活的分组 |
| `POST` | `/ai/systemProxy`  | 设置系统代理（set / clear / pac） |
| `POST` | `/ai/aiConfig` | 读取/修改 AI 配置（开关、间隔、API 地址等） |

---

## 1. 你最关心的：丢一个 URL 给 AI，让它自己搞定

```bash
curl -X POST http://127.0.0.1:26066/ai/analyzeUrl \
  -H "Content-Type: application/json" \
  -d '{
    "url": "https://github.com/xxx/free-nodes",
    "group": "AI自动获取",
    "maxNodes": 30,
    "autoTest": true
  }'
```

**AI 内部会执行：**

1. 下载 `url` 指向的内容（网页 / GitHub raw / 订阅链接）
2. 用正则直接提取 `vmess:// vless:// trojan:// ss:// hy2:// tuic://` 节点
3. 如果直接提取不到，把内容（最多 8000 字符）发给外部 LLM，让 LLM 解析
4. 对每个候选节点做 TCP connect 探测（5 秒超时，失败则 DNS 兜底）
5. 把通过测试的节点（最多 `maxNodes` 个）批量加入 `group` 分组
6. 如果分组不存在，自动创建

**响应：**

```json
{
  "ok": true,
  "message": "Added 12 nodes to AI自动获取",
  "data": {
    "url": "https://github.com/xxx/free-nodes",
    "group": "AI自动获取",
    "maxNodes": 30,
    "autoTest": true,
    "added": 12
  }
}
```

---

## 2. 直接粘贴节点（跳过 AI 解析）

```bash
curl -X POST http://127.0.0.1:26066/ai/addNodes \
  -H "Content-Type: application/json" \
  -d '{
    "group": "AI自动获取",
    "links": [
      "vless://xxxx@example.com:443?type=tls&security=tls&sni=example.com#MyNode",
      "vmess://eyJ..."
    ]
  }'
```

如果 links 里是 HTTP URL，会被当作 URL 走 analyze 流程；如果是 `vmess://` / `vless://` 等直连协议前缀，会跳过下载直接入组。

---

## 3. 单独测试节点（不加入分组）

```bash
curl -X POST http://127.0.0.1:26066/ai/testNode \
  -H "Content-Type: application/json" \
  -d '{
    "links": [
      "vless://xxxx@example.com:443?type=tls&security=tls&sni=example.com#MyNode"
    ]
  }'
```

**响应：**

```json
{
  "ok": true,
  "message": "Tested 1 nodes, 1 passed",
  "data": {
    "total": 1,
    "passed": 1,
    "results": [
      { "link": "vless://...", "ok": true, "address": "example.com", "port": 443, "latencyMs": 234 }
    ]
  }
}
```

---

## 4. 列出分组 / 节点

```bash
# 列出所有分组
curl http://127.0.0.1:26066/ai/groups

# 列出所有节点（按 subId 过滤）
curl "http://127.0.0.1:26066/ai/servers?subId=5522866476265825152"

# 列出所有订阅
curl http://127.0.0.1:26066/ai/subscriptions

# 应用状态
curl http://127.0.0.1:26066/ai/status
```

---

## 5. 删除节点

```bash
# 按 remarks 删除
curl -X POST http://127.0.0.1:26066/ai/deleteNode \
  -H "Content-Type: application/json" \
  -d '{"remarks": "MyNode"}'

# 按 indexId 删除
curl -X POST http://127.0.0.1:26066/ai/deleteNode \
  -H "Content-Type: application/json" \
  -d '{"indexId": "5577802915059009599"}'
```

---

## 6. 切换分组

```bash
curl -X POST http://127.0.0.1:26066/ai/selectGroup \
  -H "Content-Type: application/json" \
  -d '{"remarks": "AI自动获取"}'
```

---

## 7. 设置系统代理

```bash
# 打开代理（跟随当前分组）
curl -X POST http://127.0.0.1:26066/ai/systemProxy -H "Content-Type: application/json" \
  -d '{"mode": "set"}'

# 关闭代理
curl -X POST http://127.0.0.1:26066/ai/systemProxy -H "Content-Type: application/json" \
  -d '{"mode": "clear"}'

# PAC 模式
curl -X POST http://127.0.0.1:26066/ai/systemProxy -H "Content-Type: application/json" \
  -d '{"mode": "pac"}'
```

---

## 8. 修改 AI 配置

```bash
# 读取当前配置
curl -X POST http://127.0.0.1:26066/ai/aiConfig -H "Content-Type: application/json" \
  -d '{}'

# 修改配置（会重启调度器）
curl -X POST http://127.0.0.1:26066/ai/aiConfig -H "Content-Type: application/json" \
  -d '{
    "enabled": true,
    "autoCrawlEnabled": true,
    "intervalMinutes": 60,
    "apiUrl": "http://your-llm-endpoint/v1",
    "apiKey": "sk-...",
    "modelId": "your-model",
    "group": "AI自动获取",
    "maxNodes": 50
  }'
```

---

## 9. 让外部 LLM 完全自主驱动 LDv2rayN（OpenAI function calling 完整示例）

下面的示例覆盖 **全部 11 个接口**，展示如何让一个外部 LLM（GPT / Claude / hermesAPI 等任何支持 OpenAI function calling 的模型）**完全自主**：

- 读状态（`status` / `groups` / `servers` / `subscriptions`）
- 分析 URL（`analyzeUrl`）
- 直接添加 / 测试 / 删除节点（`addNodes` / `testNode` / `deleteNode`）
- 切换分组（`selectGroup`）
- 开关系统代理（`systemProxy`）
- 修改 AI 后台配置（`aiConfig`）

### 9.1 静态工具定义（覆盖全部 11 个端点）

```python
import json, urllib.request

BASE = "http://127.0.0.1:26066"

def tool(name, description, properties, required=None):
    return {
        "type": "function",
        "function": {
            "name": name,
            "description": description,
            "parameters": {
                "type": "object",
                "properties": properties,
                "required": required or []
            }
        }
    }

TOOLS = [
    # ---------- 只读查询 ----------
    tool(
        "ldv2rayn_status",
        "Get the current LDv2rayN state: running core type, SOCKS port, system proxy mode, current group id, current subscription id.",
        {},
    ),
    tool(
        "ldv2rayn_groups",
        "List every subscription group with its id, remarks, enabled flag, node count, and whether it is the active group.",
        {},
    ),
    tool(
        "ldv2rayn_subscriptions",
        "List all subscription configurations.",
        {},
    ),
    tool(
        "ldv2rayn_servers",
        "List proxy servers, optionally filtered by subscription id.",
        {
            "subId": {"type": "string", "description": "Optional subscription id to filter by"}
        },
    ),
    # ---------- 节点操作 ----------
    tool(
        "ldv2rayn_analyze_url",
        "Download the URL content, extract VPN node links (vmess/vless/trojan/ss/hy2/tuic), test them, and add the passing ones to a group. Also accepts a raw node link directly (skips the download step).",
        {
            "url":      {"type": "string", "description": "http(s) URL or a direct node link"},
            "group":    {"type": "string", "description": "Target group remarks (created if missing)", "default": "AI自动获取"},
            "maxNodes": {"type": "integer", "description": "Max nodes to add per call", "default": 50},
            "autoTest": {"type": "boolean", "description": "Test each node before adding", "default": True},
        },
        required=["url"],
    ),
    tool(
        "ldv2rayn_test_node",
        "Test one or more node links for reachability (TCP + DNS). Does not add them.",
        {
            "links": {"type": "array", "items": {"type": "string"}, "description": "Node links"}
        },
        required=["links"],
    ),
    tool(
        "ldv2rayn_add_nodes",
        "Add node links to a group without testing them first. Creates the group if it doesn't exist.",
        {
            "group": {"type": "string", "description": "Target group remarks"},
            "links": {"type": "array", "items": {"type": "string"}, "description": "Node links"},
        },
        required=["group", "links"],
    ),
    tool(
        "ldv2rayn_delete_node",
        "Delete a node by its indexId or by its remarks string.",
        {
            "indexId": {"type": "string", "description": "Unique server id"},
            "remarks": {"type": "string", "description": "Remarks of the node to delete"},
        },
    ),
    tool(
        "ldv2rayn_select_group",
        "Switch to a group (by remarks or by subId).",
        {
            "remarks": {"type": "string"},
            "subId":   {"type": "string"},
        },
    ),
    # ---------- 系统代理 ----------
    tool(
        "ldv2rayn_system_proxy",
        "Set, clear, or PAC-mode the Windows system proxy.",
        {
            "mode": {"type": "string", "enum": ["set", "clear", "pac"], "default": "set"}
        },
    ),
    # ---------- AI 配置 ----------
    tool(
        "ldv2rayn_ai_config",
        "Read (empty body) or update (partial body) the AI scheduler settings. Changes trigger an automatic scheduler restart.",
        {
            "enabled":          {"type": "boolean"},
            "autoCrawlEnabled": {"type": "boolean"},
            "intervalMinutes":  {"type": "integer"},
            "apiUrl":           {"type": "string"},
            "apiKey":           {"type": "string"},
            "modelId":          {"type": "string"},
            "group":            {"type": "string"},
            "maxNodes":         {"type": "integer"},
        },
    ),
]
```

### 9.2 工具分发：调用 LDv2rayN 并把结果回喂给 LLM

```python
# Name → (HTTP method, path, arg field that carries the JSON body)
TOOL_TO_ENDPOINT = {
    "ldv2rayn_status":        ("GET",  "/ai/status",        None),
    "ldv2rayn_groups":        ("GET",  "/ai/groups",        None),
    "ldv2rayn_subscriptions": ("GET",  "/ai/subscriptions", None),
    "ldv2rayn_servers":       ("GET",  "/ai/servers",       None),
    "ldv2rayn_analyze_url":   ("POST", "/ai/analyzeUrl",    None),
    "ldv2rayn_test_node":     ("POST", "/ai/testNode",      None),
    "ldv2rayn_add_nodes":     ("POST", "/ai/addNodes",      None),
    "ldv2rayn_delete_node":   ("POST", "/ai/deleteNode",    None),
    "ldv2rayn_select_group":  ("POST", "/ai/selectGroup",   None),
    "ldv2rayn_system_proxy":  ("POST", "/ai/systemProxy",   None),
    "ldv2rayn_ai_config":     ("POST", "/ai/aiConfig",      None),
}

def dispatch(name: str, args: dict) -> str:
    """Call the local LDv2rayN HTTP API and return a JSON string."""
    method, path, _ = TOOL_TO_ENDPOINT[name]
    url = BASE + path

    if method == "GET":
        # GET endpoints read query params; the server also accepts an empty POST body.
        if args:
            from urllib.parse import urlencode
            url = f"{url}?{urlencode(args)}"
        req = urllib.request.Request(url, method="GET")
    else:
        body = json.dumps(args).encode("utf-8")
        req = urllib.request.Request(url, data=body, method="POST",
                                     headers={"Content-Type": "application/json"})

    with urllib.request.urlopen(req, timeout=60) as resp:
        return resp.read().decode("utf-8")
```

### 9.3 完整的 function-calling 循环（OpenAI Chat Completions 协议）

```python
import os

LLM_URL = os.environ["LLM_URL"]          # e.g. http://47.114.75.115:40000/v1/chat/completions
LLM_KEY = os.environ["LLM_KEY"]
LLM_MODEL = os.environ.get("LLM_MODEL", "hermesAPI")

SYSTEM_PROMPT = """
你是嵌入在 LDv2rayN 客户端里的 AI 助手。用户会告诉你目标和约束，你通过 tools 自主完成。

原则：
1. 优先查询（status / groups / servers）再动手，避免误操作。
2. 涉及删除 / 覆盖当前分组的操作，先向用户确认。
3. 添加节点前先 test_node 测活，通过后再 add_nodes。
4. 完成所有 tool_calls 后给出简短的中文总结。
""".strip()

def chat_once(messages):
    payload = json.dumps({
        "model": LLM_MODEL,
        "messages": messages,
        "tools": TOOLS,
        "tool_choice": "auto",
        "temperature": 0.2,
    }).encode("utf-8")
    req = urllib.request.Request(LLM_URL, data=payload, method="POST", headers={
        "Content-Type": "application/json",
        "Authorization": f"Bearer {LLM_KEY}",
    })
    with urllib.request.urlopen(req, timeout=120) as resp:
        return json.loads(resp.read().decode("utf-8"))

def run_agent(user_input: str, max_turns: 12 = 12):
    messages = [
        {"role": "system", "content": SYSTEM_PROMPT},
        {"role": "user",   "content": user_input},
    ]
    for turn in range(max_turns):
        data = chat_once(messages)
        choice = data["choices"][0]
        msg = choice["message"]
        tool_calls = msg.get("tool_calls") or []

        # 1) Append the assistant turn (with tool_calls if any)
        messages.append(msg)

        # 2) If no tool_calls, this is the final answer
        if not tool_calls:
            return msg.get("content") or ""

        # 3) Otherwise, dispatch each tool call and append tool results
        for call in tool_calls:
            fn_name = call["function"]["name"]
            try:
                args = json.loads(call["function"]["arguments"] or "{}")
            except json.JSONDecodeError:
                args = {}
            print(f"[turn {turn}] calling {fn_name}({args})")
            try:
                result = dispatch(fn_name, args)
            except Exception as e:
                result = json.dumps({"ok": False, "message": str(e)})
            messages.append({
                "role": "tool",
                "tool_call_id": call["id"],
                "content": result,
            })
    return "[max_turns_reached]"

if __name__ == "__main__":
    user_input = input("> ")
    print("\n=== Agent ===\n" + run_agent(user_input))
```

### 9.4 动态发现工具（不用手写 TOOLS）

如果不想维护一份和 LDv2rayN 代码同步的静态定义，直接从 `/ai/capabilities` 拉一份：

```python
def discover_tools():
    """Read /ai/capabilities and translate each descriptor into an OpenAI tool."""
    with urllib.request.urlopen(BASE + "/ai/capabilities") as resp:
        caps = json.loads(resp.read().decode("utf-8"))["data"]

    # Each cap is: { name, method, path, description, readOnly, parameters } 
    # where parameters is a dict: { paramName: { type, required, description } }

    name_to_endpoint = {}
    tools = []
    for cap in caps:
        name = f"ldv2rayn_{cap['name']}"
        name_to_endpoint[name] = (cap["method"], cap["path"])
        props = {}
        required = []
        for pname, p in (cap.get("parameters") or {}).items():
            props[pname] = {
                "type": p["type"],
                "description": p["description"],
            }
            if p.get("required"):
                required.append(pname)
        tools.append(tool(name, cap["description"], props, required))
    return tools, name_to_endpoint

TOOLS, TOOL_TO_ENDPOINT = discover_tools()
```

### 9.5 一次完整交互的示例（用户输入 → LLM 决策 → 工具调用 → 回复）

用户：`"帮我从 https://github.com/xxx/free-nodes 抓一批节点，测试后放到 AI自动获取 组，然后切到这个组并打开系统代理"`

LLM 会：

1. `ldv2rayn_analyze_url`（`url=...`, `group=AI自动获取`, `autoTest=true`, `maxNodes=30`）
2. 收到 `added: 12` 后 → `ldv2rayn_select_group`（`remarks=AI自动获取`）
3. 收到 `current=true` 后 → `ldv2rayn_system_proxy`（`mode=set`）
4. 最终回复：`已添加 12 个节点到「AI自动获取」，并切换到该分组、开启系统代理。`

### 9.6 常见错误处理

| 场景 | 你该做的 |
|---|---|
| `{"ok": false, "message": "..."}` | 把 `message` 回喂给 LLM，让它决定是否重试 |
| HTTP 5xx | LDv2rayN 服务挂了；提示用户重启 |
| 网络超时 | 增大 `urlopen(timeout=...)`；某些 `analyzeUrl` 目标站要 60+ 秒 |
| LLM 反复循环调用同一个工具 | 在 `run_agent` 里加去重或最大重试 |

---

## 10. 认证（可选）

默认不需要 Token。如果配置了 `ExternalApi.Token`，需要在每个请求头加：

```bash
X-AI-Token: <your-token>
```

例如：

```bash
curl -H "X-AI-Token: my-secret" http://127.0.0.1:26066/ai/status
```

`dispatch()` 里的用法：

```python
headers = {"Content-Type": "application/json"}
if TOKEN:
    headers["X-AI-Token"] = TOKEN
req = urllib.request.Request(url, data=body, method=method, headers=headers)
```

---

## 11. 关于"排除更多中国内陆网络访问限制"

- LDv2rayN 内置 v2ray / xray / sing-box / mihomo / hysteria2 / trojan / tuic 等主流协议核心
- 通过 `analyzeUrl` 让外部 AI 自动抓取 GitHub / TG 频道 / 订阅站的公开节点
- 通过 `testNode` 批量测活，AI 再按节点所在国家、协议安全性做二次筛选
- 通过 `selectGroup` + `systemProxy` 一键切到"中国大陆友好"的节点集

---

## 安全注意

- 默认只监听 `127.0.0.1`，改成 `0.0.0.0` 前请开启 Token 认证
- `deleteNode` / `aiConfig` 具备破坏性，务必让 LLM 先查询再改动
- `analyzeUrl` 允许下载任意 URL，恶意内容可能被喂给 LLM，请限制来源
