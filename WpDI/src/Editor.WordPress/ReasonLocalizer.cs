using System.Globalization;
using System.Resources;
using Editor.Abstractions;

namespace Editor.WordPress;

public sealed class ReasonLocalizer : IReasonLocalizer
{
    // Base name must match folder + file name (without culture + .resx)
    private static readonly ResourceManager Rm =
        new("Editor.WordPress.Resources.WpdiReasons", typeof(ReasonLocalizer).Assembly);

    private readonly CultureInfo? _configured;

    public ReasonLocalizer(CultureInfo? configured = null) => _configured = configured;

    public string Localize(string reasonCode, object[]? reasonArgs = null)
        => Localize(reasonCode, reasonArgs, _configured ?? CultureInfo.CurrentUICulture);

    public string Localize(string reasonCode, object[]? reasonArgs, CultureInfo culture)
    {
        var key = (reasonCode ?? "").Trim().ToLowerInvariant() switch
        {
            "not_found" => "Reason_not_found",
            "trashed"   => "Reason_trashed",
            "conflict"  => "Reason_conflict",
            _           => "Reason_unknown"
        };

        var fmt = Rm.GetString(key, culture) ?? Rm.GetString("Reason_unknown", culture) ?? "Unknown error.";
        return string.Format(culture, fmt, reasonArgs ?? Array.Empty<object>());
    }
}

