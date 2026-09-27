using Microsoft.JSInterop;
using MudBlazor;

namespace Safir.Client.Services
{
    public enum AppThemeScheme
    {
        Classic,
        Safir,
        CostClose
    }

    public class ThemeService
    {
        private readonly IJSRuntime _jsRuntime;
        private bool _isDarkMode = false;
        private AppThemeScheme _currentScheme = AppThemeScheme.Classic;

        public event Action? ThemeChanged;
        public event Func<Task>? OnThemeChanged;

        // ─── پالت رنگ کلاسیک (سبز سفیر / Night تلگرام) ───
        private static readonly string ClassicGreenColor = "#1DB954";

        private static readonly PaletteDark ClassicDarkPalette = new PaletteDark()
        {
            Primary = "#5288c1",
            Background = "#0e1621",
            BackgroundGrey = "#242f3d",
            Surface = "#17212b",
            AppbarBackground = "#17212b",
            AppbarText = "#f5f5f5",
            DrawerBackground = "#17212b",
            DrawerText = "#f5f5f5",
            DrawerIcon = "#708499",
            TextPrimary = "#f5f5f5",
            TextSecondary = "#708499",
            TextDisabled = "#4f5f70",
            ActionDefault = "#708499",
            LinesDefault = "#243140",
            Divider = "#0e1621",
            Dark = "#9aa9b8",
            DarkContrastText = "#0e1621",
            TableStriped = "rgba(255,255,255,0.03)",
            TableHover = "rgba(82,136,193,0.10)",
            TableLines = "#243140",
        };

        private static readonly PaletteLight ClassicLightPalette = new PaletteLight()
        {
            Primary = ClassicGreenColor,
            AppbarBackground = Colors.Shades.White,
            AppbarText = "#1e293b",
            Background = Colors.Shades.White,
            TextPrimary = Colors.Grey.Darken3,
        };

        // ─── پالت رنگ اختصاصی تم سفیر (برگرفته از گرادیان لوگوی سفیر: آبی آسمانی تا سبز زمردی) ───
        private static readonly PaletteLight SafirLightPalette = new PaletteLight()
        {
            Primary = "#0284c7",            // آبی آسمانی اقیانوسی لوگو (خوانا و پر کنتراست)
            PrimaryDarken = "#0369a1",
            PrimaryLighten = "#38bdf8",
            Secondary = "#10b981",          // سبز زمردی آرامش‌بخش لوگو
            SecondaryDarken = "#059669",
            SecondaryLighten = "#34d399",
            Tertiary = "#06b6d4",           // فیروزه‌ای میانی موج
            Info = "#0ea5e9",
            Success = "#10b981",
            Warning = "#f59e0b",
            Error = "#ef4444",
            AppbarBackground = Colors.Shades.White,
            AppbarText = "#0f172a",
            Background = "#f8fafc",        // پس‌زمینه بسیار نرم و ملایم مدرن
            BackgroundGrey = "#f1f5f9",
            Surface = Colors.Shades.White,
            DrawerBackground = Colors.Shades.White,
            DrawerText = "#0f172a",
            DrawerIcon = "#0284c7",
            TextPrimary = "#0f172a",
            TextSecondary = "#64748b",
            TextDisabled = "#94a3b8",
            ActionDefault = "#64748b",
            LinesDefault = "#e2e8f0",
            Divider = "#e2e8f0",
            TableStriped = "rgba(2, 132, 199, 0.02)",
            TableHover = "rgba(2, 132, 199, 0.05)",
            TableLines = "#e2e8f0",
        };

        private static readonly PaletteDark SafirDarkPalette = new PaletteDark()
        {
            Primary = "#38bdf8",            // آبی آسمانی لومینوس روی زمینه تیره
            PrimaryDarken = "#0284c7",
            PrimaryLighten = "#7dd3fc",
            Secondary = "#34d399",          // سبز زمردی درخشان
            SecondaryDarken = "#10b981",
            SecondaryLighten = "#6ee7b7",
            Tertiary = "#22d3ee",
            Info = "#38bdf8",
            Success = "#34d399",
            Warning = "#fbbf24",
            Error = "#f87171",
            Background = "#0b132b",         // سرمه‌ای عمیق اقیانوسی
            BackgroundGrey = "#1c2541",
            Surface = "#111c38",            // پنل‌ها و کارت‌های عمیق
            AppbarBackground = "#0d1835",
            AppbarText = "#f8fafc",
            DrawerBackground = "#0d1835",
            DrawerText = "#f8fafc",
            DrawerIcon = "#7dd3fc",
            TextPrimary = "#f8fafc",
            TextSecondary = "#94a3b8",
            TextDisabled = "#475569",
            ActionDefault = "#94a3b8",
            LinesDefault = "#1e2c4f",
            Divider = "#1e2c4f",
            Dark = "#64748b",
            DarkContrastText = "#0b132b",
            TableStriped = "rgba(56, 189, 248, 0.03)",
            TableHover = "rgba(56, 189, 248, 0.08)",
            TableLines = "#1e2c4f",
        };

