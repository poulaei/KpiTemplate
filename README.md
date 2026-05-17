# Mini KPI Framework (C#)

این نسخه با **C# / ASP.NET Core Minimal API** ساخته شده و:
- تعریف شاخص را از JSON می‌گیرد
- KPIها را auto compute می‌کند
- داشبورد و API برای visualization می‌دهد

## اجرا

```bash
dotnet run
```

بعد باز کنید:
- `http://127.0.0.1:8080` → Dashboard
- `http://127.0.0.1:8080/api/kpis` → JSON Result

## ساختار JSON

فایل `kpi_spec.json`:
- `data`: داده خام آرایه‌ای
- `kpis`: لیست شاخص‌ها با `name` و `formula`
- `dashboard`: عنوان و نوع نمودار

### فرمول‌های پشتیبانی‌شده
- `sum(arrayName)`
- `avg(arrayName)`
- عملیات ساده دوطرفه: `left + right`, `left - right`, `left * right`, `left / right`
- ارجاع به KPIهای قبلی (مثلاً `profit / total_cost`)

> نکته: برای سادگی و امنیت، فرمول‌ها محدود و کنترل‌شده هستند.
