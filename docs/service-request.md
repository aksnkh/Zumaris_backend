# Service Request

برای endpointهای تکمیل‌شده، مهاجرت 002، پوشه سرور و آپلود تصاویر، [راهنمای مدیریت و تصاویر](service-request-photos.md) را ببینید. بخش‌های زیر قرارداد ثبت اولیه را توضیح می‌دهند.

چهار پروژه مستقل در `src/Services/ServiceRequest` قرار دارند: Api، Application، Domain، Infrastructure. وابستگی Domain به کتابخانه وب یا دیتابیس نیست؛ Application قراردادها و قواعد ثبت، Infrastructure دسترسی مستقیم به MySQL و ارتباط Identity، و Api قرارداد HTTP را دارد. ORM اضافه نشده است. در Visual Studio هم هر چهار پروژه زیر فولدر اختصاصی ServiceRequest هستند.

## دامنه فعلی

قرارداد **POST ثبت درخواست**:

- مستقیم: `POST /api/service-requests`
- از Gateway: `POST https://api.zumaris.com/service-request/api/service-requests`

UserId از claim `sub` توکن معتبر گرفته می‌شود. Status همیشه Submitted، TechnicianId خالی و Version برابر 1 است. ثبت درخواست و رویداد اولیه تاریخچه در یک تراکنش InnoDB انجام می‌شود. کلاینت نمی‌تواند UserId، Status، TechnicianId، PhotoIds یا فیلد ناشناخته دیگری در بدنه ارسال کند.

GET، لیست، ویرایش، لغو، Timeline و آپلود/دریافت تصویر اکنون پیاده‌سازی شده‌اند. هدر Location به GET جزئیات اشاره می‌کند. Retry ثبت اولیه همان پاسخ 201 و شناسه قبلی را برمی‌گرداند.

AreaTypeId نوع بخش خانه مانند آشپزخانه یا اتاق‌خواب است؛ تشخیص اتاق‌خواب مشخص لازم نیست. PropertyId، CategoryId و AreaTypeId فعلاً شناسه‌های خارجی غیرخالی هستند؛ **وجود، مالکیت Property و سازگاری دسته‌بندی/نوع بخش هنوز با سرویس مرجع کنترل نمی‌شوند** چون سرویس/دیتای مرجع در اختیار این ریپوزیتوری نیست. قبل از اتصال به جریان تخصیص/پرداخت باید این اعتبارسنجی اضافه شود. هیچ Foreign Key بین اسکیماهای سرویس‌ها ایجاد نمی‌شود.

## اسکریپت دیتابیس — اجرای دستی

فایل `database/service-request/001_service_requests.sql` را خودتان با حساب مدیریتی روی MySQL نسخه 8.0.16 یا جدیدتر اجرا کنید. فایل اسکیما **zumaris_service_request** را ایجاد/انتخاب می‌کند؛ روی zumaris_identity اجرا نمی‌شود و جداول کاربران را تغییر نمی‌دهد.

جداول:

- `service_requests`: اطلاعات درخواست، Version، هش بدنه و کلید اختیاری Idempotency.
- `service_request_status_history`: تاریخچه وضعیت؛ ثبت اولیه همراه درخواست اتمیک است.
- `service_request_photos`: فقط شناسه، درخواست، مسیر نسبی فایل، جایگاه 0 تا 7 و زمان ایجاد؛ فاقد باینری تصویر.

قبل از اجرا، نام‌ها را بررسی و از دیتابیس موجود طبق روال خود بکاپ بگیرید. IF NOT EXISTS فقط اجرای مجدد همان ساختار را تحمل می‌کند و جدول ناسازگار موجود را اصلاح نمی‌کند. حساب برنامه برای endpoint فعلی به SELECT و INSERT روی درخواست/تاریخچه نیاز دارد. DBA باید مجوز اسکیما جدید را به حساب فعلی برنامه با Host واقعی همان حساب بدهد؛ رمز یا GRANT با Host حدسی در اسکریپت قرار نگرفته است.

