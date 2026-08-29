# BBDown.WebUI

BBDown 的网页前端，直连 `BBDown serve`（`BBDown serve` 启动的服务端），用于提交下载任务、查看实时进度与日志、应答服务端选项交互。

## 前置条件

需先以 `BBDown serve` 启动服务端。默认监听 `127.0.0.1:23333`（回环免令牌、回环 Origin 默认可跨域）。

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

- REST 轮询（每 2s 拉 `/api/v1/tasks`）提供任务列表与进度。
- WebSocket（`/hubs/tasks`）提供实时日志、进度样本与选项交互，服务端默认开启；关闭时日志与交互不可用，任务状态仍由轮询提供。
- 跨机器访问需服务端以 `--cors-origin` 允许本页来源；若服务端启用 `--serve-token`，请在本页「设置」中填入对应 `X-BBDown-Token`。

## 安全

- 登录凭据（Cookie / access_token）与 serve 地址、令牌持久化于浏览器 `localStorage`（明文）。
- WebSocket 鉴权令牌经 `?token=` 查询参数传递（浏览器无法自定义握手头），仅建议回环或 TLS 场景使用。
- 非回环 / 公网部署请置于反向代理之后并启用 TLS，避免凭据与令牌暴露。
