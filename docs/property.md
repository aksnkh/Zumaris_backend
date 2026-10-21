<div dir="rtl" align="right">

# مدیریت ملک

پیاده‌سازی فرم Add New Property و فهرست My Properties در سرویس Property، با دات‌نت ۸، Clean Architecture، MySQL و query پارامتری مستقیم انجام شده است. stored procedure یا ORM جدیدی اضافه نشده است. نقش انتخاب‌شده فقط اظهار کاربر است؛ ثبت نقش Owner به معنی تأیید رسمی مالکیت یا دسترسی مدیر سامانه نیست.

## راه‌اندازی دیتابیس و تنظیمات

۱. فایل زیر را با حساب مدیر دیتابیس اجرا کنید. اسکریپت، اسکیما و چهار جدول properties، property_memberships، property_user_preferences و property_membership_history را می‌سازد. Identity و ServiceRequest تغییر نمی‌کنند. اجرای مجدد همان ساختار با IF NOT EXISTS مجاز است؛ تعریف ناسازگار قبلی را اصلاح نمی‌کند.

<div dir="ltr" align="left">

```text
database/property/001_properties.sql
```

</div>

حساب برنامه باید روی `zumaris_property` مجوزهای SELECT، INSERT و UPDATE داشته باشد. مجوز را مدیر دیتابیس برای حساب و Host واقعی تنظیم کند. هیچ رمز یا دستور GRANT فرضی در اسکریپت قرار نگرفته است.

۲. برای آماده‌سازی روی کامپیوتر توسعه، از ریشهٔ ریپوزیتوری اجرا کنید:

<div dir="ltr" align="left">

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Initialize-PropertyLocalConfig.ps1
```

</div>

اسکریپت رشتهٔ اتصال و تنظیم JWT موجود Identity را می‌خواند، نام دیتابیس را به zumaris_property تغییر می‌دهد و در فایل git-ignored زیر ذخیره می‌کند. کلید JWT، issuer و audience باید با Identity یکسان باشند. اطلاعات اتصال و کلیدها چاپ نمی‌شوند؛ اسکریپت به دیتابیس وصل نمی‌شود و فایل موجود را بازنویسی نمی‌کند.

<div dir="ltr" align="left">

```text
src/Services/Property/Property.Api/appsettings.Local.json
```

</div>

برای تنظیم مستقیم پوشهٔ منتشرشدهٔ سرور، مثلاً از پوشهٔ حاوی اسکریپت، با مسیرهای واقعی خودتان اجرا کنید:

<div dir="ltr" align="left">

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\Initialize-PropertyLocalConfig.ps1 -IdentitySettingsPath C:\Zumaris\Identity\appsettings.json -OutputDirectory C:\Zumaris\Property -DatabaseServer 127.0.0.1
```

</div>

پوشهٔ خروجی باید وجود داشته باشد. اگر تنظیمات مؤثر Identity در environment یا فایل دیگری هستند، مقادیر واقعی JWT/اتصال را در فایل محلی Property تنظیم کنید؛ اسکریپت فقط فایل JSON مشخص‌شده را می‌خواند. متغیرهای محیطی و آرگومان‌های اجرا بر فایل محلی اولویت دارند. پارامتر DatabaseServer اختیاری است؛ بدون آن سرور دیتابیس همان مقدار فایل Identity باقی می‌ماند.

## انتشار IIS

۳. پروژه‌های Property.Api و YarpGateway را با پروفایل IISProduction منتشر کنید. فایل محلی تنظیمات Property در خروجی انتشار حفظ و کپی می‌شود؛ هنگام انتشارهای بعدی نسخهٔ تنظیمات مخصوص سرور را نگه دارید.

۴. برای Property یک سایت و Application Pool مستقل ایجاد کنید؛ Pool روی No Managed Code و Binding داخلی روی HTTP پورت 5003 باشد. مسیر فیزیکی باید پوشهٔ دارای Property.Api.dll باشد. نسخهٔ دات‌نت ۸ Hosting Bundle مثل سرویس‌های قبلی لازم است. پورت را فقط برای ارتباط داخلی نگه دارید.

۵. هر دو Pool را Recycle کنید. در Swagger عمومی گزینهٔ Property Service ظاهر می‌شود. سرویس‌های فعلی با پورت‌های قبلی 5001 و 5002 باقی می‌مانند. Property در محیط Production به Identity روی پورت 5001 وصل می‌شود؛ در Development به پورت 5105. پورت توسعهٔ Property همان 5107 است.

