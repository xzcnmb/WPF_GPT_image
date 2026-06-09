# 小智 AI Studio 使用说明 / Wiki

本文档面向第一次使用小智 AI Studio 的用户，说明如何配置模型、选择功能、运行图像/视频/对话/AI 编码工作流，以及如何避免把不支持的模型用于错误功能。

> 安全提醒：不要把 API Key 写进 GitHub、截图、日志或聊天内容。程序会把 API Key 使用 Windows DPAPI 加密保存在本机用户数据中。

## 1. 这是什么程序

小智 AI Studio 是一个 Windows WPF 桌面端多模型 AI 助手工作台，当前包含：

- **AI 图像创作**：文生图、参考图、图片编辑、蒙版编辑。
- **AI 视频创作**：提交视频生成任务、轮询状态、在线播放、另存为。
- **AI 对话助手**：日常对话、图片附件、文本/代码附件、会话持久化。
- **智能任务**：按 Goal 模式运行多轮自动任务。
- **AI 编码助手**：读取本地项目上下文，生成计划、文件变更提案和安全命令建议。
- **历史与资产**：查看本地生成记录和输出文件。
- **模型与设置**：按功能配置不同供应商、模型、协议、API Key 和并发。

## 2. 第一次运行

### 2.1 环境要求

- Windows 10/11
- .NET 6 SDK 或兼容 SDK

### 2.2 编译运行

```powershell
dotnet build .\Gpt2ImageWpf.sln
dotnet run --project .\src\Gpt2Image.Wpf\Gpt2Image.Wpf.csproj
```

如果你在仓库外运行：

```powershell
dotnet build E:\WPF_GptImage\src\Gpt2Image.Wpf\Gpt2Image.Wpf.csproj
dotnet test E:\WPF_GptImage\tests\Gpt2Image.Tests\Gpt2Image.Tests.csproj
```

## 3. 模型与设置：最重要

打开左侧导航：

```text
模型与设置
```

设置页遵循一个原则：**先选功能用途，再选该功能支持的供应商。**

不同模型不是万能的：

- DeepSeek、MiniMax、MiMo、Kimi、Qwen、GLM 等普通 OpenAI-compatible 文本模型，通常只适合：
  - AI 对话
  - 提示词润色
  - AI 编码
- 图片生成需要图片能力，例如 OpenAI Images、OpenAI Responses image_generation 或明确支持图片的兼容接口。
- 视频生成需要视频能力，例如 Routin xAI Video 或明确支持视频生成的接口。

### 3.1 功能用途与供应商过滤

在设置页左侧选择“配置用途”：

| 配置用途 | 可以选择的供应商 | 不应选择的供应商 |
| --- | --- | --- |
| AI 图像创作 / 图片生成 API | OpenAI 图像、自定义图片兼容接口 | DeepSeek、MiniMax、MiMo、Kimi、Qwen、GLM、Routin 视频 |
| AI 视频创作 / 视频生成 API | Routin xAI Video | DeepSeek、MiniMax、MiMo、Kimi、Qwen、GLM、OpenAI 图像 |
| AI 对话助手 / 聊天 API | OpenAI、DeepSeek、MiniMax、MiMo、Kimi、Qwen、GLM、自定义 OpenAI-compatible | Routin 视频 |
| 提示词润色 / 对话 API | OpenAI、DeepSeek、MiniMax、MiMo、Kimi、Qwen、GLM、自定义 OpenAI-compatible | Routin 视频 |
| AI 编码助手 | OpenAI、DeepSeek、MiniMax、MiMo、Kimi、Qwen、GLM、自定义 OpenAI-compatible | Routin 视频、纯图片接口 |
| Agent / Responses API | OpenAI Responses、自定义 Responses 接口 | 普通聊天模型、视频接口 |

程序会根据当前用途自动过滤“模型接口供应商”和“接口协议”，避免把文本模型错误用于图片/视频。

### 3.2 对话/编码供应商推荐配置

