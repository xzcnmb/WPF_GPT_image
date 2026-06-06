# WPF GPT Image

使用 WPF 做的 GPT Image 图片生成程序，支持多种接口协议。

## 功能

- OpenAI Images 兼容接口
- Responses 图片工具接口
- Chat Completions 图片兼容返回
- 本地 SQLite 历史记录
- 图片预览、保存和运行日志

## 环境

- .NET 6 SDK
- Windows

## 运行

```powershell
dotnet build .\Gpt2ImageWpf.sln
dotnet run --project .\src\Gpt2Image.Wpf\Gpt2Image.Wpf.csproj
```

## 测试

```powershell
dotnet test .\Gpt2ImageWpf.sln
```

## 本地数据

- 日志：程序目录下的 `logs`
- 数据库、图片和缓存：当前用户本地应用数据目录
- API Key 使用 DPAPI 按当前 Windows 用户加密保存
