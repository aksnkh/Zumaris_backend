<div dir="rtl" align="right">

# پروفایل شخصی در Identity

مالک اطلاعات شخصی حساب و عکس پروفایل، Identity است؛ سرویس جدیدی ایجاد نشده است. این پیاده‌سازی بک‌اند صفحهٔ Personal Information را پوشش می‌دهد. ایمیل طبق تصمیم فعلی فقط خواندنی است و از حساب اصلی Identity خوانده می‌شود. نام کاربری، رمز، نقش و تنظیمات ورود با این APIها تغییر نمی‌کنند.

## قرارداد API

آدرس پایهٔ عمومی: `https://api.zumaris.com/identity/api/profile/me`

| متد و مسیر | کاربرد |
| --- | --- |
| `GET /identity/api/profile/me` | اطلاعات شخص فعلی |
| `PUT /identity/api/profile/me` | ذخیره نام، نام خانوادگی و تلفن |
| `POST /identity/api/profile/me/photo` | آپلود یا جایگزینی عکس، فرم multipart با فیلد Photo |
| `GET /identity/api/profile/me/photo` | دریافت عکس شخص فعلی |
| `DELETE /identity/api/profile/me/photo` | حذف عکس و بازگشت به آواتار پیش‌فرض فرانت |

همهٔ مسیرها به Bearer JWT معتبر Zumaris و نشست فعال Identity نیاز دارند. کلید BFF لازم نیست و توکن آزمایشی `1` در این سرویس پذیرفته نمی‌شود. POST، PUT و DELETE علاوه بر Bearer به `X-Zumaris-CSRF: 1` نیاز دارند. شناسهٔ کاربر از `sub` توکن تعیین می‌شود؛ userId ورودی نداریم. مسیر با شناسهٔ کاربر دیگر برای مشاهده یا تغییر پروفایل ایجاد نشده است. پاسخ‌ها no-store هستند.

بدنهٔ PUT:

```json
{
  "firstName": "James",
  "lastName": "Davidson",
  "phone": "+44 7700 900123"
}
```

نام و نام خانوادگی بعد از Trim باید هرکدام ۱ تا ۱۰۰ نویسه و بدون control character باشند. حروف فارسی و زبان‌های دیگر معتبرند. تلفن اختیاری است: null یا رشتهٔ خالی برای پاک‌کردن؛ در غیر این صورت شمارهٔ بین‌المللی با +، کد کشور و مجموع ۷ تا ۱۵ رقم لازم است. فاصله، خط تیره و پرانتز حذف می‌شوند؛ نمونهٔ بالا به `+447700900123` تبدیل می‌شود. این صرفاً اعتبارسنجی قالب است و به معنی تأیید مالکیت شماره یا ارسال SMS نیست.

`version` در قرارداد یا جدول پروفایل وجود ندارد. آخرین ذخیرهٔ موفق اطلاعات برنده است؛ تغییر عکس و تغییر متن فقط فیلدهای خودشان را تغییر می‌دهند تا یکدیگر را پاک نکنند. درخواست PUT شامل email، userId، role یا فیلد ناشناخته با 400 رد می‌شود. برای تغییر ایمیل در آینده باید جریان تأیید جداگانه اضافه شود.

نمونهٔ پاسخ موفق (فیلدهای اصلی):

```json
{
  "statusCode": 200,
  "title": "Success",
  "message": "Profile updated successfully.",
  "payload": {
    "userId": "<user GUID>",
    "userName": "<current username>",
    "firstName": "James",
    "lastName": "Davidson",
    "displayName": "James Davidson",
    "email": "<current account email>",
    "phone": "+447700900123",
    "photoUrl": "/identity/api/profile/me/photo",
    "updatedAtUtc": "<UTC timestamp>"
  }
}
```

