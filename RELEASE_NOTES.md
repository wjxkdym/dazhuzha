# 大猪扎 v1.1.0-preview.2-lossless

## 2026-10-25 安装热修复（hotfix 1）

修复部分中文 Windows 用户选择游戏目录后出现红字、`Install.ps1:16` / `ConvertFrom-Json` 报错的问题。安装器现在明确按 UTF-8 读取文件清单，兼容 Windows 自带的 PowerShell 5.1。

- **已下载的用户**：只下载 `Dazhuzha-v1.1.0-preview.2-lossless-InstallerFix.zip`，将其中的 `Install.ps1` 覆盖到原解压目录（与“安装大猪扎.cmd”同级），再运行安装。无需重新下载素材。
- **新用户**：下载本页更新后的 Core ZIP 和全部 3 个 Assets ZIP；Core 已包含修复。
- GitHub 与百度网盘沿用原下载链接。3 个 Assets ZIP 完全不变；Core 校验值已更新，补丁校验值也收录在 `SHA256SUMS.txt`。
- Windows PowerShell 5.1（中文默认代码页）和 PowerShell 7 各通过 9 项隔离安装检查，包括配置保留、备份及损坏文件拒绝安装；完整 6,450 项文件清单解析成功。

「大猪扎」英雄皮肤补齐后的整合版本。Logo 基于游戏原字标的金边、银蓝字面和菱形宝石结构猪化，配置器同步使用新名称与图标。

- 整合卡牌、英雄、事件、野怪、储存箱和主界面 6 个模块。
- 本地配置器可按单卡、单皮肤或成组的动画图层选择内容，支持预览、搜索、方案导入导出。
- 收录 1,376 张可启用的正式卡猪化图，保留卡牌原始背景和文字，附魔沿用原生材质与特效。
- 当前客户端清单中的 176 套英雄皮肤全部完成，共 352 张头像与立绘，每套独立配置；不提供皮肤解锁功能。
- 保留旧插件 ID 与配置路径，安装更新保留用户的勾选配置。
- 安装器校验素材并备份要更新的文件，发生写入错误时回滚。

## 下载与安装

下载本版本 Core 和所有 Assets ZIP，解压到同一位置并合并同名目录，再运行“安装大猪扎.cmd”。BepInEx 5 需要另行安装，本包不包含游戏程序或 BepInEx 核心。

## 下载渠道

本版使用百度网盘和 [GitHub Releases](https://github.com/wjxkdym/dazhuzha/releases/tag/v1.1.0-preview.2-lossless)，完整清单与校验值见 [下载说明](https://github.com/wjxkdym/dazhuzha/blob/main/docs/DOWNLOADS.md)。无损压缩包合计约 3.63 GB，图片尺寸和解码像素保持一致。

百度网盘：https://pan.baidu.com/s/1mrMRBw9EiSt3kIHC6y7zKw?pwd=q79x ，提取码：q79x。