برای بررسی مستقیم روی سرور:

<div dir="ltr" align="left">

```text
http://127.0.0.1:5003/swagger/v1/swagger.json
```

</div>

## قرارداد API

تمام endpointهای زیر به Bearer token معتبر Zumaris و نشست فعال Identity نیاز دارند. کلید BFF لازم نیست. شناسهٔ کاربر از توکن خوانده می‌شود و در بدنهٔ فرم پذیرفته نمی‌شود. آدرس پایهٔ عمومی:

<div dir="ltr" align="left">

```text
https://api.zumaris.com/property
```

</div>

| متد | مسیر نسبت به آدرس پایه | کاربرد |
| --- | --- | --- |
| POST | `/api/properties` | ثبت ملک و ارتباط کاربر با نقش اظهارشده |
| GET | `/api/properties?pageNumber=1&pageSize=20` | فهرست ملک‌های فعال مجاز کاربر، ملک جاری در ابتدای فهرست |
| GET | `/api/properties/{id}` | جزئیات ملک مجاز |
| PUT | `/api/properties/{id}` | ویرایش کامل فرم و نقش اظهارشده، همراه version |
| DELETE | `/api/properties/{id}?version=1` | غیرفعال‌سازی؛ بدون حذف فیزیکی |
| GET | `/api/properties/current` | ملک جاری کاربر؛ در صورت نبود، پاسخ 404 |
| PUT | `/api/properties/{id}/current` | انتخاب ملک جاری؛ بدون بدنه و بدون تغییر version خود ملک |
| GET | `/api/property-options` | گزینه‌های نوع ملک، نقش و کشورها |

نمونهٔ ثبت مطابق تصویر:

<div dir="ltr" align="left">

```json
{
  "addressLine": "12 Example Road",
  "postcode": "NW11 8HB",
  "city": "London",
  "countryCode": "GB",
  "latitude": 51.556,
  "longitude": -0.192,
  "propertyType": "Terraced",
  "bedrooms": 3,
  "bathrooms": 2,
  "yearBuilt": 1985,
  "ownershipRole": "Owner"
}
```

</div>

پاسخ ثبت 201 و مستقیم شامل PropertyDto است؛ پوشش payload ندارد. فیلدهای فرم به‌علاوهٔ propertyId، isCurrent، healthScore، equipmentCount، documentCount، createdAtUtc، updatedAtUtc و version برمی‌گردند. version اولیه 1 است. در ویرایش همان فرم کامل را همراه آخرین version بفرستید. پاسخ موفق ویرایش و انتخاب ملک جاری 200 و پاسخ حذف 204 بدون بدنه است.

فهرست، items، totalCount، pageNumber، pageSize و totalPages دارد. اندازهٔ صفحه بین 1 و 100 است. ملک جاری ابتدا و سپس بقیه بر اساس زمان ایجاد و شناسه نزولی مرتب می‌شوند. فهرست خالی پاسخ 200 با items خالی و تعداد صفر دارد. شمارش و صفحه در یک snapshot خوانده می‌شوند.

## قواعد فرم و دسترسی

