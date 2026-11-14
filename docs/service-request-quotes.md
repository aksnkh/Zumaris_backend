<div dir="rtl" align="right">

# پیشنهاد قیمت تکنسین و تصمیم مشتری

> مسیر کامل پروفایل، تأیید مدارک، خوداظهاری تخصص و اجرای کار در [راهنمای گردش تکنسین](technician-order-workflow.md) مستند شده است. تصمیم جدید محصول: تأیید هویت و مدارک با ادمین است؛ دسته‌های تخصصی با خوداظهاری تکنسین ثبت می‌شوند و تأیید مجزا نمی‌خواهند.

مالک Quote سرویس ServiceRequest است. هویت حرفه‌ای و صلاحیت تکنسین در Provider نگهداری و از API بررسی می‌شود. Identity حساب و نشست را تأمین می‌کند؛ Billing در این تغییر دخیل نیست. این ریپوزیتوری بک‌اند است و صفحهٔ Main/Quote Details در فرانت باید به APIهای این سند متصل شود.

## قواعد اصلی

- تکنسین باید با حساب Identity خودش وارد شود، در Provider پروفایل فعال و تأییدشده و صلاحیت فعال برای categoryId سفارش داشته باشد. طبق تصمیم محصول دعوت اختصاصی لازم نیست؛ چنین تکنسینی می‌تواند برای سفارش Pending که شناسه‌اش در اختیار اوست پیشنهاد بدهد، به‌جز سفارش متعلق به خودش. API عمومیِ فهرست تمام سفارش‌های مشتریان ایجاد نشده است؛ تأمین شناسهٔ کار توسط فرایند dispatch/پنل تکنسین موضوع جداگانه است.
- ProviderId، نام، تصویر حرفه‌ای، امتیاز، تعداد کار و Verified از Provider خوانده می‌شوند، نه از بدنهٔ ارسال پیشنهاد. Provider هیچ حق ثبت/تأیید صلاحیت از طریق API مشتری ندارد؛ داده‌های تأییدشده را مدیر سیستم با اسکریپت ثبت می‌کند.
- مبلغ نهایی در بک‌اند از Labour + Call-out محاسبه می‌شود. فقط GBP، حداکثر دو رقم اعشار، مبلغ غیرمنفی هر بخش و مجموع بیشتر از صفر و حداکثر 999999.99 پذیرفته می‌شود. 75 + 20 = 95. اعداد با DECIMAL ذخیره می‌شوند.
- متن قیمت و یادداشت بعد از ارسال ویرایش نمی‌شوند؛ تکنسین باید پیشنهاد Pending را Withdraw و پیشنهاد جدیدی ارسال کند. تغییر قیمتِ پیشنهاد Accepted یا افزودن هزینهٔ قطعات بدون موافقت جدید مشتری در این API وجود ندارد.
- حداکثر یک پیشنهاد Pending از هر Provider برای هر سفارش داریم. تکرار همان پیشنهاد Pending همان ID را برمی‌گرداند؛ مبلغ‌های معادل مانند 75 و 75.00 یکسان‌اند. بدنهٔ متفاوت تا زمان Withdraw پیشنهاد قبلی، 409 می‌گیرد. امکان وجود پیشنهاد چند تکنسین برای یک سفارش هست.
- Quote ورودی version ندارد. کنترل درخواستِ جاری و تغییر وضعیت در سرور و با قفل تراکنشی انجام می‌شود. نسخهٔ داخلی سفارش در پیشنهاد ثبت می‌شود؛ هر ویرایش/افزودن عکس/تغییر وضعیت سفارش که version را تغییر دهد، پیشنهاد Pending قبلی را غیرقابل قبول می‌کند. مشتری باید پیشنهاد تازه دریافت کند.
- Accepted فقط زمانی مجاز است که پیشنهاد Pending، منقضی‌نشده و مربوط به درخواست Pending بدون تغییر باشد. صلاحیت جاری Provider دوباره بررسی می‌شود. عدم دسترسی به Provider، 503 و عدم صلاحیت، 403 است؛ هیچ تخصیصی انجام نمی‌شود.
- Accept در یک تراکنش: Quote را Accepted، سفارش را Assigned، technician_id را Provider GUID و technician_name را snapshot نام تکنسین می‌کند؛ version سفارش افزایش می‌یابد و یک رویداد Pending→Assigned ذخیره می‌شود. پیشنهادهای Pending دیگر Withdrawn می‌شوند.
- قفل یکسان ردیف سفارش و unique index دیتابیس، پذیرش هم‌زمان دو پیشنهاد را کنترل می‌کنند: فقط یکی موفق می‌شود. Accept تکراری همان پیشنهاد، همان نتیجه را بدون رویداد/تخصیص مجدد می‌دهد. Decline تکراری همان پیشنهاد هم بدون تغییر مجدد موفق است. تلاش برای Accept بعد از Decline/Withdraw/Expiry یا پس از تغییر سفارش، 409 دارد.
- Decline فقط پیشنهاد را Declined می‌کند؛ سفارش Pending باقی می‌ماند. Withdraw فقط برای ارسال‌کنندهٔ پیشنهاد مجاز است. مشتری و سایر تکنسین‌ها نمی‌توانند پیشنهاد دیگری را Withdraw کنند.
- Accept پرداخت نیست: payment_status و total_amount مالیِ تاریخچه دست‌نخورده‌اند. پرداخت، بازپرداخت، کمیسیون و صورتحساب بعداً باید در Billing پیاده‌سازی شوند.

