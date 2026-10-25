# GitHub 发布准备

建议仓库名：`dazhuzha`。当前候选 tag：`v1.1.0-preview.2-lossless`。

## 本地准备

1. 运行构建与测试；完成素材校验。
   安装器必须在 Windows PowerShell 5.1 与 PowerShell 7 中运行 `tests/Test-Installer.ps1`，并用 `-ReleaseManifest` 验证待发布的完整清单。
2. 更新 `release-status.json`、README 与发布说明中的皮肤数量。
3. 使用 `scripts/prepare_release.py` 生成 ZIP 与 SHA256SUMS。
4. 解压验证 Core 与所有 Assets ZIP 合并后，`package-files.json` 中的文件全部存在且哈希一致。
5. 记录实际游戏内验证结果；未验证时保持预发布标记和明确说明。

GitHub [Release 附件须小于 2 GiB](https://docs.github.com/en/repositories/releasing-projects-on-github/about-releases)，本项目把素材按 1.5 GiB 未压缩大小分组。素材 ZIP 相互独立，都解压到同名根目录；无需专用分卷工具。大体积素材不进入 Git 历史。

## 创建仓库与上传

将此目录作为 Git 仓库根目录。选择自己的 GitHub 账户与仓库地址，提交检查后的源码，设置 remote 并推送。仓库中不应包含 `installation.json`、`settings.json`、游戏 DLL、原图提取目录、会话地址、日志或本地备份。

可在 GitHub 网页创建一个 **Draft / Pre-release**，标题“大猪扎 v1.1.0-preview.2-lossless”，使用 `RELEASE_NOTES.md`，上传同一次构建的全部 ZIP、SHA256SUMS.txt 和 release-report.json。

如果已安装并登录 GitHub CLI，可手动运行：

```powershell
.\scripts\Create-DraftRelease.ps1 -Repository '你的账户/dazhuzha' -Artifacts '发布输出目录'
```

脚本只创建草稿预发布，不自动公开。它不会创建仓库、修改 remote 或推送源码。附件上传后需核对数量、大小与摘要，再公开预发布。

运行脚本前，需要把候选 tag 推送到选定仓库；脚本使用 `--verify-tag`，不会替你创建指向未知提交的 tag。确认当前提交就是待发布代码后，执行 `git tag v1.1.0-preview.2-lossless` 和 `git push origin v1.1.0-preview.2-lossless`。

## 下载渠道发布核对

- 仅保留 GitHub Releases 和百度网盘，不使用个人网站、CDN 或 BT。
- 本次采用已经完成像素验证的无损安装包，发布目录见本地渠道准备记录。不要重新打包或混用此前非 lossless 版本。
- 安装热修复沿用当前 Release 链接，仅更新 Core、发布报告与校验文件，另提供 InstallerFix ZIP；保留 3 个 Assets ZIP 的字节和校验值。公开旧包前保留本地备份，更新后核对两个渠道的实际文件。
- 百度网盘链接与 4 个 ZIP 的摘要见 DOWNLOADS.md；GitHub 应上传字节完全相同的 ZIP。
- 作者已授权将本版发布至 GitHub，并向 B 站投稿宣传片。
- 正式发布后，将真实 Release URL 更新到 DOWNLOADS.md、README、release-status.json 与 B 站简介，删除“待发布”说明。
- B 站简介提供百度网盘与 GitHub 两个入口及提取码，并说明开发预览与 AI 辅助美术状态。
