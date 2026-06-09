# 小智 AI Studio

小智 AI Studio 是一款基于 WPF 的多模型 AI 助手工作台，聚合图像创作、视频创作、日常对话、智能任务、AI 编码、历史资产和多供应商模型配置能力。

## 功能

- 图像创作：支持 OpenAI Images、Responses 图片工具和 Chat Completions 图片兼容返回。
- 创作页支持上传参考图 / 编辑图，并支持可选蒙版图片编辑。
- 视频创作：支持视频生成 API 提交、轮询、在线播放生成结果和另存为。
- 视频结果操作：另存为、复制链接、打开文件、打开文件夹、复制路径。
- AI 对话：支持文本、图片和文本/代码附件，会话持久化到 SQLite。
- AI 编码助手：支持本地工作区、项目上下文、计划任务、文件变更审批和安全命令审批。
- 多模型接入：支持 OpenAI-compatible 对话/编码供应商预设，包括 DeepSeek、MiniMax、Mino、Kimi、Qwen、GLM 等。
- 历史与资产：本地 SQLite 历史记录、图片/视频预览、自动保存、手动另存为和运行日志。
- 优化全局按钮视觉样式和视频播放器底部按钮，提升层级、对比度和交互反馈。

## 页面说明

- **AI 图像创作**：输入提示词生成图片，也可以上传参考图或编辑图进行二次编辑。
- **AI 视频创作**：提交视频生成任务并轮询结果，生成后可在线播放或保存。
- **AI 对话助手**：选择聊天模型供应商后进行日常对话、图片理解和代码问答。
- **智能任务**：运行可控轮次的 Goal 自动化任务。
- **AI 编码助手**：围绕本地项目进行计划、搜索、文件提案和验证命令审批。
- **历史与资产**：查看历史任务、预览输出、保存文件。
- **模型与设置**：配置 Base URL、API Key、协议、主模型、图像模型、视频模型和并发。

## 生图与图片编辑

程序会根据当前协议自动选择请求方式：

- **OpenAI Images**
  - 纯文生图：`/v1/images/generations`
  - 上传图片编辑：`/v1/images/edits`
- **OpenAI Responses**
  - 使用 `image_generation` 工具
  - 上传图片时自动附加 `input_image`
- **Chat Completions 图片兼容协议**
  - 通过多模态 `image_url` 内容提交输入图片

> 提示：蒙版能力是否可用取决于你所接入的兼容后端是否支持相应字段。

## 环境

- Windows
- .NET 6 SDK

## 运行

```powershell
dotnet build .\Gpt2ImageWpf.sln
dotnet run --project .\src\Gpt2Image.Wpf\Gpt2Image.Wpf.csproj
```

如果本机没有仓库 `global.json` 指定的 SDK 版本，也可以在仓库外目录执行：

```powershell
dotnet build E:\WPF_GptImage\src\Gpt2Image.Wpf\Gpt2Image.Wpf.csproj
dotnet test E:\WPF_GptImage\tests\Gpt2Image.Tests\Gpt2Image.Tests.csproj
```

## 测试

```powershell
dotnet test .\Gpt2ImageWpf.sln
```

## 本地数据

- 日志：程序目录下的 `logs`
- 数据库、图片和缓存：当前用户本地应用数据目录
- 输入图片与生成结果会保存到本地图片目录
- API Key 使用 DPAPI 按当前 Windows 用户加密保存
