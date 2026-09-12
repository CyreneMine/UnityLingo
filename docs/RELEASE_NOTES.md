UnityLingo 首个 Windows 桌面版本。

## 下载与运行

下载下方 **UnityLingo-win-x64.zip**，解压后运行 `UnityLingo.exe`。请保留解压目录中的所有依赖文件，无需另行安装 .NET。

首次使用：打开“设置 → 模型配置”，填写自己的 API Base URL、模型 ID 和 API Key，测试连接后保存。发布包没有预置 Key。

## 功能

- 中英互译与 Unity 开发文档翻译。
- Unity C# 命名：推荐名、两个备选、中文解释及一键复制。
- 多套兼容 OpenAI 的模型配置，可编辑的场景提示词。
- 本地历史记录，默认上限 200 条，可随时修改。
- Windows 当前用户加密保存 API Key。

## 发布验证

发布由 GitHub Actions 从版本标签的源码构建，通过离线模拟接口、SQLite、DPAPI 和 WPF 检查后生成自包含包。构建不读取开发电脑上的 Key、设置或历史记录；不包含调试符号。

`SHA256SUMS.txt` 提供压缩包校验值。程序尚未进行代码签名。实际服务兼容性与翻译质量需要使用自己的模型配置验证。
