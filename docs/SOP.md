# 小智 AI Studio 快速 SOP

这是一份给普通用户的最短操作流程。详细说明见 [USER_GUIDE.md](USER_GUIDE.md)。

## 1. 启动程序

```powershell
dotnet build .\Gpt2ImageWpf.sln
dotnet run --project .\src\Gpt2Image.Wpf\Gpt2Image.Wpf.csproj
```

## 2. 配置对话模型

1. 打开 **模型与设置**。
2. 配置用途选择 **聊天 API**。
3. 模型接口供应商选择：DeepSeek / MiniMax / MiMo / Kimi / Qwen / GLM。
4. 点击 **应用该供应商接口预设**。
5. 填 API Key。
6. 点击 **保存配置**。
7. 打开 **AI 对话助手**，选择模型后开始聊天。

## 3. 配置 AI 编码模型

1. 打开 **模型与设置**。
2. 配置用途选择 **AI 自动编码**。
3. 选择文本/代码模型供应商：DeepSeek / MiniMax / MiMo / Kimi / Qwen / GLM。
4. 点击 **应用该供应商接口预设**。
5. 填 API Key。
6. 点击 **保存配置**。
7. 打开 **AI 编码助手**。
8. 选择本地项目目录。
9. 输入目标。
10. 查看计划和审批文件/命令。

## 4. 配置图片生成

1. 打开 **模型与设置**。
2. 配置用途选择 **图片生成 API**。
3. 只能选择图片能力供应商：OpenAI 图像或自定义图片兼容接口。
4. 不要选择 DeepSeek / MiniMax / MiMo / Kimi / Qwen / GLM。
5. 保存后进入 **AI 图像创作**。

## 5. 配置视频生成

1. 打开 **模型与设置**。
2. 配置用途选择 **视频生成 API**。
3. 选择视频能力供应商，例如 Routin xAI Video。
4. 不要选择普通文本模型。
5. 保存后进入 **AI 视频创作**。

## 6. 能力边界速查

| 功能 | 应该选什么 | 不应该选什么 |
| --- | --- | --- |
| AI 对话 | DeepSeek / MiniMax / MiMo / Kimi / Qwen / GLM | 视频接口 |
| AI 编码 | DeepSeek / MiniMax / MiMo / Kimi / Qwen / GLM | 视频接口 / 纯图片接口 |
| 图片生成 | OpenAI 图像 / 自定义图片兼容接口 | 普通文本模型 |
| 视频生成 | Routin xAI Video / 视频接口 | 普通文本模型 |

## 7. 安全注意

- API Key 只填在程序设置页，不要提交到 GitHub。
- AI 编码只会生成提案，不会绕过审批自动改文件。
- `.env`、密钥、token、证书等敏感路径不会提供给模型。
