# BRCL 修改版说明

基线：Plain Craft Launcher 2（PCL2）`2.13.1.1`，开源版源码 `C:\works\BRCL\PCL-2.13.1.1`。
本文件记录本次改造的**全部落地内容**与**构建方式**，供后续继续修改时查阅。

---

## 一、已完成的改动

### 1. 移除「下载」页

| 文件 | 改动 |
|---|---|
| `FormMain.xaml` | 删除导航按钮 `BtnTitleSelect1`（下载） |
| `FormMain.xaml.vb` | `Handles` 子句去掉该按钮；删除 `PageType.Download` 的路由分支；`PageChange` 入口增加拦截：目标为 Download/Other 时直接返回 |
| `PageSetupUI.xaml` | 隐藏「隐藏主页面 → 下载」「更多 子页面」相关勾选项 |
| `PageLaunchLeft.xaml.vb` | 无版本时固定为「下载已禁用」状态，不再出现「下载游戏」按钮 |
| `PageSelectRight.xaml.vb` | 空版本列表不再显示「下载游戏」按钮，文案改为引导「添加已有文件夹」 |
| `PageInstanceMod.xaml` | 隐藏「下载 Mod」「下载新 Mod」按钮，空状态文案改为「从文件安装」 |
| `PageInstanceModDisabled.xaml.vb` | 「转到下载页面」按钮强制隐藏 |

说明：`Modules/Minecraft/ModDownload.vb` **保留未删**。它同时承担 Java 运行时下载、版本资源补全等启动必需能力，删除会导致启动链路断裂；页面级入口已全部移除，用户无法再从界面进入下载功能。

### 2. 移除「更多」页，保留「关于」

| 文件 | 改动 |
|---|---|
| `FormMain.xaml` | 删除导航按钮 `BtnTitleSelect4`（更多） |
| `FormMain.xaml.vb` | 删除 `PageType.Other` 路由分支 |
| `PageSetupLeft.xaml` | 设置页左栏「其他」项改为「关于」（Tag=3，图标换成信息图标） |
| `PageSetupLeft.xaml.vb` | 原「其他」页的选中/切换/重置逻辑全部改绑到 `SetupAbout` |
| `ModMain.vb` | 字段 `FrmSetupSystem` → `FrmSetupAbout` |
| `PageOtherAbout.xaml` | 新增首张「BRCL」卡片：二次创作声明、许可说明、原项目地址；隐藏「检查更新」按钮 |

帮助页、百宝箱（测试页）、反馈页的入口随「更多」页一并消失（对应源文件保留，未参与界面路由）。

### 3. 移除设置页「其他」栏

设置左栏由「启动 / 个性化 / 其他」变为「启动 / 关于」。原「其他」栏下的系统设置页不再可达。

### 3.1 移除「个性化」栏（第二轮）

`PageSetupLeft.xaml` 删除 `ItemUI` 项；`PageSetupLeft.xaml.vb` 删除对应选中/切换/重置分支；`PageSetupUI.xaml.vb` 的可用性计算去掉该项，并把左栏「只剩一项就隐藏」的阈值由 2 改为 1。
设置左栏现为 **启动 / 关于** 两项。`PageSetupUI` 页面类仍保留编译（其 `HiddenForceShow` 是全局静态属性，多个页面在引用），但已无入口。

### 3.2 新增 BRS 页面（原生单页，读接口）

- 导航栏顺序：**启动 / BRS / 交易所 / 设置**。
- `PageType.Brs = 10`；`Pages/PageBRS/PageBRS.xaml(.vb)`。
- **不再内嵌网页**：直接请求 `https://zhushu.homes/BRS/api/status`（带浏览器 UA，站点对无 UA 请求回 403），用启动器自带控件渲染，一屏不滚动：
  - 顶部：Logo、运行状态徽章、赛季天数 + QQ 群、MOTD（解析 `§` 颜色码，转成浅色背景可读色）、在线人数 `n/100`、更新时间、刷新按钮
  - 中部四指标：TPS（由 `tps_overall_ms` 换算，封顶 20）、运行时长、实体数、世界大小 + 崩溃报告数
  - 底部左：在线玩家列表；底部右：近 14 天在线柱状图（随容器高度自适应，悬停显示当天人数）
  - 60 秒自动刷新；失败时保留旧数据并提示

