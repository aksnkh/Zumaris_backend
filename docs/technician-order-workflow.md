# گردش سفارش مشتری، تکنسین و مدیر — راهنمای پیاده‌سازی

مبنای محصول: [تصمیم‌های نهایی کاربر](order-workflow-confirmed.md) و [سند مرجع](references/Zumaris-BP.pdf). این تغییر فقط بک‌اند است؛ هیچ صفحهٔ اپلیکیشن ساخته نشده است. APIهای قبلی درخواست، Quote و تاریخچه حفظ شده‌اند. قواعد تخصص با تصمیم جدید خوداظهاری تطبیق یافته‌اند.

## مالکیت و رفتار

- Identity: ورود و نشست موجود؛ تشخیص ادمین از جدول جدید platform_administrators. نقش ادمین از بدنه، تخصص، توکن انتخابی کاربر یا تنظیم Provider استنتاج نمی‌شود. مسیر GET /identity/api/auth/session/admin-access با JWT و نشست فعال، برای ادمین 204 و برای کاربر عادی 403 می‌دهد. اعطا/لغو فقط با SQL مدیریتی در Identity است؛ JWT جدید لازم نیست و لغو در درخواست بعدی مؤثر است.
- Provider: یک پروفایل به ازای هر حساب، نام حرفه‌ای، تخصص‌های خوداظهاری، مدارک خصوصی، بررسی ادمین و نظر مشتری. وضعیت پروفایل Draft / PendingReview / Approved / Rejected است. approved فقط پس از حداقل یک مدرک و تصمیم ادمین ممکن است؛ تعداد/نوع اجباری مدارک توسط محصول مشخص نشده و الزام جدیدی برای نوع خاص سند اضافه نشده است. هر فایل kind دارد و ادمین کفایت مدارک را ارزیابی می‌کند. تغییر نام حرفه‌ای یا افزودن مدرک تأیید را باطل می‌کند؛ تغییر تخصص‌ها تأیید جداگانه نمی‌خواهد. is_active/is_verified/rating/completed_jobs ورودی پروفایل نیستند.
- ServiceRequest: سفارش Submitted متعلق به دیگران به تمام تکنسین‌های فعال و تأییدشده با تخصص مرتبط، هنگام خواندن فهرست نشان داده می‌شود؛ صف دعوت یا تصمیم موردی ادمین لازم نیست. این نمایش مبتنی بر GET است، نه push notification. قبل از پذیرش، فهرست اطلاعات محدود برای قیمت‌گذاری می‌دهد؛ نام/شناسه مشتری، آدرس کامل و عکس خصوصی درخواست منتشر نمی‌شود. جریان فعلی quote-context / ثبت Quote / Accept / Decline / Withdraw حفظ شده است.
- ServiceRequest: فقط کاربر تکنسین در Quote پذیرفته‌شده می‌تواند کار را شروع یا کامل کند. شروع Assigned → InProgress، بدون version ورودی و با رویداد دقیقاً یک‌بار؛ تکرار شروع رویداد اضافی تولید نمی‌کند. اتمام InProgress → Completed به report غیرخالی و حداقل یک عکس معتبر نیاز دارد. گزارش حداکثر ۱۰۰۰۰ کاراکتر و عکس ۱ تا ۸ عدد، هرکدام JPEG/PNG تا ۵ MiB است؛ همان decode، حذف metadata و ذخیرهٔ خصوصی موجود استفاده می‌شود. وضعیت، گزارش، عکس‌ها و رویداد اتمام در یک تراکنش ثبت می‌شوند؛ شکست پیش از commit باعث پاک‌سازی فایل‌های جدید می‌شود. ارسال مجدد اتمام پس از Completed، 409 است.
- serviceDate زمان واقعی اتمام UTC است؛ preferredDate و ساعت ترجیحی مشتری تغییر نمی‌کنند. مبلغ نهاییِ قابل پرداخت پس از اتمام از همان Quote پذیرفته‌شده ثبت می‌شود؛ هنوز Unpaid است، نه Paid. مشتری و تکنسین منتخب به گزارش و عکس نتیجه دسترسی دارند؛ دیگران حتی با دانستن شناسه دسترسی ندارند.
- Provider: فقط مشتری مالک سفارش Completed می‌تواند برای همان تکنسین نظر ثبت کند؛ پرداخت شرط نیست. rating عدد ۱ تا ۵ و comment غیرخالی تا ۲۰۰۰ کاراکتر است. تکرار همان نظر همان نتیجه را می‌دهد؛ تغییر نظر ثبت‌شده 409 است. امتیاز از میانگین نظرهای مبتنی بر کار واقعی محاسبه می‌شود. CompletedJobs در quote-profile از آمار ServiceRequest گرفته می‌شود؛ هنگام عدم دسترسی، snapshot معتبر موجود یا null برمی‌گردد. پیشنهادهای قبلی بازنویسی نمی‌شوند.
- Billing: فقط مرحلهٔ کلی پرداخت پس از اتمام: Payment Intent با مبلغ و واحد پول Quote قطعی از ServiceRequest، به نام مشتری مالک کار. requestId تنها ورودی است. تکرار درخواست یک رکورد می‌دهد. gateway=PayPal و status=AwaitingGateway؛ canPay=false و checkoutUrl=null. ثبت درخواست پرداخت، پرداخت واقعی نیست و تاریخچهٔ سرویس را Paid نمی‌کند. جزئیات اتصال PayPal، webhook، charge، تسویه و Ticket طبق تصمیم کاربر فعلاً پیاده‌سازی نشده‌اند.