## APIهای ServiceRequest

آدرس پایه: `https://api.zumaris.com/service-request`

همهٔ مسیرها Bearer JWT واقعی و نشست فعال لازم دارند. توکن آزمایشی 1 مخصوص تست ServiceCatalog است و در Provider/ServiceRequest پذیرفته نیست. کلید BFF و CSRF برای APIهای این سرویس لازم نیست، چون احراز هویت صرفاً Bearer است. کاربر از sub توکن تعیین می‌شود.

| متد و مسیر | استفاده |
| --- | --- |
| `GET /api/service-requests/{requestId}/quote-context` | تکنسین واجد صلاحیت، اطلاعات محدود سفارش برای قیمت‌گذاری را می‌بیند؛ نام/شناسه مشتری و آدرس کامل برنمی‌گردد |
| `POST /api/service-requests/{requestId}/quotes` | تکنسین واجد صلاحیت پیشنهاد ارسال می‌کند |
| `GET /api/quotes?status=Pending` | کارت‌های Main مشتری؛ فقط پیشنهادهای متعلق به سفارش‌های خودش |
| `GET /api/quotes?status=Pending&propertyId={propertyId}` | پیشنهادها برای ملک انتخاب‌شده |
| `GET /api/quotes/sent` | پیشنهادهای ارسال‌شده توسط تکنسین فعلی |
| `GET /api/quotes/{quoteId}` | جزئیات برای مشتری مالک سفارش یا تکنسین همان پیشنهاد |
| `POST /api/quotes/{quoteId}/accept` | مشتری مالک سفارش می‌پذیرد؛ بدون بدنه و version |
| `POST /api/quotes/{quoteId}/decline` | مشتری مالک سفارش رد می‌کند؛ بدون بدنه |
| `POST /api/quotes/{quoteId}/withdraw` | تکنسین پیشنهاد خودش را پس می‌گیرد؛ بدون بدنه |

بدنهٔ ارسال:

```json
{
  "labourAmount": 75,
  "calloutAmount": 20,
  "currencyCode": "GBP",
  "providerNote": "Likely a flexible hose under the sink. Additional parts require approval.",
  "priceSummary": "Includes labour and call-out. Parts may be extra if required.",
  "expiresAtUtc": null
}
```

expiresAtUtc اختیاری است؛ null یعنی انقضای زمانی تعیین نشده است. در صورت ارسال باید UTC آینده با Z باشد. providerNote حداکثر ۲۰۰۰ و priceSummary حداکثر ۵۰۰ نویسه دارد. اگر شرح قیمت خالی باشد، متن انگلیسی پیش‌فرض اعلام می‌کند که labour/call-out شامل قیمت‌اند و قطعات اضافی نیاز به تأیید مشتری دارند. totalAmount، providerId، userId، verified، rating و status ورودی پذیرفته نمی‌شوند.

پاسخ ارسال 201 با Location سازگار با Gateway و envelope مشترک است؛ سایر پاسخ‌های موفق 200 هستند:

