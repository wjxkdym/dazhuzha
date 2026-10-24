# 大猪扎下载

当前版本：**v1.1.0-preview.2-lossless**，Windows 开发预览版。完整安装包为 4 个 ZIP，共 3,629,800,543 字节（约 3.63 GB）。

## 下载渠道

- **百度网盘**：[下载本版本完整文件夹](https://pan.baidu.com/s/1mrMRBw9EiSt3kIHC6y7zKw?pwd=q79x)，提取码 **q79x**。分享设置为永久有效。
- **GitHub Releases**：[下载本版本安装包](https://github.com/wjxkdym/dazhuzha/releases/tag/v1.1.0-preview.2-lossless)。

本项目仅使用以上两种渠道。百度网盘已上传 4 个安装 ZIP、发布包说明、RELEASE_NOTES.md 和 SHA256SUMS.txt。

## 必须下载的文件

| 文件 | 大小（字节） | SHA-256 |
| --- | ---: | --- |
| `Dazhuzha-v1.1.0-preview.2-lossless-Core.zip` | 8,503,158 | `2f582294065ed3399e597cf04da8a58a8766df4f62acd8a625cc117fc18add37` |
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

本版无损压缩保持图片分辨率、颜色、透明度和解码后的 RGBA 像素一致。尚未完成实际游戏内完整回归。Mod 不提供皮肤解锁功能；素材包含 AI 辅助重绘，与游戏官方无隶属关系。