اگر پروفایل هنوز ذخیره نشده باشد، GET همان ایمیل و نام کاربری واقعی را همراه نام/تلفن/عکس null برمی‌گرداند و رکورد جدیدی نمی‌سازد. displayName در نبود نام شخصی، نام کاربری است. دادهٔ نمونهٔ تصویر یا کاربر ساختگی ذخیره نمی‌شود.

خطاهای validation با HTTP 400، پیام انگلیسی در `message` و جزئیات `errors` برمی‌گردند؛ کاربر یا عکس ناموجود 404، توکن/نشست نامعتبر 401 و خطای ذخیره‌سازی/دیتابیس پاسخ عمومی سرور دارد. هیچ مسیر فیزیکی، اطلاعات اتصال یا رمز در خروجی پروفایل نیست.

## عکس و اتصال فرانت

فرمت‌های پذیرفته‌شده JPEG و PNG با حجم حداکثر ۵ MiB هستند. نام و پسوند ارسالی کاربر مبنای اعتماد نیست؛ امضای فایل و محتوای قابل decode بررسی می‌شوند. سقف تصویر ۱۲ مگاپیکسل و حداکثر ۱۰۰۰۰ پیکسل در هر ضلع است. تصویر با اصلاح جهت، حذف متادیتا و محدودیت ابعاد ۵۱۲×۵۱۲ بازپردازش می‌شود؛ تصویر کوچک بزرگ نمی‌شود. SVG، فایل اجرایی و URL بیرونی پذیرفته نیستند.

فایل‌ها در `C:\ZumarisData\ProfilePhotos\profiles\<user-id>\<random-id>.png` یا jpg ذخیره می‌شوند. نام فایل از کاربر گرفته نمی‌شود. فایل قبلی پس از ثبت موفق مسیر جدید حذف می‌شود. اگر ثبت در دیتابیس شکست بخورد فایل تازه پاک می‌شود. شکست cleanup در لاگ برای پیگیری ثبت می‌شود. پوشه خصوصی است و نباید به‌صورت Static Files عمومی منتشر شود.

چون عکس خصوصی و مبتنی بر Bearer است، برای فرانت روی دامنهٔ دیگر آن را با fetch و هدر Authorization دریافت و به blob URL تبدیل کنید؛ `<img>` معمولی به‌تنهایی هدر Bearer نمی‌فرستد. بعد از جایگزینی/حذف عکس، blob قبلی را revoke و عکس جدید را دوباره fetch کنید. مقدار photoUrl را نسبت به دامنهٔ API resolve کنید.

```javascript
const response = await fetch(new URL(profile.photoUrl, apiBaseUrl), {
  headers: { Authorization: `Bearer ${accessToken}` }
});
if (!response.ok) throw new Error("Unable to load profile photo.");
const imageSrc = URL.createObjectURL(await response.blob());
// Use imageSrc in <img>; URL.revokeObjectURL(imageSrc) on replacement/unmount.
```

## اسکریپت و انتشار دستی IIS

۱. با root/admin فایل `database/identity/003_user_profiles.sql` را در دیتابیس موجود `zumaris_identity` اجرا کنید. فقط جدول جدید `identity_user_profiles` ساخته می‌شود؛ اطلاعات کاربران، ایمیل، هش رمز، نشست‌ها و stored procedureها تغییر نمی‌کنند. اجرای مجددِ همان ساختار مجاز است. روی دیتابیس شما هیچ اسکریپتی خودکار اجرا نمی‌شود.

۲. کاربر برنامه باید SELECT روی identity_users و SELECT/INSERT/UPDATE روی identity_user_profiles داشته باشد. برای سازگاری با نسخه‌های قدیمی MySQL 8 که قفل FOR UPDATE مجوز بیشتری می‌خواهد، مجوز UPDATE روی identity_users نیز لازم است؛ کد پروفایل اطلاعات آن جدول را تغییر نمی‌دهد. حساب app با مجوزهای قبلی شما این موارد را دارد. شناسهٔ کاربر و فعال‌بودن حساب در همان تراکنش بررسی و قفل می‌شود؛ FK جدید فرضی روی collation جدول کاربران قدیمی ایجاد نشده است.

