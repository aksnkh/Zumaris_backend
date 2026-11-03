<div dir="rtl" align="right">

# سوابق خدمات

این بخش در همان سرویس ServiceRequest و چهارچوب Clean Architecture پیاده‌سازی شده است؛ پروژه، دیتابیس سرویس یا جدول History تکراری ایجاد نشده است. مدل‌ها و قواعد در Application، خواندن پارامتری MySQL در Infrastructure و کنترلر در API قرار دارند. اشارهٔ سند ورودی به SQL Server به MySQL پروژه تطبیق داده شده است. این پیاده‌سازی از query مستقیم استفاده می‌کند و stored procedure جدید لازم ندارد.

## راه‌اندازی

۱. قبل از انتشار نسخهٔ جدید، فایل زیر را **یک بار** در اسکیما `zumaris_service_request` اجرا کنید. اسکریپت‌های قبلی ۰۰۱ و ۰۰۲ باید قبلاً اجرا شده باشند. این فایل ستون‌ها، محدودیت‌ها و ایندکس‌های جدید را به جدول موجود اضافه می‌کند؛ داده‌ای حذف نمی‌کند و نباید دوباره اجرا شود. ALTER ممکن است روی جدول بزرگ زمان‌بر باشد.

<div dir="ltr" align="left">

```text
database/service-request/003_service_history.sql
```

</div>

۲. پروژهٔ ServiceRequest.Api را منتشر کنید و فایل‌های خروجی را در سایت همین سرویس روی IIS قرار دهید؛ فایل تنظیمات محلی و مسیر عکس‌های سرور را حفظ کنید. Application Pool مربوط به ServiceRequest را Recycle کنید. تغییر در Identity یا Gateway لازم نیست؛ پیشوند فعلی Gateway همین مسیرهای جدید را عبور می‌دهد.

۳. در Swagger گزینهٔ Service Request Service را انتخاب کنید. مسیرهای جدید در بخش ServiceHistory دیده می‌شوند. هر دو مسیر، Bearer token معتبر Zumaris و نشست فعال Identity لازم دارند؛ کلید BFF لازم نیست.

## فهرست سوابق

<div dir="ltr" align="left">

```http
GET https://api.zumaris.com/service-request/api/service-history?status=All&pageNumber=1&pageSize=20
Authorization: Bearer <Zumaris access token>
```

</div>

فیلترهای مجاز: بدون مقدار، All، Completed، Cancelled و Paid. حروف کوچک و بزرگ و فاصلهٔ اطراف مقدار پذیرفته می‌شوند. All همهٔ درخواست‌ها را شامل می‌شود؛ Paid مستقل از وضعیت خدمت، روی وضعیت پرداخت اعمال می‌شود. فیلتر ناشناخته خطای ۴۰۰ می‌دهد. شمارهٔ صفحه از ۱ شروع می‌شود؛ اندازهٔ صفحه بین ۱ تا ۱۰۰ است و پیش‌فرض آن ۲۰ است.

خروجی مستقیماً شامل `items`، `pageNumber`، `pageSize`، `totalCount` و `totalPages` است؛ پوشش `payload` ندارد. برای فهرست خالی، تعداد کل و تعداد صفحه صفر است. شمارش و خواندن صفحه در یک snapshot تراکنشی انجام می‌شود. ترتیب بر اساس تاریخ واقعی خدمت، و در نبود آن تاریخ ایجاد، نزولی است؛ شناسهٔ درخواست ترتیب رکوردهای هم‌تاریخ را قطعی می‌کند. گروه‌بندی ماهانه در مصرف‌کننده انجام می‌شود.

## جزئیات

<div dir="ltr" align="left">

```http
GET https://api.zumaris.com/service-request/api/service-history/{id}
Authorization: Bearer <Zumaris access token>
```

</div>

شناسه همان شناسهٔ درخواست است؛ `historyId` و `requestId` برابر هستند. نبود سابقه و درخواست متعلق به کاربر دیگر هر دو پاسخ ۴۰۴ دارند. شناسهٔ کاربر فقط از توکن خوانده می‌شود. خروجی، فیلدهای جزئیات سند را دارد. پاسخ‌ها ذخیره در cache را غیرفعال می‌کنند.

برای خط زمانی، API موجود زیر قابل استفاده است؛ endpoint اختیاری جدید تکراری اضافه نشده است:

<div dir="ltr" align="left">

```http
GET /service-request/api/service-requests/{id}/timeline
```

</div>

## داده‌های واقعی و فیلدهای هنوز نامشخص

