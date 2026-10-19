<div dir="rtl" align="right">

# ورود Google در Identity

فعلاً فقط بک‌اند پیاده‌سازی شده است؛ اتصال NextAuth و ذخیره توکن در فرانت هنوز انجام نشده است. Redis برای این بک‌اند لازم نیست.

## آماده‌سازی سرور

روش ساده‌تر برای ثبت تنظیمات: روی سرور، فایل `scripts/Initialize-IdentityGoogleConfig.ps1` را اجرا کنید. شناسهٔ عمومی Google این پروژه در اسکریپت ثبت شده و فقط مسیر پوشهٔ منتشرشدهٔ Identity را می‌پرسد؛ کلید داخلی را خودش می‌سازد و تنظیمات را در `appsettings.json` همان پوشه ذخیره می‌کند. برای استفاده از شناسهٔ دیگری، پارامتر `-GoogleClientId` را بدهید. مقادیر فعلی دیتابیس و JWT و کلید داخلی معتبر قبلی حفظ می‌شوند. سپس Application Pool مربوط به Identity را در IIS دوباره راه‌اندازی کنید. این فایل تنظیمات سرور را هنگام انتشارهای بعدی حفظ کنید؛ داخل Git قرار ندهید. اگر متغیرهای محیطی هم‌نام قبلاً تعریف شده‌اند، آن‌ها بر تنظیمات فایل اولویت دارند. این روش جایگزین مرحلهٔ ۳ زیر است؛ جدول همچنان باید دستی ساخته شود.

<div dir="ltr" align="left">

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\Initialize-IdentityGoogleConfig.ps1
```

</div>

1. در اسکیما `zumaris_identity` فایل `database/identity/002_google_external_logins.sql` را دستی اجرا کنید. این فایل فقط جدول نگاشت حساب گوگل را ایجاد می‌کند؛ ALTER مربوط به nullable شدن `identity_users.password_hash` که قبلاً انجام داده‌اید تکرار نمی‌شود. جدول‌های نشست اسکریپت `001_auth_sessions.sql` نیز باید موجود باشند. هیچ migration خودکاری اجرا نمی‌شود.
2. کاربر اتصال Identity باید دسترسی SELECT و INSERT روی `identity_users` و `identity_external_logins` و دسترسی‌های قبلی جدول‌های نشست را داشته باشد. برای ورود گوگل stored procedure جدید لازم نیست.
3. متغیرهای محیطی **Identity.Api** را تنظیم کنید و Application Pool را restart کنید:
   - `Google__ClientIds__0`: همان OAuth Client ID مورد استفاده در `AUTH_GOOGLE_ID` فرانت. چند Client ID مجاز را با اندیس‌های بعدی وارد کنید؛ wildcard مجاز نیست.
   - `Bff__ClientKey`: کلید تصادفی اختصاصی، حداقل ۳۲ بایت، فقط مشترک میان سرور Next.js و Identity. آن را داخل Git، JavaScript مرورگر یا متغیر `NEXT_PUBLIC_` قرار ندهید. این کلید با Google Client Secret و JWT signing key متفاوت است.
4. نسخه جدید Identity را روی IIS منتشر کنید. مسیر فعلی Gateway روی `https://api.zumaris.com/identity` همین endpointها را عبور می‌دهد و به route جدید نیاز ندارد. اتصال Identity به MySQL روی همان سرور می‌تواند با `127.0.0.1` باقی بماند.

Google Client Secret در بک‌اند لازم نیست. سرور باید برای دریافت کلیدهای عمومی امضای Google دسترسی HTTPS خروجی داشته باشد. نبود تنظیمات بالا باعث پاسخ 503 در endpointهای جدید می‌شود و ورود معمولی به کار خود ادامه می‌دهد.

## قرارداد برای سرور Next.js

