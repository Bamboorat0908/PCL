#!/bin/bash
set -euo pipefail
cd "$(dirname "$0")"
app=/opt/brcl-inkadmin
conf=/etc/brcl-inkadmin
install -d -m 0755 "$app"
id brcl-inkadmin >/dev/null 2>&1 || useradd --system --no-create-home --shell /usr/sbin/nologin brcl-inkadmin
install -d -m 0710 -o root -g brcl-inkadmin "$conf"
test -s "$conf/upload.pem"
chown root:brcl-inkadmin "$conf/upload.pem"
chmod 0640 "$conf/upload.pem"
test -s "$conf/login-key.hash" || { echo "Missing InkAdmin login verifier" >&2; exit 1; }
chown root:brcl-inkadmin "$conf/login-key.hash"
chmod 0640 "$conf/login-key.hash"
for f in app.py auth.py codec.py admin.html app.css app.js login.html login.css login.js; do install -m 0644 "$f" "$app/$f"; done
cat >/etc/systemd/system/brcl-inkadmin.service <<'UNIT'
[Unit]
Description=BRCL experience and crash diagnostics
After=network.target
[Service]
User=brcl-inkadmin
Group=www-data
SupplementaryGroups=brcl-inkadmin
UMask=0077
StateDirectory=brcl-inkadmin
StateDirectoryMode=0700
RuntimeDirectory=brcl-inkadmin
RuntimeDirectoryMode=0750
Environment=BRCL_REPORT_KEY=/etc/brcl-inkadmin/upload.pem
ExecStart=/usr/bin/python3 /opt/brcl-inkadmin/app.py
Restart=on-failure
NoNewPrivileges=true
ProtectSystem=strict
ProtectHome=true
PrivateTmp=true
MemoryMax=320M
TasksMax=48
[Install]
WantedBy=multi-user.target
UNIT
cat >/etc/cron.daily/brcl-inkadmin-retention <<'CRON'
#!/bin/sh
exec runuser -u brcl-inkadmin -- env BRCL_REPORT_KEY=/etc/brcl-inkadmin/upload.pem /usr/bin/python3 /opt/brcl-inkadmin/app.py --purge
CRON
chmod 0755 /etc/cron.daily/brcl-inkadmin-retention
cat >/etc/nginx/conf.d/brcl-inkadmin-limit.conf <<'NG'
limit_req_zone $binary_remote_addr zone=ink_clients:2m rate=4r/s;
limit_req_zone $binary_remote_addr zone=ink_admin:1m rate=5r/s;
limit_req_zone $binary_remote_addr zone=ink_login:1m rate=10r/m;
upstream ink_diagnostics { server unix:/run/brcl-inkadmin/api.sock; }
NG
cat >/etc/nginx/snippets/brcl-inkadmin-auth.conf <<'NG'
auth_basic off;
limit_req_status 429;
proxy_set_header X-Ink-Actor "";
proxy_set_header Authorization "";
proxy_pass http://ink_diagnostics;
add_header Cache-Control no-store always;
add_header X-Frame-Options DENY always;
add_header Content-Security-Policy "default-src 'self'; frame-ancestors 'none'; object-src 'none'; base-uri 'none'; form-action 'self'" always;
NG
cat >/etc/nginx/snippets/brcl-inkadmin.conf <<'NG'
location = /BRCL/InkAdmin { limit_req zone=ink_admin burst=30 nodelay; include snippets/brcl-inkadmin-auth.conf; }
location = /BRCL/InkAdmin/api/reports {
    access_log off;
    limit_req zone=ink_admin burst=30 nodelay;
    include snippets/brcl-inkadmin-auth.conf;
}
location = /BRCL/InkAdmin/api/login {
    client_max_body_size 4k;
    limit_req zone=ink_login burst=8 nodelay;
    access_log off;
    include snippets/brcl-inkadmin-auth.conf;
}
location ^~ /BRCL/InkAdmin/ { limit_req zone=ink_admin burst=30 nodelay; include snippets/brcl-inkadmin-auth.conf; }
location = /BRCL/InkAdmin/collect/crash {
    client_max_body_size 3m;
    limit_req zone=ink_clients burst=20 nodelay;
    limit_req_status 429;
    proxy_set_header Authorization "";
    access_log off;
    proxy_set_header X-Ink-Actor "";
    proxy_pass http://ink_diagnostics;
}
location = /BRCL/InkAdmin/collect/activity {
    client_max_body_size 4k;
    limit_req zone=ink_clients burst=20 nodelay;
    limit_req_status 429;
    proxy_set_header Authorization "";
    access_log off;
    proxy_set_header X-Ink-Actor "";
    proxy_pass http://ink_diagnostics;
}
NG
site=$(readlink -f /etc/nginx/sites-enabled/mcstatus)
backup="/etc/nginx/backups/inkadmin-$(date +%Y%m%d-%H%M%S)"
mkdir -p /etc/nginx/backups
cp -p "$site" "$backup"
if ! grep -qF 'include snippets/brcl-inkadmin.conf;' "$site"; then
  test "$(grep -cF 'include snippets/brcl-site.conf;' "$site")" -eq 1
  sed -i '/include snippets\/brcl-site.conf;/a\    include snippets/brcl-inkadmin.conf;' "$site"
fi
if ! nginx -t; then cp -p "$backup" "$site"; exit 1; fi
systemctl daemon-reload
systemctl enable --now brcl-inkadmin
systemctl restart brcl-inkadmin
for i in $(seq 1 30); do test -S /run/brcl-inkadmin/api.sock && break; sleep 0.2; done
test -S /run/brcl-inkadmin/api.sock
systemctl reload nginx
echo 'InkAdmin service and route installed'
