using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using Microsoft.Win32;
using Windows.UI.ViewManagement;

namespace FileExplorer;

// One palette for XAML resources and programmatically created controls. UI-owned
// brushes remain mutable so an already-open dialog/widget changes with the theme;
// worker callers receive frozen snapshots, never dispatcher-owned brushes.
internal static class Appearance
{
    static readonly Dictionary<string, SolidColorBrush> brushes = new(StringComparer.OrdinalIgnoreCase);
    static readonly Dictionary<string, PaletteColor> colors = new(StringComparer.OrdinalIgnoreCase);
    static readonly Dictionary<string, PaletteColor> namedColors = new(StringComparer.Ordinal);
    static UISettings system;
    static readonly PaletteColor windowColor = new();
    static readonly SolidColorBrush window = Bound(windowColor);
    static readonly HashSet<string> resourceColors = new(StringComparer.OrdinalIgnoreCase);
    static bool nativeLight;
    sealed class PaletteColor : DependencyObject
    {
        internal static readonly DependencyProperty ValueProperty=DependencyProperty.Register(nameof(Value),typeof(Color),typeof(PaletteColor));
        public Color Value { get=>(Color)GetValue(ValueProperty); set=>SetValue(ValueProperty,value); }
    }
    static SolidColorBrush Bound(PaletteColor source)
    {
        var brush=new SolidColorBrush(source.Value);
        // A bound Freezable cannot be frozen by Style.Seal. The palette source
        // stays authoritative for both style setters and ordinary controls.
        BindingOperations.SetBinding(brush,SolidColorBrush.ColorProperty,new Binding(nameof(PaletteColor.Value)){Source=source,Mode=BindingMode.OneWay});
        return brush;
    }
    internal static void SetBrush(string key,Color color)
    {
        if(!namedColors.TryGetValue(key,out var source))
        {
            namedColors.Add(key,source=new PaletteColor{Value=color});
            Application.Current.Resources[key]=Bound(source);
        }
        else source.Value=color;
    }
    internal static bool Native => Settings.NativeTheme || SystemParameters.HighContrast;
    internal static bool Light => Native && nativeLight;
    internal static Color Background => SystemParameters.HighContrast ? SystemColors.WindowColor : Native ? Light ? Color.FromRgb(243,243,243) : Color.FromRgb(28,28,28) : Color.FromRgb(10,13,19);

