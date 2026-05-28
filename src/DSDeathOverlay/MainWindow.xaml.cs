using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using DSDeathOverlay.Memory;
using DSDeathOverlay.Services;
using DSDeathOverlay.Settings;

namespace DSDeathOverlay;

/// <summary>
/// Transparent, click-through, always-on-top overlay window.
///
/// Two display modes:
///   * Normal (default):  WS_EX_TRANSPARENT is set, all clicks pass through to the game.
///   * Edit:              WS_EX_TRANSPARENT is cleared, the user can drag the window
///                        with the left mouse button. Background tints to make the
///                        edit state obvious. Toggled by the F8 hotkey.
/// </summary>
public partial class MainWindow : Window, INotifyPropertyChanged
{
    private const int HotkeyIdToggleEdit       = 1;
    private const int HotkeyIdShowHide         = 2;
    private const int HotkeyIdResetPosition    = 3;
    private const int HotkeyIdCloseApp         = 4;
    private const int HotkeyIdToggleBossList   = 5;
    private const int HotkeyIdCycleBossForward = 6;
    private const int HotkeyIdCycleBossBack    = 7;

    /// <summary>Default overlay position used by Shift+F8 reset and on first launch.</summary>
    private const double DefaultLeft = 20;
    private const double DefaultTop  = 20;

    private DispatcherTimer? _topmostKeeper;
    private DispatcherTimer? _bossSnapshotTimer;
    private DeathPoller? _poller;
    private SettingsStore? _settings;
    private BossDeathTracker? _bossTracker;
    private HwndSource? _hwndSource;
    private IntPtr _hwnd;

    private bool _isEditMode;
    private bool _isVisible = true;

    public OverlayViewModel ViewModel { get; } = new();

    public bool IsEditMode
    {
        get => _isEditMode;
        private set
        {
            if (_isEditMode == value) return;
            _isEditMode = value;
            ApplyClickThroughStyle();
            OnPropertyChanged();
        }
    }

    public MainWindow()
    {
        InitializeComponent();
        DataContext = ViewModel;
    }

    /// <summary>Wire up dependencies. Called by <see cref="App"/> at startup.</summary>
    public void Initialize(DeathPoller poller, SettingsStore settings, BossDeathTracker bossTracker)
    {
        _poller = poller;
        _settings = settings;
        _bossTracker = bossTracker;

        // Apply persisted position / font size before showing the window.
        Left = settings.Current.Left;
        Top  = settings.Current.Top;
        DeathText.FontSize = settings.Current.FontSize;
        ViewModel.IsBossListExpanded = settings.Current.IsBossListExpanded;

        poller.Updated += OnPollerUpdated;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        _hwnd = new WindowInteropHelper(this).Handle;
        _hwndSource = HwndSource.FromHwnd(_hwnd);
        _hwndSource?.AddHook(WndProc);

        ApplyClickThroughStyle();
        RegisterHotkeys();

        // Re-assert topmost every second. Borderless fullscreen games sometimes
        // bump our HWND down a slot when they take focus.
        _topmostKeeper = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(1),
        };
        _topmostKeeper.Tick += (_, _) => ReAssertTopmost();
        _topmostKeeper.Start();