| 供应商 | Base URL | 推荐主模型 | 用途 |
| --- | --- | --- | --- |
| DeepSeek | `https://api.deepseek.com/v1` | `deepseek-chat` | 日常对话、AI 编码 |
| MiniMax | `https://api.minimax.chat/v1` | `MiniMax-Text-01` | 日常对话、AI 编码 |
| MiMo / Xiaomi | `https://token-plan-cn.xiaomimimo.com/v1` | `mimo-v2.5-pro` | 日常对话、AI 编码 |
| Kimi / Moonshot | `https://api.moonshot.cn/v1` | `moonshot-v1-8k` | 日常对话、代码问答 |
| Qwen / 通义千问 | `https://dashscope.aliyuncs.com/compatible-mode/v1` | `qwen-plus` | 日常对话、AI 编码 |
| GLM / 智谱 | `https://open.bigmodel.cn/api/paas/v4` | `glm-4-flash` | 日常对话、轻量编码 |

> MiniMax Starter key 已测试可用 `MiniMax-Text-01`。`MiniMax-M1` 也能调用，但会输出 reasoning/think 内容，不适合作为普通聊天默认模型。

### 3.3 保存配置 SOP

1. 打开 **模型与设置**。
2. 在 **配置用途** 选择你要配置的功能，例如：
   - 日常对话：选 `聊天 API`
   - AI 编码：选 `AI 自动编码`
   - 图片：选 `图片生成 API`
   - 视频：选 `视频生成 API`
3. 在 **模型接口供应商** 选择供应商。
4. 点击 **应用该供应商接口预设**。
5. 检查：
   - 接口地址 Base URL
   - 接口协议
   - 主模型 / 图像模型 / 视频模型
6. 输入 API Key。
7. 点击 **保存配置**。
8. 回到对应功能页面使用。

## 4. AI 对话助手

### 4.1 配置

先到 **模型与设置**：

- 配置用途：`聊天 API`
- 供应商：DeepSeek / MiniMax / MiMo / Kimi / Qwen / GLM / 自定义
- 保存配置

### 4.2 使用

1. 打开 **AI 对话助手**。
2. 顶部选择对话模型。
3. 输入消息。
4. 可选：添加图片、文本、代码附件。
5. 点击发送。

注意：

- 新建会话会绑定当时选择的模型配置。
- 已有会话继续使用创建时的后端和模型，避免历史对话混乱。
- 代码块会用深色等宽样式展示。

## 5. AI 编码助手

### 5.1 配置

先到 **模型与设置**：

- 配置用途：`AI 自动编码`
- 供应商：DeepSeek / MiniMax / MiMo / Kimi / Qwen / GLM / 自定义
- 保存配置

### 5.2 使用 SOP

1. 打开 **AI 编码助手**。
2. 点击 **选择代码目录**，选择本地项目目录。
3. 点击刷新项目树或搜索关键字。
4. 选择会话模式：
   - `Chat`：只问答，不建议本地工具。
   - `Clarify`：先澄清需求和风险。
   - `Cowork`：可提出读取、修改和验证建议。
   - `Code`：专注代码编辑。
   - `ACP`：计划-实现-审查流程。
5. 选择编码模型。
6. 输入目标，例如：

```text
修复设置页供应商过滤逻辑，并运行 dotnet build 验证。
```

7. 点击启动。
8. 查看计划 / Tasks。
9. 在审批中心逐项确认：
   - 文件变更：查看 diff 后手动应用或拒绝。
   - 命令：只允许安全白名单命令，手动运行或拒绝。

### 5.3 AI 编码安全边界

AI 编码助手采用 Human-in-the-loop：

- AI 只能提出文件变更提案，不能直接写文件。
- AI 只能建议命令，不能绕过审批自动执行。
- 敏感文件不会提供给模型，例如：
  - `.env`
  - `secrets.json`
  - `*.pem`
  - `*.key`
  - `*.pfx`
  - 路径含 `secret`、`password`、`token`、`credential`
