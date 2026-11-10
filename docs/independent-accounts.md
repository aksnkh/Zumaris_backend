# حساب‌های مستقل App، Tech و Admin

مدل قبلی «یک حساب مشترک با چند دسترسی» کنار گذاشته شده است. هر ساید در **Identity** جدول حساب مستقل دارد:

| ساید | جدول | ثبت‌نام | ورود |
|---|---|---|---|
| App | `app_accounts` | `POST /identity/api/app/register` | `POST /identity/api/app/login` |
| Tech | `tech_accounts` | `POST /identity/api/tech/register` | `POST /identity/api/tech/login` |
| Admin | `admin_accounts` | `POST /identity/api/admin/register` | `POST /identity/api/admin/login` |

ایمیل و نام کاربری در هر جدول جدا یکتا هستند. یک ایمیل می‌تواند در هر سه جدول با شناسه، رمز، نام، تلفن، عکس و نشست مستقل وجود داشته باشد. ساخت/ویرایش App هیچ حساب Tech یا Admin را ایجاد یا تغییر نمی‌دهد. ورود هر مسیر تنها جدول همان ساید را بررسی می‌کند؛ وجود همان ایمیل در ساید دیگر کافی نیست.

## قرارداد ورود

در مسیرهای جدید، ساید از URL مشخص است؛ `application` در body لازم نیست و ارسال فیلد اضافه پذیرفته نیست:

```http
POST https://api.zumaris.com/identity/api/tech/login
Content-Type: application/json
X-Zumaris-CSRF: 1
```

```json
{
  "userName": "person@example.com",
  "password": "USER_PASSWORD"
}
```

`userName` می‌تواند ایمیل یا نام کاربریِ همان ساید باشد. نام کاربری داخلی ثبت‌نام جدید در سرور ساخته می‌شود. پاسخ 200 از `Response<AuthResponse>` است و در payload، accessToken، تاریخ‌های انقضا، userId، userName، application، role و providerId وجود دارند. role برای App برابر Customer، برای Tech برابر Provider و برای Admin برابر Admin است. providerId فقط برای Tech پر می‌شود. JWT و نشست به همان جدول و حساب مقیدند؛ اطلاعات نقش یا شناسهٔ حساب از body پذیرفته نمی‌شود.

حسابی که فقط در App ثبت‌نام کرده با همان credentials از Tech/Admin پاسخ 401 می‌گیرد. ثبت‌نام مستقل همان ایمیل در Tech مجاز است، ولی شناسهٔ دیگری ایجاد می‌کند و باید واقعاً رمز حساب Tech را در ورود Tech ارائه کند.

## ثبت‌نام App

```http
POST /identity/api/app/register
Content-Type: application/json
X-Zumaris-CSRF: 1
```

```json
{
  "firstName": "John",
  "lastName": "Smith",
  "email": "person@example.com",
  "phone": "+447700900123",
  "password": "USER_PASSWORD"
}
```

پاسخ 201 شامل توکن/کوکی App است. اطلاعات شخصی در همان ردیف app_accounts ذخیره می‌شود. ثبت‌نام تکراری در App، حتی اگر ایمیل با حروف بزرگ ارسال شود، 409 است. همان ایمیل در Tech یا Admin مانع ثبت‌نام App نیست.

## ثبت‌نام Tech؛ بدون حساب یا توکن App

```http
POST /identity/api/tech/register
Content-Type: application/json
X-Zumaris-CSRF: 1
```

```json
{
  "firstName": "John",
  "lastName": "Smith",
  "email": "person@example.com",
  "phone": "+447700900123",
  "password": "TECH_PASSWORD",
  "acceptTermsAndPrivacy": true
}
```

این درخواست حساب مستقل tech_accounts می‌سازد، رضایت صریح را ثبت و پروفایل حرفه‌ای را از طریق ارتباط سروری با **Provider** ایجاد می‌کند. زمان درخواست enrolment در حساب Tech ذخیره می‌شود؛ نسخه/زمان رضایت حقوقی و پروفایل حرفه‌ای همچنان در Provider هستند. تخصص خودکار یا تأیید مدارک ساخته نمی‌شود.

پس از موفقیت، پاسخ 201 توکن **Tech** و providerId می‌دهد. دیگر مراحل «ورود App → Provider signup → ورود Tech» وجود ندارند. اطلاعات حرفه‌ای و مدارک در Provider و اطلاعات شخصی/رمز در tech_accounts هستند. هیچ تراکنش یا SQL بین دو schema اجرا نمی‌شود.