    static bool ReadLightPreference()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return key?.GetValue("AppsUseLightTheme") is not int value || value != 0;
    }

    internal static string ResourceKey(string color) => "Palette." + color.TrimStart('#').ToUpperInvariant();
    internal static Color Resolve(string original)
    {
        var color = (Color)ColorConverter.ConvertFromString(original);
        if (!Native || color.A == 0) return color;
        int max = Math.Max(color.R, Math.Max(color.G,color.B)), min = Math.Min(color.R,Math.Min(color.G,color.B));
        bool white = min == 255, black = max == 0;
        if (SystemParameters.HighContrast)
        {
            var system = max >= 110 && color.A >= 96 ? SystemColors.WindowTextColor : SystemColors.WindowColor;
            return system;
        }
        if (white)
        {
            byte alpha = color.A >= 96 ? (byte)Math.Max(170,(int)color.A) : color.A;
            return Color.FromArgb(alpha, Light ? (byte)0 : (byte)255, Light ? (byte)0 : (byte)255, Light ? (byte)0 : (byte)255);
        }
        if (black && color.A < 255) return Color.FromArgb((byte)Math.Min(color.A,Light ? 28 : color.A),0,0,0);
        if (max-min < 72)
        {
            if (max < 110)
            {
                byte surface = Light ? (byte)249 : (byte)Math.Clamp((color.R+color.G+color.B)/3,24,48);
                return Color.FromArgb(color.A >= 96 ? (byte)255 : color.A,surface,surface,surface);
            }
            byte ink = Light ? max >= 190 ? (byte)32 : (byte)86 : (byte)Math.Max(174,(color.R+color.G+color.B)/3);
            return Color.FromArgb(255,ink,ink,ink);
        }
        return color; // file-type/status colors retain their semantics
    }

    internal static Brush Brush(string color)
    {
        if (Application.Current?.Dispatcher.CheckAccess() != true)
        {
            var snapshot = new SolidColorBrush(Resolve(color)); snapshot.Freeze(); return snapshot;
        }
        if (!brushes.TryGetValue(color,out var brush))
        {
            var source=new PaletteColor{Value=Resolve(color)};
            colors.Add(color,source);brushes.Add(color,brush=Bound(source));
        }
        return brush;
    }

    internal static Brush WindowBrush()
    {
        var color=Background;
        color.A=Native || Settings.Backdrop == 1 ? (byte)255 : (byte)(Settings.Opacity*2.55);
        windowColor.Value=color;
        return window;
    }

    internal static Color Accent
    {
        get
        {
            if (SystemParameters.HighContrast) return SystemColors.HighlightColor;
            var color=(Color)ColorConverter.ConvertFromString(Settings.Accent);
            if (Settings.NativeTheme && Settings.WindowsAccent)
            {
                var accent=system.GetColorValue(UIColorType.Accent);
                color=Color.FromRgb(accent.R,accent.G,accent.B);
            }
            if (Native)
            {
                // Use an accessible tone of the chosen accent for both controls
                // and text; Windows similarly has lighter/darker accent variants.
                for(int i=0;i<32 && MainWindow.Contrast(color,Background)<4.5;i++)
                {
                    byte Tone(byte channel)=>(byte)Math.Round(channel*0.9+(Light?0:255)*0.1);
                    color=Color.FromRgb(Tone(color.R),Tone(color.G),Tone(color.B));
                }
            }
            return color;
        }
    }

    internal static (Color Fill,Color Text) Selection(Color accent)
    {
        if(SystemParameters.HighContrast)return(SystemColors.HighlightColor,SystemColors.HighlightTextColor);
        if(!Native)return MainWindow.Selection(accent,Settings.AccentStrength);
        var ground=Background; double amount=Light?0.13:0.24;
        byte Mix(byte a,byte b)=>(byte)Math.Round(a*amount+b*(1-amount));
        var fill=Color.FromRgb(Mix(accent.R,ground.R),Mix(accent.G,ground.G),Mix(accent.B,ground.B));
        return(fill,Light?Colors.Black:Colors.White);
    }

    internal static void Initialize()
    {
        // The mechanical XAML migration records its complete palette here.
        foreach(var color in PaletteColors.All)resourceColors.Add(color);
        system=new UISettings();
        system.ColorValuesChanged+=AccentChanged;
        Apply();
        MainWindow.ApplyAccent();
        SystemEvents.UserPreferenceChanged+=SystemChanged;
        Application.Current.Exit+=(_,_)=>{SystemEvents.UserPreferenceChanged-=SystemChanged;system.ColorValuesChanged-=AccentChanged;};
    }

    static void SystemChanged(object sender,UserPreferenceChangedEventArgs args)
        =>RefreshSystem();
    static void AccentChanged(UISettings sender,object args)=>RefreshSystem();
    static void RefreshSystem()
    {
        var app=Application.Current;
        if(app==null||app.Dispatcher.HasShutdownStarted)return;
        app.Dispatcher.BeginInvoke(()=>
        {
            if(app.MainWindow is MainWindow main)main.ApplyAppearance();else Apply();
        });
    }

    internal static void Apply()
    {
        nativeLight=Native && !SystemParameters.HighContrast && (Settings.NativeMode=="light" || Settings.NativeMode=="system" && ReadLightPreference());
        foreach(var pair in colors)pair.Value.Value=Resolve(pair.Key);
        WindowBrush();
        foreach(var color in resourceColors)
        {
            Application.Current.Resources[ResourceKey(color)]=Brush(color);
            Application.Current.Resources["Color."+color.TrimStart('#').ToUpperInvariant()]=Resolve(color);
        }
        SetBrush("Muted",Resolve("#8CFFFFFF"));
        SetBrush("TabStripBg",Resolve("#2E000000"));
        SetBrush("AgeBackground",SystemParameters.HighContrast?SystemColors.WindowColor:Light?Color.FromRgb(232,232,232):Color.FromRgb(55,55,55));
        SetBrush("AgeForeground",SystemParameters.HighContrast?SystemColors.WindowTextColor:Light?Color.FromRgb(64,64,64):Color.FromRgb(232,232,232));
        foreach(Window current in Application.Current.Windows)
        {
            current.Foreground=Brush("#E8E8E8");
            MainWindow.SetBackdrop(current);
        }
    }
}