۳. روی سرور پوشهٔ `C:\ZumarisData\ProfilePhotos` را بسازید و به هویت Application Pool مربوط به Identity روی همان پوشه مجوز Modify بدهید. پوشهٔ منتشرشدهٔ برنامه همچنان فقط Read & Execute لازم دارد. مسیر عکس خارج از پوشهٔ Publish است و هنگام انتشار پاک نمی‌شود.

۴. تنظیم مسیر در appsettings پایه ثبت شده است. اگر لازم شد در فایل محلی سرور مقدار زیر را اضافه کنید:

```json
"ProfilePhotos": {
  "RootPath": "C:\\ZumarisData\\ProfilePhotos"
}
```

۵. Identity.Api را با پروفایل IISProduction منتشر کنید؛ تمام خروجی، شامل DLLهای وابستگی و پوشه runtimes کتابخانهٔ تصویر، باید منتقل شود. در IIS از pool دارای No Managed Code، Hosting Bundle دات‌نت ۸ و معماری منطبق x64 استفاده کنید. تنظیمات واقعی ConnectionStrings/Jwt/Google/Bff در فایل محلی سرور حفظ شوند؛ فایل محلی کامپیوتر توسعه را روی فایل معتبر سرور کپی نکنید. دیتابیس Identity باید `zumaris_identity` باشد. پروفایل Publish جدید Environment را Production می‌کند.

۶. مسیر catch-all موجود Gateway یعنی `/identity/{**catch-all}` تمام APIهای جدید را عبور می‌دهد؛ Gateway به تغییر یا Publish مجدد نیاز ندارد. پس از انتشار Identity، pool را Recycle و GET، PUT، آپلود و دریافت/حذف عکس را با کاربر تست بررسی کنید.

## بقیهٔ آیتم‌های صفحهٔ حساب

| قسمت تصویر | سرویس/مسیر موجود یا مالک |
| --- | --- |
| نام، ایمیل، تلفن، عکس و Personal Information | Identity، APIهای همین سند |
| Current Property | Property: `GET /property/api/properties/current` |
| My Properties و تعداد ملک‌ها | Property: `GET /property/api/properties` و totalCount |
| Switch | Property: `PUT /property/api/properties/{id}/current` |
| Payment Methods | Billing؛ این تغییر سیستم پرداخت نمی‌سازد |
| Notifications | Notification؛ این تغییر ترجیحات اعلان نمی‌سازد |
| Settings | مسیرهای موجود نشست/امنیت Identity؛ تنظیمات بیشتر با نیاز مشخص |
| Help & Support، Terms & Privacy | لینک صفحات محتوایی فرانت؛ این تغییر مدیریت محتوا نمی‌سازد |
| Log Out | endpoint موجود Cookie Logout یا BFF Logout مطابق نوع نشست فرانت |

این ریپوزیتوری بک‌اند است؛ طراحی/کدنویسی صفحات فرانت در این تغییر انجام نشده است. نام محصولات/منوها باعث ساخت سرویس تکراری یا دسترسی مستقیم به اسکیمای سرویس دیگر نمی‌شود.

## تست

`dotnet test tests/Identity.Tests/Identity.Tests.csproj` تست‌های HTTP، مجوز، validation انگلیسی، عدم تغییر ایمیل/شناسه، آخرین ویرایش بدون version، decode واقعی عکس و جایگزینی/حذف فایل را اجرا می‌کند. برای تست مخزن واقعی MySQL از دیتابیس loopback جدا با نام `zumaris_identity_test` و متغیر `ZUMARIS_IDENTITY_MYSQL_TEST_CONNECTION` استفاده می‌شود؛ هرگز دیتابیس برنامه را به تست ندهید. تست، fixture کاربران خودش را فقط در دیتابیس آزمایشی می‌سازد؛ جدول کاربران جدیدی در اسکریپت production ایجاد نمی‌شود.

</div>
