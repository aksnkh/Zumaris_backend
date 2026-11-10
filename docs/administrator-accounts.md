# ساخت حساب مدیر در Identity

حساب، رمز، اطلاعات شخصی و عضویت مدیر همگی متعلق به **Identity** هستند. Provider تغییری برای این قابلیت ندارد. این قابلیت دو مسیر جدید دارد: ساخت حساب مدیر توسط مدیر مجاز، و راه‌اندازی اولیهٔ اولین مدیر با کلید سرور.

## مسیرهای جدید

| متد | مسیر Gateway | مجوز |
|---|---|---|
| POST | `/identity/api/admin/accounts` | توکن Admin، نشست معتبر و عضویت فعال مدیر |
| POST | `/identity/api/admin/bootstrap` | قابلیت راه‌اندازی فعال روی سرور + کلید محرمانه؛ فقط اولین بار |

ورودی هر دو همان فرم حساب جدید است:

```json
{
  "firstName": "System",
  "lastName": "Admin",
  "email": "admin@example.com",
  "phone": "+44 7700 900123",
  "password": "REPLACE_WITH_USER_PASSWORD"
}
```

نام‌ها هرکدام ۱ تا ۱۰۰ کاراکتر و مجموع نام کامل حداکثر ۲۰۰ کاراکتر با فاصله است. ایمیل تا ۲۵۴ کاراکتر، تلفن بین‌المللی ۷ تا ۱۵ رقم با + و رمز ۸ تا ۱۰۲۴ کاراکتر است. قواعد نرمال‌سازی و هش رمز با ثبت‌نام عادی مشترک‌اند. نام کاربری داخلی در سرور ساخته می‌شود؛ ورود با ایمیل امکان‌پذیر است. `role`، `isAdmin`، `userId`، `application` یا وضعیت حساب را در این ورودی نفرستید؛ فیلد اضافی پذیرفته نیست و نقش به‌صورت سروری Admin تعیین می‌شود.

هر دو مسیر موفق، HTTP 201 و `Response<AdministratorAccountResponse>` دارند:

```json
{
  "statusCode": 201,
  "title": "Created",
  "message": "Administrator account created successfully.",
  "messageLevel": "Admin",
  "informationLink": null,
  "devMessage": null,
  "parameters": null,
  "payload": {
    "userId": "11111111-1111-4111-8111-111111111111",
    "userName": "user_SERVER_GENERATED_VALUE",
    "email": "admin@example.com",
    "firstName": "System",
    "lastName": "Admin",
    "phone": "+447700900123",
    "role": "Admin",
    "createdAtUtc": "2026-11-09T08:00:00Z",
    "createdByUserId": null
  }
}
```

نمونهٔ بالا ساختار پاسخ را نشان می‌دهد. پیام bootstrap با پیام مسیر معمولی متفاوت است. `createdByUserId` برای bootstrap برابر null و برای ساخت توسط مدیر، شناسهٔ مدیر فراخواننده است. این پاسخ رمز، هش رمز، access token یا refresh token ندارد و Cookie نشست مدیر فعلی را تغییر نمی‌دهد. حساب تازه پس از ساخت، از Login معمولی وارد می‌شود.

## راه‌اندازی اولین مدیر

۱. اسکریپت **`database/identity/007_administrator_accounts.sql`** را پس از اسکریپت‌های قبلی Identity، روی `zumaris_identity` اجرا کنید. SQL در startup برنامه اجرا نمی‌شود و در توسعه نیز روی دیتابیس کاربر اجرا نشده است.

۲. در تنظیمات خصوصی **Identity** روی سرور، مثلاً `appsettings.Local.json` موجود، بخش زیر را اضافه کنید؛ مقادیر فعلی ConnectionStrings/Jwt را حذف نکنید:

```json
{
  "AdminBootstrap": {
    "Enabled": true,
    "Key": "REPLACE_WITH_A_RANDOM_SERVER_ONLY_KEY_OF_AT_LEAST_32_UTF8_BYTES"
  }
}
```

