namespace BBDown.Core.Workflow;

/// <summary>可选项：Id 为稳定标识（CLI 输入映射 / serve 帧传输 / GUI 弹窗选择共用），Label 为展示文本。</summary>
public sealed record AskOption(string Id, string Label);

/// <summary>
/// 应答结果：OptionId 必须属于请求选项集合；RawInput 为宿主收到的原始输入（CLI 别名映射用，serve / GUI 为 null）。
/// </summary>
public sealed record AskAnswer(string OptionId, string? RawInput = null);
