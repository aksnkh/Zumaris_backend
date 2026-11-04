# ثبت‌نام و ورود تکنسین — تصویر نهایی

مبنای این نسخه، تصویر نهایی کاربر با دو صفحهٔ Welcome / Create account است. فرم ثبت‌نام فقط firstName، lastName، email، phone، password و checkbox شرایط Provider/حریم خصوصی دارد. تخصص اصلی از فرم حذف شده است. ثبت تخصص‌ها و مدارک در مراحل بعدی پروفایل انجام می‌شود. احراز هویت و مدارک همچنان باید توسط ادمین تأیید شوند؛ ساخت حساب به معنی تأیید حرفه‌ای نیست.

بازیابی رمز ایمیلی ابتدا مطرح و سپس طبق آخرین دستور کاربر فعلاً کنار گذاشته شد. در این تغییر endpoint یا ارسال ایمیل برای Forgot پیاده‌سازی نشده؛ فرانت نباید ارسال موفق ایمیل را شبیه‌سازی کند.

## مالکیت داده و اجرای دکمهٔ Create account

این مخزن بک‌اند است؛ صفحهٔ فرانت‌اند در این تغییر ساخته نشده است. برای حفظ مالکیت سرویس‌ها، دکمهٔ Create account دو فراخوانی متوالی انجام می‌دهد:

۱. ساخت حساب و اطلاعات شخصی در Identity. نام، نام خانوادگی، ایمیل و تلفن در Identity می‌مانند؛ رمز فقط هش و در Identity ذخیره می‌شود. پاسخ شامل accessToken و کوکی HttpOnly برای refresh است.

۲. با همان accessToken، ثبت Provider و رضایت شرایط. نام حرفه‌ای از پروفایل معتبر Identity خوانده می‌شود؛ مرورگر userId، displayName، زمان رضایت یا وضعیت تأیید تعیین نمی‌کند. Provider نه رمز را دریافت می‌کند و نه در دیتابیس Identity می‌نویسد.

قبل از مرحلهٔ اول، فرانت checkbox را کنترل کند؛ مرحلهٔ دوم هم در سرور فقط acceptTermsAndPrivacy=true را می‌پذیرد. موفقیت کامل ثبت‌نام تکنسین را فقط پس از موفقیت مرحلهٔ دوم نشان دهید.

دو مرحله یک تراکنش توزیع‌شده نیستند. اگر ساخت حساب موفق و مرحلهٔ Provider قطع شد، همان مرحلهٔ دوم را با توکن معتبر تکرار کنید. اگر توکن یا پاسخ اول در دسترس نبود، با ایمیل/رمز وارد و مرحلهٔ دوم را ادامه دهید؛ دوباره حساب نسازید. تکرار ثبت Provider همان profile و همان زمان اولیهٔ رضایت را برمی‌گرداند. حساب موجود صرفاً با تطبیق ایمیل به شخص دیگری متصل نمی‌شود؛ ورود معتبر لازم است.

## مرحلهٔ اول: حساب و اطلاعات شخصی

```http
POST https://api.zumaris.com/identity/api/auth/signup
Content-Type: application/json
X-Zumaris-CSRF: 1
```

```json
{
  "firstName": "John",
  "lastName": "Smith",
  "email": "john@example.com",
  "phone": "+44 7700 900123",
  "password": "REPLACE_WITH_USER_PASSWORD"
}
```

firstName و lastName هرکدام ۱ تا ۱۰۰ کاراکتر بدون control character و مجموعشان حداکثر ۱۹۹ کاراکتر است تا نام کامل در displayName فعلی Provider جا شود. ایمیل تا ۲۵۴ کاراکتر و رمز ۸ تا ۱۰۲۴ کاراکتر است. فاصلهٔ ابتدا/انتهای نام و ایمیل حذف، ایمیل lowercase و تلفن به قالب بین‌المللی مثل +447700900123 تبدیل می‌شود. تلفن ۷ تا ۱۵ رقم و با + و کد کشور معتبر شکلی لازم است؛ ارسال SMS یا تأیید مالکیت تلفن/ایمیل در این مرحله پیاده‌سازی نشده است.

ورودی username یا primaryTradeId نداریم. Identity یک نام کاربری داخلی یکتا و کوتاه می‌سازد؛ کاربر می‌تواند برای ورود از ایمیل استفاده کند. رمز تغییر یا trim نمی‌شود.

پاسخ 201 از Response<AuthResponse>؛ نمونهٔ خلاصه:
```json
{
  "statusCode": 201,
  "title": "Created",
  "message": "Account created successfully.",
  "messageLevel": "Cif",
  "payload": {
    "accessToken": "JWT",
    "expiresAtUtc": "UTC_TIMESTAMP",
    "userName": "SERVER_GENERATED_USERNAME",
    "userId": "IDENTITY_USER_GUID",
    "refreshTokenExpiresAtUtc": "UTC_TIMESTAMP"
  }
}
```