این مقدار، جای‌نگهدار است و نباید همان متن نمونه به‌عنوان کلید استفاده شود. کلید تصادفی و خصوصی، ۳۲ تا ۵۱۲ بایت UTF-8 باشد؛ مقدار ASCII تصادفی توصیه می‌شود. تنظیم معادل محیطی `AdminBootstrap__Enabled` و `AdminBootstrap__Key` است. پس از تغییر تنظیم محلی/محیطی، برنامه یا App Pool مربوط به Identity را مجدداً راه‌اندازی کنید.

نمونهٔ تولید تصادفی کلید در PowerShell روی سرور مورداعتماد:

```powershell
$bootstrapBytes = New-Object byte[] 48
$bootstrapRng = [Security.Cryptography.RandomNumberGenerator]::Create()
$bootstrapRng.GetBytes($bootstrapBytes)
$bootstrapRng.Dispose()
$bootstrapKey = [Convert]::ToBase64String($bootstrapBytes)
# Store $bootstrapKey privately in Identity configuration.
# Use the same value in the one-time request header.
```

این کلید بخشی از فرم عمومی signup یا متغیر `NEXT_PUBLIC` نیست؛ فقط برای عملیات اولیهٔ دارندهٔ دسترسی مجاز سرور است. مقدار واقعی آن در مخزن، مثال‌ها، لاگ‌ها یا فرانت عمومی قرار نمی‌گیرد.

۳. درخواست زیر را با کلید تنظیم‌شده ارسال کنید. در Swagger نیز پارامتر هدر `X-Zumaris-Bootstrap-Key` نمایش داده می‌شود:

```http
POST https://api.zumaris.com/identity/api/admin/bootstrap
Content-Type: application/json
X-Zumaris-CSRF: 1
X-Zumaris-Bootstrap-Key: YOUR_PRIVATE_SERVER_BOOTSTRAP_KEY
```

بدنه همان فرم پنج‌فیلدی بالا است. Bearer نیاز نیست؛ به‌جای آن کلید خصوصی و کنترل یک‌بارمصرف دیتابیس لازم‌اند. فقط وقتی هنوز هیچ عضویت مدیر و هیچ علامت تکمیل bootstrap وجود ندارد، حساب ساخته می‌شود. درخواست تکراری پس از موفقیت 409 می‌گیرد. وجود مدیر قبلی، حتی عضویت غیرفعال، مسیر را برای ساخت حساب بعدی باز نمی‌کند.

۴. پس از موفقیت، `AdminBootstrap:Enabled` را false کنید، کلید را از تنظیم موقت بردارید و Identity را restart/recycle کنید. علامت تکمیل در دیتابیس نیز مستقل از این تنظیم، استفادهٔ دوباره را مسدود می‌کند.

۵. با حساب تازه وارد شوید:

```http
POST https://api.zumaris.com/identity/api/auth/login
Content-Type: application/json
X-Zumaris-CSRF: 1
```

```json
{
  "userName": "admin@example.com",
  "password": "REPLACE_WITH_USER_PASSWORD",
  "application": "Admin"
}
```

از `payload.accessToken` برای APIهای مدیر استفاده کنید. نیازی به اجرای INSERT دستی برای اعطای نقش پس از این ساخت حساب نیست؛ عضویت همراه حساب در همان تراکنش ایجاد شده است.

## ساخت مدیر بعدی توسط مدیر موجود

```http
POST https://api.zumaris.com/identity/api/admin/accounts
Authorization: Bearer ADMIN_ACCESS_TOKEN
Content-Type: application/json
X-Zumaris-CSRF: 1
```

همان بدنهٔ حساب جدید را ارسال کنید. توکن باید برای ساید Admin باشد؛ داشتن حساب مشتری یا تکنسین کافی نیست. عضویت فعال مدیر در API بررسی و داخل تراکنش ایجاد حساب نیز با حساب فعال/حذف‌نشده دوباره کنترل می‌شود. کلید bootstrap برای این مسیر لازم نیست و جایگزین توکن مدیر نمی‌شود.

