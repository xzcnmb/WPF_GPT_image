# WPF GPT Image

使用 WPF 开发的 GPT Image 图片生成程序，支持多种 OpenAI-compatible 接口协议，并提供聊天、历史记录和图片编辑能力。

## 功能

- OpenAI Images 兼容接口
- Responses 图片工具接口
- Chat Completions 图片兼容返回
- 创作页支持上传参考图 / 编辑图
- 支持可选蒙版图片编辑
- 新增 Chat 对话页面，聊天记录持久化到 SQLite
- 本地 SQLite 历史记录
- 图片预览、自动保存、手动另存为和运行日志
 - 新增文生视频流程，支持提交任务、轮询状态、在线播放生成结果
 - 新增视频结果操作：另存为、复制链接、打开文件、打开文件夹、复制路径
 - 优化视频播放器底部按钮，修复深色背景下按钮看不清的问题
- 优化全局按钮视觉样式，提升层级和交互反馈

## 页面说明

- **创作**：输入提示词生成图片，也可以上传参考图或编辑图进行二次编辑。
- **对话**：与当前配置的主模型聊天，会话和消息会保存到本地数据库。
- **自动任务**：运行自动化生图任务。
- **历史**：查看历史任务、预览输出图片、保存文件。
- **设置**：配置 Base URL、API Key、主模型、图片模型和协议。

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


```

## 本地数据

- 日志：程序目录下的 `logs`
- 数据库、图片和缓存：当前用户本地应用数据目录
- 输入图片与生成结果会保存到本地图片目录
- API Key 使用 DPAPI 按当前 Windows 用户加密保存
