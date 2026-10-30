namespace KieuViet;
public static class Rules
{
    public static readonly string[] Statuses = ["Đang sử dụng", "Dự phòng", "Hỏng", "Thanh lý"];
    public static readonly string[] Results = ["Chưa kiểm kê", "Khớp", "Thiếu", "Hỏng", "Sai vị trí"];
    public static bool VersionMatches(string? version, byte[] current) => version == Convert.ToBase64String(current);
    public static string? Validate(AssetInput a)
    {
        if (string.IsNullOrWhiteSpace(a.Code) || a.Code.Length > 80 || string.IsNullOrWhiteSpace(a.Name) || a.Name.Length > 250) return "Mã (tối đa 80) và tên tài sản (tối đa 250 ký tự) là bắt buộc.";
        if (a.Cost < 0 || a.Cost > 9999999999999999.99m) return "Nguyên giá không hợp lệ.";
        if (!Statuses.Contains(a.Status)) return "Tình trạng không hợp lệ.";
        if (a.Category is null || a.Category.Length > 120 || a.Department is null || a.Department.Length > 150 || a.Location is null || a.Location.Length > 150 || a.Custodian is null || a.Custodian.Length > 150 || a.Serial is null || a.Serial.Length > 150 || a.Notes is null || a.Notes.Length > 2000) return "Phòng ban, vị trí, người sử dụng, serial: tối đa 150 ký tự; nhóm: 120; ghi chú: 2000.";
        if (a.PurchaseDate is { Year: < 1900 }) return "Ngày mua không hợp lệ.";
        return null;
    }
    public static void Apply(Asset a, AssetInput i)
    {
        a.Code = i.Code.Trim(); a.Name = i.Name.Trim(); a.Category = i.Category.Trim(); a.Department = i.Department.Trim();
        a.Location = i.Location.Trim(); a.Custodian = i.Custodian.Trim(); a.Serial = i.Serial.Trim(); a.PurchaseDate = i.PurchaseDate;
        a.Cost = i.Cost; a.Status = i.Status; a.Notes = i.Notes.Trim(); a.UpdatedAt = DateTime.UtcNow;
    }
    public static void Audit(AssetDb db, HttpContext c, string action, string detail) => db.Audit.Add(new AuditEntry { User = c.User.Identity?.Name ?? "system", Action = action, Detail = detail });
}