        // Pull a fresh boss snapshot a few times per second so death counts
        // and the active-boss highlight stay current without coupling the UI
        // to the poller's thread.
        _bossSnapshotTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(300),
        };
        _bossSnapshotTimer.Tick += (_, _) => RefreshBossSnapshot();
        _bossSnapshotTimer.Start();
    }

    /// <summary>
    /// Apply the extended window styles required for an always-on-top, click-through,
    /// non-activating overlay. Called whenever <see cref="IsEditMode"/> flips.
    /// </summary>
    private void ApplyClickThroughStyle()
    {
        if (_hwnd == IntPtr.Zero) return;

        int ex = NativeMethods.GetWindowLong(_hwnd, NativeMethods.GWL_EXSTYLE);

        // Always on: non-activating, no taskbar entry, layered (for transparency).
        ex |= NativeMethods.WS_EX_NOACTIVATE
            | NativeMethods.WS_EX_TOOLWINDOW
            | NativeMethods.WS_EX_LAYERED
            | NativeMethods.WS_EX_TOPMOST;

        // Click-through only outside edit mode.
        if (_isEditMode)
            ex &= ~NativeMethods.WS_EX_TRANSPARENT;
        else
            ex |= NativeMethods.WS_EX_TRANSPARENT;

        NativeMethods.SetWindowLong(_hwnd, NativeMethods.GWL_EXSTYLE, ex);
    }

    private void ReAssertTopmost()
    {
        if (_hwnd == IntPtr.Zero || !_isVisible) return;
        NativeMethods.SetWindowPos(
            _hwnd, NativeMethods.HWND_TOPMOST, 0, 0, 0, 0,
            NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
    }

    private void RegisterHotkeys()
    {
        NativeMethods.RegisterHotKey(_hwnd, HotkeyIdToggleEdit,       NativeMethods.MOD_NONE,  NativeMethods.VK_F8);
        NativeMethods.RegisterHotKey(_hwnd, HotkeyIdShowHide,         NativeMethods.MOD_NONE,  NativeMethods.VK_F9);
        NativeMethods.RegisterHotKey(_hwnd, HotkeyIdResetPosition,    NativeMethods.MOD_SHIFT, NativeMethods.VK_F8);
        NativeMethods.RegisterHotKey(_hwnd, HotkeyIdCloseApp,         NativeMethods.MOD_SHIFT, NativeMethods.VK_F9);
        NativeMethods.RegisterHotKey(_hwnd, HotkeyIdToggleBossList,   NativeMethods.MOD_NONE,  NativeMethods.VK_F10);
        NativeMethods.RegisterHotKey(_hwnd, HotkeyIdCycleBossForward, NativeMethods.MOD_NONE,  NativeMethods.VK_F11);
        NativeMethods.RegisterHotKey(_hwnd, HotkeyIdCycleBossBack,    NativeMethods.MOD_SHIFT, NativeMethods.VK_F11);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_HOTKEY)
        {
            int id = wParam.ToInt32();
            if (id == HotkeyIdToggleEdit)
            {
                IsEditMode = !IsEditMode;
                handled = true;
            }
            else if (id == HotkeyIdShowHide)
            {
                ToggleVisible();
                handled = true;
            }
            else if (id == HotkeyIdResetPosition)
            {
                ResetPosition();
                handled = true;
            }
            else if (id == HotkeyIdCloseApp)
            {
                Application.Current.Shutdown();
                handled = true;
            }
            else if (id == HotkeyIdToggleBossList)
            {
                ViewModel.IsBossListExpanded = !ViewModel.IsBossListExpanded;
                handled = true;
            }
            else if (id == HotkeyIdCycleBossForward)
            {
                _bossTracker?.CycleActiveBoss(+1);
                RefreshBossSnapshot();
                handled = true;
            }
            else if (id == HotkeyIdCycleBossBack)
            {
                _bossTracker?.CycleActiveBoss(-1);
                RefreshBossSnapshot();
                handled = true;
            }
        }
        return IntPtr.Zero;
    }

    private void ToggleVisible()
    {
        _isVisible = !_isVisible;
        Visibility = _isVisible ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ResetPosition()
    {
        // Settings are persisted on close, so no mid-session save is needed.
        Left = DefaultLeft;
        Top  = DefaultTop;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
        => Application.Current.Shutdown();

    private void ResetBossesButton_Click(object sender, RoutedEventArgs e)
    {
        _bossTracker?.ResetAllForCurrentGame();
        RefreshBossSnapshot();
    }

    private void BossRow_Click(object sender, RoutedEventArgs e)
    {
        // Clicking is only useful in edit mode; outside edit mode the window is
        // click-through and this handler can't be reached anyway.
        if (!_isEditMode) return;
        if (sender is not Button btn) return;
        if (btn.Tag is not string bossId || string.IsNullOrEmpty(bossId)) return;

        _bossTracker?.SetActiveBoss(bossId);
        RefreshBossSnapshot();
    }

    private void RefreshBossSnapshot()
    {
        if (_bossTracker is null) return;
        var snapshot = _bossTracker.BuildSnapshot();
        ViewModel.ApplyBossSnapshot(snapshot);
    }

    private void OnPollerUpdated(object? sender, DeathCountEventArgs e)
    {
        // The poller runs on a background thread; marshal to the UI thread.
        Dispatcher.BeginInvoke(new Action(() =>
        {
            ViewModel.ApplyUpdate(e);
            RefreshBossSnapshot();
        }));
    }

    private void RootBorder_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!_isEditMode) return; // shouldn't reach here when click-through, but be defensive

        // Clicks on the per-boss buttons should select that boss, not drag the window.
        if (e.OriginalSource is DependencyObject src && IsInsideBossRow(src)) return;

        try
        {
            DragMove();
        }
        catch (InvalidOperationException)
        {
            // DragMove can throw if the mouse is already released; ignore.
        }
    }

    private static bool IsInsideBossRow(DependencyObject src)
    {
        // Walk up the visual tree; if we hit a Button, the click was on a clickable
        // row element and the button's own handler should take it from here.
        DependencyObject? cursor = src;
        while (cursor is not null)
        {
            if (cursor is Button) return true;
            cursor = System.Windows.Media.VisualTreeHelper.GetParent(cursor);
        }
        return false;
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        // Persist position / font size / panel state on close.
        if (_settings is not null)
        {
            _settings.Current = _settings.Current with
            {
                Left = Left,
                Top = Top,
                FontSize = DeathText.FontSize,
                IsBossListExpanded = ViewModel.IsBossListExpanded,
            };
            _settings.Save();
        }

        _bossTracker?.Save();

        NativeMethods.UnregisterHotKey(_hwnd, HotkeyIdToggleEdit);
        NativeMethods.UnregisterHotKey(_hwnd, HotkeyIdShowHide);
        NativeMethods.UnregisterHotKey(_hwnd, HotkeyIdResetPosition);
        NativeMethods.UnregisterHotKey(_hwnd, HotkeyIdCloseApp);
        NativeMethods.UnregisterHotKey(_hwnd, HotkeyIdToggleBossList);
        NativeMethods.UnregisterHotKey(_hwnd, HotkeyIdCycleBossForward);
        NativeMethods.UnregisterHotKey(_hwnd, HotkeyIdCycleBossBack);

        _topmostKeeper?.Stop();
        _bossSnapshotTimer?.Stop();
        if (_poller is not null) _poller.Updated -= OnPollerUpdated;
        _hwndSource?.RemoveHook(WndProc);

        base.OnClosing(e);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