> MOTD 里的换行是**字面量 `\n`**（反斜杠+n）而非真实换行，已做还原处理；否则会挤成一行并出现多余字符。

### 3.3 新增交易所页面（内嵌网页）

- `PageType.Stock = 11`；`Pages/PageStock/PageStock.xaml(.vb)`，WebView2 内嵌 `https://zhushu.homes/stock/`。
- 沿用加载中 / 失败重试 / 在浏览器打开三态；缓存目录 `BRCL\WebView2\`。

### 3.4 启动弹窗与关于页精简

- 删除启动时必弹的「开源版本说明」对话框（`FormMain` 初始化线程内）。
- `PageOtherAbout` 只保留首张「BRCL」声明卡；关于/特别鸣谢/赞助者/法律信息/许可与版权声明四张卡全部删除，`PageOtherAbout.xaml.vb` 相应精简。
- 因赞助入口随之移除，`PageSetupSystem` 的识别码按钮改为直接复制识别码（该类目前已无界面入口，仅为保持可编译）。

### 3.5 UI 风格改为 BRS 站点的设计系统

对照 `zhushu.homes/BRS` 的 CSS 变量落地：

| 设计令牌 | BRS 值 | 落地位置 |
|---|---|---|
| 主文字 | `#2A2338` | 主题 Color1 |
| 品牌深色 | `#B82F86` | Color2 |
| 品牌主色 | `#D6469F` | Color3 |
| 紫色强调 | `#8B5CF6` / `#A78BFA` | Color4 / Color5 |
| 浅紫 | `#E9D5FF` | Color6 |
| 面板底 / 页面底 | `#FCF5FB` / `#FDF3F8` | Color7 / Color8 |
| 顶栏渐变 | `#B82F86 → #8B3FD6 → #C026A3` | `ThemeRefreshMain` 顶栏画刷 |
| 页面底色渐变 | `#FDF3F8 → #FBF6FC → #F3F8FF` | `ThemeRefreshMain` 的 PanForm 背景 |
| 卡片圆角 | 14px | `MyCard` 的 `CornerRadius` |
| 按钮圆角 | 8px | `MyButton.xaml` |

配色已从 HSL 动态计算改为**固定值**（开源版主题切换本就不可用），避免运行时被重新计算覆盖。

### 4. 整体更名为 BRCL

| 位置 | 改动 |
|---|---|
| 程序集名 | `Plain Craft Launcher 2` → `BRCL`（产物 `BRCL.exe`） |
| 窗口标题 | `Plain Craft Launcher　` → `BRCL　` |
| 单例互斥体 | `PCL_SingletonMutex` → `BRCL_SingletonMutex` |
| 单例唤起 | `FindWindow` 目标标题改为 `BRCL　` / `BRCL` |
| 数据目录 | 程序目录下 `PCL\` → `BRCL\`；`%TEMP%\PCL\` → `%TEMP%\BRCL\`（共 50 处路径） |
| 日志目录 | `MeloongCore.Main.Init("BRCL")` |
| 配置迁移 | 首次运行若发现旧 `PCL\Setup.ini` 而 `BRCL\` 不存在，自动复制一次 |
| 程序集信息 | Title/Product/Description/Copyright 改为 BRCL 及原作者署名 |
| 资源引用 | 所有 `pack://.../Plain Craft Launcher 2;component/...` 与 `assembly=Plain Craft Launcher 2` 改为 `BRCL` |
| 标题栏 Logo | 原「PCL」矢量描边换成 `Images/TitleLogo.png`（吉祥物圆头像 + BRCL 白字） |
| 关于页 | 版本条目改名 BRCL，署名「龙腾猫跃」保留在首位 |
| 用户文案 | 启动/主窗体的提示、EULA、错误弹窗中的 PCL 改为 BRCL |