این اسکریپت در Codex اجرا نشده است. تست‌های خودکار از مخزن حافظه‌ای استفاده می‌کنند و اثبات اجرای SQL، rollback واقعی و رقابت unique index در MySQL نیستند.

## تنظیمات و IIS

- قبل از اجرا/Publish در PowerShell، دستور `./scripts/Initialize-ServiceRequestLocalConfig.ps1` را از ریشه ریپوزیتوری اجرا کنید. این ابزار تنظیمات موجود Identity را بدون چاپ رمز می‌خواند و در فایل **خارج از Git** به نام `ServiceRequest.Api/appsettings.Local.json` قرار می‌دهد؛ فقط Database به `zumaris_service_request` تغییر می‌کند. نام کاربری، رمز، Server محلی و JWT همان مقادیر موجود باقی می‌مانند. فایل موجود بازنویسی نمی‌شود.
- فایل local خودکار همراه خروجی Publish روی سرور کپی می‌شود، ولی Commit نمی‌شود. فایل پایه تنظیمات سرویس جدید هیچ رمز/کلید JWT ندارد. برای انتشار از دستگاه دیگر ابتدا همان ابزار را اجرا کنید؛ محیط‌هایی که Identity را با متغیر محیطی تنظیم می‌کنند باید `ConnectionStrings__DefaultConnection` و `Jwt__Key`/`Jwt__Issuer`/`Jwt__Audience` را برای سرویس جدید هم تنظیم کنند.
- Jwt:Key، Issuer و Audience باید با Identity برابر بمانند؛ متغیرهای محیطی و آرگومان‌های خط فرمان بر فایل local اولویت دارند.
- روی سرور IIS، Identity همان `http://127.0.0.1:5001/` و Service Request روی **پورت داخلی 5002** اجرا شود. Gateway موجود `api.zumaris.com:443` از قبل به 5002 مسیر دارد.
- ابتدا Identity را با نسخه جدید منتشر کنید؛ endpoint محافظت‌شده `GET /api/auth/session` برای بررسی نشست اضافه شده است. سپس Service Request را با پروفایل **IISProduction** به پوشه و Application Pool مستقل (No Managed Code) منتشر کنید؛ فایل‌ها را داخل پوشه Identity یا Gateway نریزید. تنظیمات سایت عمومی و گواهی Gateway تغییر نمی‌کنند.
- نسخه Development سرویس جدید روی پورت 5106 و Identity روی 5105 است؛ Gateway Development هم به 5106 هدایت می‌کند. در IIS محیط Production باشد.
- سرویس جدید ابتدا امضا، الگوریتم HS256، صادرکننده، مخاطب، زمان و claimهای sub/sid را بررسی می‌کند و سپس با همان Bearer Token از Identity تأیید فعال بودن نشست می‌گیرد. بنابراین Logout/revoke در Identity روی درخواست بعدی این سرویس هم اثر می‌گذارد.
- پاسخ 401/403 از Identity به معنی نشست غیرمجاز است؛ قطع ارتباط، timeout یا پاسخ غیرمنتظره Identity باعث 503 بدون ذخیره درخواست می‌شود. Identity باید در دسترس باشد. endpoint تأیید نشست از محدودکننده عمومی 30 درخواست Identity مستثناست؛ امضا و نشست همچنان اجباری‌اند.
- HTTP برای ارتباط داخلی فقط با مقصد loopback مجاز است؛ مقصد شبکه‌ای Identity باید HTTPS با تأیید عادی گواهی باشد. تشخیص HTTPS پشت Gateway فقط برای پروکسی loopback مورد اعتماد است.

```powershell
dotnet publish src/Services/ServiceRequest/ServiceRequest.Api/ServiceRequest.Api.csproj -c Release -p:PublishProfile=IISProduction
```

برای اجرای توسعه، Identity، ServiceRequest.Api و YarpGateway را در Multiple startup projects روی Start و پروفایل http تنظیم کنید.

## درخواست نمونه

در Swagger گیت‌وی، ابتدا در **Identity Service** لاگین کنید؛ سپس **Service Request Service** را انتخاب و Access Token را در Authorize وارد کنید. این endpoint فقط Bearer می‌پذیرد؛ Cookie احراز هویت نیست و هدر CSRF مربوط به endpointهای Identity است.