اگر حساب Tech ساخته شود اما سرویس Provider قطع باشد، پاسخ 503 با پیام ورود مجدد داده می‌شود و حساب پاک نمی‌شود. با همان credentials از `/identity/api/tech/login` وارد شوید؛ بک‌اند اگر پروفایل/رضایت Provider هنوز ناقص باشد، همان enrolment را به‌شکل قابل تکرار تکمیل می‌کند. ثبت‌نام را برای ساخت حساب دوم تکرار نکنید. قطع Provider مانع ورود App نیست.

مدارک هنوز شرط دریافت فرصت/Quote هستند، نه شرط ورود برای تکمیل پروفایل. کاربر با Tech وارد و Postcode، categoryIds و سه مدرک را کامل می‌کند. در صورت غیرفعال‌بودن پروفایل Provider، ورود Tech مجاز نمی‌شود.

## ثبت‌نام Admin

```http
POST /identity/api/admin/register
Content-Type: application/json
X-Zumaris-CSRF: 1
```

بدنه همان فرم App با پنج فیلد است. این API حساب مستقل admin_accounts می‌سازد؛ وجود همان ایمیل در App یا Tech مانع نیست و هیچ‌کدام از آن رکوردها به مدیر تبدیل نمی‌شوند.

ایجاد مدیر جدید فقط با توکن مدیر فعال مجاز است. برای اولین مدیر، همان مسیر `/api/admin/register` با `X-Zumaris-Bootstrap-Key` و تنظیم سروری `AdminBootstrap:Enabled=true` / `AdminBootstrap:Key` استفاده می‌شود. کلید تصادفی خصوصی حداقل ۳۲ بایت است؛ در کد عمومی فرانت قرار نگیرد. پس از راه‌اندازی آن تنظیم غیرفعال شود. علامت یک‌بارمصرف دیتابیس همچنان استفادهٔ دوباره را می‌بندد. جزئیات در [راهنمای ساخت مدیر](administrator-accounts.md).

پاسخ 201، `Response<AdministratorAccountResponse>` است و حساب ساخته‌شده را برمی‌گرداند؛ نشست مدیر فعلی را عوض نمی‌کند و برای حساب هدف توکن صادر نمی‌کند. حساب جدید با `/identity/api/admin/login` و credentials خودش وارد می‌شود. نقش Admin از وجود حساب فعال در admin_accounts می‌آید؛ جدول platform_administrators دیگر استفاده نمی‌شود.

## Refresh و Logout مستقل

```http
POST /identity/api/app/refresh
POST /identity/api/tech/refresh
POST /identity/api/admin/refresh

POST /identity/api/app/logout
POST /identity/api/tech/logout
POST /identity/api/admin/logout
```

ساید از مسیر تعیین می‌شود؛ query `application` لازم نیست. کوکی‌ها همچنان مجزا هستند: `zumaris_refresh_token_app`، `zumaris_refresh_token_tech` و `zumaris_refresh_token_admin`. هدر CSRF و credentials مرورگر لازم‌اند. در صورت ارسال Bearer، ساید آن هم باید با مسیر مطابق باشد. جابه‌جایی یک refresh token میان این مسیرها 403 می‌دهد و نقش دیگری نمی‌سازد. خروج 204 دارد و بدنهٔ JSON ندارد.

جلسه، accountId و application را ذخیره می‌کند. Refresh فقط همان accountId را در همان جدول می‌خواند؛ با ایمیل یا نام مشترک، حساب دیگری انتخاب نمی‌شود. اعتبارسنجی نشست نیز وجود حساب فعال در جدول مربوط را بررسی می‌کند. نشست‌های قدیمی با reset حذف می‌شوند.

## پروفایل و Google

مسیرهای مشترک `/identity/api/profile/me` و photo و sessions باقی‌اند و با توکن ساید، اطلاعات شخصی همان حساب مستقل را می‌دهند. اطلاعات در ستون‌های هر جدول حساب ذخیره می‌شوند. `identity_account_directory` فقط یک **View خواندنی** فنی برای پروفایل/اعتبارسنجی است؛ حساب چهارم یا رکورد مشترک ایجاد نمی‌کند و تمام نوشتن‌ها به جدول همان ساید می‌روند.

لینک Google با کلید `(application, provider, subject)` جداست. یک Google subject می‌تواند به سه حساب جدا لینک شود. تطبیق ایمیل، لینک یا ارتقای ضمنی ایجاد نمی‌کند. ورود اولیهٔ Google فقط می‌تواند App بسازد. برای Tech/Admin ابتدا ثبت‌نام مستقل و مجاز انجام و سپس Google صریحاً به همان حساب لینک شود. BFF همچنان سروری است و کلیدش در مرورگر قرار نمی‌گیرد.

