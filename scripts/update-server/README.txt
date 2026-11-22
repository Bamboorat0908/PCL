BRCL 更新服务部署记录（2026-11-13）

SSH: ssh -p 25210 root@t42.sjcmc.cn
服务: brcl-updates.service（已启用开机启动）
目录: /opt/brcl-updates
监听: 0.0.0.0:25261
公网更新基址: http://t42.sjcmc.cn:25261/
已验证映射: 公网 TCP 25261 -> 192.168.1.83:25261
25210 是 SSH 端口，不能用于 HTTP 下载。

访问入口:
  /latest.json      版本、更新说明、包大小与 SHA-256
  /latest.sig       RSA SHA-256 签名（Base64）
  /releases/BRCL-2026.11.13.12.exe（当前公开下载，仅一个EXE）
  /releases/BRCL-2026.11.13.12.zip（自动更新内部传输，仅含EXE）
目录索引和其他路径均返回 404。
服务以 nobody 运行；签名私钥不上传服务器。

客户端:
  Modules/ModBrclUpdate.vb 固定更新基址及签名公钥。
  每次启动在首屏与协议授权后独立后台检查；更新偏好控制提示与下载行为。
  手动入口：设置 -> 检查更新，或设置 -> 关于 -> 检查启动器更新。
  自动下载模式也会在安装前询问；有下载任务时阻止安装。
  发布版本使用独立的 BrclReleaseVersion，避免与上游版本号混淆。
  安装器仅接受指定的根目录程序文件；不会替换游戏或登录配置。
  备份位置: 启动器目录/BRCL/UpdateBackups/时间戳
  安装失败时恢复替换过的文件；临时目录 error.txt 保留错误。

后续发布（在源码仓库 PowerShell 中执行）:
  1. 按 website/brcl/deploy/RELEASE.md 的「确定版本号」选定三段式展示版本，然后同步修改 BrclReleaseVersion、BrclUpdateRevision、AssemblyFileVersion 和 Changelog.json。
  2. .\scripts\test-all.ps1
  3. .\scripts\publish-update.ps1 -Version '2026.11.13.13' -DisplayVersion '1.0.3' -Notes '更新说明' -PrepareOnly
  4. .\scripts\test-release-package.ps1 -ReleaseDirectory artifacts\updates\2026.11.13.13 -Version '2026.11.13.13' -DisplayVersion '1.0.3'
  5. .\scripts\publish-update.ps1 -Version '2026.11.13.13' -DisplayVersion '1.0.3' -PublishPrepared
  6. .\scripts\verify-public-release.ps1 -Version '2026.11.13.13'
     紧急修复可增加 -Critical。
     PrepareOnly 编译、打包并签名，PublishPrepared 只上传已验证的字节，不重建。
     已发布的 ZIP 不允许被不同内容覆盖；同哈希的中断发布允许重试激活。

签名私钥（必须备份并保密）:
  C:\Users\BRPC\.codex\secrets\brcl-update-signing.xml
公钥: scripts/update-server/public-key.xml（已内置到客户端）
私钥丢失后无法为已分发客户端发布可信更新，不能随意重新生成。

验证:
  正常安装/重启、路径越界拒绝、失败恢复、游戏目录保留测试通过。
  通过 SSH 隧道下载服务器清单及完整 ZIP，签名、大小、哈希通过。
  篡改清单校验失败；服务器私有路径/目录索引被拒绝。
  Release 编译通过。桌面启动器已通过安装器更新并重启。
  公网 25261 已连通，清单签名和完整更新包 SHA-256 已通过验证:
    curl.exe --connect-timeout 5 http://t42.sjcmc.cn:25261/latest.json

BRS 服务器整合包（2026-11-13）:
  当前包: TRM 1.10 高预设，Modrinth 格式。
  下载地址: http://t42.sjcmc.cn:25261/modpacks/TRM-1.10-high.zip
  文件大小: 170920685 bytes
  SHA-256: f86adf0aff1711c2eb80a91812be14338b246693cfc0d3eaf35d4ef74a9d2fef
  Minecraft 1.21.1 / NeoForge 21.1.252，清单含 325 项模组下载（约 800 MB）。
  服务支持 HTTP Range 分段和续传，非法范围返回 416。
  ModBrsPack.vb 固定包地址、大小和 SHA-256，下载后先校验再调用现有 Modrinth 安装器。
  当前游戏目录没有有效 TRM 实例时，首页显示“下载服务器整合包”。
  安装目录名称冲突时自动使用新名称，保留原有实例；安装成功后标记并选择 TRM。
  安装中首页按钮禁用，可在任务管理中查看进度和取消。
  使用 TRM 名称、英文/中文整合包名称及安装标记识别，排除无关版本和错误实例。
  客户端版本 2026.11.13.4 已发布，桌面启动器已更新。
  编译及 test-brs-pack.ps1 通过，公网完整 ZIP 下载哈希通过。
  HTTP 首部、中部、尾部、开放范围与越界范围测试通过。
  UI 已验证缺少实例入口、确认框、下载任务及取消后的恢复。
  实际下载随后被取消，完整游戏依赖安装尚未完成端到端验证。
  后续替换整合包时，请使用新文件 URL，并同步修改客户端大小和哈希后发布新版。
