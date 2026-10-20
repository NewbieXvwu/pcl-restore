# PCL Restore — 解除 PCL 临时的 Minecraft 下载限制

> **1、出于监管原因，本项目不支持中国大陆用户，请勿在中国大陆区域内使用本项目。本项目作者不对任何违反用户所在地区法律的行为负责。**
>
> **2、本项目与龙腾猫跃、网易公司、Mojang 公司无任何关联。**

对官方 `Plain Craft Launcher 2.exe` 做 **IL 改写**，解除 PCL 2.99.0.0 起引入的
「MC 下载 / 整合包安装」限制。工具是**可重放**的：按方法名 / 字段名 / 字符串特征定位，
未来版本重新跑一遍即可，不依赖字节偏移。

> ⚠️ **本仓库不提供任何改好的成品 EXE。** 请使用工具对**你自己那份** EXE 的副本运行 patch。
> 仓库里只有补丁工具（源码）、脚本和一份开源版补丁文件。

## 许可证

本仓库遵循 PCL 上游的《PCL 分发有限许可》（见根目录 [`LICENCE`](LICENCE)，
在线：<https://shimo.im/docs/rGrd8pY8xWkt6ryW>）。

本仓库是**非官方**的第三方工具，与 PCL 作者无关，不代表官方立场；仅对用户自行持有的
EXE 副本做本地改写，不重新分发任何 PCL 二进制。

---

## 一、快速开始（Windows，推荐）

> **必须安装 .NET 8。** 本仓库不提供任何预编译产物，`patch-windows.bat` 首次运行会
> 从源码构建工具，因此需要 **.NET 8 SDK**：<https://dotnet.microsoft.com/download/dotnet/8.0>
> 构建完成后运行同样依赖 .NET 8。本项目**没有**免 .NET 的使用方式。

1. 安装 **.NET 8 SDK**（见上）。
2. 准备好官方 PCL 程序文件 `Plain Craft Launcher 2.exe` 。
3. 在资源管理器里把 `Plain Craft Launcher 2.exe` **拖到 `patch-windows.bat` 图标上**松开即可。会在**原程序文件旁边**生成解除限制后的 `Plain Craft Launcher 2.restored.exe`。**原文件不会被修改。**

> 运行过程中会显示「正在编译补丁工具」「正在修补 EXE」「修补完成」等进度提示；首次运行需先编译（约 10 秒）。

---

## 二、跨平台（macOS / Linux）

```bash
cd tool
./build.sh
./patch.sh "/path/to/Plain Craft Launcher 2.exe" "/path/to/restored.exe"
```

或直接：

```bash
dotnet tool/bin/Release/net8.0/PclRestore.dll <input.exe> [output.exe]
```

---

## 三、它到底改了什么

`RestrictMcDownload` 是 `Const`（编译期常量），官方 EXE 里所有引用都被**内联**成
`True`，未限制的分支也被编译器**删掉了**。所以只翻字段常量毫无效果——本工具真正做的是
对 5 个方法的 IL 体做外科手术：

| # | 位置 | 改写 |
|---|---|---|
| 1 | `ModSecret::RestrictMcDownload` | 字段常量 `True` → `False`（元数据诚实化） |
| 2 | `PageDownloadInstall::LoaderInit` | 把「关闭下载」失败加载器换成 `ldsfld DlClientListLoader`，恢复 **MC 版本列表下载** |
| 3 | `PageDownloadModify::LoadInstanceFinished` | `CardMinecraft.Visibility` 由 `Collapsed` 翻回 `Visible` |
| 4 | `PageDownloadResourceDetail::InitializeStackResourceVersions` | ModPack 时恢复「安装」按钮（`Install_Click` + `Save_Click`） |
| 5 | `ModModpack::ModpackInstall(File,…)` | 把结尾 `throw` 换成按 `PackType` 派发到 `InstallPackCurseForge/HMCL/MMC/MCBBS/Modrinth/LauncherPack/Compress`，恢复**整合包安装** |

拖拽、双击关联、资源页「安装」都走第 5 条，因此都能用。

---

## 四、已知局限

- **左侧「导入整合包」/ 资源页「安装已有整合包」按钮**：这两个入口先弹 `Dialogs.SelectFile`
  文件框再安装；`SelectFile` 在 PCL 主程序**外的依赖库**里，且需现场拼后台线程闭包，
  二进制层面无法安全重建。它们目前仍会提示限制原文。
  **替代方式**：把整合包文件**直接拖进 PCL 窗口**即可安装，效果相同。
- **`FormMain::FileDrag` 旧特判**：编译期内联成运行时判断，现已成死代码，无害，未动。
- 以上两点在**源码补丁**重编译时可一并彻底去除（见下节）。

---

## 五、开源版补丁（次要）

`patch/remove-mc-download-restriction.patch` 是给**自行编译开源版**用的补丁：把
`ModSecret.RestrictMcDownload` 置 `False`，并移除 `FormMain` 更新日志里的两条限制说明。
它适用于 PCL 开源仓库（`Plain Craft Launcher 2/` 目录结构）。

> 本仓库的重点是**对官方 EXE 打补丁**；开源版补丁仅是附带参考，方便能自行编译的人获得完整功能。

---

## 目录

```
pcl-restore/
├── README.md
├── LICENCE                   # PCL 上游许可证（《PCL 分发有限许可》）
├── .gitignore / .gitattributes
├── tool/                     # 打补丁工具（重点）
│   ├── Program.cs            # 入口：读 EXE → 打补丁 → 写 EXE
│   ├── Patcher.cs            # 5 处 IL 改写
│   ├── PclRestore.csproj     # dnlib 走 NuGet，仓库保持纯源码
│   ├── patch-windows.bat     # ★ Windows 一键 patch（需 .NET 8，纯 ASCII）
│   ├── notice.txt            # 启动输出的两行声明 + 简短许可提示（UTF-8）
│   ├── msg/                  # 进度提示文案（编译中 / 修补中 / 完成 / 失败）
│   ├── build.sh / patch.sh   # macOS / Linux
└── patch/
    └── remove-mc-download-restriction.patch   # 开源版补丁（附带）
```
