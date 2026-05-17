using System.Text.Json;
using AuthWithAdmin.Shared.AuthSharedModels;

namespace AuthWithAdmin.Server.Data;

// ─────────────────────────────────────────────────────────────────────────────
//  ExternalIntegrationMapper — pure, stateless transformation helper.
//
//  Given a raw incoming JSON object and two mapping tables (field + status),
//  produces a *new* JSON object whose keys match our internal contract
//  (camelCase target fields) and whose Status / StatusLabel values are
//  translated through the status mappings.
//
//  Behaviour when no mappings are configured: the input is returned
//  unchanged. This keeps the existing webhook compatible.
// ─────────────────────────────────────────────────────────────────────────────

public static class ExternalIntegrationMapper
{
    public sealed record MappingResult(
        string             TransformedJson,
        List<string>       Warnings);

    /// <summary>Apply field + status mappings to a JSON object body.</summary>
    /// <param name="rawJson">The exact JSON bytes received on the wire.</param>
    /// <param name="fieldMappings">All active field-mapping rows for the
    /// relevant source system. Each row routes one source key → target key
    /// (with optional default + required flag).</param>
    /// <param name="statusMappings">All active status-mapping rows. Used to
    /// translate the *value* of the Status field after it's been mapped to
    /// the canonical target key.</param>
    public static MappingResult Apply(
        string rawJson,
        IReadOnlyList<ExternalIntegrationFieldMappingDto>  fieldMappings,
        IReadOnlyList<ExternalIntegrationStatusMappingDto> statusMappings)
    {
        var warnings = new List<string>();

        if (string.IsNullOrWhiteSpace(rawJson))
            return new MappingResult("{}", new() { "empty_payload" });

        JsonElement source;
        try
        {
            using var doc = JsonDocument.Parse(rawJson);
            source = doc.RootElement.Clone();
        }
        catch (Exception ex)
        {
            return new MappingResult("{}", new() { "invalid_json: " + ex.Message });
        }

        if (source.ValueKind != JsonValueKind.Object)
            return new MappingResult("{}", new() { "root_is_not_object" });

        // Start with a verbatim copy of the source — we then *overlay* any
        // mapped target keys on top of it. Unmapped keys pass through, which
        // means an Innovation Team that already uses our camelCase contract
        // doesn't have to define a single mapping row.
        var target = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        foreach (var prop in source.EnumerateObject())
            target[prop.Name] = prop.Value;

        // Apply field mappings. Active rows only.
        bool hasFieldMappings = false;
        foreach (var m in fieldMappings)
        {
            if (!m.IsActive) continue;
            hasFieldMappings = true;

            JsonElement value = default;
            bool present = source.TryGetProperty(m.SourceFieldName, out value);

            if (!present || IsEmpty(value))
            {
                if (!string.IsNullOrEmpty(m.DefaultValue))
                {
                    target[m.TargetFieldName] = JsonElementFromString(m.DefaultValue);
                }
                else if (m.IsRequired)
                {
                    warnings.Add($"missing_required: {m.TargetFieldName} (from {m.SourceFieldName})");
                }
                // else: leave target unset — controller-side COALESCE handles it.
                continue;
            }

            target[m.TargetFieldName] = value;
        }

        if (!hasFieldMappings && fieldMappings.Count > 0)
            warnings.Add("all_field_mappings_inactive");

        // Apply status mapping — translate `status` value, fill `statusLabel`
        // when blank. We operate on the camelCase target keys produced above.
        if (target.TryGetValue(ExternalIntegrationTargetFields.Status, out var statusEl)
            && statusEl.ValueKind == JsonValueKind.String)
        {
            var raw = statusEl.GetString()?.Trim() ?? "";
            var match = statusMappings.FirstOrDefault(m =>
                m.IsActive &&
                string.Equals(m.SourceStatusValue?.Trim(), raw, StringComparison.OrdinalIgnoreCase));

            if (match is not null)
            {
                target[ExternalIntegrationTargetFields.Status] =
                    JsonElementFromString(match.InternalStatus);

                bool labelMissing =
                    !target.TryGetValue(ExternalIntegrationTargetFields.StatusLabel, out var lbl) ||
                    IsEmpty(lbl);

                if (labelMissing && !string.IsNullOrWhiteSpace(match.DisplayLabel))
                {
                    target[ExternalIntegrationTargetFields.StatusLabel] =
                        JsonElementFromString(match.DisplayLabel);
                }
            }
            else if (statusMappings.Any(m => m.IsActive))
            {
                warnings.Add($"status_unmapped: '{raw}'");
            }
        }

        // Serialise back. Preserves Unicode (Hebrew) verbatim.
        var transformed = JsonSerializer.Serialize(target,
            new JsonSerializerOptions
            {
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                WriteIndented = false,
            });

        return new MappingResult(transformed, warnings);
    }

    private static bool IsEmpty(JsonElement e) =>
        e.ValueKind == JsonValueKind.Null     ||
        e.ValueKind == JsonValueKind.Undefined||
        (e.ValueKind == JsonValueKind.String  && string.IsNullOrWhiteSpace(e.GetString()));

    /// <summary>Best-effort coercion of a default string into a JsonElement.
    /// Numeric / boolean defaults are recognised; everything else becomes a
    /// JSON string. Keeps the admin UI free of type pickers.</summary>
    private static JsonElement JsonElementFromString(string raw)
    {
        var trimmed = raw.Trim();
        if (trimmed == "true" || trimmed == "false")
            return JsonDocument.Parse(trimmed).RootElement.Clone();
        if (long.TryParse(trimmed, out _) || double.TryParse(trimmed,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out _))
            return JsonDocument.Parse(trimmed).RootElement.Clone();
        return JsonDocument.Parse(JsonSerializer.Serialize(raw)).RootElement.Clone();
    }
}