- 命令白名单主要包括：
  - `dotnet build`
  - `dotnet test`
  - `git status`
  - `git diff`
- 禁止自动执行危险命令，例如：
  - 删除文件
  - `git push`
  - `git reset`
  - `git clean`
  - `git commit`
  - `git add`
  - 安装依赖
  - 下载并执行脚本

## 6. AI 图像创作

### 6.1 配置

先到 **模型与设置**：

- 配置用途：`图片生成 API`
- 供应商：OpenAI 图像 或 自定义图片兼容接口
- 协议按接口实际能力选择：
  - OpenAI Images：`/v1/images/generations`、`/v1/images/edits`
  - OpenAI Responses：`/v1/responses` + `image_generation`
  - Chat Completions 图片兼容：接口明确支持图片返回时使用
- 保存配置

不要把 DeepSeek、MiniMax、MiMo、Kimi、Qwen、GLM 这类普通文本模型配置到图片生成里。

### 6.2 使用

1. 打开 **AI 图像创作**。
2. 输入提示词。
3. 可选：上传参考图 / 编辑图 / 蒙版。
4. 选择参数。
5. 点击生成。
6. 在预览区查看结果，可另存为。

## 7. AI 视频创作

### 7.1 配置

先到 **模型与设置**：

- 配置用途：`视频生成 API`
- 供应商：Routin xAI Video
- 默认视频模型：`grok-imagine-video`
- 保存配置

不要把普通聊天模型配置到视频生成里。

### 7.2 使用

1. 打开 **AI 视频创作**。
2. 输入视频提示词。
3. 可选：填写起始图片 URL / 参考图片 URL。
4. 提交生成。
5. 程序会自动轮询状态。
6. 完成后可在线播放、另存为、复制链接、打开文件或打开文件夹。

## 8. 智能任务 / Goal

1. 打开 **智能任务**。
2. 输入目标。
3. 配置最大轮次。
4. 根据需要启用联网搜索。
5. 启动任务。
6. 查看每一轮状态和输出。

## 9. 历史与资产

1. 打开 **历史与资产**。
2. 查看历史任务。
3. 预览图片/视频输出。
4. 使用另存为或打开文件夹。

本地数据位置：当前 Windows 用户本地应用数据目录。API Key 使用 DPAPI 加密保存。

## 10. 常见问题

### Q1：为什么我在图片生成里看不到 DeepSeek / Qwen / GLM？

因为这些通常是文本/代码模型，不是图片生成模型。程序按能力过滤，避免误用。

### Q2：为什么视频生成里只有视频供应商？

视频生成需要专门的视频生成 API。普通聊天模型不能直接生成视频。

### Q3：MiMo 的 tp- key 应该用哪个地址？

MiMo Token Plan key 推荐：

```text
https://token-plan-cn.xiaomimimo.com/v1
```

推荐模型：

```text
mimo-v2.5-pro
```

### Q4：MiniMax Starter 默认用哪个模型？

推荐：

```text
MiniMax-Text-01
```

`MiniMax-M1` 能调用，但可能输出 reasoning/think 内容，不适合作为普通聊天默认。

### Q5：为什么保存后聊天页没立刻看到新模型？

回到 AI 对话助手后点击刷新模型，或重新打开页面。新会话会使用新配置，旧会话仍使用创建时绑定的模型。

### Q6：AI 编码会不会自动改我代码？

不会。它只生成提案。文件修改和命令执行都要你在审批中心手动确认。

## 11. 推荐上手流程

如果你只是想先跑通：

1. 进入 **模型与设置**。
2. 选择 `聊天 API`。
3. 选择 `DeepSeek` 或 `MiMo`。
4. 应用预设，填 API Key，保存。
5. 进入 **AI 对话助手**，新建会话测试。
6. 再回设置页选择 `AI 自动编码`，用同样供应商保存一份编码配置。
7. 进入 **AI 编码助手**，选择本地项目，输入小目标，查看计划和审批。