        private static readonly Typography AppTypography = new Typography()
        {
            Default = new Default()
            {
                FontFamily = new[] { "IRANYekanFN", "Helvetica", "Arial", "sans-serif" }
            }
        };

        private static readonly LayoutProperties AppLayout = new LayoutProperties()
        {
            DrawerWidthLeft = "260px",
            DrawerWidthRight = "300px"
        };

        // تم‌های کامل
        private static readonly MudTheme ClassicLightTheme = new MudTheme()
        {
            Palette = ClassicLightPalette,
            PaletteDark = ClassicDarkPalette,
            Typography = AppTypography,
            LayoutProperties = AppLayout
        };

        private static readonly MudTheme ClassicDarkTheme = new MudTheme()
        {
            Palette = ClassicDarkPalette,
            PaletteDark = ClassicDarkPalette,
            Typography = AppTypography,
            LayoutProperties = AppLayout
        };

        private static readonly MudTheme SafirLightTheme = new MudTheme()
        {
            Palette = SafirLightPalette,
            PaletteDark = SafirDarkPalette,
            Typography = AppTypography,
            LayoutProperties = AppLayout
        };

        private static readonly MudTheme SafirDarkTheme = new MudTheme()
        {
            Palette = SafirDarkPalette,
            PaletteDark = SafirDarkPalette,
            Typography = AppTypography,
            LayoutProperties = AppLayout
        };

        // ─── پالت رنگ اختصاصی تم بهای تمام‌شده (برگرفته از تم نئون سایبری: بنفش سیر، فیروزه‌ای و ارغوانی) ───
        private static readonly PaletteLight CostCloseLightPalette = new PaletteLight()
        {
            Primary = "#6366f1",            // بنفش نیلی مدرن
            PrimaryDarken = "#4f46e5",
            PrimaryLighten = "#818cf8",
            Secondary = "#0891b2",          // فیروزه‌ای عمیق
            SecondaryDarken = "#0e7490",
            SecondaryLighten = "#22d3ee",
            Tertiary = "#db2777",
            Info = "#0284c7",
            Success = "#16a34a",
            Warning = "#d97706",
            Error = "#e11d48",
            AppbarBackground = Colors.Shades.White,
            AppbarText = "#1e1035",
            Background = "#faf8ff",        // زمینه بسیار ملایم یاسی
            BackgroundGrey = "#f3e8ff",
            Surface = Colors.Shades.White,
            DrawerBackground = Colors.Shades.White,
            DrawerText = "#1e1035",
            DrawerIcon = "#6366f1",
            TextPrimary = "#1e1035",
            TextSecondary = "#6b7280",
            TextDisabled = "#9ca3af",
            ActionDefault = "#6b7280",
            LinesDefault = "#e9d5ff",
            Divider = "#e9d5ff",
            TableStriped = "rgba(99, 102, 241, 0.03)",
            TableHover = "rgba(99, 102, 241, 0.06)",
            TableLines = "#e9d5ff",
        };

