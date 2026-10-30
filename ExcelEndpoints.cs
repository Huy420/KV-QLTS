using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using System.IO.Compression;

namespace KieuViet;
public static class ExcelEndpoints
{
    private const string Mime = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    private static readonly string[] Headers = ["Mã tài sản", "Tên tài sản", "Nhóm tài sản", "Phòng ban", "Vị trí", "Người sử dụng", "Serial", "Ngày mua", "Nguyên giá", "Tình trạng", "Ghi chú"];
    public static void Map(RouteGroupBuilder api)
    {
        api.MapGet("/excel/template", () => Workbook("Mau_nhap_tai_san.xlsx", Headers, []));
        api.MapGet("/excel/assets", async (AssetDb db) =>
        {
            var assets = await db.Assets.AsNoTracking().Where(x => !x.Archived).OrderBy(x => x.Code).ToListAsync();
            return Workbook("Danh_muc_tai_san.xlsx", Headers, assets.Select(a => new object?[] { a.Code, a.Name, a.Category, a.Department, a.Location, a.Custodian, a.Serial, a.PurchaseDate?.ToString("yyyy-MM-dd"), a.Cost, a.Status, a.Notes }));
        });
        api.MapGet("/excel/sessions/{id:int}", async (int id, AssetDb db) =>
        {
            var session = await db.Sessions.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id);
            if (session is null) return Results.NotFound();
            var items = await db.Items.AsNoTracking().Where(x => x.SessionId == id).OrderBy(x => x.Code).ToListAsync();
            return Workbook($"Kiem_ke_{id}.xlsx", ["Đợt kiểm kê", "Mã tài sản", "Tên tài sản", "Vị trí sổ sách", "Người sử dụng sổ sách", "Vị trí thực tế", "Kết quả", "Chênh lệch", "Ghi chú", "Người kiểm kê", "Thời điểm UTC"],
                items.Select(i => new object?[] { session.Name, i.Code, i.Name, i.ExpectedLocation, i.ExpectedCustodian, i.ActualLocation, i.Result,
                    i.Result == "Chưa kiểm kê" ? "Chưa xác định" : i.Result != "Khớp" || i.ActualLocation != i.ExpectedLocation ? "Có" : "Không", i.Notes, i.CheckedBy, i.CheckedAt?.ToString("O") }));
        });
        api.MapPost("/excel/import", async (HttpContext c, AssetDb db) =>
        {
            if (!c.Request.HasFormContentType) return Results.BadRequest(new { error = "Chọn file .xlsx." });
            var form = await c.Request.ReadFormAsync(); var file = form.Files.GetFile("file");
            if (file is null || file.Length == 0 || file.Length > 5 * 1024 * 1024 || !file.FileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
                return Results.BadRequest(new { error = "File phải là .xlsx, tối đa 5 MB." });
            using var buffer = new MemoryStream(); await file.CopyToAsync(buffer); buffer.Position = 0;
            try
            {
                using (var zip = new ZipArchive(buffer, ZipArchiveMode.Read, true))
                    if (zip.Entries.Sum(e => e.Length) > 40 * 1024 * 1024 || zip.Entries.Count > 1000)
                        return Results.BadRequest(new { error = "File Excel quá lớn sau giải nén." });
                buffer.Position = 0;
                using var book = new XLWorkbook(buffer); var sheet = book.Worksheet(1);
                if (!Headers.Select((h, i) => sheet.Cell(1, i + 1).GetString().Trim() == h).All(x => x))
                    return Results.BadRequest(new { error = "Tiêu đề không đúng mẫu. Tải file mẫu và giữ nguyên hàng đầu tiên." });
                int last = sheet.LastRowUsed()?.RowNumber() ?? 1;
                if (last > 5001) return Results.BadRequest(new { error = "Mỗi lần nhập tối đa 5.000 tài sản." });
                var errors = new List<string>(); var inputs = new List<AssetInput>();
                var codes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var existing = new HashSet<string>(await db.Assets.Select(x => x.Code).ToListAsync(), StringComparer.OrdinalIgnoreCase);
                for (int row = 2; row <= last; row++)
                {
                    string S(int col) => sheet.Cell(row, col).GetString().Trim();
                    if (Enumerable.Range(1, 11).All(col => S(col) == "")) continue;
                    if (Enumerable.Range(1, 11).Any(col => sheet.Cell(row, col).HasFormula)) { errors.Add($"Dòng {row}: không nhận ô có công thức; hãy dán giá trị."); continue; }
                    DateTime? date = null;
                    if (S(8) != "")
                    {
                        if (sheet.Cell(row, 8).TryGetValue<DateTime>(out var dt)) date = dt.Date;
                        else if (DateTime.TryParseExact(S(8), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out dt)) date = dt;
                        else { errors.Add($"Dòng {row}: ngày mua phải là ngày Excel hoặc yyyy-MM-dd."); continue; }
                    }
                    decimal cost = 0;
                    if (S(9) != "" && !sheet.Cell(row, 9).TryGetValue<decimal>(out cost))
                    { errors.Add($"Dòng {row}: nguyên giá phải là số."); continue; }
                    var input = new AssetInput(S(1), S(2), S(3), S(4), S(5), S(6), S(7), date, cost, S(10) == "" ? "Đang sử dụng" : S(10), S(11), null);
                    var error = Rules.Validate(input);
                    if (error is not null) errors.Add($"Dòng {row}: {error}");
                    else if (!codes.Add(input.Code) || existing.Contains(input.Code)) errors.Add($"Dòng {row}: mã '{input.Code}' đã tồn tại (kể cả tài sản lưu trữ).");
                    else inputs.Add(input);
                }
                if (errors.Count > 0) return Results.BadRequest(new { error = "Chưa nhập dữ liệu. Sửa các dòng lỗi rồi thử lại.", errors = errors.Take(100), errorCount = errors.Count });
                if (inputs.Count == 0) return Results.BadRequest(new { error = "File không có dòng tài sản." });
                foreach (var input in inputs) { var asset = new Asset(); Rules.Apply(asset, input); db.Assets.Add(asset); }
                Rules.Audit(db, c, "Nhập Excel", $"{inputs.Count} tài sản"); await db.SaveChangesAsync();
                return Results.Ok(new { imported = inputs.Count });
            }
            catch (Exception ex) when (ex is IOException or ArgumentException or FormatException or System.Xml.XmlException or DocumentFormat.OpenXml.Packaging.OpenXmlPackageException)
            { return Results.BadRequest(new { error = "Không đọc được file Excel. Kiểm tra file .xlsx và cấu trúc theo mẫu." }); }
        }).RequireAuthorization("Write");
    }
    private static IResult Workbook(string filename, string[] headers, IEnumerable<object?[]> rows)
    {
        using var book = new XLWorkbook(); var s = book.AddWorksheet("Du lieu");
        for (int col = 0; col < headers.Length; col++) s.Cell(1, col + 1).Value = headers[col];
        int row = 2;
        foreach (var values in rows)
        {
            for (int col = 0; col < values.Length; col++)
            {
                if (values[col] is decimal d) s.Cell(row, col + 1).Value = d;
                else s.Cell(row, col + 1).Value = values[col]?.ToString() ?? "";
            }
            row++;
        }
        var range = s.Range(1, 1, Math.Max(1, row - 1), headers.Length);
        range.CreateTable(); s.SheetView.FreezeRows(1); s.Columns().AdjustToContents(8, 50);
        s.Row(1).Style.Fill.BackgroundColor = XLColor.FromHtml("#176B52"); s.Row(1).Style.Font.FontColor = XLColor.White;
        using var output = new MemoryStream(); book.SaveAs(output); return Results.File(output.ToArray(), Mime, filename);
    }
}