```json
{
  "statusCode": 200,
  "message": "Quote retrieved successfully.",
  "payload": {
    "id": "<quote GUID>",
    "requestId": "<request GUID>",
    "requestNumber": "<actual SR number>",
    "propertyId": "<property GUID>",
    "propertyAddress": "<stored address or null>",
    "categoryId": 1,
    "areaTypeId": 1,
    "categoryName": "Plumbing",
    "areaName": "Kitchen",
    "categoryIconUrl": "/service-catalog/api/categories/icons/1.svg",
    "requestDescription": "<actual request description>",
    "preferredDate": null,
    "preferredStartTime": null,
    "preferredEndTime": null,
    "timeZoneId": "Europe/London",
    "providerId": "<Provider GUID>",
    "providerName": "<verified business display name>",
    "providerPhotoUrl": null,
    "providerRating": null,
    "providerCompletedJobs": null,
    "providerVerified": true,
    "status": "Pending",
    "labourAmount": 75,
    "calloutAmount": 20,
    "totalAmount": 95,
    "currencyCode": "GBP",
    "providerNote": "<provider note>",
    "priceSummary": "<price inclusions>",
    "createdAtUtc": "<UTC timestamp>",
    "expiresAtUtc": null,
    "decidedAtUtc": null,
    "decisionReason": null
  }
}
```

فهرست‌ها payload شامل items/pageNumber/pageSize/totalCount/totalPages دارند. pageSize از ۱ تا ۱۰۰ و pageNumber مثبت است. فیلترهای Quote: All، Pending، Accepted، Declined، Withdrawn، Expired؛ فیلتر پیش‌فرض فهرست مشتری Pending است. فیلترها و DTO با status سفارش یا paymentStatus اشتباه نشوند.

نام و امتیاز، عکس، یادداشت، شرح درخواست، زمان‌بندی و مبالغ snapshot هنگام ارسال‌اند؛ تغییر بعدی آن‌ها سابقهٔ Quote را بازنویسی نمی‌کند. نام Category و Area و آیکن از API ServiceCatalog تکمیل می‌شوند؛ قطع آن سرویس مانع قیمت‌گذاری نمی‌شود و snapshot موجود سفارش یا null جایگزین می‌شود. امتیاز/تعداد کار در نبود دادهٔ معتبر null است، نه عدد ساختگی. ProviderVerified وضعیت تأیید در زمان ارسال را ثبت می‌کند؛ پذیرش علاوه بر آن تأیید جاری را بررسی می‌کند.

propertyAddress از snapshot موجود سفارش می‌آید و ممکن است برای سفارش‌های قبلی null باشد. مشتری می‌تواند با propertyId از API موجود Property اطلاعات ملک خودش را بخواند. این تغییر اسکیمای Property را مستقیم نمی‌خواند و آدرس ساختگی تولید نمی‌کند. آدرس کامل در پاسخ تکنسین برای پیشنهاد Pending/Withdrawn/Declined پنهان است؛ پس از Accepted در صورت وجود snapshot در دسترس تکنسین منتخب است.

وضعیت Expired و Withdrawn برای پیشنهادهای Pending با انقضا یا تغییر سفارش در زمان خواندن محاسبه می‌شود؛ اجرای background job لازم نیست. هنگام ارسال جایگزین، وضعیت قدیمی در جدول نیز نهایی می‌شود. Quoteهای Accepted سابقه‌اند؛ لغو بعدی سفارش آن‌ها را تبدیل به پرداخت/بازپرداخت نمی‌کند.

## ساخت Provider و صلاحیت‌ها

Provider اکنون پروفایل، مدارک خصوصی و بررسی مدیر دارد. تکنسین دسته‌های کاری را در پروفایل خود اعلام می‌کند؛ مدیر هویت و مدارک را تأیید می‌کند و تأیید تخصص جداگانه نداریم. ارسال Quote نیازمند پروفایل فعال و تأییدشده، مدارک بررسی‌شده و تخصص اعلام‌شدهٔ مرتبط است.

APIهای Provider:

```text
GET /provider/api/providers/me/quote-profile?categoryId=1
GET /provider/api/providers/{providerId}/quote-profile?categoryId=1
```

