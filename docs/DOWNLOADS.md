# 大猪扎下载

## 2026-10-25 安装报错修复

若运行安装后出现 `Install.ps1:16` / `ConvertFrom-Json` 红字，这是旧安装器读取 UTF-8 清单的兼容性问题。

- **已经下载**：从下方任一渠道下载 `Dazhuzha-v1.1.0-preview.2-lossless-InstallerFix.zip`，将其中的 `Install.ps1` 覆盖原解压目录中的同名文件（与“安装大猪扎.cmd”同级），再运行安装。无需重新下载素材；此错误发生在覆盖游戏文件之前。
- **尚未下载**：按下面清单下载更新后的 Core 和全部 3 个 Assets ZIP。新 Core 已包含修复，无需再使用补丁。
- 补丁仅适用于本页的 `v1.1.0-preview.2-lossless`；3 个 Assets ZIP 沿用原文件及原 SHA-256。

当前版本：**v1.1.0-preview.2-lossless**，Windows 开发预览版。完整安装包为 4 个 ZIP，共 3,629,804,803 字节（约 3.63 GB）。

## 下载渠道

- **百度网盘**：[下载本版本完整文件夹](https://pan.baidu.com/s/1mrMRBw9EiSt3kIHC6y7zKw?pwd=q79x)，提取码 **q79x**。分享设置为永久有效。
- **GitHub Releases**：[下载本版本安装包](https://github.com/wjxkdym/dazhuzha/releases/tag/v1.1.0-preview.2-lossless)。

本项目仅使用以上两种渠道。两个渠道均提供安装 ZIP、InstallerFix 小补丁及更新后的校验文件；安装修复的操作说明随补丁提供。

## 必须下载的文件

| 文件 | 大小（字节） | SHA-256 |
| --- | ---: | --- |
| `Dazhuzha-v1.1.0-preview.2-lossless-Core.zip` | 8,507,418 | `5050596da98aa0f04b0bcf6515fa07b56e738fde14021c77108619f1db93f3b1` |
| `Dazhuzha-v1.1.0-preview.2-lossless-Assets-01.zip` | 1,610,216,778 | `cc19b102d63ff79008752d036719ee3e4425d36d299aab8ea53454a432020ecc` |
| `Dazhuzha-v1.1.0-preview.2-lossless-Assets-02.zip` | 1,610,539,801 | `9cd29a877a50cc91be016fcd1be8027a6e8a2e888ee2fc0ff1797bd57f7d16c4` |
| `Dazhuzha-v1.1.0-preview.2-lossless-Assets-03.zip` | 400,540,806 | `46d21cd4dcceedf646ec8ef461d91c243c7d36ce9fc8d2b19d27ea6c7eeb83d2` |

4 个 ZIP 均为独立压缩包，需全部下载，解压到同一目录并合并同名文件夹。它们不是只能打开第一卷的分卷压缩包。

## 安装顺序

1. 游戏预先安装匹配的 BepInEx 5；本包不含 BepInEx 核心或游戏程序。
2. 退出游戏和大猪扎配置器，将全部 ZIP 解压合并。
3. 运行“安装大猪扎.cmd”，选择包含 TheBazaar.exe 的游戏目录。
4. 运行“打开大猪扎配置器.cmd”，选择模块、单卡或单套皮肤，保存配置后重新启动游戏。

GitHub 自动生成的 Source code 源码包不包含完整美术，不能替代上述安装包。两个渠道的同版本 ZIP 应具有完全相同的 SHA-256，可按随包 SHA256SUMS.txt 核对。

本版无损压缩保持图片分辨率、颜色、透明度和解码后的 RGBA 像素一致。作者已确认进行了实际游戏内测试。Mod 不提供皮肤解锁功能；素材包含 AI 辅助重绘，与游戏官方无隶属关系。

## 已下载用户的小补丁

`Dazhuzha-v1.1.0-preview.2-lossless-InstallerFix.zip`：3,394 字节。SHA-256：`80b73d28768ddd2c738f0642f67dd8cc4f84b0a9c551a4e08f29a3d5ee875de6`。
