# 本机 EXE 升级

用于每次应用版本更新后的本机同步。普通文档提交不需要重复安装。这里的本机升级是开发交付动作，不代表应用具备联网自动更新功能。

## 载荷与安装器

- 固定预览：`artifacts/Luma/Luma.App.exe`；实验候选：`artifacts/Luma-candidate`。优先使用本轮实际验证通过的完整 RTX 载荷，保留其 `build-info.json` 和逐文件 SHA256 清单。
- 标准打包：`scripts/Build-Release.ps1 -Variant rtx -TestInstaller`，输出 `artifacts/release-rtx`；它会重新构建 `artifacts/release-payload-rtx`。`-SkipBuild` 只适用于该 release-payload 目录已是所需源版本的情况，脚本会写入当前 HEAD 来源，不可给无关旧载荷重新贴来源。
- `tests/verify_release.py --variant rtx` 与 `tests/verify_installer.py` 验证的是上述标准输出及隔离安装夹具，不要指向别的目录后仍宣称这些检查通过。
- 若已有完整固定预览且只需本机 EXE，可复用 `packaging/Luma.iss` 编译安装器，免去另一份完整 payload / portable ZIP。按 `Build-Release.ps1` 的真实实现，从 manifest 生成文件关联 / 卸载 include，传入 PayloadDir、OutputDir、ReleaseVersion、FileVersion、BuildVariant=rtx、RepositoryUrl、FileAssociationsFile、UninstallAssociationsFile、RtxLicenseFile；编译器由 `scripts/Install-BuildTools.ps1` 定位。此路径须独立保存载荷清单、安装器 hash、安装结果，不能冒称跑过标准夹具。
- 本机正式安装必须使用普通 ProductId，不能传 `TestInstall`。核对 RTX 两个 DLL、原始 PDF、runtime terms 和第三方许可证，符合已验证来源；不把头文件、SDK 输入 archive、调试符号或个人数据混入消费者包。
- Inno 明确排除 `*.pdb`、`AppxManifest.xml`，安装后比较消费者文件清单；其余完整运行时不能随意删减。

## 升级前

1. 从 `HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\{0E473918-221F-45FC-A413-6145712ADFE5}_is1` 读 `InstallLocation` / `DisplayVersion`。一般是 `%LOCALAPPDATA%\Programs\Luma`，尊重实际已注册位置，不能靠猜路径覆盖其他程序。若已同版本且 hash 相同，不重复安装；禁止无意降级。
2. 枚举 Luma 进程并核对实际 EXE 路径。正常关闭该安装的全部窗口，等待进程退出；一个进程可能同时有多个图库 / 预览窗口，关闭 MainWindow 不代表所有窗口已退出。逐窗关闭应有数量 / 时间上限，核对窗口确实消失，不对同一未响应窗口持续重发。不要杀掉无关候选或其他目录的进程。未能关闭时停止替换并保留候选，不能持续强杀 / 重试。
3. 生产数据在 `%LOCALAPPDATA%\Luma`，与安装目录分离。对 `library.db` 做 SQLite backup API 的一致性副本到忽略的 `artifacts/local-install-<version>/`；不要只复制正在 WAL 写入的主文件。保留副本及升级前的 schema / 表行数 / 内容摘要，不输出私人照片路径，不提交数据库。
4. 检查安装器版本、RTX 来源和 SHA256，与实际候选一致；记录升级前版本。不默认卸载旧版，不清理用户目录、不修改系统 HDR / 默认应用 / 证书信任。

## 安装与验收

可在用户现有授权下静默运行已核验的安装器，参数为 `/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP- /CLOSEAPPLICATIONS /LOG=<绝对日志路径>`。等待完成并检查退出码，不只看进程启动。保持同一 AppId，在原安装目录覆盖升级。

安装完成、首次正常启动前：

- 重新读取注册版本 / 安装位置，检查实际 EXE / 托管程序集版本及 `build-info.json` 源提交。
- 按消费者清单逐个核验 SHA256，特别是原生 DLL、NVIDIA 运行时和许可；同时检查没有本轮已移除依赖的陈旧残留。
- 比较 `albums` / `photos` / `album_photos` / `settings` 的逻辑行与备份，核对图库链接、收藏、历史、偏好未被安装器修改。使用稳定排序的逻辑摘要，不能用 SQLite 文件 hash 代替。

随后从**已安装 EXE** 在隔离 `--test-library` 中跑本轮关键场景，再正常打开生产图库。若新版本有 schema 迁移，额外在隔离旧库验证迁移及原有数据字段保留；不能因新列 / 新默认设置就误判数据损坏，也不能把 EXE 结论直接套用 MSIX。

保存紧凑报告：安装器退出码 / hash / 字节数、前后版本、源提交、实际路径、文件校验、数据保留、已安装版冒烟结果和恢复副本位置；将实际证据摘要写入 TODO / VERIFICATION。失败时保留日志与副本，不自动覆盖现有用户库作回滚；文件锁已明确且旧程序已正常退出时可作一次有界重试，仍失败则报告限制。