تمام درخواست‌های زیر باید از **سرور** فرانت با هدر `X-Zumaris-Bff-Key` فرستاده شوند. این endpointها در بخش GoogleBff در Swagger نمایش داده می‌شوند و فیلد اجباری `X-Zumaris-Bff-Key` دارند. مقدار واقعی کلید در مستندات قرار داده نمی‌شود. پاسخ‌ها `Cache-Control: no-store` دارند و refresh token را در JSON برمی‌گردانند؛ کوکی مرورگر ایجاد نمی‌کنند.

| درخواست | بدنه JSON | نتیجه |
| --- | --- | --- |
| `POST /identity/api/auth/google` | `{ "idToken": "<account.id_token>" }` | ورود یا ساخت کاربر جدید و صدور توکن Zumaris |
| `POST /identity/api/auth/google/link` | `{ "idToken": "<account.id_token>" }` | اتصال صریح به کاربر فعلی؛ علاوه بر کلید BFF، هدر `Authorization: Bearer <Zumaris access token>` الزامی است؛ پاسخ 204 |
| `POST /identity/api/auth/bff/refresh` | `{ "refreshToken": "<refresh token>" }` | چرخش refresh token و صدور access token جدید |
| `POST /identity/api/auth/bff/logout` | `{ "refreshToken": "<refresh token>" }` | ابطال نشست؛ پاسخ 204 |

پاسخ موفق ورود/refresh:

<div dir="ltr" align="left">

```json
{
  "payload": {
    "accessToken": "<Zumaris JWT>",
    "expiresAtUtc": "<UTC timestamp>",
    "userName": "<local username>",
    "userId": "<internal user GUID>",
    "refreshTokenExpiresAtUtc": "<UTC timestamp>"
  },
  "refreshToken": "<opaque refresh token>"
}
```

</div>

NextAuth باید `account.id_token` را ارسال کند، نه Google access token یا JWT نشست NextAuth. توکن خروجی Zumaris برای ServiceRequest با هدر Bearer قابل استفاده است. مرورگر نباید کلید BFF یا refresh token را دریافت کند؛ اتصال فرانت و هماهنگی refresh هم‌زمان در مرحله بعد پیاده‌سازی خواهد شد. استفاده مجدد از refresh token مصرف‌شده کل نشست را باطل می‌کند؛ بنابراین refresh موازی برای یک نشست مجاز نیست.

اعتبارسنجی رسمی Google شامل امضا، audience، issuer، انقضا و ایمیل تأییدشده است. شناسه پایدار حساب `sub` مبنای نگاشت است. رمز ساختگی ایجاد نمی‌شود و کاربر صرفاً گوگلی با رمز خالی امکان ورود با رمز ندارد. تغییر ایمیل گوگل کاربر داخلی جدیدی برای همان `sub` ایجاد نمی‌کند.

ایمیل موجود به صورت خودکار به Google متصل نمی‌شود: پاسخ 409 یعنی کاربر باید ابتدا وارد حساب فعلی شود و اتصال صریح را انجام دهد. برای اتصال، ایمیل تأییدشده Google باید با ایمیل حساب داخلی یکسان باشد. حساب غیرفعال یا حذف‌شده اجازه ورود گوگل ندارد.

خطاهای معمول: 400 بدنه نامعتبر، 401 توکن/کلید/نشست نامعتبر، 409 تعارض اتصال حساب، 429 محدودیت نرخ، 503 تنظیمات ناقص یا عدم دسترسی به اعتبارسنجی Google. نبود جدول یا مجوز SQL خطای سرور است. محدودیت نرخ فعلی Identity بین درخواست‌های هر IP مشترک است؛ در اتصال سرور Next.js ظرفیت آن باید متناسب با بار واقعی بررسی شود.

## بررسی انجام‌شده

تست‌های خودکار با مخزن و اعتبارسنج جایگزین، ورود، جلوگیری از اتصال خودکار ایمیل، اتصال صریح، نشست، چرخش، تشخیص استفاده مجدد و خروج را بررسی می‌کنند. اعتبارسنج واقعی برای تنظیمات ناقص و توکن خراب نیز بررسی شده است. ورود واقعی Google و اجرای SQL روی سرور کاربر در این محیط انجام نشده است.

</div>
