# BRS 新闻与公告

该服务仅依赖 Python 3.8 及以上版本的标准库。公开新闻、管理员后台和 BRCL 共用同一 SQLite 数据库，不预置新闻。

- 新闻：`https://zhushu.homes/BRS/news/`
- 管理：`https://zhushu.homes/BRS/news/admin`
- 公开 API：`https://zhushu.homes/BRS/news/api/news`

管理员可新建、编辑、删除、置顶新闻，在草稿与已发布之间切换。草稿不会出现在公开列表或详情接口中。标题最多 160 字、摘要 300 字、正文 50000 字；摘要留空时从正文生成。正文按纯文本展示，保留段落。后台修改密码会退出全部会话，新密码重启后仍有效。

## 部署到已有 BRS 服务器

线上应用绑定 `127.0.0.1:8094`，由现有 HTTPS nginx 转发。代码放在 `/opt/brs-news`，数据库为 `/var/lib/brs-news/news.sqlite3`，更改后的密码哈希放在同一私有状态目录，环境文件为 `/etc/brs-news.env`。systemd 服务名为 `brs-news`。这些目录与 BRS 原站分开，代理保持 `/BRS/news` 完整路径。

1. 创建系统用户 `brs-news`，创建 `/opt/brs-news` 与 `/var/lib/brs-news`，应用代码由 root 所有，状态目录由 `brs-news` 所有、权限 `0700`。
2. 复制 `server.py` 和 `static/` 到 `/opt/brs-news`；不要把数据库或环境文件放进静态目录。
3. 运行 `python3 /opt/brs-news/server.py --hash-password`，交互输入至少 12 位的初始管理员密码。命令只输出 PBKDF2 哈希，不能使用 SSH 密码。
4. 参考 `environment.example` 创建 `/etc/brs-news.env`，填入生成的 `BRS_NEWS_PASSWORD_HASH`，文件由 root 所有、权限 `0600`。当前部署使用 `BRS_NEWS_PORT=8094`。
5. 安装 `brs-news.service` 到 `/etc/systemd/system/`，执行 `systemctl daemon-reload`、`systemctl enable --now brs-news`。
6. 将 `nginx.conf.example` 的两个 location 添加到现有 `zhushu.homes` HTTPS server 块。保留 BRS 原有代理；新闻的 `^~` location 应关闭代理缓存，并用 `X-Real-IP $remote_addr` 覆盖客户端传入值。规范化跳转使用 `return 308 /BRS/news/$is_args$args;` 保留查询参数，确保 `/BRS/news?id=123` 的详情直链仍能打开正确文章。
7. 执行 `nginx -t` 后 reload；检查公开页面、静态文件、未登录管理接口和原 BRS 状态接口。

环境配置：

| 变量 | 默认值 / 含义 |
| --- | --- |
| `BRS_NEWS_HOST` | `127.0.0.1`，仅本机监听 |
| `BRS_NEWS_PORT` | 当前部署 `8094`；未提供环境变量时程序默认 `18764` |
| `BRS_NEWS_DB` | `/var/lib/brs-news/news.sqlite3` |
| `BRS_NEWS_ADMIN_USER` | `admin` |
| `BRS_NEWS_PASSWORD_HASH` | 必填的初始 PBKDF2 哈希 |
| `BRS_NEWS_BASE_PATH` | `/BRS/news`，与网页资源路径一致 |
| `BRS_NEWS_PUBLIC_ORIGIN` | `https://zhushu.homes`，用于来源校验 |
| `BRS_NEWS_SECURE_COOKIE` | `1`；仅独立的本地 HTTP 测试可设为 `0` |
| `BRS_NEWS_TRUST_PROXY` | `1`；仅当连接来自 loopback 时读取 nginx 的 `X-Real-IP` |

后台更改密码后，私有状态目录中的 `admin-password.hash` 优先于初始环境哈希。恢复管理员账号时应停服、重新生成初始哈希、移走该状态文件并清空 `sessions` 表，随后启动；避免把旧环境哈希误当作当前密码。日常备份应同时包含数据库与该哈希文件，环境文件另行妥善保存。

## API

`GET /BRS/news/api/news?category=announcement&limit=30&offset=0`

`category` 可省略，也可选 `announcement` 或 `update`。`limit` 默认为 100，范围 1–100；`offset` 为 0–1000000 的整数。按置顶、发布时间、ID 倒序排列。

```json
{
  "items": [],
  "total": 0,
  "limit": 30,
  "offset": 0,
  "has_more": false,
  "next_offset": null
}
```

每条新闻包括 `id,title,category,summary,content,published_at,updated_at,pinned`；时间使用 UTC ISO8601 字符串，例如 `2026-11-13T08:00:00Z`。详情 `GET /api/news/{id}` 返回 `{"item": {...}}`，草稿和已删除内容返回 404。管理接口均位于 `/api/admin`，需要会话 Cookie；写操作还需 `X-CSRF-Token` 和 JSON 请求体。错误响应为 `{"error":"说明"}`。

管理员接口：

- `POST /login`：`{username,password}`；返回会话与 CSRF token。
- `GET /session`：返回当前用户名与 CSRF token。
- `POST /logout`：`{}`；退出当前会话。
- `GET /news`：同样支持分页，包含草稿及管理字段 `status,created_at`。
- `POST /news`、`PUT /news/{id}`：`{title,category,summary,content,status,pinned}`；`status` 为 `draft` 或 `published`。
- `DELETE /news/{id}`：请求体 `{}`。
- `POST /password`：`{current_password,new_password}`；成功后全部会话失效。

会话有效期 12 小时，Cookie 带 HttpOnly、Secure 和 SameSite=Strict。登录按来源 IP 限制为 15 分钟最多 5 次失败尝试，PBKDF2-SHA256 使用随机盐与 600000 次迭代。密码、Cookie、请求正文不会写入应用日志。SQL 全部参数化；页面使用 textContent 展示正文，并通过 CSP 限制脚本和样式来源。

## 验证与维护

在服务代码目录运行 `python3 -m unittest -v test_server.py`。测试使用临时数据库与随机 loopback 端口，不触碰线上数据；覆盖登录、会话过期、CSRF、跨站来源、发布状态、分页、分类、置顶、输入限制、SQL/XSS 字面值、密码更改与持久化。

应用日志使用 `journalctl -u brs-news` 查看。备份 SQLite 可使用标准库 `sqlite3.Connection.backup()`，避免仅复制正在写入的 WAL 数据库主文件。升级时替换代码和静态资源后重启 `brs-news`，无需重建数据库。
