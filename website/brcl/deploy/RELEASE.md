# BRCL 客户端发布

源码版本由 `ModBrclUpdate.vb`、`AssemblyInfo.vb` 和 `Changelog.json` 共同定义。展示版本使用三段式。新增功能升中间一位，小修复升末位，重大改版或破坏性兼容变更升首位。自动更新按独立的四段修订号递增。首次发布使用 `brcl-v<展示版本>` 标签。同一展示版重建时，使用 `brcl-v<展示版本>-build-<修订号>` 标签记录对应构建。

1. 在干净检出上执行 `scripts/verify-release.ps1`，保存产物和回归报告。正式发布可加 `-PackArchive` 校验本次服务器整合包。
2. 执行 `scripts/publish-update.ps1 -PrepareOnly -Version <修订> -DisplayVersion <版本> -VerifiedReleaseDirectory <验证目录>`。准备环节使用外部私钥，校验并签名验证目录里的 ZIP 与 EXE。运行 `scripts/test-release-package.ps1` 校验包和签名。
3. 使用 `-PublishPrepared` 上载已验签的产物。旧客户端保留原有 HTTP 地址和相对包路径，仍校验 RSA 签名和 SHA-256。核对兼容入口后，执行 `scripts/publish-transfer.ps1` 同步 HTTPS 静态主源。
4. 对 HTTPS 主源、镜像和兼容入口分别运行 `scripts/verify-public-release.ps1 -BaseUrl <入口>`。对整合包验证完整哈希、Range 请求和跨源拼接。验证报告与 `release-record.json` 归档。
5. 在单独暂存目录将验证过的 EXE 放入 `website/brcl/dist/download/BRCL.exe`，运行 `website/brcl/tools/prepare-release.py --release-record <记录>`。内置更新日志是官网可见列表、文本下载和 JSON 版本信息的来源。按现有 `deploy.py` 流程激活官网，随后用 `verify.py` 验证公网 TLS、响应头、文件哈希和网页资源。

## 更新日志

在内置的 Changelog.json 首位新增一条正式发布记录。title 只填三段式展示版本号，date 使用 YYYY-MM-DD，按版本号从新到旧排列。每条发布记录用 sections 按功能分组，每组用 items 记录改动。summary 概括玩家可感知的变化，detail 说明影响、使用入口和必要的限制。已随版本发布的阶段性改动补入对应版本。

官网的本次更新、可展开的往期版本和最新版本文本下载由 prepare-release.py 从内置日志生成。release.json 中的 release_notes 继续提供最新版本的纯文本条目。

1.3.0 对应 2026-11-23 的发布内容。往期条目的原始发行编号可以通过 changelog 中的 original_version 查询。历史编号按功能阶段和发布时间排列，包括 1.0.0 至 1.0.9、1.1.0 至 1.1.1，以及 1.2.0 至 1.2.1。标题有变化的条目保留原编号，供查询此前的安装包和发布凭据。

## HTTPS 地址

- 主源使用 `/BRCL/transfer/` 下的 Nginx 静态文件。带版本的 ZIP 和 EXE 为不可变文件，脚本校对已验证的 `latest.json` 和 `latest.sig` 后用同一软链接激活清单。
- 镜像使用 `/BRCL/transfer-mirror/`。Nginx 经 `brcl-transfer-tunnel.service` 的回环端口访问原更新服务器。SSH 公钥仅允许该反向端口，SSH 客户端固定服务端公钥。
- 两条 HTTPS 路径有不同的文件后端，共用官网 TLS 和 Nginx 入口。两条路径均保留 Range 和原有签名。`transfer.nginx.conf` 安装在官网 TLS server 的 snippet 中，对外只发布清单和允许的下载路径。
