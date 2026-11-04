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

Các migration liên quan hiện có: `AddAdjustmentDirection` cho điều chỉnh số dư ví và `AddWalletClientIdAndTokenOwnership` cho `clientId` idempotency cùng ownership duy nhất của FCM token. Chỉ Development mới tự chạy migration khi khởi động. Production phải chạy migration bằng pipeline hoặc:

```powershell
dotnet ef database update --project Backend_Nhom6_Kiet&Tam_App_QuanLyChiTieu.csproj
```

Không tự áp dụng migration vào database local; không sửa migration cũ.

## Health và local configuration

`GET /api/health` trả trạng thái API và không yêu cầu xác thực. Dùng `appsettings.Example.json` làm mẫu, tạo `appsettings.Development.json` hoặc User Secrets cho cấu hình local. Không commit `appsettings.Development.json`, service account hoặc database.

Flutter dùng `GET /api/transactions?year=2026&month=11&page=1&pageSize=100` để phân trang; mặc dù có phân trang, response vẫn là JSON array để giữ tương thích hiện tại. DTO không trả `UserId`.

## Báo cáo tháng

`GET /api/reports/monthly` trả `breakdown` gồm hai mảng rõ ràng: `breakdown.income` và `breakdown.expense`. Mỗi mục có `categoryName` và `amount`; trường thống nhất cho giá trị phân loại là `amount`. Transfer không được tính là income hoặc expense.

## Contract ví

`POST /api/wallets` nhận `name`, `type` và `initialBalance`. `type` chỉ nhận `cash`, `bank` hoặc `ewallet`.

`PUT /api/wallets/{id}` nhận cùng các trường `name`, `type`, `initialBalance`; Flutter không được gửi `currentBalance` vào `initialBalance`. Nếu update không cần thay đổi số dư ban đầu, có thể bỏ `initialBalance`; API vẫn giữ `initialBalance` hiện tại.

## Điều chỉnh số dư ví

Điều chỉnh số dư được lưu qua giao dịch:

- `POST /api/transactions`
- `PUT /api/transactions/{id}`
- `DELETE /api/transactions/{id}`

Giao dịch điều chỉnh dùng:

```json
{
  "eventId": "uuid-hoac-key-dong-bo-on-dinh",
  "amount": 500000,
  "type": "adjustment",
  "adjustmentDirection": "increase",
  "walletId": 1
}
```

`amount` phải lớn hơn 0. `adjustmentDirection` chỉ nhận `increase` hoặc `decrease`. Điều chỉnh chỉ cập nhật ví được xác định bởi `walletId`; không tính vào income, expense, breakdown báo cáo hoặc cảnh báo ngân sách. `eventId` vẫn được áp dụng idempotency giống các loại giao dịch khác.

## Xác thực Google và tài khoản

Backend đăng ký tài khoản email/mật khẩu với `EmailConfirmed=true` vì API hiện chưa triển khai luồng xác minh email riêng. Google đăng nhập qua Firebase ID token và chỉ chấp nhận email có `email_verified=true`. Backend chỉ cấp JWT Google sau khi đã có liên kết ASP.NET Identity `Firebase/<Firebase UID>`:

- Google lần đầu tạo tài khoản và liên kết Firebase UID trong cùng luồng, sau đó trả JWT.
- Nếu email đã tồn tại nhưng chưa có liên kết Firebase UID phù hợp, API trả `409` và không cấp JWT chỉ dựa trên email.
- Không ghép tài khoản Google với tài khoản mật khẩu chỉ vì email trùng.

Đăng nhập mật khẩu dùng Identity lockout, tối đa 5 lần sai trong 15 phút cho mỗi tài khoản. API trả `423` khi tài khoản bị khóa và `401` với thông điệp đăng nhập lại khi thông tin sai hoặc token hết hạn. JWT hiện không có refresh token.

Rate limiting login/register/google/join được phân vùng theo IP; một IP không làm ảnh hưởng hoàn toàn đến IP khác. Khi chạy sau reverse proxy, cấu hình forwarded headers đúng để lấy IP thực; ở một số môi trường serverless/multi-instance giới hạn theo bộ nhớ vẫn có thể khác nhau giữa các instance.

`OccurredAtUtc` được chuẩn hóa sang UTC, đầu ra transaction sử dụng `DateTimeOffset` theo offset `+00:00`. Mốc báo cáo, ngân sách và nhóm ngày hàng tháng dùng múi giờ Việt Nam (UTC+7), không phụ thuộc múi giờ máy chủ.

## Kiểm thử

Chạy build và test:

```powershell
dotnet build Backend_Nhom6_Kiet&Tam_App_QuanLyChiTieu.csproj
dotnet test SmartMoney.Api.Tests/SmartMoney.Api.Tests.csproj
```