## APIهای جدید

همهٔ مسیرها JWT واقعی و نشست فعال می‌خواهند. خطاهای ورودی انگلیسی و دارای message و errors هستند. توکن آزمایشی 1 در این سرویس‌ها معتبر نیست. مقادیر کاربر مثل شرح و نظر ترجمه نمی‌شوند.

| سمت | متد و مسیر عمومی | کاربرد |
| --- | --- | --- |
| تکنسین | GET /provider/api/providers/me | پروفایل خود، مدارک، تخصص‌ها، وضعیت و revision بررسی |
| تکنسین | POST یا PUT /provider/api/providers/me | ایجاد/ویرایش نام حرفه‌ای و تخصص‌های خوداظهاری |
| تکنسین | POST /provider/api/providers/me/documents | multipart: kind و file |
| تکنسین | GET /provider/api/providers/me/documents/{documentId} | دانلود خصوصی مدرک خود |
| تکنسین | GET /provider/api/providers/me/eligibility | هویت حرفه‌ای مجاز و categoryIds |
| مدیر | GET /provider/api/admin/providers | صف پروفایل‌های دارای مدرک و تأییدنشده |
| مدیر | GET /provider/api/admin/providers/{id} | جزئیات برای بررسی |
| مدیر | GET /provider/api/admin/providers/{id}/documents/{documentId} | دانلود خصوصی مدرک برای بررسی |
| مدیر | POST /provider/api/admin/providers/{id}/verification | تأیید/رد هویت و مدارک؛ بدون تأیید دسته‌ها |
| تکنسین | GET /service-request/api/technician/opportunities | فرصت‌های متناسب با همهٔ تخصص‌های مجاز خودش |
| تکنسین | GET /service-request/api/technician/jobs | فهرست کارهای خود؛ فیلتر status اختیاری |
| تکنسین | GET /service-request/api/technician/jobs/{id} | جزئیات سفارش تخصیص‌یافته |
| تکنسین | POST /service-request/api/technician/jobs/{id}/start | شروع؛ بدون بدنه |
| تکنسین | POST /service-request/api/technician/jobs/{id}/complete | multipart: report و photos؛ هردو الزامی |
| مشتری/تکنسین منتخب | GET /service-request/api/service-requests/{id}/execution | جزئیات اجرای کار، گزارش و مسیر عکس‌ها |
| مشتری/تکنسین منتخب | GET /service-request/api/technician/jobs/{id}/photos/{photoId} | دانلود مجاز عکس اولیه یا نتیجه |
| مشتری و سرویس‌های مرجع با توکن او | GET /service-request/api/service-requests/{id}/completed-work | منبع معتبر مالکیت، تکنسین و مبلغ کار Completed |
| کاربر واردشده | GET /service-request/api/technician/providers/{providerId}/work-stats | فقط تعداد کار تکمیل‌شدهٔ حرفه‌ای؛ بدون اطلاعات مشتری |
| مشتری | POST /provider/api/service-requests/{requestId}/review | نظر برای کار Completed؛ مستقل از پرداخت |
| کاربر واردشده | GET /provider/api/providers/{providerId}/reviews | نظرهای مبتنی بر کار؛ بدون شناسهٔ مشتری |
| مشتری | POST /billing/api/payments | ثبت مرحلهٔ کلی پرداخت؛ PayPal هنوز متصل نیست |
| مشتری | GET /billing/api/payments/{id} | فقط رکورد پرداخت متعلق به خودش |

