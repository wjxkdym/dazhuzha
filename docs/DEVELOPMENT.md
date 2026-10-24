# 构建与测试

## 结构

`src/` 为运行时插件与配置器；`Configurator/web/` 为静态界面；`Configurator/catalog.json` 为独立选择项。各插件的 JSON 清单存放在 `BepInEx/plugins/` 中。完整美术素材仅在安装包分发，不进入 Git。

## 构建

Windows 自带 .NET Framework C# 编译器用于构建配置器：

```powershell
.\Build.ps1 -ConfiguratorOnly
```

构建全部插件需要本地合法安装的游戏及 BepInEx 5 依赖：

```powershell
.\Build.ps1 -GamePath '你的游戏目录'
```

脚本只读取游戏依赖，不将这些第三方 DLL 复制进仓库。现有编译器可能报告 Newtonsoft.Json 对 netstandard 版本的 CS1701 兼容性警告；编译成功不等同于已完成游戏内验证。

## 测试

```powershell
pwsh -File tests/Test-SkinIndex.ps1
pwsh -File tests/Test-Gates.ps1
```

API 测试在完整安装包目录运行，需要 Python 3；测试工具使用单独的假游戏目录：

```powershell
pwsh -File tests/Run-ApiTests.ps1
```

不会连接真实游戏配置。GitHub Actions 只做不依赖游戏程序与完整素材的源码编译和配置逻辑测试。

## 制作发布包

先准备包含最新 DLL、素材与 catalog 的完整工作包，再执行：

```powershell
python scripts/prepare_release.py --suite '完整工作包路径' --output '发布输出目录'
```

该脚本校验引用、每张运行时素材的 SHA-256、皮肤数量和清单一致性；只收录当前引用的预览与允许的文件。PNG 母版字节不变。输出 Core ZIP、按大小分组的 Assets ZIP、SHA256SUMS、发布说明与检查报告。

保留现有插件 ID、配置 ID 与路径，避免把旧用户的逐项选择重置。换皮只更改显示素材，不改变皮肤拥有状态。
