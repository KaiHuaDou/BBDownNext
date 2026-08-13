# BBDown.Sample

BBDown 外部后处理协议（[PROTOCOL.md](../../PROTOCOL.md)）的示例插件与**模板**：主仓库内置，
作为新插件的起点。读取主程序落盘的请求 JSON 并打印字段，以 0 退出且不写产物（演示「无需处理」语义）。

## 目录结构

```
Plugins/BBDown.Sample/
├── BBDown.Sample.slnx              # 解决方案（主项目 + 测试）
├── BBDown.Sample/
│   ├── BBDown.Sample.csproj        # 主项目（含 AOT 发布配置示范）
│   └── Program.cs                  # 协议实现
├── BBDown.Sample.Tests/
│   ├── BBDown.Sample.Tests.csproj
│   └── ProtocolTests.cs            # 协议契约测试（字段严格对齐）
├── Directory.Build.props           # 自带构建配置，阻断继承主仓库
├── Directory.Packages.props        # 自带中央包管理
├── global.json                     # 固定 SDK 版本
├── .editorconfig                   # 代码规范（与主仓库一致）
├── .gitignore                      # bin / obj 等
└── README.md
```

## 独立性

除 `BBDown.Core`（复用协议类型与源生成器上下文 `PostProcessJsonContext`，保证字段严格对齐）外，
本模板不依赖主仓库任何构建配置：自带 `Directory.Build.props` / `Directory.Packages.props` /
`global.json`，MSBuild 向上查找时先命中本目录，主仓库配置不生效。复制为独立插件时可原样保留。

## 构建与测试

```bash
dotnet build Plugins/BBDown.Sample.slnx -c Release
dotnet test  Plugins/BBDown.Sample.slnx -c Release
dotnet publish BBDown.Sample/BBDown.Sample.csproj -c Release -r win-x64
```

产物为 `BBDown.Sample.exe`（AOT 单文件）。

## 使用

```bash
BBDown --post-process <BBDown.Sample.exe 路径> <视频链接>
```

## 行为

读取请求 JSON，打印 `Aid` / `Cid` / `Kind` / `TrackPath` / `DestPath` 后以退出码 0 结束
且不写产物——主程序据此判定「轨道无需处理」，原文件照常参与混流。

实际插件应按协议返回：

- 处理成功：把产物写入 `DestPath`（存在且非空）后以 0 退出，主程序用产物覆盖原轨参与混流；
- 无需处理：以 0 退出且不写 `DestPath`，原文件照常混流；
- 处理失败：返回非 0，主程序静默保留原文件。

解密实现不受协议约束（密钥与加密信息由插件自行获取），安全边界见
[PROTOCOL.md](../../PROTOCOL.md)「安全边界」节。

## 复制为新插件

1. 复制本目录为 `Plugins/<插件名>`，并把目录下的 `BBDown.Sample.slnx` / `BBDown.Sample.csproj`
   与 `InternalsVisibleTo` 同步改名（含目录名与 `AssemblyName` / `RootNamespace`）。
2. 在 `Program.cs` 中实现实际处理：取钥、解密，把产物写入 `DestPath`。
3. 若独立成仓库，主仓库 `.gitignore` 的 `Plugins/*` 会忽略新目录，无需再开特例。