### 5. 图标

`Images/icon.ico` 换为自制图标（吉祥物圆角方图，含 16/20/24/32/40/48/64/96/128/256 十种尺寸）；
`Images/Heads/Logo.png`（关于页头像）换为同源圆形头像。

### 6. 关键 bug 修复（改造引入）

`PageChange` 原先用 `PanTitleSelect.Children(PageType)` 按索引取导航按钮。删除两个按钮后索引错位，跳转设置页会抛「参数超出有效范围」。
已改为**按 `Tag` 查找按钮**，并在入口拦截已删除的页面。

---

## 二、构建环境（本机一次性搭建）

本机无 .NET SDK、无 net48 参考程序集，且 nuget.org 不可达，采用如下方案（全部免管理员权限）：

| 组件 | 位置 | 来源 |
|---|---|---|
| .NET SDK 8.0.404 | `C:\works\BRCL\tools\dotnet8` | `dotnetcli.blob.core.windows.net`（分 14 段并行下载） |
| net48 参考程序集 | `C:\works\BRCL\tools\net48ref\build` | NuGet 包 `Microsoft.NETFramework.ReferenceAssemblies.net48`，经华为云镜像 |
| MSBuild | VS 2022 Build Tools 17.14 | 本机已装 |
| NuGet 源 | `PCL-2.13.1.1\NuGet.config` | 华为云镜像（`mirrors.huaweicloud.com`） |

构建命令（在 `Plain Craft Launcher 2` 目录下）：

```bash
export DOTNET_ROOT="C:\\works\\BRCL\\tools\\dotnet8"
export MSBuildSDKsPath="C:\\works\\BRCL\\tools\\dotnet8\\sdk\\8.0.404\\Sdks"
export DOTNET_HOST_PATH="C:\\works\\BRCL\\tools\\dotnet8\\dotnet.exe"
"/c/Program Files (x86)/Microsoft Visual Studio/2022/BuildTools/MSBuild/Current/Bin/MSBuild.exe" \
  "Plain Craft Launcher 2.vbproj" \
  -p:Configuration=Release \
  -p:MSBuildEnableWorkloadResolver=false \
  "-p:TargetFrameworkRootPath=C:\works\BRCL\tools\net48ref\build" \
  -v:m -nologo
```

产物：`Plain Craft Launcher 2\bin\BRCL.exe`（约 5.4 MB）。

依赖文件（缺一不可，需与 exe 同目录）：`BRCL.exe.config`、`MeloongCore.dll`、`MeloongCore.Wpf.dll`、`PCLCS.dll`、`Microsoft.Win32.Registry.dll`。

---

## 三、自检钩子

新增环境变量 `BRCL_START_PAGE`，用于界面自检（无需手工点击）：

- `setup`：启动后直接进设置页
- `ui`：进设置-个性化
- `about`：进设置-关于

示例：`BRCL_START_PAGE=about BRCL.exe`

---

## 四、已知限制

1. **未做代码签名**，Windows SmartScreen 会提示「未知发布者」。
2. 本版本基于**开源版**源码：正版登录（缺 Azure Client ID）、CurseForge、主题切换、更新检查均不可用（源码本身就是空实现）。离线登录可用。
3. 下载功能的**底层模块**（`ModDownload.vb`）保留，仅界面入口移除。
4. `Pages/PageDownload`、`PageOtherHelp`、`PageOtherTest` 等源文件仍在工程内编译，只是无入口；彻底删除需逐个清理交叉引用，本次未做。
5. 「更多」页删除后，**帮助文档（Help.zip）**不再有界面入口。
6. 许可证：PCL 的「分发有限许可」要求二次创作版本名称以 `Plain Craft Launcher (PCL)` 开头、界面主色相为蓝色。本改版改名 + 换图标**仅限自用**；若对外分发需重新评估许可合规。
