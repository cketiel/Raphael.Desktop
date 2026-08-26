namespace Raphael.Desktop.Models;

/// <summary>
/// A notification rule, as much of it as the administration panel needs.
/// </summary>
/// <remarks>
/// Slim copy of <c>Raphael.Notification.Application.DTOs.NotificationRuleDto</c>: the
/// server sends recipients, channels, actions and conditions too, and the deserialiser
/// ignores what is not declared here. The panel only ever asks one question of a rule —
/// is this notice switched on — so copying the rest would be copying drift for nothing.
/// See <c>_meta/CONTRACT_MAP.md</c>.
/// </remarks>
public sealed class NotificationRuleSummaryDto
{
    public Guid Id { get; set; }

    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string BusinessEventCode { get; set; } = string.Empty;

    public bool IsActive { get; set; }
}
