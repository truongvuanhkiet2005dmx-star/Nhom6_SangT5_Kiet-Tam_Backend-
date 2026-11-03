# SmartMoney Backend

Backend ASP.NET Core 8 cho ứng dụng quản lý chi tiêu gia đình.

## Cấu hình máy phát triển

Môi trường Development dùng SQLite và JWT. Không commit các file cấu hình hoặc khóa thật.

- `ConnectionStrings:DefaultConnection`: đường dẫn SQLite, ví dụ `Data Source=smartmoney-family.db`.
- `Jwt:Issuer`, `Jwt:Audience`, `Jwt:Key`: thông tin JWT dành cho môi trường local hoặc triển khai.
- `Firebase:ProjectId`: Project ID Firebase.
- `Firebase:ServiceAccountPath`: đường dẫn đến file service account JSON trên máy.

Trên máy khác, tạo file `appsettings.Development.json` tại thư mục chứa `.csproj` bằng các khóa cấu hình trên. Dùng `Jwt:Key` riêng đủ dài cho mỗi môi trường; không ghi mật khẩu hoặc khóa thực vào Git.

## Firebase

Google login và FCM cần Firebase Admin service account hợp lệ.

- Dự án mặc định: `smartmoney-quanlychitieu`.
- Đường dẫn local hiện tại: `D:\Firebase\serviceAccountKey.json`.
- File Firebase không được commit; `.gitignore` đã loại `serviceAccountKey.json`.

Nếu không có service account, backend vẫn khởi động và chạy được SQLite/JWT, nhưng Google login và gửi FCM không hoạt động.
