# Triển khai Kiểu Việt lên Vercel

Đích yêu cầu: workspace `https://vercel.com/huy-kv`, mã nguồn `Huy420/KV-QLTS`.

## Trạng thái

Đã chuẩn bị `Dockerfile`, `.dockerignore` và `vercel.json` theo Container runtime của Vercel. Chưa có deployment được xác nhận. Máy phát triển chưa có Docker để kiểm tra build image.

Hiện database vận hành chỉ có trên SQL Express của máy Windows. Địa chỉ `.\SQLEXPRESS`, Windows Authentication và file `.mdf` cục bộ không thể dùng trực tiếp từ container Vercel. Không mở cổng SQL Express ra Internet để xử lý việc này.

## Điều kiện trước khi đưa dữ liệu thật lên web

1. Kết nối tài khoản Vercel có quyền triển khai vào workspace `huy-kv`.
2. Tạo database SQL Server có thể truy cập từ cloud (ví dụ Azure SQL), cấu hình tài khoản ứng dụng và quy tắc truy cập mạng. Việc này chưa thực hiện; không có tài nguyên tính phí nào được tạo trong bước chuẩn bị.
3. Khởi tạo schema và tài khoản admin cho database mới bằng `--init-db` từ môi trường tin cậy, dùng biến môi trường `ConnectionStrings__Assets` và `KV_ADMIN_PASSWORD`. Chỉ dùng trên database trống. Nếu chuyển dữ liệu đang có, cần kế hoạch di chuyển riêng; `--init-db` không sao chép dữ liệu từ SQL Express.
4. Bật `DataProtection__PersistToDatabase=true` để lưu khóa đăng nhập chung trong bảng `DataProtectionKeys` của SQL Server; cấu hình Vercel đã bật biến này. Khởi tạo bằng `--init-db` sẽ bổ sung bảng này nếu chưa có, giữ nguyên tài khoản và hồ sơ hiện có. Bản cập nhật đã kiểm thử giữ cookie đăng nhập sau khi dừng và chạy lại tiến trình. Giới hạn quyền truy cập database và bảo vệ bản sao lưu vì bảng chứa khóa xác thực; mã hóa lưu trữ cần được bật ở dịch vụ SQL.
5. Xác nhận gói Vercel phù hợp sử dụng cho công ty và hạn mức của Container runtime. Gói Hobby miễn phí giới hạn cho mục đích cá nhân, phi thương mại; không mặc định dùng gói này cho ứng dụng vận hành công ty. Không có gói trả phí nào được đăng ký trong bước chuẩn bị. Tham khảo [điều kiện Hobby](https://vercel.com/docs/plans/hobby).

## Biến môi trường Vercel

Đặt trực tiếp trong **Project → Settings → Environment Variables**, không commit bí mật:

| Biến | Giá trị |
|---|---|
| `ConnectionStrings__Assets` | Chuỗi kết nối database cloud, mã hóa TLS; không dùng `.\SQLEXPRESS` hoặc Integrated Security |
| `PORT` | `8080`, đã khai báo trong `vercel.json` |
| `DataProtection__PersistToDatabase` | `true`, đã khai báo trong `vercel.json` |
| `AllowedHosts` | Mặc định `*.vercel.app;localhost;127.0.0.1`; khi có domain riêng cần thêm domain tương ứng |

Ví dụ định dạng chuỗi kết nối (chỉ là placeholder):

```text
Server=tcp:YOUR-SERVER.database.windows.net,1433;Database=KieuVietAssets;User ID=YOUR-APP-USER;Password=SET-IN-VERCEL;Encrypt=True;TrustServerCertificate=False;
```

Không đặt `KV_ADMIN_PASSWORD` vào môi trường chạy web sau khi đã khởi tạo tài khoản. Không đặt biến bí mật vào `NEXT_PUBLIC_*`, file JS hoặc `vercel.json`.

## Tạo deployment sau khi hoàn tất các điều kiện

1. Trong workspace `huy-kv`, chọn **Add New → Project → Import Git Repository → Huy420/KV-QLTS**.
2. Root Directory là thư mục gốc repository, không chọn `wwwroot` hay thư mục bản sao.
3. Dùng framework **Container** theo `vercel.json` và Dockerfile ở thư mục gốc. Container runtime đang được Vercel công bố ở trạng thái beta.
4. Thêm biến môi trường database đã chuẩn bị và triển khai.
5. Kiểm tra log build/runtime, HTTPS, đăng nhập, thêm tài sản thử, kiểm kê, nhập/xuất Excel và khả năng giữ phiên khi thay container trước khi bàn giao.

`Run.cmd`, `Start.ps1` và `Open-VSCode.ps1` phục vụ máy Windows; Vercel khởi chạy container trực tiếp bằng `dotnet KieuViet.Assets.dll`.

Tài liệu: [Vercel Container runtime](https://vercel.com/kb/guide/docker), [Azure SQL free offer](https://learn.microsoft.com/en-us/azure/azure-sql/database/free-tier).

## Tiến độ ngày 30/10/2026

- Azure CLI đã được tải từ Microsoft vào thư mục `.tools`, không thay đổi cài đặt hệ thống.
- Đã thực hiện xác thực bằng mã thiết bị. Azure trả về **No subscriptions found**; chưa tạo được tài nguyên cloud và chưa có chi phí triển khai phát sinh từ các thao tác này.
- Cần chủ tài khoản kích hoạt một Azure subscription ở Azure Portal trước khi có thể tạo website/database. Việc đăng nhập portal đơn thuần chưa tạo subscription.
- Build Release đạt 0 lỗi/0 cảnh báo. Có 55 kiểm tra SQL Server/API đạt, bao gồm khóa đăng nhập lưu trong database và cookie còn dùng được sau khi khởi động lại tiến trình.
- Vercel hiện chưa được kết nối với phiên làm việc này. Không có URL deployment được xác nhận.