崩溃日志上报（版本 2026.11.13.5）:
  设置 -> 启动 -> 崩溃日志上报，可启用或关闭，默认启用。
  接口: POST http://t42.sjcmc.cn:25261/api/crash-reports
  归档: /var/lib/brcl-reports/launcher/YYYY-MM-DD/报告编号/
        /var/lib/brcl-reports/minecraft/YYYY-MM-DD/报告编号/
  metadata.json 保存分类、版本、时间和实例名；同目录为脱敏日志。
  日志不在 public 目录，HTTP GET 无法下载；需通过 SSH 管理。
  客户端先脱敏，再用 RSA 包装的 AES/HMAC 加密；收到有效签名回执后才删除本地待传文件。
  关闭开关会停止收集和上传；待传文件保留加密状态，重新启用后重试。
  本地待传目录: BRCL/CrashReports/Pending，最多 50 份，上报时清理超过 14 天的缓存。
  服务端限制单次请求 3 MB、并发 4、每来源每小时 120 次、每日 500 份及总量 2 GB。
  新报告到达时清理超过 30 天的分类归档。
  服务端私钥: /etc/brcl-reports/upload-private.pem，必须备份，不可公开。
  已验证模拟启动器/游戏日志归类、脱敏、加密、签名回执、重复抑制、失败重试及关闭开关行为。
v1.0.1 微软登录配置修复:
  按用户明确要求，公开发布包内置 Prism Launcher 的公开 Microsoft 应用 ID。
  ID: c36a9fb6-4f2a-41ff-90bd-ae7cc92031eb
  来源: https://github.com/PrismLauncher/PrismLauncher/blob/develop/CMakeLists.txt
  仍优先使用环境变量和 BRCL/MicrosoftClientId.txt；缺失时使用编译内置值。
  通过编译内置避免向旧更新器增加不受支持的包文件。
  内部更新编号 2026.11.13.8，对外版本 1.0.1。
  编译、无配置默认值、覆盖配置归一化、非法配置拒绝，以及微软设备码接口验证通过。
  未执行真实用户授权、Xbox/Minecraft 完整换票和游戏启动。

性能与最大化更新（2026-11-13）:
  已发布 1.0.2 / 内部编号 2026.11.13.10，随后发布 1.0.2.1 / 2026.11.13.11。
  当前最新版下载: http://t42.sjcmc.cn:25261/releases/BRCL-2026.11.13.11.zip
  包大小: 7061789 bytes
  SHA-256: 7fa71f34762c7962565d4e118c5c5e8ec4ac894ddcabe4336f6204d324c42235
  包含首次启动/切页/内存优化、每次启动独立后台检查更新、最大化与还原按钮及发布日志。
  最终包通过 26 组 246 项回归检查、原生工作区/还原尺寸检查和实际窗口最大化/还原操作。
  公网完整回下载的清单、签名、ZIP 与本地验证产物逐字节一致，RSA-SHA256、包大小与哈希通过。
  验证记录: C:\works\BRCL\release-1.0.2.1-checks\public-download\receipt.json
  已发布的旧版本包保留，不覆盖。

单EXE发行（2026-11-13，1.0.2.2 / 2026.11.13.12）:
  公开下载: http://t42.sjcmc.cn:25261/releases/BRCL-2026.11.13.12.exe
  HTTP Content-Disposition 固定文件名 BRCL.exe，支持 Range。
  EXE 大小: 10067456 bytes
  EXE SHA-256: c02233ffd3f2e41453f04be77e1de448226fbacfb212d473b7b112c26d01c9f0
  发行目录只含 BRCL.exe；托管依赖、图片/图标、原生加载组件、更新安装器及LICENCE均内嵌。
  系统.NET Framework 4.8、WebView2 Runtime与用户游戏/账号/缓存不作为发行文件打包。
  内部更新ZIP只含EXE，兼容旧更新客户端；新安装器兼容用户自行改名的EXE。
  已通过27组263项检查、纯EXE目录实际启动/交易所测试、多尺寸与Shell图标验证。
  公网EXE完整回下载、RSA签名与SHA-256验证通过。

2026-11-13 下载连接关闭修复:
  用户客户端出现 ConnectionClosed；服务器日志确认大量 RuntimeError: can't start new thread。
  原服务 TasksMax=32，启动器并发分段下载能耗尽可用线程。
  修复: DownloadServer 限制最多128个工作线程、接受队列256；systemd TasksMax256、MemoryMax384M、LimitNOFILE4096。
  连接设置120秒超时，取消下载导致的常见断开不再输出异常堆栈。
  旧客户端的下载地址和Range协议保持兼容，无需先更新客户端。
  test_concurrency.py 验证64个长Range请求保持连接时服务65tasks，新范围请求仍返回正确字节；取消后恢复1task。
  该测试期间峰值内存约171MB，未发生线程耗尽或重启。
  改动前服务代码及unit备份于 /opt/brcl-updates/backups/ 下 before-concurrency 文件。
  公网 .NET Framework HttpClient 实测:48路分段完整下载170920685字节ZIP，用时32.5秒，SHA-256与原包一致。
