# Kiểu Việt · Quản lý kiểm kê tài sản

## Mở ứng dụng hằng ngày

Nhấp đúp **Run.cmd** trong thư mục dự án, chờ dòng `Now listening on: http://localhost:5080`, rồi mở **http://localhost:5080**. Giữ cửa sổ chạy ứng dụng mở trong lúc sử dụng; đóng cửa sổ hoặc nhấn `Ctrl+C` sẽ dừng máy chủ.

Trong VS Code có thể chọn **Terminal → Run Task → Chạy ứng dụng Kiểu Việt**. `Open-VSCode.ps1` chỉ mở trình soạn thảo, không chạy máy chủ web. Lỗi `ERR_CONNECTION_REFUSED` xuất hiện khi địa chỉ/cổng đang mở không có máy chủ lắng nghe; hãy kiểm tra cửa sổ chạy ứng dụng trước.

Nếu cổng 5080 đã có ứng dụng đang chạy, mở trang đang chạy hoặc dùng `Run.cmd -Port 5081` và truy cập `http://localhost:5081`. Không cần khởi tạo lại database mỗi lần sử dụng.

Ứng dụng web tiếng Việt, ASP.NET Core 10 và **Microsoft SQL Server**. Giao diện responsive, không phụ thuộc CDN. Dữ liệu thật chỉ lưu qua API vào SQL Server. `/?demo=1` là chế độ minh họa riêng, có nhãn rõ ràng và chỉ lưu dữ liệu mẫu trong trình duyệt.

## Chức năng

- Đăng nhập bằng cookie HttpOnly; mật khẩu băm PBKDF2; chống CSRF và giới hạn thử đăng nhập.
- Ba quyền: **Admin** (toàn quyền, lưu trữ tài sản, khóa đợt, tạo người dùng, xem nhật ký), **Inventory** (thêm/sửa tài sản, nhập Excel, tạo đợt, ghi nhận), **Viewer** (xem/xuất).
- Hồ sơ tài sản: mã duy nhất, tên, nhóm, phòng ban, vị trí, người sử dụng, serial, ngày mua, nguyên giá, tình trạng, ghi chú. Mỗi hồ sơ tương ứng **một đơn vị tài sản**.
- Tìm mã/tên/serial/người dùng/vị trí, lọc phòng ban/tình trạng, phân trang; thống kê nguyên giá và phân bổ phòng ban.
- Mở đợt toàn công ty hoặc theo phòng ban, chụp danh sách và vị trí/người sử dụng sổ sách. Ghi nhận khớp, thiếu, hỏng, sai vị trí; lưu người và thời điểm thực hiện.
- Chỉ khóa khi đã kiểm kê hết. Đợt đã khóa không được sửa. Kiểm kê không tự sửa hồ sơ tài sản; điều chỉnh hồ sơ là một thao tác riêng có nhật ký.
- Chống ghi đè thay đổi đồng thời bằng SQL Server `rowversion`. Kết quả kiểm kê và khóa đợt dùng transaction Serializable.
- Báo cáo chênh lệch, tải mẫu, nhập/xuất Excel `.xlsx`; nhập tối đa 5 MB, 5.000 dòng/lần. Nếu có lỗi, không nhập dòng nào.
- Nhật ký thao tác lưu cùng transaction thay đổi dữ liệu. Lưu trữ tài sản không xóa lịch sử kiểm kê.

## Chạy trên máy Windows / SQL Express

Yêu cầu **.NET SDK 10** và **SQL Server 2019+ / SQL Express**. Máy chủ SQL cần cho phép tài khoản Windows hiện tại tạo database trong lần khởi tạo đầu tiên. Script mặc định dùng `.\SQLEXPRESS` và database mới `KieuVietAssets`.

```powershell
# Lần đầu: nhập mật khẩu admin tối thiểu 12 ký tự khi được hỏi.
powershell -NoProfile -ExecutionPolicy Bypass -File .\Start.ps1 -Initialize

# Những lần sau
powershell -NoProfile -ExecutionPolicy Bypass -File .\Start.ps1

# Xem giao diện mẫu, không cần khởi tạo SQL Server
powershell -NoProfile -ExecutionPolicy Bypass -File .\Start.ps1 -Demo
```

Mở **http://localhost:5080** và đăng nhập tài khoản **admin** với mật khẩu đã đặt. Script dùng SDK cục bộ `.tools/dotnet` nếu có, hoặc `dotnet` đã cài. Dừng bằng `Ctrl+C`.

Với `-Demo`, mở **http://localhost:5080/?demo=1**. Có thể thêm `-Port 5081` để đổi cổng, `-NoBuild` để bỏ qua biên dịch nếu đã build, hoặc `-InitializeOnly` để chỉ khởi tạo database rồi thoát. Script phục hồi thư mục và biến môi trường của phiên PowerShell sau khi dừng. Dùng mã nguồn ở `D:\project_KV_KiemKeTS`; thư mục bản sao `project_KV_KiemKeTS/` không được đưa vào biên dịch.

SQL Server khác: cấu hình biến môi trường trước khi chạy (không commit mật khẩu):

