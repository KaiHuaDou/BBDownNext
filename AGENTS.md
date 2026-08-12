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

- 单个 `.cs` 文件不得超过 384 行（测试除外）、单个方法不得超过 128 行（测试除外）。
    - 如因清理历史遗留问题暂时超过，必须在在后续改动中尽快拆分。
    - 使用 `just tokei` 来分析行数

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
- 每次提交之前都先更新 `CHANGELOG.md`/`README.md`，更新到最后一节，永远不要新建一节。
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
- 所有语法必须兼容 AOT
    - 正则表达式不要使用不适用于源生成的语法
- 偏好`TryGetValue`，更偏好`GetValueOrDefault`
- 偏好 `var`
- 偏好 `lock(<System.Threading.Lock gate>)`
- 偏好 `is null`/`is not null`
- 偏好 `await using`
- `record class` 可简写为 `record`
- 偏好自动属性
- 正则表达式、路径偏好 `@""`
- 字符串拼接偏好 `StringBuilder` 和 `$""`，避免使用 `+` 拼接
- 空括号中间要加空格，即`( )`
- 不要在方法/嵌套方法/构造函数上使用 `=>`，属性可以
- `<summary>`、`</summary>` 无论如何都单独占一行
- **除非冲突的情况**，不要在 class 前使用 namespace 前缀，尽量使用 `using <namespace>;`（即便只有一处调用也要。

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
- 异步方法必须由 `Async` 结尾（测试除外）
- Namespace / Type / Const / Property / Public 使用 UpperCamelCase
- CONST 也可使用全大写 + 下划线
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
    - sb（尤其禁用这个！） -> builder
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

## WPF 要求

- 开发 WPF 遵守以下要求，与其他冲突以此为准

- 界面设计
    - 灵活性
        - 不同的系统字体大小
        - 窗口任意伸缩
            - 可设最小 `Height`/`Width`
    - `Margin` 从大到小：`20 -> 15 -> 10 -> 5 -> 3`
    - 布局`Panel`
        - 明确表格/表单（或实质类似）场景：`Grid`
        - 其余场景：`StackPanel`/`WrapPanel`/`DockPanel`
            - 尤其善用 `DockPanel`
        - 不建议写死任何 `Height`/`Width`，特殊情况可以（窗口整体、大的布局面板）
            - 图标场景除外
        - 窗口最外层布局 `Panel` 至少 `Margin="10"`
        - `Grid`
            - 不能写死 `ColumnDefintion` 和 `RowDefintion` 的绝对大小
        - `ZIndex`：可选取值 `1/2/4/8/16/32/64/128/256...`
    - `Button`
        - 推荐默认值`Padding="15,3"`，按需覆盖
- 控件命名
    - UpperCamelCase
    - `TextBlock` 以 `Text` 为后缀
    - `ListBox`/`ListView` 以 `List` 为后缀
    - `XxxxButton` 以 `Button` 为后缀
    - 其余 `XxxxBox` 以 `Box` 为后缀
    - 其余以控件名为后缀
- 事件处理程序
    - UpperCamelCase
    - `<控件名><事件名>`，中间无下划线
    - `(object o, XxxxEventArgs e)`
    - 处理逻辑相近的，可以公用事件处理程序
        - 起手式：

            ```csharp
            if (o is not <Control> <control>)
            {
                return;
            }

            <control>...
            ```

        - 可以使用 `Tag` 来区分/标记
- 文件布局
    1. 私有字段
    2. 公开字段/属性
    3. 构造函数
    4. 事件处理程序
    5. 其他方法
- 外部使用
    - 永远不要 `using System.Windows.Forms`
    - 永远不要为 WPF 程序创建任何测试
    - 选择文件夹请使用 `ookii-dialogs-wpf` 中的 `VistaFolderBrowserDialog`
    - `MessageBox` 请使用 `ookii-dialogs-wpf` 中的 `TaskDialog`
    - `ookii-dialogs-wpf` 最新版本是 `v5.0.1`
- 杂项
    - 使用 `nullable`
    - 永远要注册 `Application.DispatcherUnhandledException` 兜住所有异常
    - 所有窗体类都应为 `public`
    - 单独分出 `UserControl` 要经过我确认
    - 应用程序全局变量放在 `App` 下的 `static` (`readonly` 可选) 属性。
    - 充分使用 `VirtualizingStackPanel`
    - 避免过深视觉树
    - 样式具有大量重复的，在 `Theme.xaml` 新建 `Style`
        - 在 `App.xaml` 的 `MergedDictionary` 中包含 `Theme.xaml`

## 工作

你总会遇到另一个 AGENT 在仓库里面同时工作，永远不要破坏别人的工作

## 其他内容

AGENT 对此文档的修改只能添加在本节，在本节添加内容无需经过批准。

其他节不许动。我会定期从中选取移动到上面。

此处应做为添加**约定**的最高优先级。超越 `MEMORY.md` 和工作日志。

### 判别联合特例

`BBDown.Core/ResourceId.cs` 采用嵌套 `record`（`abstract record ResourceId` 内含 `Av` / `Ep` / `Season` / `CheeseEp` / `CheeseSeason` / `Fav` / `MediaList` / `Series` / `Space` / `WatchLater` 等 `sealed record` 子类型）实现判别联合，属「禁止嵌套类」规则的已确认例外：它以类型安全替代字符串前缀打标，且 `FetcherRegistry` 的 `switch` 据此分发、缺分支编译报错。其余场景仍遵守「禁止嵌套类」。注意直播录制走独立链路（`LiveInputResolver` → `LiveDownload`），不经 `ResourceId`，故无对应子类型。
