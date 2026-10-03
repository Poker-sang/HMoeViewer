namespace HMoeWebCrawler.LocalModels;

public record Settings
{
    /// <summary>
    /// 是否为新会话
    /// <see langword="true"/>时，仅本次写入的项目使用同一个新的写入时间
    /// <see langword="false"/>时，上次最新一批项目和本次写入的项目使用新的写入时间，更旧的项目保持原时间
    /// </summary>
    public required bool NewSession { get; init; }

    public required string Email { get; init; }

    public required string Password { get; init; }

    public string? Cookies { get; set; }
}
