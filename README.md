# WPF GPT Image

一个基于 WPF 的本地生图客户端，支持 OpenAI 兼容图片接口、Responses 图片工具和部分聊天兼容图片返回格式。

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
