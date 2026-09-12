# UnityLingo

为 Unity / C# 开发者制作的 Windows AI 场景翻译助手。输入中文意图，得到适合代码的名称；也可以翻译中英文和开发文档。

**[下载 Windows x64 最新版本](https://github.com/CyreneMine/UnityLingo/releases/latest)** · 在 Assets 中选择 `UnityLingo-win-x64.zip`。

## 快速开始

1. 解压 Windows x64 发布包，运行 `UnityLingo.exe`，保留同目录所有依赖文件。无需另外安装 .NET。
2. 打开 **设置 → 模型配置 → 新增**，填写名称、API Base URL、模型 ID 和自己的 API Key。
3. 可点击 **测试连接**（发送一次简短请求，可能产生少量 API 费用），再保存设置。
4. 选择场景、模型和命名对象或目标语言，输入文本；点击生成或按 `Ctrl+Enter`。
5. 复制推荐名称、备选名称或译文。

示例配置仅用于说明，须替换成服务商提供的实际值：

| 字段 | 示例 |
| --- | --- |
| 名称 | 我的模型 |
| API Base URL | `https://api.example.com/v1` |
| 模型 ID | `your-model-id` |
| API Key | `your-api-key`（在软件中填写） |

程序在 Base URL 后追加 `/chat/completions`。保留服务商要求的路径前缀，不要填写完整 endpoint。支持 HTTPS；本机回环地址允许 HTTP。模型必须支持 Chat Completions 的 `system` / `user` 消息与文本输出；不支持仅提供 Responses 或其他独立协议的接口。

## 场景与命名

- **中英翻译**：默认按主要语言自动中英互译，也可指定中文或英文。
- **Unity 开发文档**：提示模型保留代码、API、链接及 Markdown；结果按原始文本显示和复制，不执行代码或渲染 HTML。
- **Unity C# 命名**：局部变量、字段使用 `camelCase`；属性、方法、类使用 `PascalCase`。一个推荐名、两个备选和中文解释。
- **自定义场景**：新增、复制、编辑和删除提示词，选择翻译或命名输出。内置场景可编辑和恢复默认。

例如输入“移动速度”，上下文“角色 float 字段”，预期得到 `moveSpeed` 一类名称；输入“受到伤害”并选择方法，预期得到 `TakeDamage` 一类名称。具体结果取决于模型。

自定义提示词示例：

> 你是一位 Unity UI 开发者。优先使用 UGUI 常见术语，按按钮、面板、事件处理的职责命名。说明使用简短中文，避免生僻缩写。

程序会另行附加输出结构、大小写与任务约束。命名结果不符合格式时会报错，不自动重试或产生额外请求。第一版采用提示词 JSON 约束和本地校验，不依赖厂商的 JSON Schema 参数。

## 历史与数据

- **设置 → 历史与存储**明确显示历史记录上限，默认 200，可随时调整为正整数（最大为 32 位整数范围，不是固定 200 条）。调低并需要清理时会确认数量。
- 历史支持搜索、分页、查看、复制、删除、清空；关闭保存不会删除已有记录。
- 数据目录：`%LOCALAPPDATA%\UnityLingo`。`settings.json` 保存配置及加密 Key；`history.db` 保存本地历史。
- Key 使用 Windows DPAPI 当前用户加密，换用户或机器后需重新填写。历史文本不加密；需要备份时先退出软件，再备份数据目录。
- 正常请求仅发送当前文本、上下文和场景提示词给配置的模型服务，不自动发送历史；模型服务自身的数据政策以该服务为准。
- 不记录 API Key 或服务端原始错误正文。取消会停止本地等待，但服务端可能已经处理请求。

## 开发与验证

要求 Windows 与 .NET 10 SDK。

```powershell
dotnet restore UnityLingo.sln
dotnet build UnityLingo.sln -c Release --no-restore
dotnet run --project tests/UnityLingo.Tests -c Release --no-build
dotnet run --project src/UnityLingo -c Release --no-build
```

测试使用模拟 HTTP，不需要真实 Key，也不会调用付费模型。包含 SQLite / DPAPI 验证和 WPF 布局渲染；截图输出到被 Git 忽略的 `artifacts/qa`。测试运行在独立临时目录，不改动真实用户设置。

生成自包含包：

```powershell
powershell -ExecutionPolicy Bypass -File scripts/publish.ps1
```

每次使用新的 `artifacts/publish/<随机目录>` 构建，输出 `artifacts/UnityLingo-win-x64.zip` 和 `artifacts/SHA256SUMS.txt`，不会将旧发布目录中的用户文件打包。发布包未做代码签名，暂无自动更新。

推送 `v*` 版本标签时，GitHub Actions 会从干净检出构建、运行离线检查并发布 ZIP 与校验值至 GitHub Releases；无需上传个人 API Key 或配置。

项目结构和验证细节见 [开发说明](docs/DEVELOPMENT.md)。配置示例见 [模型配置示例](docs/model-profile.example.json)。

## 第一版边界

不包含划词、OCR、全局快捷键唤起、Unity 编辑器插件或自动模型发现。模型质量与实际服务兼容性需要使用自己的 API 配置验证；程序校验命名格式，不能保证每次语义判断都最合适。
