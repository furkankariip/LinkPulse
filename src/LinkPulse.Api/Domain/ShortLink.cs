namespace LinkPulse.Api.Domain;

public sealed class ShortLink
{
    public int Id { get; set; }
    public required string Code { get; set; }
    public required string TargetUrl { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public List<LinkClick> Clicks { get; set; } = [];
}
