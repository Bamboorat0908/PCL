# BRCL 官网服务器部署

站点地址：`https://zhushu.homes/BRCL/`。本目录为用户指定服务器的静态站部署，不依赖 Sites 云托管或新的后台服务。

## 服务器边界

- SSH：`45.207.197.246:64886`，凭证仅通过会话内存和 stdin 传递；本目录不保存口令。
- 现有 HTTPS vhost：`/etc/nginx/sites-available/mcstatus`，启用路径为软链接。
- 增量路由：`/etc/nginx/snippets/brcl-site.conf`，仅在 HTTPS server 中 include。
- 发布目录：`/var/www/brcl/releases/<release-id>`；`/var/www/brcl/current` 指向当前目录。
- 备份与收据：`/var/backups/brcl-site/<release-id>`；未更改 BRS、新闻、交易所、博客及自动更新清单。

## 发布输入

准备一个 gzip tar 包，内容直接以 `index.html` 为根，包括静态资源、`release.json` 和 `download/BRCL.exe`。不加入 Git、源码凭证、私钥、数据库或配置文件。

更换下载文件后，先更新 `dist/release.json` 的版本、官网构建日期与说明，再运行 `python tools/prepare-release.py`（从 `website/brcl` 目录运行）。脚本以实际 EXE 重新生成大小与 SHA，并把对应版本和校验值写入静态 HTML；即使 JavaScript 未运行或元数据请求延迟，下载区仍与同包文件一致。随后检查 JavaScript 和页面，再打包发布。

`release.json` 至少包含：

```json
{
  "display_version": "1.0.2.5",
  "version": "2026.11.15.1",
  "build_date": "2026-11-15",
  "size": 10180096,
  "sha256": "dca2ca42afbf88ff801e6e051eb6c7104c918f412d97eafbc0f52a02c01bbdc5",
  "download_url": "/BRCL/download/BRCL.exe"
}
```

官网与自动更新服务当前均提供同一份 1.0.2.5 正式发行 EXE，文件大小与 SHA-256 一致。官网的 `release_notes` 与启动器内置更新日志首条同步，`prepare-release.py` 会同时生成静态下载信息和可见更新说明。

上传包和 `deploy.py` 到服务器临时目录，以 root 执行：

```text
python3 /tmp/brcl-deploy.py --archive /tmp/brcl-site.tar.gz --release <unique-release-id> --exe-size 10180096 --exe-sha dca2ca42afbf88ff801e6e051eb6c7104c918f412d97eafbc0f52a02c01bbdc5
```

脚本先拒绝越界、隐藏、软链接等非预期成员，验证下载文件和公开元数据；随后备份 nginx 配置、增量 include、原子切换 `current`，执行 `nginx -t` 后 reload。校验或 reload 失败会还原原配置与软链接并再次验证/reload。每次使用新 release ID，不覆盖已发布目录。

## 验证与恢复

- `/BRCL` 应为 308，保留查询参数；`/BRCL/`、主资源和元数据为 200；不存在文件为 404。
- 下载应包含附件文件名、`application/octet-stream`、`nosniff`，支持 `Range`；完整 HTTPS 下载大小和 SHA 必须与元数据一致。
- `/`、`/BRS/`、`/stock/`、`/BRS/news/`、`/BRS/news/api/news` 在部署前均为 200，部署后重新检查。
- 页面使用自身静态脚本；nginx CSP 允许本域脚本、本域和 data 图片，样式可内联，禁止对象与第三方 iframe 嵌入。
- 服务器原有全站策略会拒绝 `curl/`、`wget/` 等脚本 UA，官网部署保留此策略。`verify.py` 使用已观察到的普通 Chromium UA 验证真实浏览器路径；另已分别核对 Chromium fetch、JSON 导航和完整浏览器 EXE 下载。不得为官网放宽其他站点的安全规则。
- 手工恢复时读取备份收据的 `previous_target`，将 `current` 原子切回，并恢复备份的 vhost/snippet，再 `nginx -t` 与 reload。不要把旧配置复制到陌生 vhost。

部署保护已在服务器 Python 3.8 的独立 `/tmp` 临时目录验证：正常激活、nginx 校验失败回滚、错误 SHA 拒绝、包路径越界拒绝四项通过；这些测试不触碰线上站点。

## 首次上线结果（历史记录）

