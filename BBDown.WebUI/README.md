# BBDown.WebUI

BBDown 的网页前端，直连 `BBDown serve`（`BBDown serve` 启动的服务端），用于提交下载任务、查看实时进度与日志、应答服务端选项交互。

## 前置条件

需先以 `BBDown serve` 启动服务端。默认监听 `127.0.0.1:23333`（回环免令牌、回环 Origin 默认可跨域）。

也可以不单独部署前端：以 `BBDown serve --webui` 启动时，服务端会在同一端口托管内嵌的前端（即本仓库 `dist` 构建产物），直接访问监听地址即可使用，无需配置跨域或令牌。构建 BBDown 时若未先构建 BBDown.WebUI，`--webui` 仅告警、不托管前端。

## 开发命令

```sh
pnpm install
pnpm dev          # 启动开发服务器
pnpm build        # 产物输出 dist/
pnpm type-check   # vue-tsc 类型检查
pnpm test:unit    # vitest 单元测试
pnpm lint         # oxlint
pnpm lint:fix     # oxlint 自动修复
```

## 与服务端的关系

- WebSocket（`/hubs/tasks`）是任务状态的唯一推送通道：连接建立后先收到一次快照，服务端在任务增删改时广播全量任务列表帧（`taskList`），前端据此免轮询刷新列表与进度。
- 事件流（日志、进度样本、选项交互）始终启用，服务端没有关闭开关；交互选项在无订阅者时自动回落非交互。
- 连接断开时前端每 60 秒探测一次 `/healthz`（匿名放行）以感知服务端存活，任务列表不依赖轮询。
- 跨机器访问需服务端以 `--cors-origin` 允许本页来源；若服务端启用 `--serve-token`，请在本页「设置」中填入对应 `X-BBDown-Token`。
- 凭据由本页「设置」填写，随每次任务请求提交给服务端，不在浏览器侧持久化；服务端与 CLI / GUI 共用同一份本机凭据。

## 安全

- serve 地址与令牌持久化于浏览器 `localStorage`（明文）；登录凭据不持久化，随请求提交。
- 令牌鉴权在服务端按 IP 限流，避免被批量触发。
- WebSocket 鉴权令牌经 `?token=` 查询参数传递（浏览器无法自定义握手头），仅建议回环或 TLS 场景使用。
- 非回环 / 公网部署请置于反向代理之后并启用 TLS，避免凭据与令牌暴露。
