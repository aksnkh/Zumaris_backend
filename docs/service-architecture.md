<div dir="rtl" align="right">

# ساختار سرویس‌های Zumaris

تمام سرویس‌ها در همین Monorepo و `Zumaris.sln` هستند. Identity، ServiceRequest و Property پیاده‌سازی دارند؛ نه سرویس دیگر فعلاً اسکلت قابل build هستند. ایجاد اسکلت به معنی آماده‌بودن قابلیت کسب‌وکار یا انتشار روی IIS نیست.

## محل هر قابلیت

| سرویس | مالکیت و محل endpointهای آینده | پیشوند Gateway | اسکیمای MySQL |
| --- | --- | --- | --- |
| Identity | حساب، ورود، Google، نشست، نقش عمومی سامانه | `/identity`، فعال | `zumaris_identity` |
| Property | مشخصات و آدرس ملک، ارتباط کاربران، تاریخ ارتباط‌ها، نمای Home Passport و Timeline تجمیعی ملک | `/property`، فعال | `zumaris_property` |
| Document | اسناد ملک، گواهی‌ها، ضمانت‌نامه‌ها، نسخه‌های سند و تاریخ انقضا | `/document`، رزرو | `zumaris_document` |
| Maintenance | تجهیزات ملک، مشخصات دستگاه، کار و برنامهٔ نگهداری و موعد سرویس | `/maintenance`، رزرو | `zumaris_maintenance` |
| ServiceRequest | سفارش خدمت، تصاویر سفارش، وضعیت اجرا، تخصیص متخصص، پیشنهاد قیمت، زمان‌بندی و History درخواست | `/service-request`، فعال | `zumaris_service_request` |
| ServiceCatalog | دسته و نوع خدمت، گزینه‌های فضای خانه، مشخصات لازم هر خدمت | `/service-catalog`، رزرو | `zumaris_service_catalog` |
| Provider | متخصص و شرکت، مهارت، پوشش جغرافیایی، مدارک حرفه‌ای، نمونه‌کار و ارزیابی | `/provider`، رزرو | `zumaris_provider` |
| Billing | پلن، اشتراک، پرداخت، بازپرداخت، صورتحساب مالی و کمیسیون | `/billing`، رزرو | `zumaris_billing` |
| Notification | قالب پیام، ترجیحات دریافت، ارسال ایمیل/پیامک/اعلان و نتیجهٔ ارسال | `/notification`، رزرو | `zumaris_notification` |
| PropertyIntelligence | امتیاز سلامت، تحلیل سند، پیشنهاد نگهداری، تخمین هزینه و دستیار هوشمند | `/property-intelligence`، رزرو | `zumaris_property_intelligence` |
| WebsiteBuilder | سایت متخصصان، قالب و محتوا، انتشار و دامنه | `/website-builder`، رزرو | `zumaris_website_builder` |
| Integration | اتصال به سامانه‌های بیرونی بیمه، اطلاعات رسمی ملک و سایر شرکا | `/integration`، رزرو | `zumaris_integration` |

اسکیماهای سرویس‌های خالی فقط نام پیشنهادی هستند. اسکریپت دستی Property در database/property/001_properties.sql و راهنمای راه‌اندازی آن در [property.md](property.md) است. Gateway فقط Identity، ServiceRequest و Property را مسیریابی می‌کند.

Home Passport نمای اطلاعات چند سرویس است و مشخصات ملک را دوباره ذخیره نمی‌کند. Admin سرویس مستقل نیست؛ endpoint مدیریتی هر حوزه در سرویس مالک آن قرار می‌گیرد. Booking فعلاً در ServiceRequest و Review/Trust Score در Provider قرار می‌گیرند. Home DNA، HomeGraph و Digital Twin در شروع قابلیت‌های PropertyIntelligence هستند. Notification صاحب ارسال پیام است، ولی موعد نگهداری را Maintenance تعیین می‌کند. تجهیزات و اطلاعات ضمانت آن‌ها با Maintenance است و فایل مدرک ضمانت با Document. عکس سفارش در ServiceRequest باقی می‌ماند.

## لایه‌ها در Visual Studio

<div dir="ltr" align="left">

```text
src
└── Services
    └── Property
        ├── Property.Api
        ├── Property.Application
        ├── Property.Domain
        └── Property.Infrastructure
```