refresh token در JSON نمی‌آید؛ در کوکی HttpOnly، SameSite=Strict، Path=/ و در Production با Secure قرار می‌گیرد. کوکی‌های refresh مسیر قدیمی نیز پاک می‌شوند. در درخواست مرورگر credentials=include استفاده شود و Origin واقعی فرانت در CORS هر سرویس مجاز باشد. مقادیر نمونهٔ تاریخ و شناسه بالا صرفاً جای‌نگهدارند.

نام، تلفن، ایمیل، هش رمز و رزرو ایمیل در یک تراکنش Identity ثبت می‌شوند. رقابت دو signup یک ایمیل، یک حساب ایجاد می‌کند و درخواست دیگر 409 می‌گیرد. حساب‌های legacy نیز قبل از ساخت بررسی می‌شوند. اگر پس از ایجاد حساب صدور نشست موقتاً شکست بخورد، پاسخ 503 با پیام ورود مجدد برمی‌گردد؛ حساب حذف یا دوباره ساخته نمی‌شود.

## مرحلهٔ دوم: تکمیل ثبت‌نام Provider

```http
POST https://api.zumaris.com/provider/api/providers/signup
Content-Type: application/json
Authorization: Bearer ACCESS_TOKEN
```

```json
{"acceptTermsAndPrivacy": true}
```

این endpoint با JWT و نشست فعال محافظت می‌شود. false یا حذف checkbox، اطلاعات هویت ناقص و دادهٔ اضافه مانند password، userId، isVerified یا primaryTradeId پذیرفته نیستند. API پروفایل Identity منبع نام و تلفن است. Profile.Id به sub توکن مقید می‌شود؛ پاسخ نامعتبر/قطع مرجع، پروفایل تأییدشده تولید نمی‌کند.

پاسخ 200 از Response<ProviderSignupResult>؛ نمونهٔ خلاصه:
```json
{
  "statusCode": 200,
  "title": "Success",
  "message": "Provider registration completed successfully. Submit your identity documents for administrator review.",
  "messageLevel": "Cif",
  "payload": {
    "profile": {
      "providerId": "PROVIDER_GUID",
      "userId": "IDENTITY_USER_GUID",
      "displayName": "John Smith",
      "isActive": true,
      "isVerified": false,
      "onboardingStatus": "Draft",
      "revision": 1,
      "reviewReason": null,
      "categoryIds": [],
      "documents": []
    },
    "termsAndPrivacyAcceptedAtUtc": "UTC_TIMESTAMP",
    "termsVersion": null,
    "privacyVersion": null
  }
}
```

تاریخ رضایت UTC در سرور تولید می‌شود. نسخهٔ متن‌ها، اگر در ProviderTerms:Version و PrivacyPolicy:Version پیکربندی شده باشند، ثبت می‌شوند؛ چون کاربر نسخه/متن حقوقی مشخصی نداده، مقدار ساختگی تعیین نشده است. تکرار این endpoint زمان و نسخه‌های اولین رضایت را بازنویسی نمی‌کند؛ این endpoint برای تجدید رضایت نسخه‌های آینده طراحی نشده است.

تخصصی خودکار انتخاب نمی‌شود و categoryIds ابتدا خالی است. کاربر بعداً از API فعلی PUT /provider/api/providers/me تخصص‌های خودش را خوداظهاری می‌کند و از POST /provider/api/providers/me/documents مدارک می‌فرستد. حتی با تأیید مدارک، بدون تخصص مرتبط فرصت کاری یا مجوز ارسال Quote ندارد. اطلاعات حرفه‌ای یا تأیید پروفایل موجود در retry بازنویسی نمی‌شود.

## ورود صفحهٔ Welcome

همان مسیر فعلی Identity است؛ endpoint جدید یا رمز جدا در Provider نداریم:
```http
POST https://api.zumaris.com/identity/api/auth/login
Content-Type: application/json
X-Zumaris-CSRF: 1
```
```json
{"userName":"john@example.com","password":"REPLACE_WITH_USER_PASSWORD"}
```

فرانت مقدار فیلد Email را در userName قرارداد موجود ارسال کند. نام کاربری‌های قبلی همچنان پشتیبانی می‌شوند. اول نام کاربری دقیق بررسی می‌شود؛ اگر پیدا نشد و ورودی ایمیل بود، lookup ایمیل انجام می‌شود. ایمیل‌های legacy چندحسابی مبهم به‌صورت تصادفی انتخاب یا به هم متصل نمی‌شوند؛ باید با نام کاربری اصلی وارد شوند. حساب غیرفعال/حذف‌شده یا رمز نادرست مجاز نیست. قالب پاسخ login قبلی تغییر نکرده و accessToken همچنان در response.payload.accessToken است.

