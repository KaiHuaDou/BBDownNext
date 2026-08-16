# 贡献指南

欢迎提交 Issue 与 Pull Request。

## 报告问题

- 缺陷请用 Bug Report 模板，附版本、命令与日志（可加 `--debug`）。
- 功能建议请用 Feature Request 模板。

## 环境

- .NET SDK 版本以仓库 `global.json` 为准（当前 9.0.317）。
- 构建：`dotnet build -c Release`。
- 测试：`dotnet test`。

## 代码规范

- 遵守根目录 `.editorconfig` 与 `AGENTS.md` 中的约定。
- 单文件不超过 384 行、单方法不超过 128 行（测试除外），可用 `just tokei` 检查。
- 依赖方向单向无环，可用 `just check-deps` 检查。
- 注释用中文，只解释代码无法表达的契约。

## 提交信息

格式：

```text
<type>(<domain>): <简短描述>

<详细说明>
```

- `type`：feat / fix / refactor / chore / docs / test / style 等。
- `domain`：修改的模块，如 download / cli / mux / parser / serve / gui。
- 正文用中文；不要附加 Co-authored-by 等元数据。

## 提交前

- 有用户可见变化时更新 `CHANGELOG.md` 与 `README.md`。
- 确保 `dotnet test` 通过。
