using System.Collections.Generic;

namespace BBDown.Core.Opus;

/// <summary>
/// 专栏/图文动态的段落类型。命名与 B 站 opus 接口的 <c>para_type</c> 对齐，但渲染时按结构探测填充，不依赖枚举值。
/// </summary>
public enum OpusParagraphKind
{
    Unknown = 0,
    Text,
    Heading,
    Image,
    Divider,
    Quote,
    List,
    Code,
    LinkCard,
}

public enum OpusListStyle
{
    Unordered = 0,
    Ordered,
}

/// <summary>
/// 行内文本节点（兼容 opus/detail 的 <c>type:"TEXT_NODE_TYPE_WORD"</c> 与 article/view 的 <c>node_type:1</c> 两种 schema）。
/// </summary>
public sealed class OpusTextNode
{
    public string Text { get; set; } = "";
    public string? Url { get; set; }
    public bool Bold { get; set; }
    public int FontSize { get; set; }
    public bool IsFormula { get; set; }
    public string? FormulaLatex { get; set; }
    /// <summary>Text 已是可直接输出的 Markdown（旧版 HTML 转换产物），渲染时跳过行内转义。</summary>
    public bool IsRawMarkdown { get; set; }
}

public sealed class OpusImage
{
    public string Url { get; set; } = "";
    public string Caption { get; set; } = "";
    public int Width { get; set; }
    public int Height { get; set; }
}

public sealed class OpusListItem
{
    public int Level { get; set; }
    public int Order { get; set; }
    public List<OpusTextNode> Nodes { get; set; } = [];
}

public sealed class OpusParagraph
{
    public OpusParagraphKind Kind { get; set; } = OpusParagraphKind.Unknown;
    /// <summary>仅 <see cref="Kind"/> 为 <see cref="OpusParagraphKind.Heading"/> 时有效，取值 2 或 3（不产出 H1，避免与文档标题冲突）。</summary>
    public int HeadingLevel { get; set; }
    public List<OpusTextNode> TextNodes { get; set; } = [];
    public List<OpusImage> Images { get; set; } = [];
    public string CodeLang { get; set; } = "";
    public string Code { get; set; } = "";
    public OpusListStyle ListStyle { get; set; } = OpusListStyle.Unordered;
    public List<OpusListItem> ListItems { get; set; } = [];
    public string LinkTitle { get; set; } = "";
    public string LinkUrl { get; set; } = "";
}

/// <summary>
/// 专栏（cv）或图文动态（opus）的领域模型，与具体接口解耦。所有字符串字段默认空串，调用方无需判空。
/// </summary>
public sealed class OpusDocument
{
    public string Title { get; set; } = "";
    public string AuthorName { get; set; } = "";
    public string AuthorMid { get; set; } = "";
    public long PublishTime { get; set; }
    public List<string> Tags { get; set; } = [];
    public string Summary { get; set; } = "";
    public string OpusId { get; set; } = "";
    public string CvId { get; set; } = "";
    public string SourceUrl { get; set; } = "";
    public List<OpusParagraph> Paragraphs { get; set; } = [];
}
