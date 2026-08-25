using Raphael.Desktop.Services;
using Raphael.Desktop.Services.Notifications;

namespace Raphael.Desktop.ViewModels;

/// <summary>
/// One business event in the administration area, with its on/off switch.
/// </summary>
/// <remarks>
/// The emergency stop. When a notice turns out to be wrong or too noisy, an administrator
/// silences it here instead of waiting for a deploy — it takes effect across every
/// application the notice reaches, because it switches the rules of all its audiences at
/// once.
///
/// <para>
/// ⚠️ It does not survive a catalog synchronisation: re-seeding imposes what the catalog
/// declares, including whether a rule is active. A silence meant to last has to be written
/// into the catalog too.
/// </para>
/// </remarks>
public sealed class NotificationEventToggleViewModel : BaseViewModel
{
    private readonly NotificationAdminApiClient _admin;

    private readonly Func<string, string> _resolveName;

    private readonly Action<string> _report;

    private bool _isActive;

    private bool _isBusy;

    public string BusinessEventCode { get; }

    public string Name => _resolveName(BusinessEventCode);

    /// <summary>How many audiences this event reaches, for context next to the switch.</summary>
    public int RuleCount { get; }

    public NotificationEventToggleViewModel(
        string businessEventCode,
        int ruleCount,
        bool isActive,
        NotificationAdminApiClient admin,
        Func<string, string> resolveName,
        Action<string> report)
    {
        BusinessEventCode = businessEventCode;
        RuleCount = ruleCount;
        _isActive = isActive;
        _admin = admin;
        _resolveName = resolveName;
        _report = report;
    }

    public string AudiencesLabel =>
        string.Format(
            LocalizationService.Instance["NotificationRuleAudiences"],
            RuleCount);

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
                OnPropertyChanged(nameof(IsNotBusy));
        }
    }

    /// <summary>The switch is disabled while its call is in flight.</summary>
    public bool IsNotBusy => !IsBusy;

    /// <summary>
    /// Bound two-way to the switch. Writing it calls the API and rolls back on failure.
    /// </summary>
    public bool IsActive
    {
        get => _isActive;
        set
        {
            if (_isActive == value || IsBusy)
                return;

            _ = ApplyAsync(value);
        }
    }

    private async Task ApplyAsync(bool isActive)
    {
        IsBusy = true;

        try
        {
            var ok = await _admin.SetEventActiveAsync(BusinessEventCode, isActive);

            if (ok)
            {
                _isActive = isActive;

                _report(string.Format(
                    LocalizationService.Instance[
                        isActive
                            ? "NotificationRuleEnabled"
                            : "NotificationRuleDisabled"],
                    Name));
            }
            else
            {
                _report(string.Format(
                    LocalizationService.Instance["NotificationRuleFailed"],
                    Name));
            }
        }
        catch (Exception ex)
        {
            _report(ex.Message);
        }
        finally
        {
            IsBusy = false;

            // Either it took, or the switch snaps back to the truth.
            OnPropertyChanged(nameof(IsActive));
        }
    }
}
