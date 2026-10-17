# اجرای Gateway کنار Identity روی IIS

Identity طبق اجرای تأییدشده روی همان سرور در `http://127.0.0.1:5001` است. مقدار `127.0.0.1` برای اتصال محلی MySQL و مقصد داخلی Identity حفظ می‌شود. این مراحل را روی سرور ویندوز اجرا کنید؛ Codex به IIS سرور دسترسی ندارد.

## ۱. دریافت کد و انتشار Identity

در Visual Studio از شاخه `main`، Pull بگیرید و Identity را دوباره Publish کنید. این نسخه هدرهای `X-Forwarded-Proto` و `X-Forwarded-For` را فقط از پروکسی محلی IPv4/IPv6 می‌پذیرد. بنابراین درخواست HTTPS گیت‌وی در مسیر داخلی HTTP به‌اشتباه HTTP تشخیص داده نمی‌شود و Redirect و Cookie صحیح باقی می‌مانند. این تنظیم برای Gateway و Identity روی یک ماشین است؛ پروکسی‌های شبکه‌ای دیگر به‌طور پیش‌فرض مورد اعتماد نیستند.

اگر قبلاً اسکریپت `database/identity/001_auth_sessions.sql` را روی `zumaris_identity` اجرا نکرده‌اید، قبل از Login/Refresh آن را اجرا کنید. این تغییر Gateway به SQL جدیدی نیاز ندارد.

## ۲. خروجی Gateway

روی پروژه `YarpGateway` راست‌کلیک و **Publish** را انتخاب کنید. پروفایل **IISProduction** را انتخاب یا Import کنید:

`src/Gateways/YarpGateway/Properties/PublishProfiles/IISProduction.pubxml`

این پروفایل خروجی Release برای .NET 8، بدون بسته‌بندی Runtime و با محیط Production تولید می‌کند. روی سرور ASP.NET Core Hosting Bundle نسخه .NET 8 لازم است. همه فایل‌های پوشه خروجی، از جمله `web.config` و فایل‌های JSON، باید با هم کپی شوند.

دستور معادل از ریشه ریپوزیتوری:

```powershell
dotnet publish src/Gateways/YarpGateway/YarpGateway.csproj -c Release -p:PublishProfile=IISProduction
```

خروجی پیش‌فرض: `src/Gateways/YarpGateway/bin/Release/net8.0/iis-publish/`.

## ۳. سایت جداگانه IIS

- پوشه‌ای مستقل مانند `C:\Sites\Zumaris.Gateway` ایجاد و خروجی Gateway را آنجا کپی کنید؛ داخل پوشه Identity کپی نکنید.
- Application Pool جداگانه مانند `Zumaris.Gateway` با تنظیم **No Managed Code** بسازید و مجوز Read & Execute پوشه را به هویت آن بدهید.
- یک Website مستقل متصل به همان پوشه و Pool بسازید. برای تست محلی Binding پیشنهادی **HTTPS روی پورت 5000** با گواهی معتبر برای نام میزبان انتخاب‌شده است. برای دسترسی کاربران واقعی، دامنه و HTTPS Binding مناسب سرور را استفاده کنید.
- پورت پیشنهادی 5000 را فقط اگر آزاد است استفاده کنید. پورت IIS از Binding می‌آید؛ مقادیر `launchSettings.json` برای اجرای Visual Studio هستند.
- محیط سایت باید **Production** باشد. پروفایل، آن را در `web.config` خروجی می‌نویسد. در Development مقصد Identity به پورت 5105 تغییر می‌کند و برای این سرور مناسب نیست.
- نیازی به نصب ARR برای YARP نیست؛ Reverse Proxy بخشی از خود برنامه است.

## ۴. تنظیم مقصد و آزمایش

در `appsettings.json` خروجی Gateway این مقصد از قبل تنظیم است:

```json
"identity-cluster": {
  "Destinations": {
    "identity-srv": {
      "Address": "http://127.0.0.1:5001"
    }
  }
}
```

ابتدا از خود سرور، Swagger مستقیم Identity را روی پورت 5001 بررسی کنید. سپس سایت HTTPS Gateway را باز کنید؛ Swagger UI در مسیر اصلی است. مسیر `/identity/swagger/v1/swagger.json` باید سند Identity را برگرداند و `/identity/api/auth/...` به Identity هدایت می‌شود.

در Swagger گزینه **Identity Service** را انتخاب و در **Authorize** مقدار CSRF را `1` بگذارید. Login را با کاربر ثبت‌شده اجرا کنید، سپس `payload.accessToken` را در Bearer وارد کنید. Refresh باید در همان مرورگر از Cookie استفاده کند. مسیرهای دقیق و قرارداد کلاینت در `docs/jwt.md` آمده‌اند.

برای جریان Login/Refresh از HTTPS استفاده کنید؛ Cookie در محیط Production دارای Secure است. نام میزبان را میان درخواست‌ها عوض نکنید؛ Cookie مربوط به localhost به آدرس IP یا دامنه دیگری منتقل نمی‌شود.

## عیب‌یابی

- **502**: Identity متوقف است، Binding محلی پورت 5001 درست نیست، یا محیط Gateway اشتباهاً Development است.
- **500**: متن خطای Identity و Gateway را در لاگ سرور بررسی کنید؛ پاسخ 500 به‌تنهایی علت را ثابت نمی‌کند.
- **400 در POST/DELETE**: هدر `X-Zumaris-CSRF: 1` و Response body را بررسی کنید.
- **401 در Refresh**: Cookie، ثبات نام میزبان، HTTPS و اجرای اسکریپت نشست‌ها را بررسی کنید؛ استفاده دوباره از Refresh Token مصرف‌شده کل نشست را باطل می‌کند.
- **Redirect به آدرس داخلی**: نسخه جدید Identity و محیط Production را بررسی کنید. فقط هدر پروکسی محلی مورد اعتماد است.
- فعلاً Service Request را انتخاب نکنید؛ سرویس آن هنوز به ریپوزیتوری اضافه نشده است.

خروجی Release و هدرهای پروکسی در محیط توسعه Codex بررسی می‌شوند؛ موفقیت استقرار روی IIS و Login/Refresh متصل به MySQL سرور باید پس از کپی خروجی توسط شما تأیید شود.
