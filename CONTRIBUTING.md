# Contributing to Luma

欢迎帮助 Luma 变得更快、更自然、更适合看照片。

请先阅读 [待办清单](docs/TODO.md) 和 [开发说明](docs/DEVELOPMENT.md)。提交问题时，说明 Windows 版本、显卡/驱动、显示器 HDR 状态、图片格式/尺寸、复现步骤，以及是否开启 HDR 或 VSR；涉及个人照片时，可以使用可公开的替代样本。

改动请保持聚焦，运行与改动相匹配的检查。`tests/Luma.Tests` 检查图库索引和激活，原生测试检查 WIC / D3D11 / FP16；实际 NVIDIA 效果与窗口体验需要兼容设备的实机验证。普通 GitHub 托管构建不具备 RTX 显卡，不能替代这部分检查。

```powershell
./scripts/Build.ps1 -Publish -WithoutNvidia
dotnet run --project tests/Luma.Tests/Luma.Tests.csproj -c Release
python tests/check_public_source.py
```

不要提交自己的图库、照片、缓存、证书、令牌、SDK、运行库或构建产物。NVIDIA SDK 应由贡献者按照独立许可自行取得，放置方法见 `external/nvidia/README.md`。

提交说明使用清楚的 Conventional Commits，推荐中文。参与需求、讨论、开发或验证的人可以作为共同作者；署名必须使用对方确认的身份，详见 [AGENTS.md](AGENTS.md)。

维护者推送 `v*` 标签会进入受控的 RTX Release 草稿构建流程，详见 [发布说明](docs/RELEASING.md)。日常提交与 Pull Request 只生成开发检查和 standard 测试构建。