```powershell
$env:ConnectionStrings__Assets = 'Server=TEN-MAY-CHU;Database=KieuVietAssets;Integrated Security=True;Encrypt=True;TrustServerCertificate=False'
```

`TrustServerCertificate=True` trong Start.ps1 chỉ phục vụ SQL Express phát triển trên máy này. Khi triển khai, dùng chứng chỉ SQL hợp lệ và `False`.

Khởi tạo thủ công khi đã có SDK trên PATH:

```powershell
dotnet restore
# Đặt ConnectionStrings__Assets và KV_ADMIN_PASSWORD qua biến môi trường.
dotnet run -- --init-db
dotnet run --urls http://localhost:5080 --environment Development
```

`--init-db` dùng EF `EnsureCreated` trên **database mới/trống**, tạo bảng và tài khoản admin nếu chưa có người dùng; không nâng cấp cấu trúc database cũ. Chạy ứng dụng thông thường không tự tạo hoặc sửa schema. Với phiên bản tiếp theo cần bổ sung EF migrations và sao lưu trước nâng cấp.

## Nhập file Excel công ty

File nguồn được nhắc tới: `K:\KV_HANH CHINH\HC_LUU TRU TAM\KieuViet_KiemKeTS_VPVDD.xlsx`. Đã nhận và đọc được bản gửi lại tại `C:\Users\GIA HUY\Downloads\KieuViet_KiemKeTS_VPVDD.xlsx` ngày 27/10/2026. File có 11 sheet; xem cấu trúc tại `EXCEL_SOURCE.md`. Chưa nhập dữ liệu công ty vào SQL Server; chức năng nhập hiện tại vẫn yêu cầu mẫu chuẩn của ứng dụng.

Trong ứng dụng, chọn **Nhập Excel → Tải mẫu Excel**, giữ nguyên tiêu đề ở hàng 1:

| Cột | Nội dung |
|---|---|
| Mã tài sản | Bắt buộc, duy nhất, tối đa 80 ký tự |
| Tên tài sản | Bắt buộc, tối đa 250 ký tự |
| Nhóm tài sản | Tối đa 120 ký tự |
| Phòng ban, Vị trí, Người sử dụng, Serial | Mỗi cột tối đa 150 ký tự |
| Ngày mua | Ngày Excel hoặc `yyyy-MM-dd`, có thể trống |
| Nguyên giá | Ô số không âm, không chứa ký hiệu tiền tệ |
| Tình trạng | Đang sử dụng / Dự phòng / Hỏng / Thanh lý; trống mặc định Đang sử dụng |
| Ghi chú | Tối đa 2.000 ký tự |

Đọc sheet đầu tiên, không nhận công thức; hãy dán giá trị. Mã đã tồn tại (kể cả tài sản đã lưu trữ) bị từ chối; nhập chỉ thêm mới. File gốc có số lượng lớn hơn 1 cần tách mỗi tài sản thành một dòng và đặt mã riêng trước khi nhập. Cần file nguồn truy cập được để bổ sung ánh xạ trực tiếp đúng mẫu công ty.

## Triển khai mạng nội bộ

```powershell
dotnet publish -c Release -o publish
```

Chạy trên IIS với ASP.NET Core Hosting Bundle 10 hoặc dịch vụ Kestrel sau reverse proxy HTTPS. Cấu hình:

- `ASPNETCORE_ENVIRONMENT=Production`, `ConnectionStrings__Assets`, `AllowedHosts` đúng hostname nội bộ. Production bắt buộc HTTPS cho cookie.
- Cấp tài khoản dịch vụ quyền DML cần thiết trên database; dùng tài khoản triển khai riêng cho khởi tạo schema.
- Lưu Data Protection keys bền vững và giới hạn quyền đọc theo tài khoản dịch vụ để phiên đăng nhập không mất sau tái triển khai. Đồng bộ key ring nếu nhiều máy chủ.
- Sao lưu SQL Server định kỳ và kiểm thử khôi phục. Không mở SQL Server trực tiếp ra Internet.
- Không dùng `Start.ps1` để phục vụ production: script chủ động bật Development và chỉ bind localhost.

Database không chứa dữ liệu mẫu tự động. Bản minh họa chỉ phục vụ đánh giá giao diện và luồng cơ bản; thao tác nhập/xuất Excel cần máy chủ thật.

## Kiểm tra

```powershell
dotnet build
# Kiểm tra tích hợp với SQL Express trên máy hiện tại:
powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\Integration.ps1
# Kiểm tra giao diện bằng Chrome không cửa sổ (nếu đã cài Chrome)
powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\Browser.ps1
```

Script tích hợp tạo database thử nghiệm riêng `KVAssets_Test_<mã>`, thực hiện luồng API và giữ lại database để xem xét. Không chạm vào database công ty. Chi tiết kết quả kiểm tra phiên phát triển được ghi ở `VERIFICATION.md`.

Tham khảo: [ASP.NET Core CSRF](https://learn.microsoft.com/en-us/aspnet/core/security/anti-request-forgery), [EF Core SQL Server](https://www.nuget.org/packages/Microsoft.EntityFrameworkCore.SqlServer/10.0.13), [ClosedXML](https://github.com/ClosedXML/ClosedXML/releases).
