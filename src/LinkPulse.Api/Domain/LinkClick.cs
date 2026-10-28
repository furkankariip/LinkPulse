namespace LinkPulse.Api.Domain;

public sealed class LinkClick
{
    public long Id { get; set; }
    public int ShortLinkId { get; set; }
    public DateTime ClickedAt { get; set; }
    public string? Referrer { get; set; }
    public DeviceType Device { get; set; }
}

public enum DeviceType
{
    Unknown,
    Desktop,
    Mobile,
    Tablet,
    Bot
}
