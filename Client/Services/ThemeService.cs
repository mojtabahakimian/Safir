using Microsoft.JSInterop;
using MudBlazor;

namespace Safir.Client.Services
{
    public class ThemeService
    {
        private readonly IJSRuntime _jsRuntime;
        private bool _isDarkMode = false;
        private readonly MudTheme _lightTheme;
        private readonly MudTheme _darkTheme;

        public event Action ThemeChanged;

        public bool IsDarkMode
        {
            get => _isDarkMode;
            set
            {
                if (_isDarkMode != value)
                {
                    _isDarkMode = value;
                    SaveThemePreference();
                    ThemeChanged?.Invoke();
                }
            }
        }

        #region Gemini

        public event Func<Task>? OnThemeChanged;

        // Define your Green color (adjust hex code as needed)
        //     Primary = "#2e7d32", // Green
        private static readonly string GreenColor = "#1DB954"; // Example Spotify Green

        // MudThemeProvider در حالت تاریک از PaletteDark تمِ جاری استفاده می‌کند، نه از Palette.
        // قبلاً هیچ تمی PaletteDark نداشت و MudBlazor پالت پیش‌فرضِ بنفش خودش را نشان می‌داد
        // (نوار بالای بنفش)؛ این پالت به هر دو تم داده می‌شود چون برنامه در شروع، حتی با
        // ترجیح «تاریک»، روی LightTheme بالا می‌آید.
        // رنگ‌ها از تم «Night» تلگرام دسکتاپ: سرمه‌ای به‌جای خاکستری، تأکید آبی
        private static readonly PaletteDark DarkPalette = new PaletteDark()
        {
            Primary = "#5288c1",            // آبی تأکید تلگرام (دکمه‌ها، آیتم فعال منو)
            Background = "#0e1621",         // زمینه‌ی اصلی (پشت پیام‌ها در تلگرام)
            BackgroundGrey = "#242f3d",     // جعبه‌ی جستجو / ورودی‌ها
            Surface = "#17212b",            // کارت‌ها و پنل‌ها
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
            // Color.Dark پیش‌فرض MudBlazor (#27272f) روی زمینه‌ی سرمه‌ای دیده نمی‌شود؛ جاهایی که متن یا
            // چیپ را «Dark» گذاشته‌اند (کد مشتری، وضعیت گزارش خطا، …) در تم تاریک ناپدید می‌شدند.
            Dark = "#9aa9b8",
            DarkContrastText = "#0e1621",
            // پیش‌فرض MudBlazor برای ردیف‌های راه‌راه (سفید ۲۰٪) هر ردیف دوم جدول را خاکستری پررنگ می‌کرد
            TableStriped = "rgba(255,255,255,0.03)",
            TableHover = "rgba(82,136,193,0.10)",
            TableLines = "#243140",
        };

        private static readonly MudTheme LightTheme = new MudTheme()
        {
            Palette = new PaletteLight()
            {
                Primary = GreenColor,
                // نوار بالا مثل سرتیتر پنل حقوق و دستمزد: سفید با متن تیره، نه رنگ برند
                AppbarBackground = Colors.Shades.White,
                AppbarText = "#1e293b",
                Background = Colors.Shades.White,
                TextPrimary = Colors.Grey.Darken3,
                // Add other palette color overrides if needed
            },
            PaletteDark = DarkPalette,
            Typography = new Typography()
            {
                Default = new Default()
                {
                    FontFamily = new[] { "IRANYekanFN", "Helvetica", "Arial", "sans-serif" }
                }
                // Define other typography settings if needed
            },
            LayoutProperties = new LayoutProperties()
            {
                DrawerWidthLeft = "260px",
                DrawerWidthRight = "300px"
            }
        };

        private static readonly MudTheme DarkTheme = new MudTheme()
        {
            Palette = DarkPalette,
            PaletteDark = DarkPalette,
            Typography = new Typography()
            {
                Default = new Default()
                {
                    FontFamily = new[] { "IRANYekanFN", "Helvetica", "Arial", "sans-serif" }
                }
                // Define other typography settings if needed
            },
            LayoutProperties = new LayoutProperties()
            {
                DrawerWidthLeft = "260px",
                DrawerWidthRight = "300px"
            }
        };

        public async Task ToggleTheme()
        {
            IsDarkMode = !IsDarkMode;
            CurrentTheme = IsDarkMode ? DarkTheme : LightTheme;
            if (OnThemeChanged != null)
            {
                await OnThemeChanged.Invoke();
            }
        }
        public MudTheme CurrentTheme { get; private set; } = LightTheme;
        #endregion