بعد از ورود GET /provider/api/providers/me وضعیت حرفه‌ای را مشخص می‌کند: 404 یعنی ثبت Provider کامل نشده؛ Draft/PendingReview/Rejected/Approved مطابق گردش موجود است. ورود به معنی تأیید ادمین نیست. کلید BFF به فرانت داده نشود و برای این مسیرهای عادی لازم نیست.

## Response و خطاهای جدید

خروجی موفق endpointهای جدید دقیقاً مدل مشترک Response<T> است. ApiResponseError زیرکلاس Response<object?> است و فقط errors و traceId را برای خطا اضافه می‌کند؛ قالب endpointهای قبلی یا تمام پروژه به‌صورت global تغییر نکرده است. ResponseErrorsAttribute فقط روی controllerهای جدید signup فعال است؛ خطاهای model validation/CSRF در همان مدل و با message انگلیسی و errors برمی‌گردند. 401 و قطع Identity در مسیر جدید Provider signup نیز از همین قالب استفاده می‌کنند. Errorهای داخلی SQL/stack trace به مشتری داده نمی‌شوند.

```json
{
  "statusCode": 400,
  "title": "Request Failed",
  "message": "Password must contain 8 to 1024 characters.",
  "messageLevel": "Cif",
  "payload": null,
  "errors": {"password":["Password must contain 8 to 1024 characters."]},
  "traceId": "REQUEST_TRACE_ID"
}
```

خروجی واقعی علاوه بر فیلدهای نشان‌داده‌شده می‌تواند فیلدهای موجود InformationLink/DevMessage/Parameters مدل مشترک را با null داشته باشد. خطاهای business که به فیلد خاصی مربوط نیستند الزاماً errors ندارند. HTTP status واقعی حفظ می‌شود؛ خطا با HTTP 200 پنهان نمی‌شود.

## SQL و استقرار

با root/admin و به‌صورت دستی:

1. database/identity/005_signup.sql بعد از Identity قبلی و 003_user_profiles.sql.
2. database/provider/003_signup.sql بعد از 001_provider_quote_profiles.sql و 002_onboarding_reviews.sql.

اسکریپت‌ها فقط جدول‌های محلی همان سرویس را ایجاد می‌کنند، روی ساختار یکسان قابل تکرارند و کاربر، تخصص یا تأیید ساختگی seed نمی‌کنند. schema موجود identity_users با ستون‌های استفاده‌شده در ثبت Google قبلی حفظ می‌شود؛ stored procedureهای قبلی تغییر نکرده‌اند. برنامه نباید migration خودکار روی دیتابیس کاربر اجرا کند.

مجوز runtime علاوه بر دسترسی نشست و پروفایل فعلی:
```sql
GRANT SELECT, INSERT ON zumaris_identity.identity_signup_emails TO 'app'@'localhost';
GRANT SELECT, INSERT ON zumaris_identity.identity_users TO 'app'@'localhost';
GRANT SELECT, INSERT ON zumaris_identity.identity_user_profiles TO 'app'@'localhost';
GRANT SELECT, INSERT, UPDATE ON zumaris_provider.provider_signup_details TO 'app'@'localhost';
```

Identity و Provider را مجدداً Publish کنید؛ مسیرهای Gateway موجود /identity و /provider کافی‌اند و Gateway یا ServiceCatalog تغییر ندارد. JWT/ConnectionStrings محلی واقعی سرور را حفظ کنید. Provider از Identity:BaseUrl فعلی استفاده می‌کند؛ آدرس مرجع فقط HTTPS یا HTTP loopback و بدون redirect است. نیاز به تنظیم SMTP یا انتشار Notification برای این مرحله نیست.

تست‌های HTTP فیلدها، رضایت، CSRF، JWT/نشست، ایمیل تکراری، هش رمز، ورود با ایمیل، عدم افشای خطای داخلی، عدم انتخاب تخصص و Swagger را پوشش می‌دهند. تست MySQL روی zumaris_identity_signup_test و zumaris_provider_test جداگانه، تراکنش، rollback و signup هم‌زمان را بررسی می‌کند؛ هیچ SQL روی سرور کاربر اجرا نمی‌شود.

## نتیجهٔ بررسی این تغییر

Build و Publish در Release/IISProduction برای Identity و Provider موفق بود. تست‌های سراسری ۲۷۲ مورد موفق داشتند؛ ۱۴ تست اختیاریِ فاقد اتصال مخصوص خودشان skip شدند. تست‌های MySQL جدیدِ signup در Identity و Provider اجرا و موفق شدند، از جمله درخواست هم‌زمان، تکرار، rollback و lookup ایمیل مبهم. هیچ SQL یا استقراری روی سرور کاربر انجام نشد.