</div>

همین ساختار برای هر سرویس وجود دارد:

- Domain: موجودیت‌ها و قواعد کسب‌وکار؛ بدون وابستگی به لایه‌های بیرونی.
- Application: use caseها، مدل‌ها و قرارداد repository یا ارتباط با سرویس دیگر؛ وابسته به Domain.
- Infrastructure: پیاده‌سازی دسترسی MySQL با query پارامتری یا stored procedure و اتصال بیرونی؛ وابسته به Application و Domain. بدون ORM.
- Api: کنترلرها و تنظیم وابستگی‌ها؛ وابسته به Application و Infrastructure.

بین پروژه‌های سرویس‌های مختلف ProjectReference ایجاد نمی‌کنیم. یک سرویس مستقیم جدول‌های اسکیمای سرویس دیگر را نمی‌خواند؛ ارتباط از API یا رویداد است. صحت دسترسی کاربر به ملک در Property بررسی می‌شود. نقش اظهارشدهٔ مالک/مستأجر، نقش مدیریتی سامانه نیست.

## اجرای اسکلت‌ها

برای دیدن پروژه‌ها فقط Pull و بازکردن `Zumaris.sln` کافی است؛ اسکریپت قبلاً اجرا شده و خروجی آن داخل Git قرار دارد. برای اجرای یک اسکلت، پروژهٔ Api آن را Startup Project کنید و پروفایل http را انتخاب کنید.

| سرویس | پورت توسعه | پورت پیشنهادی آیندهٔ IIS |
| --- | --- | --- |
| Property | 5107 | 5003 |
| Document | 5108 | 5004 |
| Maintenance | 5109 | 5005 |
| ServiceCatalog | 5110 | 5006 |
| Provider | 5111 | 5007 |
| Billing | 5112 | 5008 |
| Notification | 5113 | 5009 |
| PropertyIntelligence | 5114 | 5010 |
| WebsiteBuilder | 5115 | 5011 |
| Integration | 5116 | 5012 |

Swagger در `/swagger` و سند آن در `/swagger/v1/swagger.json` است. نه سرویس خالی هنوز عملیات کسب‌وکار ندارند؛ Property دارای CRUD است و ابتدا تنظیمات محلی/JWT آن را مطابق راهنمای اختصاصی آماده کنید. مسیر `/health` فقط زنده‌بودن پردازه را نشان می‌دهد و به معنی آماده‌بودن دیتابیس، احراز هویت یا قابلیت محصول نیست. تنظیم CORS فعلاً همان `https://app.zumaris.com` است.

پیش از انتشار اولین endpoint واقعی هر سرویس، احراز هویت JWT و اعتبارسنجی نشست Identity، مجوزهای منابع، اسکریپت دستی دیتابیس، تنظیمات محرمانهٔ سرور و در صورت نیاز route و Swagger Gateway را تکمیل می‌کنیم. پورت‌های IIS جدول فقط رزرو پیشنهادی‌اند و سایت یا Binding جدیدی ایجاد نشده است.

## اسکریپت ایجاد پروژه‌ها

در کامپیوتر توسعه، از ریشهٔ ریپوزیتوری اجرا کنید؛ به اجرای آن روی سرور IIS نیازی نیست. Windows PowerShell 5.1 یا PowerShell 7 و SDK دات‌نت ۸ یا جدیدتر لازم است. اسکریپت در محیط فعلی با PowerShell 7 اجرا شده است.

<div dir="ltr" align="left">

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Initialize-ServiceScaffolds.ps1

# Optional: restore NuGet dependencies too
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Initialize-ServiceScaffolds.ps1 -Restore

# Optional: create only selected missing scaffolds
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Initialize-ServiceScaffolds.ps1 -Service Property
```

</div>

اجرای مجدد فقط فایل‌های موجودنبوده و عضویت‌های ناقص Solution را ایجاد می‌کند؛ فایل فعلی را بازنویسی نمی‌کند. Identity و ServiceRequest در فهرست تولید نیستند و کد آن‌ها تغییر نمی‌کند. تعریف ده سرویس جدید در `scripts/service-scaffolds.json` قرار دارد. این ابزار دیتابیس، endpoint کسب‌وکار، دادهٔ آزمایشی، مسیر Gateway یا استقرار سرور ایجاد نمی‌کند.

</div>
