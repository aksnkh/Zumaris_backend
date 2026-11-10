# حساب مستقل مدیر در Identity

حساب مدیر در جدول `admin_accounts` ذخیره می‌شود؛ حساب App یا Tech با همان ایمیل، حساب جداگانه‌ای است و دسترسی مدیر نمی‌دهد. راهنمای کامل تغییر ساختار در [حساب‌های مستقل](independent-accounts.md) آمده است.

## APIهای مدیر

مسیرهای زیر پس از پیشوند Gateway یعنی `/identity` هستند. پاسخ‌های JSON جدید از `Response<T>` استفاده می‌کنند.

| متد | مسیر | کاربرد |
|---|---|---|
| POST | `/api/admin/register` | ایجاد حساب مدیر؛ مدیر فعال یا کلید خصوصی اولین راه‌اندازی لازم است |
| POST | `/api/admin/login` | ورود با ایمیل و رمز حساب Admin |
| POST | `/api/admin/refresh` | تمدید نشست Admin با Cookie همین ساید |
| POST | `/api/admin/logout` | پایان نشست Admin؛ پاسخ 204 |

بدنهٔ ثبت‌نام:

```json
{
  "firstName": "System",
  "lastName": "Admin",
  "email": "admin@example.com",
  "phone": "+447700900123",
  "password": "REPLACE_WITH_USER_PASSWORD"
}
```

نام‌ها هرکدام حداکثر ۱۰۰ کاراکتر و مجموع نام کامل حداکثر ۲۰۰ کاراکتر، ایمیل حداکثر ۲۵۴ کاراکتر، تلفن بین‌المللی ۷ تا ۱۵ رقم و رمز ۸ تا ۱۰۲۴ کاراکتر هستند. فیلدهای `role` و `application` پذیرفته نمی‌شوند. پاسخ ثبت‌نام 201 شامل مشخصات مدیر در `payload` است؛ رمز و توکن ندارد و نشست مدیر سازنده را جایگزین نمی‌کند. سپس حساب ساخته‌شده از مسیر ورود استفاده می‌کند:

```json
{
  "userName": "admin@example.com",
  "password": "REPLACE_WITH_USER_PASSWORD"
}
```

مسیر ورود نوع حساب را تعیین می‌کند؛ `application` در بدنه ارسال نشود. توکن در `payload.accessToken` و refresh token در Cookie مخصوص Admin قرار می‌گیرد.

## اولین مدیر

پس از اجرای دستی اسکریپت تخریبی `database/identity/008_reset_independent_accounts.sql`، حسابی وجود ندارد. تنظیمات خصوصی Identity را با حفظ ConnectionStrings و Jwt تکمیل کنید:

```json
{
  "AdminBootstrap": {
    "Enabled": true,
    "Key": "REPLACE_WITH_A_RANDOM_SERVER_ONLY_KEY_OF_AT_LEAST_32_UTF8_BYTES"
  }
}
```

کلید واقعی تصادفی و خصوصی، ۳۲ تا ۵۱۲ بایت UTF-8 باشد و در فرانت عمومی یا مخزن قرار نگیرد. Identity را restart کنید. فرم ثبت‌نام بالا را با هدرهای زیر ارسال کنید:

```http
POST https://api.zumaris.com/identity/api/admin/register
Content-Type: application/json
X-Zumaris-CSRF: 1
X-Zumaris-Bootstrap-Key: YOUR_PRIVATE_SERVER_BOOTSTRAP_KEY
```

این روش فقط اولین مدیر را می‌سازد. پس از موفقیت، Enabled را false کنید، کلید موقت را بردارید و Identity را restart کنید. علامت تکمیل در دیتابیس مستقل از تنظیمات نگهداری می‌شود؛ غیرفعال‌کردن مدیر آن را دوباره باز نمی‌کند.

## مدیرهای بعدی

همان `/api/admin/register` با بدنهٔ بالا و هدر `Authorization: Bearer ADMIN_ACCESS_TOKEN` و `X-Zumaris-CSRF: 1` فراخوانی می‌شود. حساب و نشست سازنده باید فعال و متعلق به Admin باشند. توکن App یا Tech مجاز نیست.

ایمیل تکراری فقط در `admin_accounts` باعث 409 می‌شود. وجود همان ایمیل در App یا Tech مانع ساخت مدیر نیست و هیچ حسابی را ارتقا نمی‌دهد. هر حساب شناسه، رمز و پروفایل مستقل دارد.

## ثبت اتمیک و خطاها

حساب، سابقهٔ ساخت در `identity_administrator_account_creations` و علامت اولین راه‌اندازی در یک تراکنش ذخیره می‌شوند. ردیف `identity_administrator_setup` ساخت اولین مدیر را در درخواست‌های هم‌زمان کنترل می‌کند. وضعیت مدیر سازنده نیز داخل تراکنش دوباره بررسی می‌شود.

| HTTP | حالت |
|---|---|
| 400 | ورودی یا CSRF نامعتبر |
| 401 | اعتبارنامه، توکن یا کلید نامعتبر |
| 403 | حساب/توکن متعلق به ساید دیگر یا مدیر غیرفعال |
| 404 | راه‌اندازی اولیه غیرفعال |
| 409 | ایمیل تکراری در Admin یا راه‌اندازی قبلاً تکمیل‌شده |
| 503 | تنظیمات یا سرویس لازم در دسترس نیست |

مسیرهای قبلی `/api/admin/accounts` و `/api/admin/bootstrap` همچنان نام‌های جایگزین حفاظت‌شده هستند. `/api/auth/login` نیز به‌عنوان مسیر قدیمی با فیلد `application: "Admin"` باقی است؛ برای فرانت جدید مسیر اختصاصی بالا را استفاده کنید.

اسکریپت 008 داده‌های قدیمی Identity را حذف می‌کند؛ اسکریپت‌های تاریخی 001 تا 007 را پس از آن اجرا نکنید. SQL در startup اجرا نمی‌شود و روی سرور کاربر اجرا نشده است. در این تغییر معماری Identity و Provider هر دو باید منتشر شوند؛ راهنمای کامل ترتیب استقرار در سند حساب‌های مستقل است.
