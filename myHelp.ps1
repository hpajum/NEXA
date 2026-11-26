# ساخت و جایگزینی NEXA روی ویندوز

همهٔ بلوک‌ها را از ریشهٔ مخزن NEXA در PowerShell اجرا کنید. فرمان‌ها از سیاست اجرای همان فرایند PowerShell استفاده می‌کنند. روی سیستم سازمانی مطابق سیاست داخلی عمل کنید.

## ۱. ساخت نسخهٔ جدید در پوشهٔ جداگانه

```powershell
$nexaPackage = Join-Path (Get-Location).Path 'Client\dist\modern'
powershell.exe -NoProfile -ExecutionPolicy Bypass -File '.\Client\build.ps1' -OutputDirectory $nexaPackage
if ($LASTEXITCODE -ne 0) { throw 'NEXA build failed.' }
```

خروجی در `Client\dist\modern` ساخته می‌شود. وزیرمتن و متن مجوزها همراه بسته‌اند و نیازی به نصب فونت در ویندوز نیست.

## ۲. اجرای آزمایشی بدون سرور

اگر NEXA قدیمی در حالت آزمایشی در حال اجراست، از منوی آیکون آن کنار ساعت «بستن برنامه» را بزنید.

```powershell
& '.\Client\dist\modern\NEXA.exe' --preview
```

## ۳. جایگزینی نسخهٔ نصب‌شده

پیش از جایگزینی، از منوی آیکون NEXA کنار ساعت «بستن برنامه» را انتخاب کنید. بستن پنجره با دکمهٔ ×، فرایند را کنار ساعت نگه می‌دارد.

```powershell
$nexaPackage = Join-Path (Get-Location).Path 'Client\dist\modern'
powershell.exe -NoProfile -ExecutionPolicy Bypass -File '.\Client\install.ps1' -PackageDirectory $nexaPackage
if ($LASTEXITCODE -ne 0) { throw 'NEXA installation failed.' }
```

اسکریپت فایل‌ها را در `%LOCALAPPDATA%\NEXA\App` جایگزین می‌کند. پس از پایان نصب از منوی Start، NEXA را باز کنید.

## ۴. جایگزینی نسخه‌ای که مستقیم از پوشهٔ پروژه اجرا می‌شود

اگر NEXA را مستقیم از پوشهٔ پروژه اجرا می‌کنید، بعد از بستن آن از کنار ساعت، به جای گام ۳ این بلوک را اجرا کنید. اگر مسیر اجرایی فعلی `Client\dist\update` است، مقدار `$nexaTarget` را روی همان پوشه بگذارید.

```powershell
$ErrorActionPreference = 'Stop'
$nexaDist = (Resolve-Path -LiteralPath '.\Client\dist').Path
$nexaPackage = Join-Path $nexaDist 'modern'
$nexaTarget = $nexaDist
$nexaFiles = @('NEXA.exe', 'NEXA.exe.config', 'Vazirmatn-OFL.txt', 'winforms-chat-MIT.txt')
foreach ($nexaFile in $nexaFiles) {
    $nexaSource = Join-Path $nexaPackage $nexaFile
    if (!(Test-Path -LiteralPath $nexaSource -PathType Leaf)) {
        throw "Build output is missing: $nexaSource"
    }
}
foreach ($nexaFile in $nexaFiles) {
    Copy-Item -LiteralPath (Join-Path $nexaPackage $nexaFile) -Destination $nexaTarget -Force
}
& (Join-Path $nexaTarget 'NEXA.exe')
```
