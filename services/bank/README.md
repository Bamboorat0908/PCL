# BRCL 银行查询服务

这是新增“银行”页配套的只读服务，使用 Python 3.10+ 标准库。它部署在服务器侧，将 TRM 0.3.1 的管理员接口转换为当前玩家可访问的团队账户查询。启动器不持有 TRM 管理员 Token。

## 启动与接入

在运行服务的环境中安全注入 `TRM_API_TOKEN`，然后执行：

```sh
python bank_service.py
```

配置项：

| 环境变量 | 默认值 | 用途 |
| --- | --- | --- |
| `TRM_API_TOKEN` | 必填 | 服务端管理员 Token，不下发给启动器 |
| `TRM_API_TOKEN_FILE` | 可选 | 未设置 Token 时，直接读取服务端 properties 文件中的 `token` 项，不复制凭据到客户端 |
| `TRM_API_URL` | `http://127.0.0.1:8765/api/v1` | TRM 地址；远程 HTTP 不允许，仅回环 HTTP 或 HTTPS |
| `BANK_PORT` | `8786` | 仅监听 `127.0.0.1` |
| `BANK_DATABASE` | `data/bank.sqlite3` | 访问采样的余额快照数据库 |
| `BANK_TRUST_PROXY` | 关闭 | 仅设为 `1` 且连接来源为回环地址时，使用反向代理覆盖写入的 `X-Real-IP` 对登录限流 |

生产环境在现有 HTTPS 反向代理增加 `/bank/v1/` 路由，并原样转发完整路径。不要公开 TRM 管理接口。反向代理应限制请求体大小和请求频率，并覆盖 `X-Real-IP`，不能透传调用者提交的该请求头。此头只用于来源地址限流，不用于认证或账户关联。

启动器默认使用已部署的 `https://zhushu.homes/bank/v1`。可在程序旁的 `BRCL/BankServiceUrl.txt` 填写其他完整服务前缀；也可通过 `BRCL_BANK_SERVICE_URL` 覆盖。由于该地址会收到玩家的 Minecraft 访问令牌，覆盖值只接受官方主机 `zhushu.homes` 的 HTTPS 默认端口（路径可变），或本机回环地址（本地联调如 `http://127.0.0.1:8786/bank/v1`）；其他主机会被拒绝并在银行页提示配置需要更新。地址文件只保存地址，不保存任何 Token。修改配置后重新进入银行页。

## 当前生产部署

- 游戏服：`mc@t42.sjcmc.cn:25160`，程序在 `/home/mc/brcl-bank`，用户服务 `brcl-bank.service`，只监听回环 `8786`。从现有 TRM 配置文件读取凭据。
- 官网：沿用 `zhushu.homes` 的证书与 HTTPS vhost，新增 `/etc/nginx/snippets/brcl-bank.conf` 和独立限流区域；配置变更前备份在 `/var/backups/brcl-bank`。
- 两台主机之间由 `brcl-bank-tunnel.service` 建立加密 SSH 反向连接：官网回环 `18786` → 游戏服回环 `8786`。专用私钥只保存在游戏服，官网仅安装限制端口、禁止执行命令的专用公钥。主机公钥固定在 `website_known_hosts`。
- 两个用户服务均已启用随用户管理器启动，`mc` 已开启 linger。SSH 连接中断会自动重连。隧道使用独立的显式 SSH 配置，避免游戏服全局 SSH 配置的权限异常影响连接。

`brcl-bank.service`、`brcl-bank-tunnel.service` 和 `brcl-bank-nginx.conf` 是本次部署的配置模板。`deploy_website.py` 只增量修改银行路由，先备份并检查 Nginx，失败会恢复原路由配置。检查服务可执行 `systemctl --user status brcl-bank brcl-bank-tunnel`；公网未认证查询应返回 JSON `401`，不应返回账户数据。

## 玩家身份和权限

