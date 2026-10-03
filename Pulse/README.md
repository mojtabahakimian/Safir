# Pulse — «نبض سازمان»

داشبوردِ مدیریتیِ صفحه‌ی `/pulse` (جای پنجره‌های NABZEFROOSH، NABZEDARY و NABZEMALI ِ WPF)،
با React 19 + Motion + d3-shape.

```bash
cd Pulse
npm ci
npm run build      # یا npm run watch هنگامِ کار
```

خروجی یک فایلِ ESM است: `Client/wwwroot/js/pulse/pulse.js` (+ `pulse.css`). این خروجی **در git هست**
تا build ِ .NET و CI به Node نیاز نداشته باشند؛ پس بعد از هر تغییر در `src/`:

1. `npm run build`
2. نسخه‌ی `Bundle` در `Client/Pages/Pulse/PulsePage.razor` (`?v=…`) را بالا ببرید تا مرورگرها نسخه‌ی تازه را بگیرند
3. هر سه را با هم commit کنید (سورس، خروجی، نسخه)

| فایل | نقش |
|---|---|
| `src/main.jsx` | `mount(el, data, dotnet)` و `unmount(el)` — Blazor همین‌ها را صدا می‌زند |
| `src/App.jsx` | چیدمانِ صفحه، بازه‌ی ۷/۳۰/۹۰ روز، مقایسه با دوره‌ی قبل |
| `src/charts.jsx` | نمودارِ روند، فعالیت‌ها، مالی، نقشه‌ی حرارتی، روزهای هفته، پرفروش‌ترین روزها |
| `src/ui.jsx` | کارتِ شاخص، شمارنده، تب‌ها، خطِ نبض |
| `src/pulse.css` | همه‌ی کلاس‌ها با `p-` یا `.pulse`؛ تمِ تیره با `html.dark-theme` |

داده را `Server/Pulse/PulseService.cs` می‌سازد (`GET /api/pulse`): ۱۸۰ روزِ تقویمی، روزِ بی‌گردش صفر.
زمان در نمودارها از راست (قدیم) به چپ (امروز) است.