pageNumber مثبت و pageSize از ۱ تا ۱۰۰ است؛ پیش‌فرض ۱ و ۲۰. برای jobs مقدار status یکی از Assigned/InProgress/Completed/Cancelled است. مسیر id سفارش با id پیشنهاد و providerId متفاوت است.

### نمونه‌ها

پروفایل:
```json
{"displayName":"Professional name","categoryIds":[1,2]}
```

تصمیم مدیر: revision را از GET جزئیات تازهٔ پروفایل بردارید. این مقدار فقط برای جلوگیری از تأیید مدارکی است که حین بررسی عوض شده‌اند؛ پروفایل و شروع/اتمام کار version ورودی ندارند.
```json
{"revision":2,"approved":true,"reason":null}
```

نظر مشتری:
```json
{"rating":5,"comment":"The repair was completed successfully."}
```

درخواست مرحلهٔ پرداخت:
```json
{"requestId":"REAL-COMPLETED-REQUEST-GUID"}
```

## دیتابیس؛ اجرای دستی توسط root/admin

ابتدا پیش‌نیازهای فعلی هر سرویس را داشته باشید. اسکریپت‌های جدید روی ساختار یکسان قابل تکرارند، داده‌ای حذف یا تأیید ساختگی ایجاد نمی‌کنند و migration خودکار هنگام startup اجرا نمی‌شود:

1. Identity: database/identity/004_platform_administrators.sql بعد از اسکریپت‌های فعلی Identity.
2. Provider: database/provider/001_provider_quote_profiles.sql (اگر قبلاً اجرا نشده) سپس database/provider/002_onboarding_reviews.sql.
3. ServiceRequest: database/service-request/006_technician_execution.sql بعد از 001/002/003/005 و تبدیل شناسه‌های عددی 004 برای نصب قدیمی. اسکریپت قدیمی 003 یک‌بارمصرف است و نباید روی ستون‌های موجود دوباره اجرا شود.
4. Billing: database/billing/001_payment_intents.sql.

اسکریپت reset_service_requests.sql مربوط به این تغییر نیست؛ آن را اجرا نکنید.

با حساب root و شناسهٔ واقعی کاربر Identity، نقش مدیر را اعطا کنید. پیش از اجرا وجود و فعال‌بودن حساب را در identity_users بررسی کنید. هیچ ادمین یا تکنسین آزمایشی seed نمی‌شود:
```sql
USE zumaris_identity;
-- Only after confirming the real Identity account:
-- INSERT INTO platform_administrators(user_id) VALUES ('REAL-IDENTITY-USER-GUID');
```