       // public MudTheme CurrentTheme => IsDarkMode ? _darkTheme : _lightTheme;
        public ThemeService(IJSRuntime jsRuntime)
        {
            _jsRuntime = jsRuntime;

            _lightTheme = new MudTheme
            {
                Palette = new PaletteLight
                {
                    Primary = GreenColor, // Green
                    PrimaryDarken = "#1b5e20",
                    PrimaryLighten = "#4caf50",
                    Secondary = "#ffffff", // White
                    Background = "#ffffff",
                    AppbarBackground = "#2e7d32",
                    DrawerBackground = "#fafafa",
                    TextPrimary = "#333333",
                    DrawerText = "#424242",
                    Surface = "#ffffff",
                },
                Typography = new Typography
                {
                    Default = new Default
                    {
                        FontFamily = new[] { "IRANYekanFN", "Roboto", "sans-serif" }
                    },
                    H1 = new H1
                    {
                        FontFamily = new[] { "IRANYekanFN", "Roboto", "sans-serif" }
                    },
                    H2 = new H2
                    {
                        FontFamily = new[] { "IRANYekanFN", "Roboto", "sans-serif" }
                    },
                    H3 = new H3
                    {
                        FontFamily = new[] { "IRANYekanFN", "Roboto", "sans-serif" }
                    },
                    H4 = new H4
                    {
                        FontFamily = new[] { "IRANYekanFN", "Roboto", "sans-serif" }
                    },
                    H5 = new H5
                    {
                        FontFamily = new[] { "IRANYekanFN", "Roboto", "sans-serif" }
                    },
                    H6 = new H6
                    {
                        FontFamily = new[] { "IRANYekanFN", "Roboto", "sans-serif" }
                    },
                    Body1 = new Body1
                    {
                        FontFamily = new[] { "IRANYekanFN", "Roboto", "sans-serif" }
                    },
                    Body2 = new Body2
                    {
                        FontFamily = new[] { "IRANYekanFN", "Roboto", "sans-serif" }
                    },
                    Button = new Button
                    {
                        FontFamily = new[] { "IRANYekanFN", "Roboto", "sans-serif" }
                    },
                    Caption = new Caption
                    {
                        FontFamily = new[] { "IRANYekanFN", "Roboto", "sans-serif" }
                    },
                    Overline = new Overline
                    {
                        FontFamily = new[] { "IRANYekanFN", "Roboto", "sans-serif" }
                    },
                    Subtitle1 = new Subtitle1
                    {
                        FontFamily = new[] { "IRANYekanFN", "Roboto", "sans-serif" }
                    },
                    Subtitle2 = new Subtitle2
                    {
                        FontFamily = new[] { "IRANYekanFN", "Roboto", "sans-serif" }
                    }
                }
            };

            _darkTheme = new MudTheme
            {
                Palette = new PaletteDark
                {
                    Primary = GreenColor, // Green
                    PrimaryDarken = "#2e7d32",
                    PrimaryLighten = "#81c784",
                    Secondary = "#f5f5f5", // Off-white
                    Background = "#121212",
                    AppbarBackground = "#1b5e20",
                    DrawerBackground = "#1f1f1f",
                    TextPrimary = "#ffffff",
                    DrawerText = "#e0e0e0",
                    Surface = "#1e1e1e",
                    DrawerIcon = "#ffffff"
                },
                Typography = new Typography
                {
                    Default = new Default
                    {
                        FontFamily = new[] { "IRANYekanFN", "Roboto", "sans-serif" }
                    },
                    H1 = new H1
                    {
                        FontFamily = new[] { "IRANYekanFN", "Roboto", "sans-serif" }
                    },
                    H2 = new H2
                    {
                        FontFamily = new[] { "IRANYekanFN", "Roboto", "sans-serif" }
                    },
                    H3 = new H3
                    {
                        FontFamily = new[] { "IRANYekanFN", "Roboto", "sans-serif" }
                    },
                    H4 = new H4
                    {
                        FontFamily = new[] { "IRANYekanFN", "Roboto", "sans-serif" }
                    },
                    H5 = new H5
                    {
                        FontFamily = new[] { "IRANYekanFN", "Roboto", "sans-serif" }
                    },
                    H6 = new H6
                    {
                        FontFamily = new[] { "IRANYekanFN", "Roboto", "sans-serif" }
                    },
                    Body1 = new Body1
                    {
                        FontFamily = new[] { "IRANYekanFN", "Roboto", "sans-serif" }
                    },
                    Body2 = new Body2
                    {
                        FontFamily = new[] { "IRANYekanFN", "Roboto", "sans-serif" }
                    },
                    Button = new Button
                    {
                        FontFamily = new[] { "IRANYekanFN", "Roboto", "sans-serif" }
                    },
                    Caption = new Caption
                    {
                        FontFamily = new[] { "IRANYekanFN", "Roboto", "sans-serif" }
                    },
                    Overline = new Overline
                    {
                        FontFamily = new[] { "IRANYekanFN", "Roboto", "sans-serif" }
                    },
                    Subtitle1 = new Subtitle1
                    {
                        FontFamily = new[] { "IRANYekanFN", "Roboto", "sans-serif" }
                    },
                    Subtitle2 = new Subtitle2
                    {
                        FontFamily = new[] { "IRANYekanFN", "Roboto", "sans-serif" }
                    }
                }
            };

            // Load theme preference on service initialization
            LoadThemePreference();
        }

        private async void LoadThemePreference()
        {
            try
            {
                _isDarkMode = await _jsRuntime.InvokeAsync<bool>("themeStorage.getTheme");
                ThemeChanged?.Invoke();
            }
            catch
            {
                // Default to light theme if there's an error
                _isDarkMode = false;
            }
        }

        private async void SaveThemePreference()
        {
            try
            {
                await _jsRuntime.InvokeVoidAsync("themeStorage.setTheme", _isDarkMode);
            }
            catch
            {
                // Handle error if needed
            }
        }
    }
}