- 最终发布：`20261113T111543Z-0048584d`，前一版 `20261113T110824Z-ddc3d2d6` 保留可回退。
- 地址：`https://zhushu.homes/BRCL/`；下载：`https://zhushu.homes/BRCL/download/BRCL.exe`。
- 最终包：9,399,707 bytes，SHA-256 `0048584df8cd34d77f546114621f1987e8342537f5666ce0e3382c1b6eb866fd`。
- nginx 语法/reload、本机 TLS 7 组、Windows 公网浏览器 UA 7 组通过；均完整读取 EXE 核对 10,160,128 bytes 与 `f0ff61…eb201`。
- 原站 `/`、`/BRS/`、`/stock/`、`/BRS/news/`、`/BRS/news/api/news` 均保持 200。
- 最终 HTML 静态包含版本、SHA 和下载地址，JS 延迟或失败不影响基本下载信息。
- 已核对 2026-11-13 19:16:23 +08:00 的普通 Chromium HTTP/2 下载记录：200、传满 10,160,128 bytes；浏览器下载事件是否被宿主环境派发不影响该服务端证据。
- 服务器收据：`/var/backups/brcl-site/20261113T111543Z-0048584d/receipt.json`，同目录有本机与公网验证结果。
- 本机收据：`C:\works\BRCL\brcl-site-20261113-checks\deployment-final.json`，同目录含 `origin-verification-final.json` 与 `public-verification-final.json`。

## 1.0.2.3 同步发行（2026-11-13）

- 官网与自动更新服务均更新为 `1.0.2.3` / `2026.11.13.13`，使用相同的正式发行 EXE。
- 下载文件为 10,169,344 bytes，SHA-256 为 `14012c97b60c9b395dce0abddd674ea9fe9ecae365536b1bed3fdf8bf34cba1d`。
- 发布目录：`20261113T114259Z-v1.0.2.3-14012c97`；原目录 `20261113T111543Z-0048584d` 保留可回退。
- 下载区静态显示本次更新的 7 条说明，与官网元数据、启动器内置日志逐条一致，JavaScript 未运行时也可阅读。
- nginx 语法检查与 reload 成功，vhost 发布前后哈希一致；源站 TLS 和 Windows 公网验证各 7 组通过，均完整下载 EXE 核对大小与 SHA。
- 原有 `/`、`/BRS/`、`/stock/`、`/BRS/news/`、`/BRS/news/api/news` 均保持 200。
- 远端收据：`/var/backups/brcl-site/20261113T114259Z-v1.0.2.3-14012c97/receipt.json`。
- 本地收据：`C:\works\BRCL\release-1.0.2.3-checks\website\deployment.json`；同目录有源站、公网下载与可见日志验证结果。

## 1.0.2.4 最大化拖动修复（2026-11-13）

- 官网与自动更新服务均提供 `1.0.2.4` / `2026.11.13.14`，EXE 为 10,172,928 bytes，SHA-256 为 `90d9b204b64a8ab39dfac01101c3f85d28acd4b3917f8a586fe438982ce5db31`。
- 官网下载区的 5 条可见更新说明与启动器内置日志一致，包含最大化拖动还原、鼠标锚点、拖动阈值、尺寸保留与状态清理。
- 当前目录：`20261113T122608Z-v1.0.2.4-90d9b204`；前版 `20261113T114259Z-v1.0.2.3-14012c97` 保留可回退。
- nginx 语法与 reload 通过，vhost 哈希未改变；源站 TLS 与 Windows 公网各 7 组验证通过，均完整读取 EXE 核对大小与 SHA。
- `/`、`/BRS/`、`/stock/`、`/BRS/news/`、`/BRS/news/api/news` 均为 200。
- 远端收据：`/var/backups/brcl-site/20261113T122608Z-v1.0.2.4-90d9b204/receipt.json`。
- 本地收据：`C:\works\BRCL\release-1.0.2.4-checks\website\deployment.json`，同目录有 `origin-verification.json`、`public-verification.json` 与 `public-notes-verification.json`。

## 1.0.2.5 全面检查修复发行（2026-11-15）

官网和自动更新已同步为 1.0.2.5 / 2026.11.15.1，EXE 10,180,096 bytes，SHA-256 dca2ca42afbf88ff801e6e051eb6c7104c918f412d97eafbc0f52a02c01bbdc5。11 条可见说明与启动器日志一致。发布目录 20261115-v1.0.2.5-dca2ca42；前目录保留可回退。nginx 校验及 reload 通过，vhost 哈希不变；源站与公网各 7 组验证通过，包含完整下载校验。收据在 /var/backups/brcl-site/20261115-v1.0.2.5-dca2ca42/receipt.json，本地记录在 C:\works\BRCL\release-1.0.2.5-checks。

## 1.0.2.6 主题切换热修复（2026-11-15）

官网与自动更新已同步为 1.0.2.6 / 2026.11.15.2，修复主题切换冻结画刷报错。EXE 为 10,180,096 bytes，SHA-256 为 1f318eb95fca1fa439dba196e45fa88d01408e5aaf83bcbf119047a5e5b1ea6c。一条可见说明与内置日志一致；源站与公网各 7 组验证通过。发布目录 20261115-v1.0.2.6-1f318eb9，前版保留；nginx 校验/reload 通过且 vhost 哈希未变。证据：C:\works\BRCL\release-1.0.2.6-checks\website。
