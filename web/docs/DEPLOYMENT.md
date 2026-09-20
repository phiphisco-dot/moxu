# Ubuntu 部署与维护

适用于一台新建的 Ubuntu 24.04 服务器，使用 Node.js 24、Nginx、systemd 和 Certbot。以下服务器命令按 root 用户执行；已有业务的服务器请先检查现有配置，勿覆盖其他站点。

## 1. 准备环境与代码

安装 Node.js 24 以及 Nginx、Git、Certbot 和 Nginx 插件。Node.js 可参考 [NodeSource 安装说明](https://github.com/nodesource/distributions)，证书参考 [Certbot 文档](https://certbot.eff.org/)。

```bash
apt-get update
apt-get install -y ca-certificates curl nginx git certbot python3-certbot-nginx
curl -fsSL https://deb.nodesource.com/setup_24.x -o /tmp/zhiwei-node-setup.sh
bash /tmp/zhiwei-node-setup.sh
apt-get install -y nodejs
node -v
nginx -v
```

每一步成功后再继续。下面两个目录应当尚不存在；首次部署时执行：

```bash
git clone https://github.com/phiphisco-dot/moxu.git /opt/moxu-source
mkdir /opt/zhiwei
cp -a /opt/moxu-source/web/. /opt/zhiwei/
cd /opt/zhiwei
node --test test.cjs
```

程序目录为 `/opt/zhiwei`，数据目录为 `/var/lib/zhiwei`。不要把项目整体作为 Nginx 静态根目录，也不要上传本机 `private/`。

## 2. 配置独立用户与后台服务

```bash
id zhiwei >/dev/null 2>&1 || useradd --system --user-group --home-dir /var/lib/zhiwei --shell /usr/sbin/nologin zhiwei
install -d -m 700 -o zhiwei -g zhiwei /var/lib/zhiwei
runuser -u zhiwei -- env DATA_DIR=/var/lib/zhiwei /usr/bin/node /opt/zhiwei/setup.cjs
```

保存终端显示的随机网站密码，不要将其放入仓库、日志截图或 README。初始化不会覆盖已有 `auth.json`。后续部署保留原数据目录，不重复初始化。

复制 `deploy/zhiwei.service` 到 `/etc/systemd/system/zhiwei.service`，将其中的 `https://example.com` 改成实际域名，例如 `https://zhiweimoxu.cn`。检查 `ExecStart` 与 Node.js 的安装路径一致。

```bash
cp /opt/zhiwei/deploy/zhiwei.service /etc/systemd/system/zhiwei.service
nano /etc/systemd/system/zhiwei.service
systemctl daemon-reload
systemctl enable --now zhiwei
systemctl is-active zhiwei
```

以下 `example.com` 替换为你的实际域名。服务启动后应返回 `{"ok":true}`：

```bash
curl -H 'Host: example.com' http://127.0.0.1:18765/health
```

systemd 模板设置了非 root 用户、只读系统目录限制、独立临时目录和失败重启。业务数据目录是允许写入的路径。

## 3. DNS 与端口

- 给实际域名添加 A 记录，主机记录 `@`，记录值为服务器公网 IPv4。
- 云防火墙允许 TCP 80 和 443 的公网访问，SSH 按自己的管理需要开放；无需开放 18765。
- 如果启用了系统 UFW，也需核对其规则；不要通过清空规则或关闭防火墙解决问题。
- 若存在 AAAA 记录，确保 IPv6 也指向本服务器并能访问；本指南只配置 IPv4。
- 本示例只配置根域名，`www` 需要另外配置解析、证书和访问策略。

## 4. Nginx 与 HTTPS

先备份已有 Nginx 配置。尚未获得证书时，不要直接启用 `deploy/nginx.conf`，该模板引用的证书文件必须先存在。

在 `/etc/nginx/sites-available/zhiwei` 创建下面的 HTTP 初始配置，并将 `example.com` 替换为实际域名：

```nginx
server {
    listen 80;
    server_name example.com;
    client_max_body_size 2m;
    location / {
        proxy_pass http://127.0.0.1:18765;
        proxy_set_header Host $host;
        proxy_set_header X-Forwarded-Proto $scheme;
        proxy_http_version 1.1;
    }
}
```

```bash
ln -s /etc/nginx/sites-available/zhiwei /etc/nginx/sites-enabled/zhiwei
nginx -t
systemctl reload nginx
certbot --nginx -d example.com --redirect
```

符号链接若已存在，先确认它指向正确配置，无需重复创建。Nginx 检查失败时不要重载，先处理错误。证书命令中的域名必须替换；按交互提示填写邮箱、阅读并确认服务条款。Certbot 会配置 HTTPS 与 HTTP 跳转。完成 HTTPS 后再登录网站，避免使用明文 HTTP 传输密码。

检查自动续期任务并测试续期流程：

```bash
systemctl list-timers --all | grep certbot
certbot renew --dry-run
```

使用本指南 apt 安装方式且未启用定时器时，可以运行 `systemctl enable --now certbot.timer`。实际续期成功后，Nginx 插件会重载相应配置。

## 5. 验收

将命令中的域名替换后运行：

```bash
curl -I http://example.com
curl -sS -o /dev/null -w '%{http_code}\n' https://example.com/login
curl https://example.com/health
```

依次应看到 HTTP 跳转到 HTTPS、登录页状态码 200、健康检查返回 `{"ok":true}`。

浏览器检查 HTTP 跳转 HTTPS、登录、新增安排、保存日记、刷新保留、退出登录。手机使用移动网络再次检查，确认不依赖本地电脑。同一共享账号会看到相同数据。重启应用后需重新登录；网站数据应保留。

## 6. 日志、备份与更新

```bash
systemctl status zhiwei --no-pager
journalctl -u zhiwei -n 50 --no-pager
nginx -t
```

每次保存前的备份在 `/var/lib/zhiwei/backups`，保留 30 份；同磁盘备份不能防止磁盘丢失。异地备份应包含业务数据，并妥善保护密码哈希文件。

恢复：停止 `zhiwei` 服务，额外备份当前 `data.json`，将选定的备份复制为 `data.json`，确认归属 `zhiwei:zhiwei` 后启动服务并检查。不要在运行中直接替换文件。

忘记密码：停止服务，将 `auth.json` 移到安全位置，再以 `zhiwei` 用户运行初始化命令生成新密码，最后重启服务；不要删除 `data.json`。

更新代码需要手动发布：保存当前代码版本和业务备份，在独立目录获取新代码并运行测试，再更新 `/opt/zhiwei` 中的程序文件并重启服务。不要覆盖数据目录。GitHub 提交不会自动更新云服务器。

常见定位：502 先看 Node.js 服务与端口；403 检查 `APP_ORIGIN` 和代理的 Host；401 重新登录；409 表示另一窗口已保存，按提示刷新并核对再操作；连接超时检查解析、云防火墙和系统防火墙。