این API برای **ساخت حساب تازه** است. اگر ایمیل از قبل متعلق به هر حساب دیگری باشد، 409 می‌گیرد؛ آن حساب صرفاً با تطبیق ایمیل به مدیر تبدیل نمی‌شود. ارتقای حساب موجود، حذف/تعلیق مدیر، مدیریت نقش‌ها، دعوت ایمیلی و بازیابی رمز، endpoint تازه‌ای در این تغییر ندارند.

## تراکنش، سابقه و هم‌زمانی

رزرو ایمیل، `identity_users`، `identity_user_profiles`، `platform_administrators`، سابقهٔ ایجاد و علامت تکمیل اولیه در یک تراکنش Identity ثبت می‌شوند. خطا در هر بخش، باقی داده‌های همان درخواست را هم برمی‌گرداند. رمز فقط با همان هش‌ساز Identity ذخیره می‌شود و در پاسخ بازنمی‌گردد.

جدول `identity_administrator_setup` یک ردیف قفل‌شونده دارد و ساخت bootstrap/مدیر عادی را سریال می‌کند؛ دو درخواست هم‌زمان نمی‌توانند دو «اولین مدیر» بسازند. جدول `identity_administrator_account_creations`، کاربر ساخته‌شده، مدیر سازنده یا منبع Bootstrap و زمان UTC را نگه می‌دارد. این تغییر API مشاهدهٔ سابقهٔ حساب‌های مدیر اضافه نمی‌کند.

Migration 007 حساب یا نقش نمونه ایجاد نمی‌کند. اگر هنگام نصب عضویت مدیر موجود باشد، setup بسته مقداردهی می‌شود. اجرای مجدد اسکریپت علامت تکمیل را پاک نمی‌کند. غیرفعال‌کردن یا حذف عضویت‌ها پس از bootstrap نیز راه‌اندازی اولیه را دوباره باز نمی‌کند؛ این مسیر بازیابی رمز/حساب مدیر نیست.

در reset کامل محیط آزمایشی، دو جدول جدید نیز جزو داده‌های Identity هستند؛ پاک‌کردن صرف `identity_users` و `platform_administrators` علامت یک‌بارمصرف را پاک نمی‌کند. هیچ endpoint عمومی برای reset این علامت وجود ندارد.

## خطاها

| HTTP | حالت |
|---|---|
| 400 | دادهٔ حساب، فیلد اضافه یا هدر CSRF نامعتبر |
| 401 | توکن مدیر نامعتبر/نبود توکن، یا کلید bootstrap اشتباه |
| 403 | توکن App/Tech یا عضویت/حساب مدیر فراخواننده غیرفعال |
| 404 | bootstrap در تنظیم سرور غیرفعال است |
| 409 | ایمیل تکراری، bootstrap تکمیل‌شده یا وجود مدیر قبلی |
| 503 | bootstrap فعال اما کلید پیکربندی‌نشده، setup ناقص یا اعتبارسنجی عضویت موقتاً ناممکن |
| 500 | خطای غیرمنتظرهٔ ایجاد؛ جزئیات دیتابیس در پاسخ منتشر نمی‌شود. |

پاسخ‌های جدید از مدل موجود `Response<T>` استفاده می‌کنند؛ خطای فیلدی `errors` و پیام انگلیسی بالای پاسخ دارد. حداکثر بدنهٔ این درخواست‌ها ۱۶ KiB است. محدودیت نرخ احراز هویت Identity نیز اعمال می‌شود. برای مسیر غیرفعال/کلید اشتباه، قبل از نوشتن حساب، درخواست رد می‌شود.

## استقرار

فقط **Identity** نیاز به انتشار دارد. ابتدا migration 007 را اجرا کنید و سپس Identity را منتشر کنید. Provider، ServiceRequest، Gateway و CORS تغییری لازم ندارند. Runtime Identity به SELECT/INSERT روی جداول حساب و عضویت/سابقه و SELECT/UPDATE روی setup نیاز دارد؛ DDL در runtime اجرا نمی‌شود. راهنمای قدیمیِ اعطای نقش با SQL هنوز برای عملیات دستی ریشه ممکن است، اما اکنون API امن برای ساخت حساب جدید مدیر وجود دارد.