- وضعیت داخلی Submitted در History به Pending تبدیل می‌شود؛ دادهٔ فعلی و endpointهای قبلی تغییر نمی‌کنند. بقیهٔ وضعیت‌ها همان Assigned، InProgress، Completed و Cancelled هستند.
- شمارهٔ درخواست به صورت قطعی از شناسهٔ کامل ساخته می‌شود: پیشوند SR- و شناسهٔ بدون خط تیره. به همین دلیل طول ستون ۴۰ انتخاب شده است، نه ۳۰؛ شمارهٔ درخواست با درخواست جدید یا قدیمی ثابت می‌ماند.
- نام خدمت، دسته، آیکن دسته، نوع فضای خانه، نام تکنسین و آدرس ملک به صورت snapshot اختیاری در همان درخواست آماده شده‌اند. سرویس‌های مرجع در ریپوزیتوری فعلی متصل نیستند؛ بنابراین این مقادیر تا ورود داده از یک اتصال معتبر، null هستند. نام یا آدرس ساختگی از روی GUID تولید نمی‌شود و هیچ join به دیتابیس سرویس‌های دیگر انجام نمی‌شود. API ثبت درخواست فعلی این فیلدهای تحت مالکیت سرور را از کاربر قبول نمی‌کند. اتصال آینده مسئول پرکردن و به‌روزرسانی این snapshotها هنگام تغییر شناسه‌های مرجع است.
- تاریخ واقعی خدمت اختیاری است و با تاریخ ترجیحی رزرو متفاوت است. برای جلوگیری از نمایش تاریخ ساختگی، `serviceDate` در لیست نیز nullable است؛ این تفاوت با نوع غیرnullable در سند باید در کلاینت رعایت شود. تاریخ‌ها UTC هستند.
- زمان تکمیل، زمان لغو و علت لغو از خط زمانی موجود خوانده می‌شوند؛ برای رکورد فاقد رویداد مربوط، null هستند. API جدیدی برای تغییر وضعیت عملیات اضافه نشده است.
- پرداخت مستقل نگهداری می‌شود: Unpaid، Pending، Paid، Refunded، Failed. رکوردهای موجود در migration مقدار اولیهٔ Unpaid می‌گیرند؛ این مقدار به معنی بررسی سابقهٔ مالی واقعی نیست. اگر پرداخت قبلی دارید باید قبل از استفادهٔ مالی، با منبع معتبر تطبیق داده شود.
- مبلغ نهایی و ارز نامشخص null هستند. مبلغ صفر یا ارز پیش‌فرض حدس زده نمی‌شود. مبلغ غیرمنفی باید همراه ارز سه‌حرفی باشد. API پرداخت و ثبت مبلغ در این مرحله اضافه نشده است؛ بنابراین فیلتر Paid فقط داده‌هایی را نشان می‌دهد که بعداً توسط اتصال معتبر مالی به‌روز شده باشند.
- هیچ API حذف، ثبت یا ویرایش مستقل برای History ایجاد نشده است. حذف فیزیکی داده‌ها در این تغییر انجام نمی‌شود.

## اعتبارسنجی

تست‌های HTTP، احراز هویت، نشست غیرفعال، استخراج مالک از توکن، دسترسی به جزئیات، فیلترها، داده‌های nullable، صفحه‌بندی، خروجی خالی و سند Swagger را با repository جایگزین بررسی می‌کنند. اجرای migration و queryها روی MySQL واقعی در این محیط انجام نشده است؛ هیچ تغییری روی دیتابیس سرور اعمال نشده است.

</div>

## Completing reference names

`service_requests.category_id` is the service/category identifier in the current product model. There is no separate serviceId or service table: history `serviceName` is an alias of the resolved `categoryName` (for example both are `Plumbing`). The independent legacy `service_name` text is not treated as a separate authoritative service name.

Both list and detail complete missing reference fields using the owner service APIs:

- Category name and current icon: ServiceCatalog `GET /api/categories/{categoryId}`. A category name is usable even when iconUrl is null.
- `serviceArea`: the matching `areaTypeId` from ServiceCatalog `GET /api/areas`.
- `propertyAddress`: Property `GET /api/properties/{propertyId}`, using the caller's Bearer token and normal property access rules.
- `technicianName`: Provider `GET /api/providers/{providerId}/summary`, which exposes only the verified professional's display identity. It does not expose personal contact or login details; verified inactive providers can still be named on historical work.

Stored historical category/area/address/technician snapshots take precedence; missing snapshots are enriched from the current records. serviceName always equals the resulting categoryName. CategoryIconUrl uses current icon metadata with a stored-icon fallback. Reference calls are deduplicated per page, limited to four concurrent calls, and share a three-second budget. No cross-schema SQL, raw credentials or service-account impersonation is used. These GETs do not modify stored history.

List items now include the real propertyId, areaTypeId and technicianId as well as categoryId; detail also includes areaTypeId. If editing an order changes one of its reference IDs, its corresponding stale snapshot columns are cleared by the existing update transaction, so future history does not display the previous category/area/address.

A missing related record, lost property permission or an unavailable reference service can still leave its optional value null; stored snapshots remain available. A missing assigned technician, actual service date, payment amount or currency is not filled with fabricated values. Preferred appointments are not actual service dates, and quote totals are not settled payment totals.

Publish ServiceRequest.Api and Provider.Api for this change. Existing Property/ServiceCatalog APIs and Gateway routes suffice; no new SQL migration is required. Configure ServiceRequest `Property:BaseUrl` as `http://127.0.0.1:5003/` in Production (5107 in Development); existing ServiceCatalog and Provider base URLs remain in use. Resolve relative icon URLs against the API origin in the frontend. Use the real user's JWT when testing protected reference lookups.