- آدرس پس از حذف فاصلهٔ ابتدا و انتها 3 تا 300 و شهر 1 تا 100 کاراکتر است.
- کشور باید از گزینه‌های property-options انتخاب شود. کد بریتانیا GB است، نه UK. کدپستی برای GB الزامی و قالب آن بررسی می‌شود؛ برای سایر کشورها اختیاری و حداکثر 20 کاراکتر است. تأیید واقعی آدرس یا مالکیت از روی کدپستی انجام نمی‌شود.
- نوع ملک یکی از Terraced، SemiDetached، Detached، FlatApartment، Bungalow یا Other است. نقش یکی از Owner، Tenant، Landlord یا PropertyManager است. نوع ملک، نقش و تعداد اتاق‌خواب/حمام باید صریحاً ارسال شوند؛ مقدار عددی به جای enum پذیرفته نمی‌شود.
- تعداد اتاق‌خواب و حمام عدد صحیح بین 0 تا 100 است؛ استودیو می‌تواند صفر اتاق‌خواب داشته باشد. سال ساخت اختیاری است؛ اگر فرستاده شود باید مثبت و حداکثر سال جاری UTC باشد.
- مختصات اختیاری‌اند؛ هر دو با هم یا هر دو null باشند. عرض بین منفی 90 تا 90، طول بین منفی 180 تا 180 و حداکثر هفت رقم اعشار پذیرفته می‌شود.
- جست‌وجوی آدرس روی نقشه و تبدیل آدرس به مختصات در این CRUD اضافه نشده است؛ API آدرس نهایی و مختصات انتخاب‌شده را دریافت می‌کند. نام country در options از دادهٔ منطقه‌ای دات‌نت به زبان انگلیسی ارائه می‌شود.
- اولین ملک خودکار جاری می‌شود؛ ثبت ملک دوم، ملک جاری را عوض نمی‌کند. کاربر می‌تواند با endpoint انتخاب جاری، بین ملک‌های مجاز جابه‌جا شود. این انتخاب برای هر کاربر مستقل است و version ملک را افزایش نمی‌دهد.
- فقط ایجادکنندهٔ ملک حق ویرایش و غیرفعال‌سازی دارد. عضویت فعال مجوز خواندن و انتخاب جاری می‌دهد. در این مرحله API اشتراک‌گذاری، دعوت یا تصاحب ملک دیگران اضافه نشده است. ثبت آدرس یکسان توسط کاربر دیگر، شناسهٔ جدید می‌سازد و به ملک یا داده‌های شخص قبلی متصل نمی‌شود.
- ویرایش با نسخهٔ قدیمی یا رقابت تراکنشی پاسخ 409 دارد؛ جزئیات را دوباره بخوانید. version را حدس نزنید. ملک ناموجود یا فاقد عضویت مجاز 404 است؛ عضو غیرایجادکننده برای تغییر، 403 می‌گیرد.
- حذف، ملک و عضویت‌ها را غیرفعال و انتخاب جاری آن را پاک می‌کند. برای ایجادکننده، اگر ملک حذف‌شده جاری بود، جدیدترین ملک فعال دیگر انتخاب می‌شود؛ در نبود ملک دیگر، current وجود ندارد. ردیف ملک و تاریخچهٔ نقش‌ها باقی می‌مانند.
- امتیاز سلامت و تعداد تجهیزات و اسناد فعلاً null هستند؛ اعداد نمونهٔ 72، 8 و 12 از فیگما جعل نشده‌اند. این داده‌ها بعداً از سرویس مالکشان تأمین می‌شوند. View Passport کامل و Timeline ملک هنوز endpoint مستقل ندارند.
- در ثبت سفارش، می‌توان propertyId برگشتی را انتخاب و ارسال کرد. اتصال اعتبارسنجی وجود/دسترسی ملک از ServiceRequest به Property هنوز در این تغییر اضافه نشده است؛ بررسی فعلی ServiceRequest برای PropertyId همچنان صرفاً خالی‌نبودن است.
- CORS برای همان app.zumaris.com تنظیم شده است. خطای 401 برای توکن/نشست نامعتبر و 503 برای عدم دسترسی به بررسی نشست Identity است. پاسخ‌های اطلاعات ملک no-store هستند.

## بررسی انجام‌شده و تست MySQL

تست‌های HTTP با اعتبارسنج نشست جایگزین، CRUD، گزینه‌ها، اجباری‌بودن فیلدها، دسترسی کاربر، دادهٔ نامعتبر، انتخاب جاری، version و Swagger را بررسی می‌کنند. همچنین اسکریپت و repository و یک جریان HTTP متصل به repository واقعی روی MySQL 8 آزمایشی و جداگانه بررسی شدند؛ تست‌های هم‌زمانی، rollback، محدودیت‌های دیتابیس و حفظ تاریخچه نیز اجرا شده‌اند. هیچ SQL روی دیتابیس سرور کاربر اجرا نشده و انتشار واقعی Windows IIS انجام نشده است.

تست‌های MySQL فقط با متغیر زیر فعال می‌شوند؛ در اجرای عادی بدون آن به‌صورت صریح skipped هستند. فقط اتصال loopback به اسکیمای مخصوص تست پذیرفته می‌شود. تست، همان migration را با جایگزینی نام اسکیما روی محیط آزمایشی اجرا می‌کند؛ از دیتابیس واقعی برنامه استفاده نکنید. حساب تست باید مجوز ساخت جدول در همین اسکیمای آزمایشی داشته باشد.

<div dir="ltr" align="left">

```powershell
$env:ZUMARIS_PROPERTY_MYSQL_TEST_CONNECTION = 'Server=127.0.0.1;Port=<test-port>;Database=zumaris_property_test;User=<test-user>;Password=<test-password>'
dotnet test tests/Property.Tests/Property.Tests.csproj
```

</div>

</div>