```json
{
  "propertyId": "11111111-1111-4111-8111-111111111111",
  "categoryId": "22222222-2222-4222-8222-222222222222",
  "areaTypeId": "33333333-3333-4333-8333-333333333333",
  "description": "شیر آب آشپزخانه نشتی دارد و نیاز به تعمیر و بررسی دارد.",
  "urgency": "Standard",
  "preferredDate": "2030-01-02",
  "preferredStartTime": "10:00:00",
  "preferredEndTime": "12:00:00"
}
```

شناسه‌های نمونه را با شناسه‌های واقعی جایگزین کنید و تاریخ آینده مناسب بگذارید. تاریخ ورودی میلادی ISO است، ساعت‌ها محلی در Asia/Tehran هستند و TimeZoneId همراه درخواست ثبت می‌شود. زمان‌های CreatedAtUtc و تاریخچه UTC هستند. ساعت‌ها فقط دقت ثانیه دارند.

اعتبارسنجی:

- توضیح پس از Trim، 20 تا 500 کاراکتر.
- Urgency رشته Standard، Priority یا Emergency است؛ عدد و مقدار ناشناخته رد می‌شوند. در صورت حذف Urgency، Standard پیش‌فرض است.
- برای Standard/Priority هر سه مقدار زمان‌بندی اجباری‌اند. Emergency می‌تواند هر سه را null/حذف کند، یا یک بازه کامل معتبر بدهد.
- زمان شروع باید آینده باشد و پایان در همان روز بعد از شروع باشد؛ بازه عبوری از نیمه‌شب فعلاً پشتیبانی نمی‌شود. زمان محلی مبهم/نامعتبر رد می‌شود.

## Retry و پاسخ‌ها

هدر اختیاری `Idempotency-Key` با 8 تا 128 کاراکتر ASCII از حروف/اعداد و `-_.` بفرستید؛ برای مثال UUID. هر عملیات جدید کلید جدید، Retry همان عملیات همان کلید و بدنه را داشته باشد. کلید به کاربر محدود است؛ تکرار هم‌زمان در دیتابیس با unique index کنترل می‌شود. همان کلید و بدنه نرمال‌شده، شناسه قبلی را می‌دهد و هدر `Idempotency-Replayed: true` دارد. بدنه متفاوت با همان کلید 409 است. بدون کلید، هر POST یک درخواست مستقل است.

پاسخ موفق 201 با envelope مشترک:

```json
{
  "statusCode": 201,
  "title": "Created",
  "payload": {
    "requestId": "شناسه ایجادشده",
    "status": "Submitted",
    "createdAtUtc": "زمان UTC",
    "version": 1
  }
}
```

400: بدنه/قواعد نامعتبر؛ 401: توکن/نشست نامعتبر؛ 409: کلید تکراری با بدنه متفاوت؛ 503: تأیید نشست Identity در دسترس نیست. خطاهای داخلی دیتابیس به‌صورت پاسخ عمومی 500 برمی‌گردند و جزئیات در لاگ سرور بررسی می‌شوند.

## تصاویر

فایل‌های واقعی در مسیر خصوصی `D:\ZumarisData\Uploads` (قابل تنظیم) ذخیره و فقط مسیر نسبی در دیتابیس ثبت می‌شود. جزئیات JPEG/PNG، سقف 8 تصویر، مجوز پوشه و آپلود با Version در [راهنمای تصاویر](service-request-photos.md) آمده است.

## بررسی بعد از اجرای SQL توسط شما

با یک کاربر ثبت‌شده Login کنید، POST را با Bearer و Idempotency-Key بفرستید و 201 بگیرید. در service_requests یک ردیف Submitted و در history یک رویداد اولیه باید وجود داشته باشد. همان کلید/بدنه را دوباره بفرستید: شناسه همان و تعداد ردیف‌ها ثابت باشد. بدنه متفاوت باید 409 بدهد. پس از Logout، همان Bearer نباید ثبت جدید انجام دهد. در محیط آزمایشی، شکست درج history باید درج درخواست را هم rollback کند.