هر دو نیازمند JWT و نشست فعال‌اند. مسیر دوم هویت حرفه‌ایِ تأییدشده برای بررسی مشتری هنگام Accept را می‌دهد؛ ایمیل/تلفن/رمز مشتری یا تکنسین را منتشر نمی‌کند. نام، تصویر حرفه‌ای عمومی، امتیاز، تعداد کار و شناسه‌های حرفه‌ای را می‌دهد. در نبود پروفایل فعال/تأیید/صلاحیت، 404 برمی‌گرداند و ServiceRequest آن را به عدم مجوز تبدیل می‌کند.

ServiceRequest توکن همان فراخواننده را فقط در درخواست HTTPS یا HTTP loopback به Provider می‌فرستد؛ redirect خودکار غیرفعال است. timeout یا پاسخ خراب/نامعتبر باعث 503 می‌شود، نه پذیرفتن تکنسین. هیچ ProjectReference یا join بین اسکیمای Provider و ServiceRequest نیست.

## اسکریپت‌های دستی

۱. با root/admin اجرا کنید:

```text
database/provider/001_provider_quote_profiles.sql
database/service-request/005_service_request_quotes.sql
```

اسکریپت دوم به ساختار کامل فعلی سفارش (001/002/003 و شناسه‌های عددی 004 در نصب قدیمی) نیاز دارد. هر دو اسکریپت روی ساختار یکسان قابل تکرارند و داده‌های موجود را حذف نمی‌کنند. هیچ SQL روی دیتابیس شما خودکار اجرا نمی‌شود.

۲. تکنسین ابتدا حساب واقعی Identity داشته باشد و از API پروفایل و مدارک، سپس تأیید مدیر استفاده کند؛ مسیرهای کامل و اسکریپت 002 در [راهنمای گردش تکنسین](technician-order-workflow.md) آمده‌اند. پروفایل قدیمی بدون مدارک و بررسی ادمین برای Quote جدید کافی نیست.

Provider.photo_url در صورت موجودبودن آدرس عمومیِ عکس حرفه‌ای است؛ برای عکس خالی فرانت placeholder نمایش دهد. امتیاز از نظرهای کار انجام‌شده و تعداد کار از ServiceRequest گرفته می‌شود؛ سابقهٔ Quote snapshot است.

۳. حساب app برای Provider اکنون SELECT/INSERT/UPDATE نیاز دارد؛ مجوزهای کامل این قابلیت در راهنمای گردش تکنسین آمده‌اند. دسترسی DDL فقط برای اجرای دستی SQL توسط مدیر دیتابیس است.

ServiceRequest روی جدول Quote به SELECT/INSERT/UPDATE و روی جدول‌های سفارش/تاریخچه به مجوزهای DML قبلی نیاز دارد. DDL فقط برای root/admin است.

## IIS و تنظیمات

- Provider.Api را با پروفایل IISProduction در سایت و Pool جداگانهٔ No Managed Code منتشر کنید. پورت داخلی Production برابر **5007** و Development برابر **5111** است. .NET 8 Hosting Bundle لازم است.
- با مسیرهای واقعی، `Initialize-ProviderLocalConfig.ps1` را اجرا کنید. این اسکریپت JWT را از Identity و نام دیتابیس را به `zumaris_provider` تنظیم می‌کند، فایل موجود را حفظ می‌کند و SQL اجرا نمی‌کند. اگر فایل قبلاً وجود دارد، مقادیر مؤثر آن را بررسی کنید.
- Provider.Identity:BaseUrl در Production برابر `http://127.0.0.1:5001/` و در Development برابر پورت 5105 است.
- ServiceRequest.Provider:BaseUrl به `http://127.0.0.1:5007/` و در Development به 5111 تنظیم شده است. ServiceCatalog:BaseUrl موجود برای تکمیل نام/آیکن استفاده می‌شود. برای سرویس‌های روی ماشین دیگر HTTPS لازم است.
- ServiceRequest.Api و YarpGateway را هم منتشر کنید. Gateway مسیر جدید `/provider/{**catch-all}` و گزینهٔ Provider Service در Swagger دارد؛ مسیرهای Quote از catch-all موجود ServiceRequest عبور می‌کنند.
- مقادیر JWT/اتصال واقعی و فایل‌های appsettings.Local.json سرور حفظ شوند. Provider باید به zumaris_provider و ServiceRequest به zumaris_service_request وصل باشد؛ نام دیتابیس Identity را اشتباهاً کپی نکنید.
- Provider دارای API پروفایل، مدارک، تأیید مدیر و نظر مشتری است؛ admin UI و فرانت صفحات در این مخزن ساخته نشده‌اند.

