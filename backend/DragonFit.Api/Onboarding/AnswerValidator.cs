using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using DragonFit.Api.Domain;

namespace DragonFit.Api.Onboarding;

/// <summary>
/// Validates one answer against its question definition and returns a normalised JSON value.
/// Every input type the character-creation screen can send is handled here, and nothing else is accepted.
/// </summary>
public static class AnswerValidator
{
    public record Result(JsonNode? Value, string? Error)
    {
        public static Result Ok(JsonNode? v) => new(v, null);
        public static Result Fail(string e) => new(null, e);
    }

    public static readonly string[] RewardSizes = ["small", "medium", "large"];

    public static Result Validate(OnboardingQuestion q, JsonElement? raw, IReadOnlySet<string>? avatarCodes = null)
    {
        var empty = raw is null || raw.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined
                    || (raw.Value.ValueKind == JsonValueKind.String && string.IsNullOrWhiteSpace(raw.Value.GetString()))
                    || (raw.Value.ValueKind == JsonValueKind.Array && raw.Value.GetArrayLength() == 0);
        if (empty) return q.IsRequired ? Result.Fail("Câu này bắt buộc.") : Result.Ok(null);
        var v = raw!.Value;

        switch (q.InputType)
        {
            case "text":
            {
                if (v.ValueKind != JsonValueKind.String) return Result.Fail("Cần nhập chữ.");
                var s = v.GetString()!.Trim();
                if (q.MinValue is { } min && s.Length < min) return Result.Fail($"Tối thiểu {min} ký tự.");
                if (q.MaxValue is { } max && s.Length > max) return Result.Fail($"Tối đa {max} ký tự.");
                return Result.Ok(JsonValue.Create(s));
            }
            case "number":
            {
                decimal n;
                if (v.ValueKind == JsonValueKind.Number) n = v.GetDecimal();
                else if (v.ValueKind == JsonValueKind.String && decimal.TryParse(v.GetString()!.Replace(',', '.'), NumberStyles.Number, CultureInfo.InvariantCulture, out var p)) n = p;
                else return Result.Fail("Cần nhập số.");
                if (q.MinValue is { } min && n < min) return Result.Fail($"Nhỏ nhất là {min:0.##}.");
                if (q.MaxValue is { } max && n > max) return Result.Fail($"Lớn nhất là {max:0.##}.");
                return Result.Ok(JsonValue.Create(n));
            }
            case "single_choice":
            {
                if (v.ValueKind != JsonValueKind.String) return Result.Fail("Hãy chọn một mục.");
                var s = v.GetString()!;
                return q.Options.Any(o => o.Value == s) ? Result.Ok(JsonValue.Create(s)) : Result.Fail("Lựa chọn không hợp lệ.");
            }
            case "multi_choice":
            {
                if (v.ValueKind != JsonValueKind.Array) return Result.Fail("Hãy chọn ít nhất một mục.");
                var picked = v.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!).Distinct().ToList();
                if (picked.Any(p => q.Options.All(o => o.Value != p))) return Result.Fail("Lựa chọn không hợp lệ.");
                var exclusive = q.Options.Where(o => o.IsExclusive).Select(o => o.Value).ToHashSet();
                if (picked.Any(exclusive.Contains) && picked.Count > 1) return Result.Fail("\"Không có\" không đi cùng mục khác.");
                if (picked.Count == 0) return q.IsRequired ? Result.Fail("Câu này bắt buộc.") : Result.Ok(null);
                return Result.Ok(new JsonArray(picked.Select(p => (JsonNode)JsonValue.Create(p)!).ToArray()));
            }
            case "time":
            {
                if (!TryTime(v, out var t)) return Result.Fail("Giờ không hợp lệ (HH:mm).");
                var minutes = t.Hour * 60 + t.Minute;
                if (q.MinValue is { } min && minutes < min || q.MaxValue is { } max && minutes > max)
                    return Result.Fail($"Chọn giờ từ {Fmt(q.MinValue)} đến {Fmt(q.MaxValue)}.");
                return Result.Ok(JsonValue.Create(t.ToString("HH:mm")));
            }
            case "time_range":
            {
                if (v.ValueKind != JsonValueKind.Object
                    || !v.TryGetProperty("start", out var a) || !v.TryGetProperty("end", out var b)
                    || !TryTime(a, out var start) || !TryTime(b, out var end))
                    return Result.Fail("Cần giờ bắt đầu và giờ kết thúc.");
                return Result.Ok(new JsonObject { ["start"] = start.ToString("HH:mm"), ["end"] = end.ToString("HH:mm") });
            }
            case "avatar_picker":
            {
                if (v.ValueKind != JsonValueKind.String) return Result.Fail("Hãy chọn một hình hài.");
                var s = v.GetString()!;
                if (avatarCodes is not null && !avatarCodes.Contains(s)) return Result.Fail("Hình hài không tồn tại.");
                return Result.Ok(JsonValue.Create(s));
            }
            case "reward_list":
            {
                if (v.ValueKind != JsonValueKind.Array) return Result.Fail("Cần danh sách phần thưởng.");
                var items = new JsonArray();
                foreach (var r in v.EnumerateArray())
                {
                    if (r.ValueKind != JsonValueKind.Object || !r.TryGetProperty("name", out var nm) || nm.ValueKind != JsonValueKind.String
                        || !r.TryGetProperty("size", out var sz) || sz.ValueKind != JsonValueKind.String)
                        return Result.Fail("Mỗi phần thưởng cần tên và cỡ.");
                    var name = nm.GetString()!.Trim();
                    var size = sz.GetString()!;
                    if (name.Length is < 2 or > 60) return Result.Fail("Tên phần thưởng từ 2 đến 60 ký tự.");
                    if (!RewardSizes.Contains(size)) return Result.Fail("Cỡ phần thưởng không hợp lệ.");
                    items.Add(new JsonObject { ["name"] = name, ["size"] = size });
                }
                if (items.Count > 3) return Result.Fail("Tối đa 3 phần thưởng.");
                if (items.Count == 0) return q.IsRequired ? Result.Fail("Cần ít nhất 1 phần thưởng.") : Result.Ok(null);
                return Result.Ok(items);
            }
            default:
                return Result.Fail("Kiểu câu hỏi không được hỗ trợ.");
        }
    }

    static bool TryTime(JsonElement v, out TimeOnly t)
    {
        t = default;
        return v.ValueKind == JsonValueKind.String
               && TimeOnly.TryParseExact(v.GetString(), ["HH:mm", "H:mm"], CultureInfo.InvariantCulture, DateTimeStyles.None, out t);
    }

    static string Fmt(decimal? minutes) => minutes is null ? "" : $"{(int)minutes / 60}:{(int)minutes % 60:00}";
}
