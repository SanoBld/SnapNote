using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Windows.Storage.Pickers;
using Windows.Storage.Streams;
using Windows.UI;
using Windows.UI.ViewManagement;
using WinRT.Interop;

namespace SnapNote;

public sealed partial class MainWindow : Window
{
    // ── Chemins ──────────────────────────────────────────────────────────────
    private static readonly string NoteDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SnapNote");
    private static readonly string DefaultNotePath = Path.Combine(NoteDir, "note.rtf");

    // ══════════════════════════════════════════════════════════════════════════
    //  MODÈLE ONGLET
    // ══════════════════════════════════════════════════════════════════════════

    private sealed class NoteTab
    {
        public string?     FilePath   { get; set; }
        public bool        IsDefault  { get; set; }
        public bool        IsModified { get; set; }
        public RichEditBox Editor     { get; set; } = null!;
        public Button      TabBtn     { get; set; } = null!;
        public TextBlock   TitleLbl   { get; set; } = null!;
        public Rectangle   Indicator  { get; set; } = null!; // barre accent en bas

        public string Title        => FilePath is null ? "Sans titre" : Path.GetFileNameWithoutExtension(FilePath);
        public string DisplayTitle => IsModified ? Title + " ●" : Title;
    }

    // ── État ─────────────────────────────────────────────────────────────────
    private readonly List<NoteTab> _tabs = new();
    private NoteTab?           _activeTab;
    private NoteTab?           _lastEditedTab;
    private bool               _isLoading     = false;
    private bool               _isPinned      = false;
    private bool               _settingsOpen  = false;
    private bool               _isCompact     = false;
    private bool               _isDarkBg      = false;
    private Color              _highlightColor = Color.FromArgb(255, 255, 229, 102);
    private Color              _noteBgColor    = Colors.White;
    private Color              _accentColor;

    // Settings tab (bouton spécial dans la barre)
    private Button?    _settingsTabBtn;
    private Rectangle? _settingsTabIndicator;
    private TextBlock? _settingsTabLbl;

    private OverlappedPresenter? _presenter;
    private DispatcherTimer?     _saveTimer;
    private AppWindow?           _appWindow;

    private RichEditBox? ActiveEditor => _activeTab?.Editor;

    // Dimensions fenêtre
    private const int NormalWidth  = 420;
    private const int NormalHeight = 520;
    private const int CompactWidth = 420;
    private const int CompactHeight = 160;

    private static readonly string[] FontFamilies =
    {
        "Cascadia Code", "Segoe UI Variable Text",
        "Consolas", "Georgia", "Arial", "Courier New"
    };

    // ══════════════════════════════════════════════════════════════════════════
    //  CONSTRUCTEUR
    // ══════════════════════════════════════════════════════════════════════════