## اتصال صفحهٔ Main و Quote Details

در Main، با JWT مشتری GET Quoteهای Pending و در صورت انتخاب ملک propertyId را بفرستید. نام/عکس تکنسین، createdAtUtc برای «sent ago»، وضعیت و totalAmount/currencyCode کارت را می‌سازند. این زمان نشان‌دهندهٔ online بودن تکنسین نیست.

View Details به GET همان quoteId متصل می‌شود. Amount، Labour، Call-out، یادداشت و Your Request از پاسخ خوانده می‌شوند. اگر rating/jobs/address/photo خالی بود، مقدار جعلی نشان ندهید. دکمه‌ها هنگام درخواست غیرفعال و درخواست بعدی تکراری با همان quoteId باشند. Accept/Decline نیازی به ساخت version یا idempotency key ندارند. پس از موفقیت، Main و جزئیات سفارش را refresh کنید. برای خطای 409 مجدداً Quote را بخوانید و `response.message` انگلیسی را نمایش دهید.

## تست

تست‌های API و MySQL، محاسبهٔ قیمت، منع overposting، دسترسی مالک/تکنسین، صلاحیت، خطای Provider، پیشنهاد تکراری، تغییر سفارش، انقضا، Decline/Withdraw، Accept تکراری، پذیرش هم‌زمان دو پیشنهاد، ثبت یک رویداد تخصیص و عدم تغییر وضعیت مالی را پوشش می‌دهند.

تست‌های واقعی فقط روی دیتابیس‌های loopback جدا به نام‌های `zumaris_quotes_test` و `zumaris_provider_test` با متغیرهای `ZUMARIS_QUOTES_MYSQL_TEST_CONNECTION` و `ZUMARIS_PROVIDER_MYSQL_TEST_CONNECTION` اجرا می‌شوند؛ هرگز اتصال دیتابیس برنامه را به تست ندهید. در نبود متغیرها تست‌های MySQL صریحاً skip می‌شوند. انتشار روی IIS و ثبت تکنسین‌های واقعی باید توسط شما انجام شود.

</div>


## Screen field mapping

All customer quote routes below use the ServiceRequest gateway prefix `/service-request` and require the owning customer's JWT. Successful quote data is in `response.payload`; customer messages are in `response.message`.

| Screen element | QuoteDto fields |
| --- | --- |
| Card / technician identity | `providerId`, `providerName`, `providerPhotoUrl` |
| Sent a quote / relative time | `createdAtUtc` (format relative time in the client) |
| Work heading | `categoryName`, `areaName`, `requestDescription` |
| Pending badge | `status` |
| Price / quoted amount | `totalAmount`, `currencyCode` |
| Request reference | `requestNumber`, `requestId` |
| Technician reputation | `providerRating`, `providerCompletedJobs`, `providerVerified` |
| Includes labour / parts disclaimer | `priceSummary` |
| Labour / call-out / total | `labourAmount`, `calloutAmount`, `totalAmount` |
| Provider note | `providerNote` |
| Your request | `requestDescription`, `propertyAddress` |
| Preferred visit | `preferredDate`, `preferredStartTime`, `preferredEndTime`, `timeZoneId` |

Use `GET /service-request/api/quotes?status=Pending&propertyId={propertyId}` for Main; omit propertyId for all properties. Cards are in `payload.items`. Use each card's `id` for `GET /service-request/api/quotes/{id}` and `POST /service-request/api/quotes/{id}/accept` or `/decline`. POST decisions have no body, version or idempotency key. Both return the updated QuoteDto in `payload`. Refresh Main after success. Hide or disable decision buttons once the quote is no longer Pending; the server remains authoritative and can return 409 when expired or changed.

Provider supplies the verified professional identity and category eligibility through its existing quote-profile endpoints; ServiceRequest owns quote creation, snapshots, customer listing, decisions and assignment. No additional table or SQL migration is required for these screens. Use the profile/document APIs and administrator review described in `docs/technician-order-workflow.md`; apply Provider scripts 001 and 002. Never invent ratings, verification, photographs or completed-job counts to match the mockup. Render absent optional values gracefully. There is no online-presence feature implied by “sent a quote”.
