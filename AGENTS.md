# AGENTS

下述“我”指用户。

## 概览

BBDown 是 B 站视频下载器，C# / .NET（AOT 单文件发布）。命令入口在 `BBDown/Program.cs`，下载编排在 `BBDown.Core`，测试在 `BBDown.Tests` / `BBDown.Core.Tests`。

bilibili API 相关文档在 `./bilibili-API-collect`文件夹下

另有新版文档：`./bilibili-API-collect/0-BACNext-Main-2MB.md` 和 `./bilibili-API-collect/0-BACNext-Passport-57KB.md`

## 强制要求

### 设计

- **去 OOP 过度包装**：不要多余的包装层 / 接口 / 枚举
    - 一个 Interface 如果没有 3+ 实现就不要设计出来
    - 不要为了方便测试去做 Dependency Injection。
    - 能合并的重复逻辑不要轻易制造基类
    - 严禁任何 pass-through 转发类、转发函数
- 除非即特别情况，**禁止**任何嵌套函数/嵌套类
- 偏好纯函数（至少是静态函数）
- 偏好不可变的 record，但是如果被迫大量使用 with 产生内存压力则改回 class
- 函数/类之间的依赖最好成树状，至少也应当是有向无环图
- 激进重构，避免任何屎山，当前在 alpha 阶段没有任何兼容性要求
- 避免 HACK，即特判逻辑（绕过逻辑）

整理后的 `### 编码与文档` 节，已合并原条目并增强可操作性：

### 编码与文档

#### 文件规模

- 单个 `.cs` 文件不得超过 384 行（测试除外）。如因清理历史遗留问题暂时超过，必须在本次提交信息中说明原因，并在后续改动中尽快拆分。
    - 使用 `just tokei` 来分析行数
    - 当前超行文件
        - .\BBDown\Auth\Login.cs
        - .\BBDown.Core\Opus\OpusFetcher.cs
        - .\BBDown\Download\DownloadUtil.cs
        - .\BBDown.Core\Util\HTTPUtil.cs

#### 注释

- 所有注释均使用中文。
- 注释只描述**当前**情况，禁止写入任何变更记录
- 代码必须自描述（Code describe itself）。**不写复述代码行为的注释。**
  注释仅用于解释代码无法表达的契约，例如：
    - 非显而易见的设计原因
    - 外部库的隐藏限制
    - 所有权转移、取消语义、线程安全等特殊约定

#### 文档与提交

- **禁止**将你自行编写的任何计划、设计草稿、临时文档提交或暂存到仓库。
- 所有对外文档（`README.md`、命令行帮助、提示语）**只陈述事实**，不得添加任何多余的形容词或修饰性语言。
- 全文（注释、文档、提交信息）**禁用“收敛”一词**。
- 当我直接要求你提交 commit 时，将暂存区的所有内容全部提交，不要将非暂存区内容提交到暂存区
- 更新文档时，严格以实际代码为准，参考最近的 git commit 记录（一定看完整提交信息）
- 提交信息使用中文，格式严格为：

```commit-msg
<type>(<domain>): <简短描述>

<详细说明>
```

- `type` 可选：`feat` / `refactor` / `fix` / `chore` / `docs` / `test` / `style`等
- `domain` 需指明修改的模块（如 `download`、`cli`、`mux`、`parser`等）
- 提交信息中**禁止**出现 `Co-authored-by` 等附加元数据

#### 书写格式

- 在注释、文档、帮助文本中，中文与英文/数字之间必须保留一个半角空格。
    - 正确：`使用 FFmpeg 合并音视频`
    - 错误：`使用FFmpeg合并音视频`
- 中文内容一律使用中文全角标点（，。！？）；代码块、标识符和内联命令不受此限制。

### 语法

- C# 13 最新语法：集合表达式（`[]`、模式匹配、LINQ 等等
- **遵守我的`.editorconfig`**
- 正则表达式不要使用不适用于源生成的语法
- 所有语法必须兼容 AOT
- 偏好`TryGetValue`，更偏好`GetValueOrDefault`
- 偏好 `var`
- 偏好 `is null`/`is not null`
- 偏好 `await using`
- 偏好 `lock(<System.Threading.Lock gate>)`
- 空括号中间要加空格，即`( )`
- 不要在方法/嵌套方法/构造函数上使用 `=>`

### 命名

- 简短直接
- **不要回退我的手动重命名**
- 不要跟我的 Visual Studio 自动代码清理对着干
- 除了 `xunit` 测试，不要使用含下划线名称
    - 序列化的部分场景例外
    - 测试命名：方法名_场景_预期行为
    - 一个测试代码文件**最多对应一个**项目代码文件
- 不许在任何名称中使用 `my`
- 接口使用 IUpperCamelCase
- 使用 `Utils` 而非 `Util`/`Utilties`/`Helper`
- 异步方法必须由 `Async` 结尾
- Namespace / Type / Const / Property / Public 使用 UpperCamelCase
- private 使用 snakeCase
- 命名空间名不能作为类名的前缀、类名不能作为函数名/属性名的前缀
    - 即调用时不许出现 `XXXX<pattern>.<pattern>YYYY( )` 的情况
- 除非与 .NET 标准库重名需要区分，否则不要加 `BBDown` 前缀
- **禁用**的缩写
    - cfg -> config
    - ct -> token（cancellation 字样在只有一个 token 时可以删去）
    - cts -> tokenSource
    - ctx -> context
    - src -> source（惯例情况除外）
    - cmd -> command
- **强制大小写规范**（以下全大写的在特定情况下也可使用全小写）
    - FFmpeg / ffmpeg
    - MP4Box / mp4box
    - Aria2c / aria2c
    - ID
    - Url
    - AV、BV、SS、EP、MD
    - Xml、Xaml、Json
    - TV
    - PGC / UGC
    - AVC / HEVC / AV1 / AAC / FLAC / Dolby
    - FLV / MP4 / MP3 / M4A
    - PCDN

## 程序设计

- 命令行选项
    - 名称不要过长，Description 要充分换行
    - 输入格式
        - 涉及音视频的：`<ID><value>`（如 opusXXXXXX、BVXXXXXX）或直接 url

## 工作

你总会遇到另一个 AGENT 在仓库里面同时工作，永远不要破坏别人的工作

## 其他内容

AGENT 对此文档的修改只能添加在本节。其他节不许动。我会定期从中选取移动到上面。
