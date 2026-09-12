# 开发说明

## 结构

- `UnityLingo.Core`：模型与场景类型、提示词组装、HTTP 请求、命名校验、设置及 SQLite 历史。
- `UnityLingo`：WPF 主窗口、设置窗口、历史窗口。界面事件调用核心服务。
- `UnityLingo.Tests`：无测试框架依赖的可执行测试套件。失败退出码为 1，适合 Windows CI。

设置窗口编辑深拷贝，保存才替换主窗口设置。API Key 进入设置对象前已加密。请求使用独立 Authorization header，禁用 HTTP 重定向，避免配置切换共享认证状态。超时 90 秒，不自动重试；第三方 HTTP 错误正文不直接显示。

接口按 [OpenAI Chat Completions 官方文档](https://developers.openai.com/api/reference/cli/resources/chat) 的基础消息与文本响应格式实现。仅发送 `model`、`messages`、`stream: false`，以避免强制使用第三方可能不支持的扩展参数。

命名 JSON 包含 `recommended` 和两个 `alternatives`，每项包含 `name`、`explanation`。程序拒绝重复项、空说明、非法英文标识符、关键字及不符合所选首字母大小写的名称。翻译结果原样保留，包括换行和代码块。

SQLite 使用参数化查询和事务保存、裁剪历史；按自增 ID 确定新旧，不依赖系统时钟。历史分页每页 100 条，独立于保存上限。数据库版本为 1，后续结构变更需增加显式迁移，不覆盖用户数据。

## 测试

运行 README 中的测试命令。测试覆盖：

- Base URL 拼接、认证、模型切换、提示词、上下文及无历史消息。
- 五类命名、布尔和方法示例、非法名称、重复项、JSON 错误。
- 中英目标语言提示词及代码文本保留。
- HTTP 400/401/403/404/429/500、网络失败、超时映射、取消、响应截断及无自动重试。
- DPAPI 加解密、无明文 Key、配置克隆和重启持久化。
- 超过 200 的自定义上限、最旧清理、分页搜索、关闭新增、删除及清空。
- WPF 场景切换、三张命名卡、上限显示及三个窗口布局渲染。

真实模型联调需在应用中输入用户自己的 Key；不要把 Key 放到测试代码、提交或命令行。检查典型输入的实际语义质量，并尝试连接测试、生成和取消。自动化模拟测试不能证明真实模型翻译质量。

## Git 工作流

仓库：`https://github.com/CyreneMine/UnityLingo`。保持 Git 配置项目级；推送前检查差异、敏感信息、测试结果，先 commit 再 push。不要提交本地用户数据或构建产物。分支保护开启时使用功能分支和 PR，不强制推送。

## 依赖

SQLitePCLRaw.bundle_e_sqlite3 显式使用 2.1.13，避免 Microsoft.Data.Sqlite 10.0.0 默认传递的旧原生 SQLite 包所触发的 NuGet 漏洞警告。发布前应重新检查依赖告警；不通过关闭 NuGet 审计来隐藏问题。
