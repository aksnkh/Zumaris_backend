# ثبت‌نام و ورود مستقل تکنسین

مدل قبلیِ ساخت حساب App و سپس enrolment به Tech کنار گذاشته شده است. حساب Tech در `zumaris_identity.tech_accounts` مستقل است؛ همان ایمیل می‌تواند در App یا Admin هم حساب متفاوت داشته باشد.

```http
POST https://api.zumaris.com/identity/api/tech/register
Content-Type: application/json
X-Zumaris-CSRF: 1
```

```json
{
  "firstName": "John",
  "lastName": "Smith",
  "email": "john@example.com",
  "phone": "+447700900123",
  "password": "REPLACE_WITH_USER_PASSWORD",
  "acceptTermsAndPrivacy": true
}
```

تخصص اصلی در فرم ثبت‌نام نیست. رضایت شرایط لازم است. Identity حساب Tech و اطلاعات شخصی خودش را می‌سازد و Provider را با فراخوانی سروریِ دارای scope مخصوص enrolment ثبت می‌کند. پروفایل حرفه‌ای، زمان/نسخهٔ رضایت و مدارک در Provider می‌مانند. ثبت‌نام موفق 201 و `Response<AuthResponse>` با توکن Tech و providerId می‌دهد؛ تخصص یا مدرکی خودکار تأیید نمی‌شود.

اگر بعد از ایجاد حساب، Provider موقتاً قطع باشد، پاسخ 503 داده می‌شود؛ ثبت‌نام را برای ساخت حساب دوم تکرار نکنید. از مسیر Login Tech با همان اطلاعات وارد شوید تا همان enrolment تکمیل شود:

```http
POST /identity/api/tech/login
Content-Type: application/json
X-Zumaris-CSRF: 1
```

```json
{"email":"john@example.com","password":"REPLACE_WITH_USER_PASSWORD"}
```

application در body مسیر جدید لازم نیست؛ مسیر نوع حساب را تعیین می‌کند. حساب App با همان ایمیل و رمز، تا زمانی که Tech مستقل ثبت نشده باشد در این Login پذیرفته نیست. اکانت موجود فقط داخل همان جدول Tech تکراری محسوب می‌شود.

پس از ورود، PUT `/provider/api/providers/me` تخصص‌های خوداظهاری و نام حرفه‌ای را ذخیره می‌کند. Postcode و OtherSkills در مسیر verification-documents/profile هستند. هر سه مدرک IdentityCheck، Insurance و Certificate باید از مسیرهای طبقه‌بندی‌شده upload و submit شوند. API آزاد قدیمی documents به‌تنهایی مدرک الزامیِ منتظر بررسی ایجاد نمی‌کند.

Refresh/Logout از `/identity/api/tech/refresh` و `/identity/api/tech/logout` با Cookie مخصوص Tech و CSRF انجام می‌شوند. دریافت فرصت/Quote به مدارک تأییدشده، Postcode، تخصص مرتبط و پروفایل فعال وابسته می‌ماند؛ تکمیل مدارک شرط ورود Tech نیست.

بازیابی رمز ایمیلی و ارسال SMTP همچنان طبق تصمیم قبلی خارج از محدوده است. APIهای مشترک پروفایل شخصی و عکس در Identity، فقط دادهٔ همین حساب Tech را با توکن آن تغییر می‌دهند و به حساب App هم‌ایمیل سرایت نمی‌کنند.

جزئیات قرارداد، ثبت‌نام Admin و ترتیب حذف/ساخت schema: [حساب‌های مستقل](independent-accounts.md).