## مسیرهای قبلی

برای جلوگیری از ایجاد حساب مشترک از مسیرهای قدیمی:

- `/identity/api/auth/register` و `/identity/api/auth/signup` فقط alias حساب **App** هستند.
- `/identity/api/auth/login` با application صریح باقی است، اما هر مقدار فقط جدول مستقل همان ساید را می‌خواند.
- `/identity/api/auth/refresh` و logout قدیمی نیز به ساید نشست و query application مقیدند.
- `/identity/api/admin/accounts` و bootstrap قبلی aliasهای محافظت‌شدهٔ ساخت Admin مستقل هستند.
- `/provider/api/providers/signup` فقط Tech را می‌پذیرد؛ حساب/توکن App دیگر از این مسیر Provider نمی‌سازد. مسیر اصلی جدید، ثبت‌نام Tech در Identity است و فراخوانی enrolment را خود بک‌اند انجام می‌دهد.

## ارتباط داخلی جدید Provider

`POST /api/internal/users/{userId}/signup` در Provider فقط برای Identity است. JWT سروری ۳۰ثانیه‌ای با audience `Zumaris.Provider.LoginAccess`، scope `provider-enrollment` و application=Tech لازم است و sub باید با userId مسیر برابر باشد. توکن کاربر و credential صرفاً lookup نمی‌توانند enrolment انجام دهند. این endpoint به نشست Identity برنمی‌گردد تا چرخهٔ وابستگی ایجاد نشود.

`Identity:BaseUrl` در Provider و `Provider:BaseUrl` در Identity و کلید/issuer مشترک JWT باید درست باشند. مهلت مرجع پنج ثانیه و redirect خودکار غیرفعال است. هیچ secret جدیدی برای مرورگر ساخته نشده است.

## پاک‌سازی و استقرار؛ بدون انتقال داده

کاربر حذف داده‌ها و Drop جدول‌های قدیمی را صریحاً مجاز دانسته است. به همین دلیل migration داده انجام نمی‌شود:

1. فایل **`database/identity/008_reset_independent_accounts.sql`** را با دسترسی مجاز روی `zumaris_identity` اجرا کنید. این فایل destructive است؛ حساب‌های قدیمی، نشست‌ها، Google links و setup/audit قدیمی را حذف و جداول جدید را می‌سازد. Stored Procedureهای ثبت‌نام/ورود قدیمی هم حذف می‌شوند و کد جدید به آن‌ها وابسته نیست. اجرای دوبارهٔ 008 دوباره همهٔ حساب‌های جدید را پاک می‌کند؛ اسکریپت عادی هر deployment نیست.
2. برای پاک‌سازی پروفایل‌ها، مدارک و رضایت‌های Provider متصل به شناسه‌های قدیمی، فایل مستقل **`database/provider/005_reset_for_independent_accounts.sql`** را بعد از وجود schema فعلی Provider 001..004 اجرا کنید. این فایل داده‌های Provider را truncate می‌کند و ساختار آن را تغییر نمی‌دهد.
3. **Identity و Provider** جدید را هماهنگ منتشر کنید. Gateway و سیاست‌های App/Tech/Admin در سایر سرویس‌ها تغییری لازم ندارند. فرانت هر ساید به register/login/refresh/logout مخصوص خودش وصل شود.
4. کاربران و اولین مدیر را دوباره ثبت کنید. تنظیم Bootstrap برای مدیر اول طبق راهنما لازم است.

**اسکریپت‌های Identity 001..007 مربوط به مدل تاریخی قبلی‌اند؛ پس از 008 اجرا نشوند.** 008 کل schema فعلی حساب را از ابتدا می‌سازد و به اجرای قبلی آن‌ها نیاز ندارد.

این resetها فقط Identity و Provider را هدف می‌گیرند. داده‌ها و فایل‌های Property، ServiceRequest و Billing یا عکس‌های قدیمیِ روی دیسک حذف/منتقل نمی‌شوند؛ اگر ریست کامل محیط لازم باشد باید آن سرویس‌ها نیز با اسکریپت مالک خودشان پاک‌سازی شوند. رکوردهای وابسته به شناسه‌های قدیمی خودکار به حساب جدید متصل نمی‌شوند.

هیچ‌کدام از اسکریپت‌ها در runtime یا روی دیتابیس سرور کاربر توسط ابزار توسعه اجرا نشده‌اند؛ آزمون SQL فقط در دیتابیس‌های disposable روی localhost انجام می‌شود.