مجوزهای runtime برای حساب واقعی، مثلاً app@localhost؛ رمز در اسکریپت قرار نگیرد:
```sql
GRANT SELECT ON zumaris_identity.platform_administrators TO 'app'@'localhost';
GRANT SELECT, INSERT, UPDATE ON zumaris_provider.* TO 'app'@'localhost';
GRANT SELECT, INSERT, UPDATE ON zumaris_service_request.* TO 'app'@'localhost';
GRANT SELECT, INSERT, UPDATE ON zumaris_billing.* TO 'app'@'localhost';
```
این مجوزها علاوه بر مجوزهای قبلی Identity هستند. DDL یا DELETE برای این قابلیت لازم نیست. مدارک در جدول خصوصی Provider با سقف ۲۰ فایل و هر فایل حداکثر ۵ MiB نگهداری می‌شوند؛ PDF/JPEG/PNG مجاز و دانلود فقط attachment است. فایل‌های نتیجهٔ کار در Storage:RootPath خصوصی فعلی ServiceRequest می‌مانند، نه داخل wwwroot.

پروفایل‌های قدیمی که فقط is_verified=1 داشته‌اند ولی مدرک و سابقهٔ بررسی ندارند، برای Quote جدید واجد شرایط نیستند؛ مدارک واقعی را ارسال و در پنل/API مدیر بررسی کنید. is_qualified قدیمی اکنون نشان‌دهندهٔ تخصص اعلامی است، نه تصمیم ادمین؛ تنظیم تخصص از Save Profile آن را به‌روز می‌کند.

## محیط و انتشار IIS

| سرویس | Development | IIS Production |
| --- | --- | --- |
| Identity | 5105 | 5001 |
| ServiceRequest | 5106 | 5002 |
| Provider | 5111 | 5007 |
| Billing | 5112 | 5008 |

Identity، Provider، ServiceRequest، Billing و YarpGateway باید منتشر شوند. Gateway مسیر /billing و Swagger آن را اضافه کرده؛ مسیرهای جدید Provider و ServiceRequest از مسیرهای فعلی عبور می‌کنند. هر سرویس Application Pool مستقل با No Managed Code و Hosting Bundle .NET 8 داشته باشد.

- appsettings.Local.json و JWT واقعی هر سرویس را حفظ کنید. JWT.Key/Issuer/Audience برای اعتبارسنجی توکن‌های Identity یکسان‌اند؛ ConnectionStrings.DefaultConnection هر سرویس فقط به دیتابیس خودش اشاره می‌کند.
- Provider: Identity:BaseUrl و ServiceRequest:BaseUrl. در Production به‌ترتیب http://127.0.0.1:5001/ و http://127.0.0.1:5002/؛ Development برابر 5105 و 5106.
- ServiceRequest: Provider:BaseUrl برابر http://127.0.0.1:5007/ و در Development برابر 5111؛ تنظیمات فعلی Identity، ServiceCatalog و Storage حفظ شوند.
- Billing: Identity:BaseUrl و ServiceRequest:BaseUrl مطابق بالا؛ DefaultConnection به zumaris_billing. scripts/Initialize-BillingLocalConfig.ps1 برای ساخت تنظیمات محلی از فایل فعال Identity موجود است؛ فایل موجود را بازنویسی نمی‌کند و SQL اجرا نمی‌کند. می‌توان فایل Local را مستقیماً با مقادیر معتبر نوشت.
- URL سرویس‌های مرجع فقط HTTPS یا HTTP loopback، با اسلش پایانی و بدون credentials مجاز است؛ redirect دنبال نمی‌شود. خطای مرجع، دسترسی ادمین یا صلاحیت را باز نمی‌کند.
- هویت ادمین از API Identity بررسی می‌شود؛ در نبود سرویس مرجع، تصمیم مدیر انجام نمی‌شود. مدارک و داده‌های حساس no-store هستند.

ساخت و Publish هر سرویس از پروژهٔ خودش با Release/IISProduction انجام شود؛ نمونهٔ Billing:
```powershell
dotnet publish src/Services/Billing/Billing.Api/Billing.Api.csproj -c Release -p:PublishProfile=IISProduction
```
خروجی کامل شامل DLLهای Application و Infrastructure و تنظیمات محیطی لازم است؛ صرفاً DLL اصلی را جایگزین نکنید. SQL ابتدا و انتشار سرویس‌ها پس از آن انجام شود. تنظیمات واقعی IIS، حساب‌های root/app و درگاه سرور کاربر در این محیط اجرا یا تغییر داده نشده‌اند.