启动器复用已有微软→Xbox→Minecraft 登录结果，向银行服务提交当前 **Minecraft Access Token**。服务端通过固定的官方 `https://api.minecraftservices.com/minecraft/profile` 获取玩家 UUID，签发 30 分钟的随机银行会话；不接收调用方自报的 UUID，也不接收微软刷新令牌。

必须确认游戏服务器中银行的授权 UUID 与正版 Minecraft UUID 一致。离线 UUID、第三方认证 UUID 需要可信的游戏内绑定流程，本版不会按名字自动推断或关联。

账户列表通过 TRM `authorizedPlayer` 筛选；服务端仍逐项校验 `grants`。历史查询每次读取当前账户并再次校验直接授权。个人账户、子账户和 FTB Teams 的间接关系均不参与关联。启动器还会校验返回的 UUID、金额格式及 `balance = visibleBalance + reserveBalance`。

银行会话仅在内存中；切换账号、退出微软登录、离开银行页面会取消请求并清空页面数据和本地银行会话。服务重启使已签发会话失效，客户端可通过刷新重新建立会话。页面金额隐藏也会隐藏趋势和分析金额。

## 启动器使用的接口

所有响应为 UTF-8 JSON，`Cache-Control: no-store`。不允许跨站重定向。

- `POST /bank/v1/session`：`Authorization: Bearer <Minecraft Access Token>`，请求体 `{}`；返回 `{token, expiresAt, player: {uuid, name}}`。服务端只存银行会话 Token 的 SHA-256 摘要。
- `GET /bank/v1/accounts`：银行会话 Bearer；返回 `{capturedAt, currency: "sdmcoin", unit: "copper", accounts: [...]}`。账户字段沿用 TRM 账户结构；仅返回当前玩家获直接授权的账户，服务端完成分页。
- `GET /bank/v1/accounts/{id}/history?days=7`：银行会话 Bearer；`days` 只允许 `7`、`30`、`90`。返回 `{accountId, currency, unit, source: "observed_snapshots", points: [{capturedAt, balance}]}`，按时间递增，最多约 480 点。

金额始终用十进制字符串传输；Python `int` 和启动器 `BigInteger` 用于金额计算。图形仅在计算归一化坐标时转为浮点数。服务端的 TRM 请求全局串行节流为每秒最多 10 次。

## 流水与分析边界

TRM 0.3.1 不提供完整流水，也不能枚举 HTTP 调整记录。当前流水标签明确显示未开放；没有伪造的交易、收入或支出数字。完整流水需经济模组在实际资金变动入口记录事件，并新增分页查询及覆盖范围说明。

本服务仅在已授权的账户查询或历史查询时采集余额快照，每分钟保留最后一次观测，保存最近 90 天；没有定时采样和启动前历史回填。分析页面将其称为访问采样，期间变化不等于收入或支出。快照不用于回写原生余额，不构成第二套资金账本。

本服务不代理任何余额调整、转账或授权修改接口。正常运行需要可用的 TRM、匹配的正版 UUID和 HTTPS 路由；仅编译通过不代表已连接生产经济系统。

## 本地验证

```sh
python -m unittest -v test_bank_service.py
```

测试使用本地伪造上游与身份提供器，检查协议、授权撤回、超大整数、写接口拒绝、会话失效和余额采样；不连接正式服务器，不执行资金操作。启动器侧的解析、隐私、布局及清理检查位于 `scripts/test-bank-page.ps1`。

2026-11-18 部署验证：两个用户服务均为 enabled/active，公网未登录查询返回 JSON 401；启动器已使用当前正版 Minecraft 登录完成会话和账户查询，当前返回的直接授权团队账户为 0，页面显示对应的空状态。官网首页、BRS、BRCL、交易所和新闻接口仍返回 200。Python 的 6 组服务测试与启动器原生解析、隐私、清理检查通过，并检查了 14 张深浅色及窄宽布局截图。界面截图中的非零余额和账户名称均为明确标注的测试数据。
