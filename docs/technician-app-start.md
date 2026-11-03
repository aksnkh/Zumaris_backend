> این سند پیش‌نویس قبلی است. تصمیم‌های نهایی در [گردش تأییدشده](order-workflow-confirmed.md) و وضعیت اجرای آن در [راهنمای پیاده‌سازی](technician-order-workflow.md) ثبت شده‌اند؛ پیشنهادهای تأییدنشدهٔ این فایل الزام نیستند.

# شروع اپلیکیشن تکنسین

مرجع محصول: [Zumaris business plan](references/Zumaris-BP.pdf)، صفحات PDF شمارهٔ ۴، ۱۳ و ۶۷–۶۸؛ بخش Trades Passport. این سند پیشنهاد دامنهٔ کار است و به معنی اجرای قابلیت‌های پیشنهادی نیست.

## فیلدهای موجود

در Provider و جدول providers: id (شناسهٔ حرفه‌ای)، user_id (حساب Identity با رابطهٔ یک‌به‌یک فعلی)، display_name، photo_url، rating، completed_jobs، is_verified و is_active.

در provider_category_qualifications: provider_id، category_id و is_qualified. هر تکنسین می‌تواند چند دستهٔ کاری داشته باشد. فعال بودن حساب حرفه‌ای، تأییدشدن و صلاحیت همان دسته برای ارسال Quote لازم است. امتیاز و تعداد کار در مدل ذخیره می‌شوند، اما موتور محاسبهٔ امتیاز یا شمارش خودکار تکمیل پروژه هنوز از این مدل نتیجه نمی‌شود.

## ورود

ورود از POST /identity/api/auth/login با userName و password و هدر X-Zumaris-CSRF: 1 انجام می‌شود. پاسخ payload شامل accessToken، expiresAtUtc، userName، userId و refreshTokenExpiresAtUtc است؛ refresh token در کوکی HttpOnly قرار می‌گیرد، نه در بدنه. استفادهٔ موبایل/کلاینت باید مدیریت کوکی را رعایت کند. کلید BFF برای ورود عادی لازم نیست و نباید کلید محرمانه در اپ قرار گیرد.

ثبت حساب فعلی POST /identity/api/auth/register با username، password و email است. ایجاد حساب Identity به‌تنهایی پروفایل Provider یا صلاحیت حرفه‌ای نمی‌سازد. ورود موفق به معنی اجازهٔ فعالیت حرفه‌ای نیست. رمز عبور در Provider ذخیره نمی‌شود.

## پیشنهاد فیلدهای فاز اول؛ هنوز اجرا نشده

- مشخصات حرفه‌ای در Provider: businessName، businessType (فرد مستقل یا شرکت)، bio، yearsOfExperience، businessPhone، businessEmail و در صورت نیاز companyRegistrationNumber.
- محدودهٔ فعالیت در Provider: basePostcode، serviceRadiusKm و در فاز بعد پوشش چندمنطقه‌ای؛ نشانی دقیق پیش‌فرض عمومی نشود.
- مهارت و صلاحیت: categoryIds، skills و وضعیت تأیید هر دسته؛ به جای ثبت مستقیم isQualified توسط اپ، درخواست و مدرک ثبت شود.
- فرایند تأیید: onboardingStatus مانند Draft، PendingReview، Approved، Rejected و Suspended، به‌همراه دلیل و زمان بررسی؛ این وضعیت‌های پیشنهادی هنوز در API موجود نیستند.
- مدارک در رکوردهای جدا: نوع مدرک، شماره، مرجع صادرکننده، تاریخ صدور/انقضا، شناسهٔ فایل خصوصی و وضعیت بررسی. برای Gas صلاحیت مناسب Gas Safe باید واقعاً بررسی شود.
- بیمه در رکوردهای جدا: insuranceType، insurerName، policyNumber، coverageAmount، currencyCode، validFrom، validTo و سند خصوصی.
- دسترس‌پذیری در Provider: availability و ظرفیت کاری، پس از تعریف قواعد رزرو و تداخل در ServiceRequest.

نام شخصی، تلفن و ایمیل حساب در Identity می‌مانند. نام تجاری و راه ارتباطی حرفه‌ای در Provider می‌توانند جدا باشند. سوابق کار از رویدادهای واقعی ServiceRequest و پرداخت/تسویه از Billing تأمین شوند؛ اتصال مستقیم دیتابیس سرویس‌ها مجاز نیست. نظرها و نمونه‌کار حرفه‌ای در Provider قرار می‌گیرند؛ Trust Score و Response Time بدون دادهٔ واقعی تولید نشوند.

## APIهای پیشنهادی برای مرحلهٔ بعد؛ فعلاً موجود نیستند

GET /provider/api/providers/me برای وضعیت عمومی پروفایل خود تکنسین، POST /provider/api/providers/me برای درخواست ایجاد پروفایل، PUT /provider/api/providers/me برای ویرایش فیلدهای مجاز و endpointهای ارسال مدارک و درخواست بررسی. فیلدهای تأیید، امتیاز، تعداد کار و تعلیق تحت کنترل سرور/مدیر باشند. API عمومی فعلی quote-profile نیازمند categoryId و تأیید و صلاحیت است؛ برای تشخیص ورودِ تکنسین در انتظار بررسی مناسب نیست.

## جریان کار موجود

پس از تأیید حرفه‌ای: GET quote-context، POST پیشنهاد، GET /api/quotes/sent و GET جزئیات پیشنهاد در ServiceRequest موجودند. مشتری در ServiceRequest پیشنهاد را می‌پذیرد و سفارش Assigned می‌شود. فهرست کارهای قابل دریافت، کارهای تخصیص‌یافتهٔ تکنسین و API اجرای کار مخصوص تکنسین نیازمند بررسی و پیاده‌سازی مستقل‌اند؛ دانستن شناسهٔ سفارش به معنی وجود سامانهٔ dispatch نیست.

در این مرحله فقط مرجع و پیشنهاد دامنه نگهداری شده‌اند؛ کد ورود، دیتابیس و APIها تغییر نکرده‌اند.