## بررسی پذیرش

۱. تکنسین با Identity ثبت‌نام/ورود کند، پروفایل و تخصص‌ها را ثبت و مدارک را ارسال کند. قبل از تأیید، opportunities و quote-profile مجاز نیستند.
۲. ادمین واقعی مدارک را ببیند و آخرین revision را تأیید کند؛ حساب عادی به API مدیر 403 می‌گیرد. تغییر مدارک حین بررسی موجب 409 برای revision قدیمی است.
۳. تکنسین تمام سفارش‌های Submitted دیگران در دسته‌های خودش را ببیند؛ تغییر تخصص به تأیید مدیر نیاز ندارد. آدرس کامل و عکس خصوصی قبل از پذیرش افشا نشوند.
۴. همان جریان Quote موجود تا Accept اجرا شود؛ فقط منتخب به jobs دسترسی داشته باشد. start تکراری رویداد اضافی نداشته باشد.
۵. complete بدون report یا photos، یا با فایل خراب، 400 و message انگلیسی بدهد؛ وضعیت و فایل‌ها نیمه‌کاره ثبت نشوند. complete معتبر Completed و گزارش و عکس و مبلغ قطعی را ثبت کند؛ paymentStatus همچنان Unpaid باشد.
۶. مشتری بدون پرداخت بتواند نظر بدهد. مشتری دیگر یا تکنسین نتواند به‌جای او نظر بدهد؛ تکرار نظر یکسان رکورد جدید نسازد.
۷. Billing قبل از اتمام یا برای کار شخص دیگر، درخواست را رد کند. پس از اتمام مبلغ را از سرور بگیرد؛ مبلغ ورودی مشتری پذیرفته نشود. درخواست تکراری همان intent را بدهد و هیچ‌گاه Paid یا checkout ساختگی نسازد.

تست‌های MySQL فقط با اتصال loopback و نام‌های ایزولهٔ zumaris_quotes_test / zumaris_provider_test / zumaris_billing_test / zumaris_identity_test اجرا می‌شوند؛ اتصال برنامه یا دیتابیس کاربر را به تست‌ها ندهید.


## نتیجهٔ بررسی در محیط توسعه

Build راه‌حل و Publish با Release/IISProduction برای Identity، Provider، ServiceRequest، Billing و YarpGateway موفق بود. تست‌های سراسری در زمان بررسی ۲۵۳ موفق و ۱۱ تست اختیاریِ فاقد اتصال مخصوص خودشان skip داشتند. تست جدید SQL ادمین جداگانه با MySQL اجرا و موفق شد. تست‌های جدید Swagger برای سه سرویس نیز پس از آن موفق شدند؛ Swagger درخواست‌ها مسیرهای قدیمی را حفظ کرده است.

سناریوی MySQL مربوط به اجرای کار، ارسال هم‌زمان دو complete را هم بررسی می‌کند: فقط یک درخواست موفق، یک گزارش/عکس نتیجه، یک گذار اتمام و پاک‌سازی فایل درخواست بازنده. دیتابیس‌های تست، محلی و موقتی بودند؛ هیچ SQL در دیتابیس کاربر یا استقرار روی IIS کاربر انجام نشد.


### رفع NETSDK1152 هنگام Publish سرویس Billing

وجود Billing.Api.deps.json یا Billing.Api.runtimeconfig.json در ریشهٔ سورس Billing.Api باعث می‌شد Web SDK آن‌ها را به‌عنوان محتوای JSON در کنار نسخهٔ تولیدشده در bin جمع‌آوری کند. پروژه اکنون فایل‌های خروجی *.deps.json و *.runtimeconfig.json واقع در ریشهٔ سورس را از Content/None خارج می‌کند؛ فایل‌های واقعی Build همچنان از SDK منتشر می‌شوند. کنترل تشخیص خروجی تکراری غیرفعال نشده است. PublishUrl پروفایل IISProduction نیز با PublishDir هماهنگ است و خروجی در bin/Release/net8.0/iis-publish باقی می‌ماند.
