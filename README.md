# SmartMoney Backend

Backend ASP.NET Core 8 cho ứng dụng quản lý chi tiêu gia đình.

## Cấu hình máy phát triển

Môi trường Development dùng SQLite và JWT. Không commit các file cấu hình hoặc khóa thật.

- `ConnectionStrings:DefaultConnection`: đường dẫn SQLite, ví dụ `Data Source=smartmoney-family.db`.
- `Jwt:Issuer`, `Jwt:Audience`, `Jwt:Key`: thông tin JWT dành cho môi trường local hoặc triển khai.
- `Firebase:ProjectId`: Project ID Firebase.
- `Firebase:ServiceAccountPath`: đường dẫn đến file service account JSON trên máy.

Trên máy khác, tạo file `appsettings.Development.json` tại thư mục chứa `.csproj` bằng các khóa cấu hình trên. Dùng `Jwt:Key` riêng đủ dài cho mỗi môi trường; không ghi mật khẩu hoặc khóa thực vào Git.

## Chạy HTTP cho Android Emulator

Chạy profile `http` để backend dùng HTTP trên cổng 5087:

```powershell
dotnet run --launch-profile http
```

Trong Android Emulator, Flutter gọi API local bằng địa chỉ `http://10.0.2.2:5087/api`.

HTTPS redirect chỉ được bật ngoài môi trường Development; profile `http` Development cần thiết cho emulator.

## Firebase

Google login và FCM cần Firebase Admin service account hợp lệ.

- Dự án mặc định: `smartmoney-quanlychitieu`.
- Đường dẫn local hiện tại: `D:\Firebase\serviceAccountKey.json`.
- File Firebase không được commit; `.gitignore` đã loại `serviceAccountKey.json`.

Nếu không có service account, backend vẫn khởi động và chạy được SQLite/JWT, nhưng Google login và gửi FCM không hoạt động.

## CORS và Swagger

Flutter Web chỉ được gọi API từ các origin khai báo trong cấu hình `Cors:AllowedOrigins`, ví dụ:

```json
{
  "Cors": {
    "AllowedOrigins": [
      "http://localhost:3000"
    ]
  }
}
```

Không dùng `AllowAnyOrigin` với credentials. Swagger mặc định chỉ bật trong Development; có thể bật Production rõ ràng bằng `Swagger:Enabled=true` khi cần vận hành.

## EF Core migrations

Sau khi kéo schema mới có cột ví đích chuyển khoản, chạy:

```powershell
dotnet ef migrations add AddTransferWallet
dotnet ef database update
```

Migration hiện tại được tạo với tên `20261103165931_AddTransferWallet`; khi áp dụng local dùng `dotnet ef database update`.

Không sửa hoặc xóa migration đã áp dụng.

## Báo cáo tháng

`GET /api/reports/monthly` trả trường `breakdown` gồm các mục có `categoryName`, `type` và `amount`. Trường thống nhất cho giá trị phân loại là `amount`; transfer không được tính là income hoặc expense.
