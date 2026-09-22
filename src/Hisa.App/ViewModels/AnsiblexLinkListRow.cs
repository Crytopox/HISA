namespace Hisa.App;

public sealed class AnsiblexLinkListRow
{
    public required string FromSystemName { get; init; }
    public required string FromZoneText { get; init; }
    public required string ToSystemName { get; init; }
    public required string ToZoneText { get; init; }
    public required string TowardToText { get; init; }
    public required string TowardFromText { get; init; }
}