    public MainWindow()
    {
        InitializeComponent();

        // Couleur d'accent Windows
        try
        {
            var ui = new UISettings();
            var c  = ui.GetColorValue(UIColorType.Accent);
            _accentColor = Color.FromArgb(c.A, c.R, c.G, c.B);
        }
        catch
        {
            _accentColor = Color.FromArgb(255, 0, 120, 212); // bleu Windows par défaut
        }

        ConfigureWindow();
        ApplyBackdrop();
        InitSettings();
        InitSaveTimer();
        InitTabs();
        CreateSettingsTabButton();
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  FENÊTRE
    // ══════════════════════════════════════════════════════════════════════════

    private void ConfigureWindow()
    {
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(TitleBarArea);

        var hwnd     = WindowNative.GetWindowHandle(this);
        var winId    = Win32Interop.GetWindowIdFromWindow(hwnd);
        _appWindow   = AppWindow.GetFromWindowId(winId);

        _appWindow.Resize(new Windows.Graphics.SizeInt32(NormalWidth, NormalHeight));
        _appWindow.Title = "SnapNote";

        if (_appWindow.Presenter is OverlappedPresenter op) _presenter = op;

        if (_appWindow.TitleBar is { } tb)
        {
            tb.ButtonBackgroundColor         = Colors.Transparent;
            tb.ButtonInactiveBackgroundColor = Colors.Transparent;
            tb.ButtonHoverBackgroundColor    = Color.FromArgb(20, 0, 0, 0);
            tb.ButtonPressedBackgroundColor  = Color.FromArgb(40, 0, 0, 0);
            tb.ButtonForegroundColor         = Colors.DimGray;
        }
    }

    private void ApplyBackdrop()
    {
        try   { SystemBackdrop = new MicaBackdrop { Kind = Microsoft.UI.Composition.SystemBackdrops.MicaKind.Base }; }
        catch { try { SystemBackdrop = new DesktopAcrylicBackdrop(); } catch { } }
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  MODE COMPACT
    // ══════════════════════════════════════════════════════════════════════════

    private void BtnCompact_Click(object s, RoutedEventArgs e) => ToggleCompact();

    private void ToggleCompact()
    {
        _isCompact = !_isCompact;

        if (_isCompact)
        {
            // Masque header + footer
            HeaderGrid.Visibility  = Visibility.Collapsed;
            FooterGrid.Visibility  = Visibility.Collapsed;
            Divider.Visibility     = Visibility.Collapsed;

            // Déplace la barre d'onglets tout en haut
            TabStripGrid.Margin    = new Thickness(0, 1, 0, 0);
            Divider2.Margin        = new Thickness(0, 31, 0, 0);
            TabContent.Margin      = new Thickness(4, 32, 4, 4);
            SettingsOverlay.Margin = new Thickness(0, 32, 0, 4);

            _appWindow?.Resize(new Windows.Graphics.SizeInt32(CompactWidth, CompactHeight));

            CompactIcon.Glyph = "\uE923"; // icône "agrandir"
            ToolTipService.SetToolTip(BtnCompact, "Quitter le mode compact");
        }
        else
        {
            HeaderGrid.Visibility  = Visibility.Visible;
            FooterGrid.Visibility  = Visibility.Visible;
            Divider.Visibility     = Visibility.Visible;

            TabStripGrid.Margin    = new Thickness(0, 45, 0, 0);
            Divider2.Margin        = new Thickness(0, 75, 0, 0);
            TabContent.Margin      = new Thickness(4, 76, 4, 36);
            SettingsOverlay.Margin = new Thickness(0, 76, 0, 36);

            _appWindow?.Resize(new Windows.Graphics.SizeInt32(NormalWidth, NormalHeight));

            CompactIcon.Glyph = "\uE922"; // icône "réduire"
            ToolTipService.SetToolTip(BtnCompact, "Mode prise de note rapide");
        }
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  GESTION DES ONGLETS
    // ══════════════════════════════════════════════════════════════════════════

    private void InitTabs()
    {
        Directory.CreateDirectory(NoteDir);
        var tab = CreateTab(DefaultNotePath, isDefault: true);
        TabStrip.Children.Add(tab.TabBtn);
        TabContent.Children.Add(tab.Editor);
        _tabs.Add(tab);
        ActivateTab(tab);
        _ = LoadFileIntoTabAsync(tab, DefaultNotePath);
    }

    // ── Créer un onglet ──────────────────────────────────────────────────────

    private NoteTab CreateTab(string? filePath, bool isDefault = false)
    {
        var tab = new NoteTab { FilePath = filePath, IsDefault = isDefault };

        // ── Éditeur ──
        var editor = new RichEditBox
        {
            BorderThickness       = new Thickness(0),
            Background            = new SolidColorBrush(Colors.Transparent),
            UseSystemFocusVisuals = false,
            FontFamily            = new FontFamily(FontFamilies[FontCombo.SelectedIndex < 0 ? 0 : FontCombo.SelectedIndex]),
            FontSize              = SizeSlider.Value,
            Foreground            = new SolidColorBrush(HexToColor(_isDarkBg ? "#F0F0F0" : "#1A1A1A")),
            PlaceholderText       = "Commence à écrire…",
            Padding               = new Thickness(16, 10, 16, 10),
            HorizontalAlignment   = HorizontalAlignment.Stretch,
            VerticalAlignment     = VerticalAlignment.Stretch,
            Visibility            = Visibility.Collapsed,
            Style                 = (Style)Application.Current.Resources["CleanRichEditStyle"],
            // Sélection en couleur accent au lieu du gris sombre
            SelectionHighlightColor = new SolidColorBrush(
                Color.FromArgb(80, _accentColor.R, _accentColor.G, _accentColor.B))
        };
        editor.TextChanged += Editor_TextChanged;
        tab.Editor = editor;

        // ── Indicateur accent (barre en bas de l'onglet actif) ──
        var indicator = new Rectangle
        {
            Height              = 2,
            Fill                = new SolidColorBrush(_accentColor),
            VerticalAlignment   = VerticalAlignment.Bottom,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Margin              = new Thickness(6, 0, 6, 0),
            RadiusX             = 1,
            RadiusY             = 1,
            Visibility          = Visibility.Collapsed
        };
        tab.Indicator = indicator;

        // ── Label titre ──
        tab.TitleLbl = new TextBlock
        {
            Text              = tab.DisplayTitle,
            FontSize          = 11,
            FontFamily        = new FontFamily("Segoe UI Variable Text, Segoe UI"),
            MaxWidth          = 90,
            TextTrimming      = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground        = new SolidColorBrush(HexToColor("#888888"))
        };

        // ── Bouton fermer ──
        var closeBtn = new Button
        {
            Width           = 16, Height = 16,
            Padding         = new Thickness(0),
            Background      = new SolidColorBrush(Colors.Transparent),
            BorderThickness = new Thickness(0),
            CornerRadius    = new CornerRadius(4),
            Content         = new FontIcon
            {
                Glyph      = "\uE711",
                FontSize   = 8,
                FontFamily = new FontFamily("Segoe MDL2 Assets"),
                Foreground = new SolidColorBrush(HexToColor("#AAAAAA"))
            },
            VerticalAlignment = VerticalAlignment.Center
        };
        closeBtn.Click += (s, e) => _ = CloseTabAsync(tab);

        var innerPanel = new StackPanel
        {
            Orientation       = Orientation.Horizontal,
            Spacing           = 5,
            VerticalAlignment = VerticalAlignment.Center
        };
        innerPanel.Children.Add(tab.TitleLbl);
        innerPanel.Children.Add(closeBtn);

        // Wrapper Grid pour le texte + indicateur bas
        var btnGrid = new Grid();
        btnGrid.Children.Add(innerPanel);
        btnGrid.Children.Add(indicator);

        var btn = new Button
        {
            Height          = 26,
            MinWidth        = 70,
            Padding         = new Thickness(8, 0, 4, 0),
            CornerRadius    = new CornerRadius(6),
            Background      = new SolidColorBrush(Colors.Transparent),
            BorderThickness = new Thickness(0),
            Content         = btnGrid,
            Tag             = tab
        };
        btn.Click += (s, e) => ActivateTab(tab);
        tab.TabBtn = btn;

        return tab;
    }

    // ── Onglet Paramètres (spécial) ──────────────────────────────────────────

    private void CreateSettingsTabButton()
    {
        _settingsTabIndicator = new Rectangle
        {
            Height              = 2,
            Fill                = new SolidColorBrush(_accentColor),
            VerticalAlignment   = VerticalAlignment.Bottom,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Margin              = new Thickness(6, 0, 6, 0),
            RadiusX             = 1, RadiusY = 1,
            Visibility          = Visibility.Collapsed
        };

        var icon = new FontIcon
        {
            Glyph      = "\uE713",
            FontFamily = new FontFamily("Segoe MDL2 Assets"),
            FontSize   = 11,
            Foreground = new SolidColorBrush(HexToColor("#888888")),
            VerticalAlignment = VerticalAlignment.Center
        };

        _settingsTabLbl = new TextBlock
        {
            Text              = "Paramètres",
            FontSize          = 11,
            FontFamily        = new FontFamily("Segoe UI Variable Text, Segoe UI"),
            VerticalAlignment = VerticalAlignment.Center,
            Foreground        = new SolidColorBrush(HexToColor("#888888"))
        };

        var closeSettingsBtn = new Button
        {
            Width           = 16, Height = 16,
            Padding         = new Thickness(0),
            Background      = new SolidColorBrush(Colors.Transparent),
            BorderThickness = new Thickness(0),
            CornerRadius    = new CornerRadius(4),
            Content         = new FontIcon
            {
                Glyph      = "\uE711",
                FontSize   = 8,
                FontFamily = new FontFamily("Segoe MDL2 Assets"),
                Foreground = new SolidColorBrush(HexToColor("#AAAAAA"))
            },
            VerticalAlignment = VerticalAlignment.Center,
            Visibility        = Visibility.Collapsed  // affiché seulement quand actif
        };
        closeSettingsBtn.Click += (s, e) => CloseSettingsTab();

        var innerPanel = new StackPanel
        {
            Orientation       = Orientation.Horizontal,
            Spacing           = 5,
            VerticalAlignment = VerticalAlignment.Center
        };
        innerPanel.Children.Add(icon);
        innerPanel.Children.Add(_settingsTabLbl);
        innerPanel.Children.Add(closeSettingsBtn);

        var grid = new Grid();
        grid.Children.Add(innerPanel);
        grid.Children.Add(_settingsTabIndicator);

        _settingsTabBtn = new Button
        {
            Height          = 26,
            Padding         = new Thickness(8, 0, 4, 0),
            CornerRadius    = new CornerRadius(6),
            Background      = new SolidColorBrush(Colors.Transparent),
            BorderThickness = new Thickness(0),
            Content         = grid
        };
        _settingsTabBtn.Click += (s, e) =>
        {
            if (_settingsOpen) CloseSettingsTab();
            else ShowSettingsTab();
        };

        // Stocker le bouton fermer pour l'afficher/masquer
        _settingsTabBtn.Tag = closeSettingsBtn;

        TabStrip.Children.Add(_settingsTabBtn);
    }

    private void ShowSettingsTab()
    {
        _settingsOpen = true;

        // Désactiver tous les onglets note
        foreach (var t in _tabs)
        {
            t.Editor.Visibility  = Visibility.Collapsed;
            t.TabBtn.Background  = new SolidColorBrush(Colors.Transparent);
            t.Indicator.Visibility = Visibility.Collapsed;
            t.TitleLbl.Foreground  = new SolidColorBrush(HexToColor(_isDarkBg ? "#777777" : "#888888"));
        }

        // Activer l'onglet paramètres
        SettingsOverlay.Visibility = Visibility.Visible;
        SettingsOverlay.RequestedTheme = _isDarkBg ? ElementTheme.Dark : ElementTheme.Light;

        if (_settingsTabIndicator is not null)
            _settingsTabIndicator.Visibility = Visibility.Visible;
        if (_settingsTabLbl is not null)
            _settingsTabLbl.Foreground = new SolidColorBrush(HexToColor(_isDarkBg ? "#F0F0F0" : "#1A1A1A"));
        if (_settingsTabBtn?.Tag is Button closeBtn)
            closeBtn.Visibility = Visibility.Visible;

        // Icône settings en vert actif
        SettingsIcon.Foreground = new SolidColorBrush(_accentColor);

        _activeTab = null;
    }

    private void CloseSettingsTab()
    {
        _settingsOpen              = false;
        SettingsOverlay.Visibility = Visibility.Collapsed;

        if (_settingsTabIndicator is not null)
            _settingsTabIndicator.Visibility = Visibility.Collapsed;
        if (_settingsTabLbl is not null)
            _settingsTabLbl.Foreground = new SolidColorBrush(HexToColor(_isDarkBg ? "#777777" : "#888888"));
        if (_settingsTabBtn?.Tag is Button closeBtn)
            closeBtn.Visibility = Visibility.Collapsed;

        var muted = _isDarkBg ? "#777777" : "#888888";
        SettingsIcon.Foreground = new SolidColorBrush(HexToColor(muted));

        // Réactiver le dernier onglet note
        if (_tabs.Count > 0)
            ActivateTab(_lastEditedTab ?? _tabs.Last());
    }

    // ── Activer un onglet note ───────────────────────────────────────────────

    private void ActivateTab(NoteTab tab)
    {
        // Fermer les paramètres si ouverts
        if (_settingsOpen)
        {
            _settingsOpen              = false;
            SettingsOverlay.Visibility = Visibility.Collapsed;
            if (_settingsTabIndicator is not null) _settingsTabIndicator.Visibility = Visibility.Collapsed;
            if (_settingsTabLbl is not null) _settingsTabLbl.Foreground = new SolidColorBrush(HexToColor(_isDarkBg ? "#777777" : "#888888"));
            if (_settingsTabBtn?.Tag is Button cb) cb.Visibility = Visibility.Collapsed;
            SettingsIcon.Foreground = new SolidColorBrush(HexToColor(_isDarkBg ? "#777777" : "#888888"));
        }

        // Désactiver tous les onglets
        foreach (var t in _tabs)
        {
            t.Editor.Visibility    = Visibility.Collapsed;
            t.TabBtn.Background    = new SolidColorBrush(Colors.Transparent);
            t.Indicator.Visibility = Visibility.Collapsed;
            // texte selon thème (inactif)
            t.TitleLbl.Foreground  = new SolidColorBrush(HexToColor(_isDarkBg ? "#777777" : "#888888"));
        }

        // Activer l'onglet sélectionné
        tab.Editor.Visibility    = Visibility.Visible;
        tab.TabBtn.Background    = new SolidColorBrush(Colors.Transparent); // pas de fond, juste l'indicateur
        tab.Indicator.Visibility = Visibility.Visible;  // barre accent
        // texte en fort contraste selon thème
        tab.TitleLbl.Foreground  = new SolidColorBrush(HexToColor(_isDarkBg ? "#F0F0F0" : "#1A1A1A"));

        _activeTab = tab;
        SetStatus(tab.FilePath is null ? "nouveau" : "prêt", _isDarkBg ? "#777777" : "#888888");
        tab.Editor.Focus(FocusState.Programmatic);
    }

    // ── Fermer un onglet ─────────────────────────────────────────────────────

    private async Task CloseTabAsync(NoteTab tab)
    {
        if (tab.IsModified && !tab.IsDefault && tab.FilePath is not null)
        {
            var dlg = new ContentDialog
            {
                Title               = "Fermer sans enregistrer ?",
                Content             = $"Les modifications de « {tab.Title} » ne sont pas sauvegardées.",
                PrimaryButtonText   = "Enregistrer",
                SecondaryButtonText = "Fermer quand même",
                CloseButtonText     = "Annuler",
                XamlRoot            = Content.XamlRoot
            };
            var result = await dlg.ShowAsync();
            if (result == ContentDialogResult.None) return;
            if (result == ContentDialogResult.Primary) await SaveTabAsync(tab);
        }

        TabStrip.Children.Remove(tab.TabBtn);
        TabContent.Children.Remove(tab.Editor);
        _tabs.Remove(tab);

        if (_tabs.Count == 0) AddNewTab();
        else if (_activeTab == tab) ActivateTab(_tabs.Last());
    }

    // ── Ajouter un onglet vierge ─────────────────────────────────────────────

    private void AddNewTab()
    {
        var tab = CreateTab(filePath: null);
        _tabs.Add(tab);
        // Insérer avant le bouton paramètres (dernier élément du TabStrip)
        var insertIdx = Math.Max(0, TabStrip.Children.Count - 1);
        TabStrip.Children.Insert(insertIdx, tab.TabBtn);
        TabContent.Children.Add(tab.Editor);
        ActivateTab(tab);
    }

    private void RefreshTabTitle(NoteTab tab) => tab.TitleLbl.Text = tab.DisplayTitle;

    private void BtnAddTab_Click(object s, RoutedEventArgs e) => AddNewTab();

    private void Editor_TextChanged(object sender, RoutedEventArgs e)
    {
        if (_isLoading) return;
        var tab = _tabs.FirstOrDefault(t => t.Editor == sender as RichEditBox);
        if (tab is null) return;

        if (!tab.IsModified)
        {
            tab.IsModified = true;
            RefreshTabTitle(tab);
        }

        _lastEditedTab = tab;
        if (tab == _activeTab)
        {
            SetStatus("…", "#AAAAAA");
            _saveTimer?.Stop();
            _saveTimer?.Start();
        }
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  PARAMÈTRES
    // ══════════════════════════════════════════════════════════════════════════

    private void InitSettings()
    {
        foreach (var f in FontFamilies) FontCombo.Items.Add(f);
        FontCombo.SelectedIndex = 0;
    }

    private void BtnSettings_Click(object s, RoutedEventArgs e)
    {
        if (_settingsOpen) CloseSettingsTab();
        else ShowSettingsTab();
    }

    private void FontCombo_Changed(object s, SelectionChangedEventArgs e)
    {
        if (FontPreview is null || FontCombo.SelectedItem is not string font) return;
        FontPreview.FontFamily = new FontFamily(font);
        foreach (var tab in _tabs)
        {
            tab.Editor.FontFamily = new FontFamily(font);
            tab.Editor.Document.GetRange(0, int.MaxValue).CharacterFormat.Name = font;
        }
    }

    private void SizeSlider_Changed(object s, RangeBaseValueChangedEventArgs e)
    {
        if (SizeLbl is null || FontPreview is null) return;
        var size = (float)e.NewValue;
        SizeLbl.Text         = ((int)size).ToString();
        FontPreview.FontSize = Math.Min(size, 15);
        foreach (var tab in _tabs)
        {
            tab.Editor.FontSize = size;
            tab.Editor.Document.GetRange(0, int.MaxValue).CharacterFormat.Size = size;
        }
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  PALETTE COULEURS + CONTRASTE
    // ══════════════════════════════════════════════════════════════════════════

    private void Swatch_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;
        var tag = btn.Tag?.ToString() ?? "transparent";

        if (tag == "transparent")
        {
            _noteBgColor        = Color.FromArgb(255, 243, 243, 243);
            RootGrid.Background = new SolidColorBrush(Colors.Transparent);
            ApplyBackdrop();
            ApplyTextContrast(243, 243, 243);
        }
        else
        {
            SystemBackdrop       = null;
            var c                = HexToColor(tag);
            _noteBgColor         = c;
            RootGrid.Background  = new SolidColorBrush(c);
            ApplyTextContrast(c.R, c.G, c.B);
        }
    }

    private void ApplyTextContrast(byte r, byte g, byte b)
    {
        _isDarkBg = (0.299 * r + 0.587 * g + 0.114 * b) < 140;
        var fg    = _isDarkBg ? "#F0F0F0" : "#1A1A1A";
        var muted = _isDarkBg ? "#777777" : "#888888";

        ApplyDocumentTextColor(_isDarkBg);

        foreach (var tab in _tabs)
            tab.Editor.Foreground = new SolidColorBrush(HexToColor(fg));

        // Rafraîchit l'onglet actif (indicateur + texte)
        if (_activeTab is not null) ActivateTab(_activeTab);
        else if (_settingsOpen)
        {
            // Mettre à jour les couleurs de l'onglet settings
            if (_settingsTabLbl is not null)
                _settingsTabLbl.Foreground = new SolidColorBrush(HexToColor(fg));
            SettingsOverlay.RequestedTheme = _isDarkBg ? ElementTheme.Dark : ElementTheme.Light;
        }

        // Couleurs de la barre titre
        BrandLabel.Foreground = new SolidColorBrush(HexToColor(muted));
        BrandIcon.Foreground  = new SolidColorBrush(HexToColor(muted));

        Divider.Fill  = new SolidColorBrush(_isDarkBg ? Colors.White : Colors.Black);
        Divider.Opacity  = _isDarkBg ? 0.12 : 0.08;
        Divider2.Fill = new SolidColorBrush(_isDarkBg ? Colors.White : Colors.Black);

        LblBold.Foreground      = new SolidColorBrush(HexToColor(muted));
        LblUnderline.Foreground = new SolidColorBrush(HexToColor(muted));
        UnderlineBar.Fill       = new SolidColorBrush(HexToColor(muted));
        LblHighlight.Foreground = new SolidColorBrush(HexToColor(muted));
        AddTabIcon.Foreground   = new SolidColorBrush(HexToColor(muted));

        PinIcon.Foreground      = new SolidColorBrush(HexToColor(_isPinned ? "#4CAF50" : muted));
        CopyIcon.Foreground     = new SolidColorBrush(HexToColor(muted));
        ClearIcon.Foreground    = new SolidColorBrush(HexToColor(muted));
        SettingsIcon.Foreground = new SolidColorBrush(HexToColor(_settingsOpen ? "#4CAF50" : muted));
        CompactIcon.Foreground  = new SolidColorBrush(HexToColor(muted));

        StatusDot.Fill        = new SolidColorBrush(HexToColor(muted));
        StatusText.Foreground = new SolidColorBrush(HexToColor(muted));

        // Couleurs de la barre de titre Windows selon thème
        if (_appWindow?.TitleBar is { } tb)
        {
            tb.ButtonForegroundColor = _isDarkBg ? Colors.LightGray : Colors.DimGray;
        }
    }

    private void ApplyDocumentTextColor(bool dark)
    {
        var textColor = dark
            ? Color.FromArgb(255, 240, 240, 240)
            : Color.FromArgb(255, 26, 26, 26);

        foreach (var tab in _tabs)
        {
            var all = tab.Editor.Document.GetRange(0, int.MaxValue);
            all.CharacterFormat.ForegroundColor = textColor;
        }
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  BOUTONS ACTION
    // ══════════════════════════════════════════════════════════════════════════

    private void BtnPin_Click(object s, RoutedEventArgs e)
    {
        _isPinned = !_isPinned;
        if (_presenter is not null) _presenter.IsAlwaysOnTop = _isPinned;
        var muted = _isDarkBg ? "#777777" : "#888888";
        PinIcon.Foreground = new SolidColorBrush(HexToColor(_isPinned ? "#4CAF50" : muted));
        SetStatus(_isPinned ? "épinglé" : "prêt", _isPinned ? "#4CAF50" : muted);
    }

    private void BtnCopy_Click(object s, RoutedEventArgs e)
    {
        if (ActiveEditor is null) return;
        ActiveEditor.Document.GetText(TextGetOptions.None, out var text);
        var pkg = new Windows.ApplicationModel.DataTransfer.DataPackage();
        pkg.SetText(text);
        Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(pkg);
        SetStatus("copié !", "#4CAF50");
    }

    private void BtnClear_Click(object s, RoutedEventArgs e)
    {
        if (ActiveEditor is null) return;
        ActiveEditor.Document.SetText(TextSetOptions.None, string.Empty);
        ApplyDocumentTextColor(_isDarkBg);
    }

    private void BtnOpen_Click(object s, RoutedEventArgs e)   => _ = OpenFileAsync();
    private void BtnSaveAs_Click(object s, RoutedEventArgs e) => _ = SaveAsAsync();

    // ══════════════════════════════════════════════════════════════════════════
    //  FORMATAGE
    // ══════════════════════════════════════════════════════════════════════════

    private void BtnBold_Click(object s, RoutedEventArgs e)
    {
        if (ActiveEditor is null) return;
        var sel    = ActiveEditor.Document.Selection;
        var isBold = sel.CharacterFormat.Bold == FormatEffect.On;
        sel.CharacterFormat.Bold = isBold ? FormatEffect.Off : FormatEffect.On;
        LblBold.Foreground = new SolidColorBrush(HexToColor(isBold
            ? (_isDarkBg ? "#777777" : "#888888")
            : (_isDarkBg ? "#F0F0F0" : "#1A1A1A")));
    }

    private void BtnUnderline_Click(object s, RoutedEventArgs e)
    {
        if (ActiveEditor is null) return;
        var sel     = ActiveEditor.Document.Selection;
        var isUnder = sel.CharacterFormat.Underline == UnderlineType.Single;
        sel.CharacterFormat.Underline = isUnder ? UnderlineType.None : UnderlineType.Single;
        var c = HexToColor(isUnder
            ? (_isDarkBg ? "#777777" : "#888888")
            : (_isDarkBg ? "#F0F0F0" : "#1A1A1A"));
        LblUnderline.Foreground = new SolidColorBrush(c);
        UnderlineBar.Fill       = new SolidColorBrush(c);
    }

    private void BtnHighlight_Click(SplitButton s, SplitButtonClickEventArgs e)
        => ApplyHighlight(_highlightColor);

    private void HighlightColor_Click(object s, RoutedEventArgs e)
    {
        if (s is not Button btn) return;
        var tag = btn.Tag?.ToString() ?? "none";
        if (tag == "none") { RemoveHighlight(); return; }
        _highlightColor      = HexToColor(tag);
        HighlightBar.Fill    = new SolidColorBrush(_highlightColor);
        HighlightBar.Opacity = 1.0;
        ApplyHighlight(_highlightColor);
    }

    private void ApplyHighlight(Color color)
    {
        if (ActiveEditor is null) return;
        var sel = ActiveEditor.Document.Selection;
        if (sel.Length == 0) return;
        sel.CharacterFormat.BackgroundColor = color;
    }

    private void RemoveHighlight()
    {
        if (ActiveEditor is null) return;
        var sel = ActiveEditor.Document.Selection;
        if (sel.Length == 0) return;
        sel.CharacterFormat.BackgroundColor = _noteBgColor;
        HighlightBar.Opacity = 0.5;
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  RACCOURCIS CLAVIER
    // ══════════════════════════════════════════════════════════════════════════

    private void KbSave_Invoked(KeyboardAccelerator s, KeyboardAcceleratorInvokedEventArgs e)
    {
        if (_activeTab is not null) _ = SaveTabAsync(_activeTab, forced: true);
        e.Handled = true;
    }

    private void KbSaveAs_Invoked(KeyboardAccelerator s, KeyboardAcceleratorInvokedEventArgs e)
    { _ = SaveAsAsync(); e.Handled = true; }

    private void KbHighlight_Invoked(KeyboardAccelerator s, KeyboardAcceleratorInvokedEventArgs e)
    {
        ActiveEditor?.Focus(FocusState.Programmatic);
        ApplyHighlight(_highlightColor);
        e.Handled = true;
    }

    private void KbOpen_Invoked(KeyboardAccelerator s, KeyboardAcceleratorInvokedEventArgs e)
    { _ = OpenFileAsync(); e.Handled = true; }

    private void KbNewTab_Invoked(KeyboardAccelerator s, KeyboardAcceleratorInvokedEventArgs e)
    { AddNewTab(); e.Handled = true; }

    private void KbCloseTab_Invoked(KeyboardAccelerator s, KeyboardAcceleratorInvokedEventArgs e)
    {
        if (_activeTab is not null) _ = CloseTabAsync(_activeTab);
        e.Handled = true;
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  TIMER AUTO-SAVE
    // ══════════════════════════════════════════════════════════════════════════

    private void InitSaveTimer()
    {
        _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(900) };
        _saveTimer.Tick += async (_, _) =>
        {
            _saveTimer!.Stop();
            if (_lastEditedTab is { } tab && (tab.IsDefault || tab.FilePath is not null))
                await SaveTabAsync(tab);
        };
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  PERSISTANCE — Charger
    // ══════════════════════════════════════════════════════════════════════════

    private async Task LoadFileIntoTabAsync(NoteTab tab, string path)
    {
        if (!File.Exists(path)) return;
        _isLoading = true;
        try
        {
            var bytes = await File.ReadAllBytesAsync(path);
            if (bytes.Length == 0) return;

            bool isRtf = bytes.Length > 5
                      && bytes[0] == (byte)'{' && bytes[1] == (byte)'\\'
                      && bytes[2] == (byte)'r' && bytes[3] == (byte)'t'
                      && bytes[4] == (byte)'f';

            using var ms = new InMemoryRandomAccessStream();
            using var dw = new DataWriter(ms.GetOutputStreamAt(0));
            dw.WriteBytes(bytes);
            await dw.StoreAsync();
            ms.Seek(0);

            if (isRtf)
                tab.Editor.Document.LoadFromStream(TextSetOptions.FormatRtf, ms);
            else
                tab.Editor.Document.SetText(TextSetOptions.None,
                    System.Text.Encoding.UTF8.GetString(bytes));

            tab.IsModified = false;
            RefreshTabTitle(tab);
        }
        catch { /* fichier absent ou corrompu */ }
        finally
        {
            _isLoading = false;
            ApplyDocumentTextColor(_isDarkBg);
            SetStatus("prêt", _isDarkBg ? "#777777" : "#888888");
        }
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  PERSISTANCE — Sauvegarder
    // ══════════════════════════════════════════════════════════════════════════

    private async Task SaveTabAsync(NoteTab tab, bool forced = false)
    {
        var path = tab.IsDefault ? DefaultNotePath : tab.FilePath;
        if (path is null) return;

        try
        {
            Directory.CreateDirectory(NoteDir);
            using var ms = new InMemoryRandomAccessStream();
            tab.Editor.Document.SaveToStream(TextGetOptions.FormatRtf, ms);
            using var dr = new DataReader(ms.GetInputStreamAt(0));
            await dr.LoadAsync((uint)ms.Size);
            var bytes = new byte[ms.Size];
            dr.ReadBytes(bytes);
            await File.WriteAllBytesAsync(path, bytes);

            tab.IsModified = false;
            RefreshTabTitle(tab);
            if (tab == _activeTab)
                SetStatus(forced ? "sauvegardé ✓" : "sauvegardé", "#4CAF50");
        }
        catch { if (tab == _activeTab) SetStatus("erreur d'écriture", "#F44336"); }
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  PERSISTANCE — Enregistrer sous
    // ══════════════════════════════════════════════════════════════════════════

    private async Task SaveAsAsync()
    {
        if (_activeTab is null) return;
        try
        {
            var picker = new FileSavePicker();
            InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
            picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
            picker.SuggestedFileName      = _activeTab.Title;
            picker.FileTypeChoices.Add("Document RTF", new List<string> { ".rtf" });
            picker.FileTypeChoices.Add("Texte brut",   new List<string> { ".txt" });

            var file = await picker.PickSaveFileAsync();
            if (file is null) return;

            SetStatus("enregistrement…", "#AAAAAA");
            var ext = file.FileType.ToLowerInvariant();

            if (ext == ".rtf")
            {
                using var ms = new InMemoryRandomAccessStream();
                _activeTab.Editor.Document.SaveToStream(TextGetOptions.FormatRtf, ms);
                using var dr = new DataReader(ms.GetInputStreamAt(0));
                await dr.LoadAsync((uint)ms.Size);
                var bytes = new byte[ms.Size];
                dr.ReadBytes(bytes);
                await File.WriteAllBytesAsync(file.Path, bytes);
            }
            else
            {
                _activeTab.Editor.Document.GetText(TextGetOptions.None, out var text);
                await File.WriteAllTextAsync(file.Path, text, System.Text.Encoding.UTF8);
            }

            _activeTab.FilePath   = file.Path;
            _activeTab.IsModified = false;
            RefreshTabTitle(_activeTab);
            SetStatus($"enregistré : {file.Name}", "#4CAF50");
        }
        catch { SetStatus("erreur d'enregistrement", "#F44336"); }
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  PERSISTANCE — Ouvrir
    // ══════════════════════════════════════════════════════════════════════════

    private async Task OpenFileAsync()
    {
        try
        {
            var picker = new FileOpenPicker();
            InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
            picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
            picker.FileTypeFilter.Add(".rtf");
            picker.FileTypeFilter.Add(".txt");
            picker.FileTypeFilter.Add(".md");

            var file = await picker.PickSingleFileAsync();
            if (file is null) return;

            if (_activeTab is not null && !_activeTab.IsDefault
                && _activeTab.FilePath is null && !_activeTab.IsModified)
            {
                _activeTab.FilePath = file.Path;
                RefreshTabTitle(_activeTab);
                await LoadFileIntoTabAsync(_activeTab, file.Path);
            }
            else
            {
                var tab = CreateTab(file.Path);
                _tabs.Add(tab);
                var insertIdx = Math.Max(0, TabStrip.Children.Count - 1);
                TabStrip.Children.Insert(insertIdx, tab.TabBtn);
                TabContent.Children.Add(tab.Editor);
                ActivateTab(tab);
                await LoadFileIntoTabAsync(tab, file.Path);
            }
        }
        catch { SetStatus("erreur d'ouverture", "#F44336"); }
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  HELPERS
    // ══════════════════════════════════════════════════════════════════════════

    private void SetStatus(string text, string hex)
    {
        var c = HexToColor(hex);
        StatusText.Text       = text;
        StatusText.Foreground = new SolidColorBrush(c);
        StatusDot.Fill        = new SolidColorBrush(c);
    }

    private static Color HexToColor(string hex)
    {
        hex = hex.TrimStart('#');
        return Color.FromArgb(255,
            Convert.ToByte(hex[0..2], 16),
            Convert.ToByte(hex[2..4], 16),
            Convert.ToByte(hex[4..6], 16));
    }
}