        private static readonly PaletteDark CostCloseDarkPalette = new PaletteDark()
        {
            Primary = "#22d3ee",            // فیروزه‌ای نئون درخشان
            PrimaryDarken = "#0891b2",
            PrimaryLighten = "#67e8f9",
            Secondary = "#a78bfa",          // بنفش ارغوانی نئون
            SecondaryDarken = "#7c3aed",
            SecondaryLighten = "#c4b5fd",
            Tertiary = "#f472b6",           // صورتی نئون
            Info = "#22d3ee",
            Success = "#4ade80",          // سبز لیمویی نئون
            Warning = "#fbbf24",          // کهربایی
            Error = "#fb7185",            // سرخ مرجانی
            Background = "#241058",        // بنفشِ سیر شبانه
            BackgroundGrey = "#1c0d45",
            Surface = "#2c1668",           // سطح مخملی بنفش
            AppbarBackground = "#1c0d45",
            AppbarText = "#f4f0ff",
            DrawerBackground = "#1c0d45",
            DrawerText = "#f4f0ff",
            DrawerIcon = "#22d3ee",
            TextPrimary = "#f4f0ff",
            TextSecondary = "#c3b6e8",
            TextDisabled = "#786c9f",
            ActionDefault = "#c3b6e8",
            LinesDefault = "#3d257a",
            Divider = "#3d257a",
            Dark = "#786c9f",
            DarkContrastText = "#f4f0ff",
            TableStriped = "rgba(167, 139, 250, 0.05)",
            TableHover = "rgba(34, 211, 238, 0.08)",
            TableLines = "#3d257a",
        };

        private static readonly MudTheme CostCloseLightTheme = new MudTheme()
        {
            Palette = CostCloseLightPalette,
            PaletteDark = CostCloseDarkPalette,
            Typography = AppTypography,
            LayoutProperties = AppLayout
        };

        private static readonly MudTheme CostCloseDarkTheme = new MudTheme()
        {
            Palette = CostCloseDarkPalette,
            PaletteDark = CostCloseDarkPalette,
            Typography = AppTypography,
            LayoutProperties = AppLayout
        };

        public bool IsDarkMode
        {
            get => _isDarkMode;
            set
            {
                if (_isDarkMode != value)
                {
                    _isDarkMode = value;
                    UpdateCurrentTheme();
                    SaveThemePreference();
                    NotifyThemeChanged();
                }
            }
        }

        public AppThemeScheme CurrentScheme
        {
            get => _currentScheme;
            set
            {
                if (_currentScheme != value)
                {
                    _currentScheme = value;
                    UpdateCurrentTheme();
                    SaveSchemePreference();
                    NotifyThemeChanged();
                }
            }
        }

        public MudTheme CurrentTheme { get; private set; } = ClassicLightTheme;

        public ThemeService(IJSRuntime jsRuntime)
        {
            _jsRuntime = jsRuntime;
            UpdateCurrentTheme();
            LoadPreferences();
        }

        public async Task ToggleTheme()
        {
            IsDarkMode = !IsDarkMode;
            await Task.CompletedTask;
        }

        public async Task SetScheme(AppThemeScheme scheme)
        {
            CurrentScheme = scheme;
            await Task.CompletedTask;
        }

        private void UpdateCurrentTheme()
        {
            CurrentTheme = _currentScheme switch
            {
                AppThemeScheme.Safir => _isDarkMode ? SafirDarkTheme : SafirLightTheme,
                AppThemeScheme.CostClose => _isDarkMode ? CostCloseDarkTheme : CostCloseLightTheme,
                _ => _isDarkMode ? ClassicDarkTheme : ClassicLightTheme
            };
        }

        private void NotifyThemeChanged()
        {
            ThemeChanged?.Invoke();
            if (OnThemeChanged != null)
            {
                _ = OnThemeChanged.Invoke();
            }
        }

        private async void LoadPreferences()
        {
            try
            {
                _isDarkMode = await _jsRuntime.InvokeAsync<bool>("themeStorage.getTheme");
            }
            catch
            {
                _isDarkMode = false;
            }

            try
            {
                var schemeStr = await _jsRuntime.InvokeAsync<string>("themeStorage.getScheme");
                if (Enum.TryParse<AppThemeScheme>(schemeStr, true, out var scheme))
                {
                    _currentScheme = scheme;
                }
            }
            catch
            {
                _currentScheme = AppThemeScheme.Classic;
            }

            UpdateCurrentTheme();
            NotifyThemeChanged();
        }

        private async void SaveThemePreference()
        {
            try
            {
                await _jsRuntime.InvokeVoidAsync("themeStorage.setTheme", _isDarkMode);
            }
            catch { }
        }

        private async void SaveSchemePreference()
        {
            try
            {
                await _jsRuntime.InvokeVoidAsync("themeStorage.setScheme", _currentScheme.ToString().ToLowerInvariant());
            }
            catch { }
        }
    }
}
