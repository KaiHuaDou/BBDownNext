tokei:
    tokei -s lines --files -c 105

# Phase 0 依赖守护：禁止破环边，保持依赖单向成树（见 docs/refactor-plan.md）
check-deps:
    @echo "checking dependency direction (tree, no cycles)..."
    @if grep -rn "using BBDown.Serve;" BBDown/Pipeline BBDown/Media BBDown/Download BBDown/Mux 2>/dev/null; then echo "FAIL: 下载链路禁止依赖 Serve（用 PipelineSink 回调解耦）"; exit 1; fi
    @if grep -rn "DownloadTask" BBDown/Pipeline BBDown/Media BBDown/Download BBDown/Mux 2>/dev/null; then echo "FAIL: 下载链路禁止持有 serve 的 DownloadTask（用 PipelineSink 回调解耦）"; exit 1; fi
    @if grep -rn "using BBDown.AppEnv;" BBDown --include=*.cs | grep -v "Program.cs" 2>/dev/null; then echo "FAIL: 非 Program 禁止依赖 AppEnv"; exit 1; fi
    @if grep -rn "using BBDown;" BBDown.Core 2>/dev/null; then echo "FAIL: Core 禁止依赖 BBDown"; exit 1; fi
    @if grep -rn "static string \(ffmpeg\|mp4box\|aria2c\)" BBDown 2>/dev/null; then echo "FAIL: 外部工具路径禁止用进程级可变静态字段（用 ToolPaths 快照透传）"; exit 1; fi
    @echo "OK: 依赖方向合规"
