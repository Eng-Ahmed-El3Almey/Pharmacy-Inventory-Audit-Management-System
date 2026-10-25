using System;
using System.Collections.Generic;
using System.IO;
using System.Diagnostics;
using System.Linq;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using Microsoft.Win32;
using PharmacyInventorySystem.ViewModels;
using PharmacyInventorySystem.Services;

namespace PharmacyInventorySystem
{
    public partial class MainWindow : Window
    {
        private MainViewModel ViewModel => (MainViewModel)DataContext;

        private const double SidebarExpandedWidth = 220;
        private const double SidebarCollapsedWidth = 68;
        private bool _sidebarCollapsed = false;
        private const string AllBranchesReportOption = "All Branches";
        private readonly ObservableCollection<string> _reportBranchOptions = new();
        private bool _updatingReportBranchSelector;

        private bool _workAreaMaximized;
        private Rect _normalWindowBounds;
        private bool _initialWindowLayoutApplied;
        private bool _inventoryPageActive = true;
        private bool IsClosing { get; set; }
        private bool _restartAfterClose;

        // Keep the native Windows 11/DWM window shadow and rounded corners
        // while retaining the custom application title bar.
        private const int DWMWA_NCRENDERING_POLICY = 2;
        private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
        private const int DWMNCRP_ENABLED = 2;
        private const int DWMWCP_ROUND = 2;

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(
            IntPtr hwnd,
            int attribute,
            ref int value,
            int valueSize);


        public MainWindow()
        {
            InitializeComponent();
            SourceInitialized += MainWindow_SourceInitialized;
            ThemeManager.ThemeChanged += ThemeManager_ThemeChanged;
            ThemeSelector.SelectedIndex = ThemeManager.GetCurrentSelectorIndex();
            Closed += MainWindow_Closed;

            var currentUser = AuthenticationService.CurrentUser;
            CurrentUserText.Text = currentUser == null
                ? "Signed in user"
                : string.IsNullOrWhiteSpace(currentUser.FullName)
                    ? $"@{currentUser.Username}"
                    : currentUser.FullName;
            CurrentUserRoleText.Text = currentUser == null || string.IsNullOrWhiteSpace(currentUser.Role)
                ? "Staff"
                : currentUser.Role;
        }

        private void MainWindow_SourceInitialized(object? sender, EventArgs e)
        {
            try
            {
                var hwnd = new WindowInteropHelper(this).Handle;
                var renderingPolicy = DWMNCRP_ENABLED;
                var cornerPreference = DWMWCP_ROUND;

                DwmSetWindowAttribute(
                    hwnd,
                    DWMWA_NCRENDERING_POLICY,
                    ref renderingPolicy,
                    sizeof(int));

                DwmSetWindowAttribute(
                    hwnd,
                    DWMWA_WINDOW_CORNER_PREFERENCE,
                    ref cornerPreference,
                    sizeof(int));
            }
            catch
            {
                // DWM is optional; the application remains fully functional
                // if the native decoration is unavailable.
            }
        }

        // ---------- Excel drag & drop ----------
        private void MainWindow_PreviewDragOver(object sender, System.Windows.DragEventArgs e)
        {
            if (ViewModel == null || ViewModel.IsLoading || !_inventoryPageActive)
            {
                HideExcelDragDropOverlay();
                e.Effects = System.Windows.DragDropEffects.None;
                e.Handled = true;
                return;
            }

            if (!e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop))
            {
                HideExcelDragDropOverlay();
                e.Effects = System.Windows.DragDropEffects.None;
                e.Handled = true;
                return;
            }

            var files = e.Data.GetData(DataFormats.FileDrop) as string[];
            var valid = files != null && files.Length == 1 && IsExcelFile(files[0]);

            ShowExcelDragDropOverlay(valid);
            e.Effects = valid ? System.Windows.DragDropEffects.Copy : System.Windows.DragDropEffects.None;
            e.Handled = true;
        }

        private void MainWindow_DragLeave(object sender, System.Windows.DragEventArgs e)
        {
            // DragLeave can fire when the pointer moves between child controls.
            // Only hide the overlay when the pointer is actually outside the window.
            var position = e.GetPosition(this);
            if (position.X < 0 || position.Y < 0 || position.X > ActualWidth || position.Y > ActualHeight)
                HideExcelDragDropOverlay();
        }

        private async void MainWindow_PreviewDrop(object sender, System.Windows.DragEventArgs e)
        {
            HideExcelDragDropOverlay();

            if (ViewModel == null || ViewModel.IsLoading)
            {
                e.Handled = true;
                return;
            }

            if (!e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop))
                return;

            var files = e.Data.GetData(DataFormats.FileDrop) as string[];

            if (files == null || files.Length != 1 || !IsExcelFile(files[0]))
            {
                UiNotificationService.Show(
                    "Please drop one Excel inventory file (.xlsx or .xlsm).",
                    "Add Stock via Excel",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                e.Handled = true;
                return;
            }

            e.Handled = true;
            await ViewModel.ConfirmAndLoadInventoryAsync(files[0]);
        }

        private void ShowExcelDragDropOverlay(bool validFile)
        {
            ExcelDragDropOverlay.Visibility = Visibility.Visible;

            if (validFile)
            {
                DragDropTitleText.Text = "Drop Excel File Here";
                DragDropSubtitleText.Text = "Release to import the inventory file";
                DragDropTitleText.Foreground = (System.Windows.Media.Brush)FindResource("HeaderTextBrush");
                DragDropSubtitleText.Foreground = (System.Windows.Media.Brush)FindResource("MutedTextBrush");
            }
            else
            {
                DragDropTitleText.Text = "Excel File Required";
                DragDropSubtitleText.Text = "Please drop one .xlsx or .xlsm inventory file";
                DragDropTitleText.Foreground = (System.Windows.Media.Brush)FindResource("DangerBrush");
                DragDropSubtitleText.Foreground = (System.Windows.Media.Brush)FindResource("DangerBrush");
            }
        }

        private void HideExcelDragDropOverlay()
        {
            if (ExcelDragDropOverlay != null)
                ExcelDragDropOverlay.Visibility = Visibility.Collapsed;
        }

        private static bool IsExcelFile(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                return false;

            var extension = Path.GetExtension(filePath);

            return string.Equals(extension, ".xlsx", System.StringComparison.OrdinalIgnoreCase)
                   || string.Equals(extension, ".xlsm", System.StringComparison.OrdinalIgnoreCase);
        }

        // ---------- Custom title bar behaviour (WindowStyle="None") ----------
        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            if (_initialWindowLayoutApplied)
                return;

            _initialWindowLayoutApplied = true;

            var workArea = SystemParameters.WorkArea;
            var normalWidth = Math.Min(1500, workArea.Width);
            var normalHeight = Math.Min(900, workArea.Height);

            _normalWindowBounds = new Rect(
                workArea.Left + Math.Max(0, (workArea.Width - normalWidth) / 2),
                workArea.Top + Math.Max(0, (workArea.Height - normalHeight) / 2),
                normalWidth,
                normalHeight);

            // Start in the Windows work area so the complete responsive layout is visible.
            // The taskbar remains visible and the maximize button still toggles the window.
            MaximizeToWorkArea();
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                MaximizeRestore_Click(sender, e);
                return;
            }

            if (e.ButtonState == MouseButtonState.Pressed)
            {
                if (_workAreaMaximized)
                {
                    // A single click never toggles the window state.
                    // The window is restored only when the user starts dragging it.
                    if (e.GetPosition(this).Y <= 46)
                    {
                        return;
                    }
                }

                DragMove();
            }
        }

        private void ThemeSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!IsLoaded || ThemeSelector.SelectedIndex < 0)
                return;

            var theme = ThemeSelector.SelectedIndex switch
            {
                1 => AppTheme.Light,
                2 => AppTheme.Blue,
                _ => AppTheme.Dark
            };

            ThemeManager.ApplyTheme(theme);
        }

        private void Minimize_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void MaximizeRestore_Click(object sender, RoutedEventArgs e)
        {
            if (_workAreaMaximized)
            {
                RestoreFromWorkArea();
            }
            else
            {
                _normalWindowBounds = new Rect(Left, Top, Width, Height);
                MaximizeToWorkArea();
            }
        }

        private void MaximizeToWorkArea()
        {
            var workArea = SystemParameters.WorkArea;

            WindowState = WindowState.Normal;
            Left = workArea.Left;
            Top = workArea.Top;
            Width = workArea.Width;
            Height = workArea.Height;

            _workAreaMaximized = true;
        }

        private void RestoreFromWorkArea()
        {
            WindowState = WindowState.Normal;

            if (_normalWindowBounds.Width <= 0 || _normalWindowBounds.Height <= 0)
            {
                var workArea = SystemParameters.WorkArea;
                _normalWindowBounds = new Rect(
                    workArea.Left + 80,
                    workArea.Top + 60,
                    Math.Min(1500, workArea.Width - 160),
                    Math.Min(900, workArea.Height - 120));
            }

            Left = _normalWindowBounds.Left;
            Top = _normalWindowBounds.Top;
            Width = _normalWindowBounds.Width;
            Height = _normalWindowBounds.Height;

            _workAreaMaximized = false;
        }

        private void ThemeManager_ThemeChanged(object? sender, EventArgs e)
        {
            if (ThemeSelector != null)
            {
                var index = ThemeManager.GetCurrentSelectorIndex();
                if (ThemeSelector.SelectedIndex != index)
                    ThemeSelector.SelectedIndex = index;
            }
        }

        private void MainWindow_Closed(object? sender, EventArgs e)
        {
            ThemeManager.ThemeChanged -= ThemeManager_ThemeChanged;

            if (_restartAfterClose && !string.IsNullOrWhiteSpace(Environment.ProcessPath))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = Environment.ProcessPath,
                    UseShellExecute = true
                });
            }
        }

        private void MainWindow_Closing(object? sender, CancelEventArgs e)
        {
            IsClosing = true;
            if (ViewModel == null || !ViewModel.HasUnsavedAuditChanges)
                return;

            var result = UiNotificationService.Show(
                "Do you want to save the current inventory audit report before closing?",
                "Save Before Closing",
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Cancel)
            {
                e.Cancel = true;
                IsClosing = false;
                _restartAfterClose = false;
                return;
            }

            if (result == MessageBoxResult.Yes)
            {
                if (!ViewModel.TrySaveCurrentAuditReport(out var message))
                {
                    UiNotificationService.Show(
                        message,
                        "Save Before Closing",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                    e.Cancel = true;
                    IsClosing = false;
                    _restartAfterClose = false;
                }
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();

        private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            if (parent == null)
                return null;

            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);

                if (child is T typedChild)
                    return typedChild;

                var nested = FindVisualChild<T>(child);
                if (nested != null)
                    return nested;
            }

            return null;
        }

        private static T? FindVisualParent<T>(DependencyObject child) where T : DependencyObject
        {
            var current = child;

            while (current != null)
            {
                if (current is T typed)
                    return typed;

                current = VisualTreeHelper.GetParent(current);
            }

            return null;
        }

        private static T? FindVisualChild<T>(
            DependencyObject parent,
            Func<T, bool> predicate) where T : DependencyObject
        {
            if (parent == null)
                return null;

            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);

                if (child is T typed && predicate(typed))
                    return typed;

                var nested = FindVisualChild(child, predicate);
                if (nested != null)
                    return nested;
            }

            return null;
        }

        private DataGridColumn? GetActualQuantityColumn()
        {
            return DetailedGrid.Columns.FirstOrDefault(
                column => string.Equals(
                    column.Header?.ToString(),
                    "Actual Quantity",
                    StringComparison.OrdinalIgnoreCase));
        }

        private void FocusActualQuantityEditor()
        {
            var column = GetActualQuantityColumn();

            if (column == null || DetailedGrid.CurrentCell.Column != column)
                return;

            if (DetailedGrid.CurrentCell.Item == null)
                return;

            var row = DetailedGrid.ItemContainerGenerator.ContainerFromItem(DetailedGrid.CurrentCell.Item) as DataGridRow;

            if (row == null)
                return;

            var cell = FindVisualChild<DataGridCell>(
                row,
                candidate => candidate.Column == column);

            if (cell == null)
                return;

            var textBox = FindVisualChild<TextBox>(cell);

            if (textBox == null)
                return;

            textBox.Focus();
            textBox.SelectAll();
        }

        private void DetailedGrid_BeginningEdit(object sender, DataGridBeginningEditEventArgs e)
        {
            var column = GetActualQuantityColumn();

            if (column == null || e.Column != column)
                return;

            Dispatcher.BeginInvoke(
                DispatcherPriority.Input,
                new Action(FocusActualQuantityEditor));
        }

        private void DetailedGrid_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var cell = FindVisualParent<DataGridCell>(e.OriginalSource as DependencyObject);
            var column = GetActualQuantityColumn();

            if (cell == null || column == null || cell.Column != column)
                return;

            Dispatcher.BeginInvoke(
                DispatcherPriority.Input,
                new Action(() =>
                {
                    DetailedGrid.SelectedItem = cell.DataContext;
                    DetailedGrid.CurrentCell = new DataGridCellInfo(cell.DataContext, column);
                    DetailedGrid.BeginEdit();
                    FocusActualQuantityEditor();
                }));
        }

        private void DetailedGrid_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter)
                return;

            var column = GetActualQuantityColumn();

            if (column == null || DetailedGrid.CurrentCell.Column != column)
                return;

            var currentItem = DetailedGrid.CurrentCell.Item as Models.Product;

            if (currentItem == null)
                return;

            e.Handled = true;

            DetailedGrid.CommitEdit(DataGridEditingUnit.Cell, true);
            DetailedGrid.CommitEdit(DataGridEditingUnit.Row, true);

            var currentIndex = DetailedGrid.Items.IndexOf(currentItem);
            var nextIndex = currentIndex + 1;

            if (currentIndex < 0 || nextIndex >= DetailedGrid.Items.Count)
                return;

            var nextItem = DetailedGrid.Items[nextIndex];

            DetailedGrid.SelectedItem = nextItem;
            DetailedGrid.SelectedIndex = nextIndex;
            DetailedGrid.CurrentCell = new DataGridCellInfo(nextItem, column);
            DetailedGrid.ScrollIntoView(nextItem, column);

            Dispatcher.BeginInvoke(
                DispatcherPriority.Input,
                new Action(() =>
                {
                    DetailedGrid.BeginEdit();
                    FocusActualQuantityEditor();
                }));
        }

        // ---------- Search mode radio buttons ----------
        private void SearchModeCode_Checked(object sender, RoutedEventArgs e)
        {
            if (DataContext is MainViewModel vm) vm.SearchMode = SearchMode.Code;
        }

        private void SearchModeName_Checked(object sender, RoutedEventArgs e)
        {
            if (DataContext is MainViewModel vm) vm.SearchMode = SearchMode.ProductName;
        }

        private void SearchModePrice_Checked(object sender, RoutedEventArgs e)
        {
            if (DataContext is MainViewModel vm) vm.SearchMode = SearchMode.Price;
        }

        // ---------- Navigation ----------
        private void HomeNavItem_Click(object sender, RoutedEventArgs e)
        {
            ShowHomePage();
        }

        private void ReportsNavItem_Click(object sender, RoutedEventArgs e)
        {
            ShowReportsPage();
        }

        private void ItemMovementNavItem_Click(object sender, RoutedEventArgs e)
        {
            ShowItemMovementPage();
        }

        private void CategoriesNavItem_Click(object sender, RoutedEventArgs e)
        {
            UiNotificationService.Show(
                "The Categories screen is currently under development.\n\nPlease check back in a future version.",
                "Categories",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            ResetNavigationToHome();
        }

        private void UserManagerNavItem_Click(object sender, RoutedEventArgs e)
        {
            if (!AuthenticationService.HasPermission(u => u.UserManagement))
            {
                UiNotificationService.Show(
                    "You do not have permission to manage users.",
                    "User Manager",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                ResetNavigationToHome();
                return;
            }

            ShowUserManagementPage();
        }

        private void ResetNavigationToHome()
        {
            HomeNavItem.IsChecked = true;
            ReportsNavItem.IsChecked = false;
            CategoriesNavItem.IsChecked = false;
            ItemMovementNavItem.IsChecked = false;
            UserManagerNavItem.IsChecked = false;
            ShowHomePage();
        }

        private void ShowHomePage()
        {
            _inventoryPageActive = true;
            HomeContentGrid.Visibility = Visibility.Visible;
            ReportsContentGrid.Visibility = Visibility.Collapsed;
            ItemMovementContentGrid.Visibility = Visibility.Collapsed;
            UserManagementContentGrid.Visibility = Visibility.Collapsed;
        }

        private void ShowReportsPage()
        {
            _inventoryPageActive = false;
            HideExcelDragDropOverlay();
            HomeContentGrid.Visibility = Visibility.Collapsed;
            ReportsContentGrid.Visibility = Visibility.Visible;
            ItemMovementContentGrid.Visibility = Visibility.Collapsed;
            UserManagementContentGrid.Visibility = Visibility.Collapsed;
            ReportsNavItem.IsChecked = true;
            HomeNavItem.IsChecked = false;
            CategoriesNavItem.IsChecked = false;
            ItemMovementNavItem.IsChecked = false;
            UserManagerNavItem.IsChecked = false;

            InitializeReportFilters();
            LoadAuditReports();
        }

        private void ShowItemMovementPage()
        {
            _inventoryPageActive = false;
            HideExcelDragDropOverlay();
            HomeContentGrid.Visibility = Visibility.Collapsed;
            ReportsContentGrid.Visibility = Visibility.Collapsed;
            ItemMovementContentGrid.Visibility = Visibility.Visible;
            UserManagementContentGrid.Visibility = Visibility.Collapsed;

            HomeNavItem.IsChecked = false;
            ReportsNavItem.IsChecked = false;
            CategoriesNavItem.IsChecked = false;
            ItemMovementNavItem.IsChecked = true;
            UserManagerNavItem.IsChecked = false;

            ItemMovementViewControl.RefreshView();
        }

        private void ShowUserManagementPage()
        {
            _inventoryPageActive = false;
            HideExcelDragDropOverlay();
            HomeContentGrid.Visibility = Visibility.Collapsed;
            ReportsContentGrid.Visibility = Visibility.Collapsed;
            ItemMovementContentGrid.Visibility = Visibility.Collapsed;
            UserManagementContentGrid.Visibility = Visibility.Visible;

            HomeNavItem.IsChecked = false;
            ReportsNavItem.IsChecked = false;
            CategoriesNavItem.IsChecked = false;
            ItemMovementNavItem.IsChecked = false;
            UserManagerNavItem.IsChecked = true;

            UserManagementViewControl.RefreshUsers();
        }

        private void RefreshReports_Click(object sender, RoutedEventArgs e)
        {
            LoadAuditReports();
        }

        private void SearchReports_Click(object sender, RoutedEventArgs e)
        {
            LoadAuditReports();
        }

        private void InitializeReportFilters()
        {
            var now = DateTime.Now;
            ReportFromDate.SelectedDate ??= now.Date.AddDays(-30);
            ReportToDate.SelectedDate ??= now.Date;

            _updatingReportBranchSelector = true;
            try
            {
                _reportBranchOptions.Clear();
                _reportBranchOptions.Add(AllBranchesReportOption);

                var branchCandidates = new List<string>();
                branchCandidates.AddRange(ViewModel?.Branches ?? Enumerable.Empty<string>());

                try
                {
                    // Reports can exist even when the current stock has not been loaded.
                    // Include every known stock location plus every location already present
                    // in audit history, so the dropdown always represents the full branch list.
                    var database = new DatabaseService();
                    branchCandidates.AddRange(database.GetAllBranchNames());
                    branchCandidates.AddRange(database.GetAuditReportBranchNames());
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine("Report branch list load failed: " + ex.Message);
                }

                foreach (var branch in branchCandidates
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Select(x => x.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
                {
                    _reportBranchOptions.Add(branch);
                }

                ReportBranchSelector.ItemsSource = _reportBranchOptions;

                var selected = ViewModel?.SelectedBranch;
                if (string.IsNullOrWhiteSpace(selected) ||
                    string.Equals(selected, AllBranchesReportOption, StringComparison.OrdinalIgnoreCase) ||
                    !_reportBranchOptions.Contains(selected, StringComparer.OrdinalIgnoreCase))
                {
                    ReportBranchSelector.SelectedItem = AllBranchesReportOption;
                }
                else
                {
                    ReportBranchSelector.SelectedItem = _reportBranchOptions.First(x =>
                        string.Equals(x, selected, StringComparison.OrdinalIgnoreCase));
                }
            }
            finally
            {
                _updatingReportBranchSelector = false;
            }
        }

        private void ReportBranchSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_updatingReportBranchSelector)
                return;

            if (ReportBranchSelector.SelectedItem is string)
                LoadAuditReports();
        }

        private DateTime GetReportDateTime(
            System.Windows.Controls.DatePicker datePicker,
            bool endOfDay)
        {
            var date = datePicker.SelectedDate?.Date ?? DateTime.Today;
            return endOfDay ? date.AddDays(1).AddTicks(-1) : date;
        }

        private void LoadAuditReports()
        {
            try
            {
                var from = GetReportDateTime(ReportFromDate, false);
                var to = GetReportDateTime(ReportToDate, true);

                if (to < from)
                {
                    UiNotificationService.Show(
                        "The end date and time must be after the start date and time.",
                        "Reports",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }

                string? branch = ReportBranchSelector.SelectedItem?.ToString();

                if (string.Equals(branch, AllBranchesReportOption, StringComparison.OrdinalIgnoreCase))
                    branch = null;

                var reports = new Services.DatabaseService()
                    .GetAuditReportSessions(branch, from, to);

                AuditSessionsPanel.Children.Clear();

                decimal shortage = reports.Sum(x => x.ShortageValue);
                decimal surplus = reports.Sum(x => x.SurplusValue);
                int totalItems = reports.Sum(x => x.ItemCount);

                int shortageItems = reports.Sum(x => x.ShortageItemCount);
                int surplusItems = reports.Sum(x => x.SurplusItemCount);

                ReportShortageValueText.Text = $"{shortage:N2} EGP";
                ReportSurplusValueText.Text = $"+ {surplus:N2} EGP";
                ReportNetDifferenceText.Text = $"{surplus - shortage:+0.00;-0.00;0.00} EGP";
                ReportTotalItemsText.Text = totalItems.ToString("N0");
                ReportShortageItemsText.Text = shortageItems.ToString("N0");
                ReportSurplusItemsText.Text = surplusItems.ToString("N0");
                ReportResultsText.Text = $"{reports.Count:N0} report(s)";

                NoReportsText.Visibility = reports.Count == 0
                    ? Visibility.Visible
                    : Visibility.Collapsed;

                foreach (var report in reports)
                    AuditSessionsPanel.Children.Add(CreateAuditSessionRow(report));
            }
            catch (Exception ex)
            {
                UiNotificationService.Show(
                    "The audit reports could not be loaded from SQL Server.\n\n" + ex.Message,
                    "Reports",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private Border CreateAuditSessionRow(Models.AuditReportSession report)
        {
            var grid = new Grid { Height = 56, MinWidth = 1080 };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.15, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.45, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.60, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.95, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.95, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.75, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2.45, GridUnitType.Star) });

            AddReportCell(grid, report.StartedAt.ToString("dd/MM/yyyy hh:mm tt"), 0);
            AddReportCell(grid, report.BranchName, 1);
            AddReportCell(grid, report.ItemCount.ToString("N0"), 2);
            AddReportCell(grid, report.ShortageValue.ToString("N2"), 3, "NegativeTextBrush");
            AddReportCell(grid, report.SurplusValue.ToString("N2"), 4, "WarningTextBrush");

            var status = new Border
            {
                Background = (Brush)FindResource("PositiveBackBrush"),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(9, 4, 9, 4),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock
                {
                    Text = report.Status,
                    Foreground = (Brush)FindResource("PositiveTextBrush"),
                    FontSize = 11,
                    FontWeight = FontWeights.SemiBold,
                    HorizontalAlignment = HorizontalAlignment.Center
                }
            };
            Grid.SetColumn(status, 5);
            grid.Children.Add(status);

            var actionPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center
            };

            var viewButton = new Button
            {
                Content = "\uE8A9  View",
                Tag = report,
                Style = (Style)FindResource("AccentButtonStyle"),
                Width = 72,
                Height = 34,
                Padding = new Thickness(10, 0, 10, 0),
                ToolTip = "View report",
                FontFamily = new FontFamily("Segoe UI, Segoe MDL2 Assets"),
                FontSize = 11,
                FontWeight = FontWeights.SemiBold
            };
            viewButton.Click += ViewAuditReport_Click;
            actionPanel.Children.Add(viewButton);

            if (AuthenticationService.HasPermission(u => u.EditReports))
            {
                var editButton = new Button
                {
                    Content = "\uE70F  Edit",
                    Tag = report,
                    Style = (Style)FindResource("AccentButtonStyle"),
                    Width = 68,
                    Height = 34,
                    Padding = new Thickness(10, 0, 10, 0),
                    Margin = new Thickness(8, 0, 0, 0),
                    ToolTip = "Edit report",
                    FontFamily = new FontFamily("Segoe UI, Segoe MDL2 Assets"),
                    FontSize = 11,
                    FontWeight = FontWeights.SemiBold
                };
                editButton.Click += EditAuditReport_Click;
                actionPanel.Children.Add(editButton);
            }

            if (AuthenticationService.HasPermission(u => u.DeleteReports))
            {
                var deleteButton = new Button
                {
                    Content = "\uE74D  Delete",
                    Tag = report,
                    Style = (Style)FindResource("DangerButtonStyle"),
                    Width = 78,
                    Height = 34,
                    Padding = new Thickness(10, 0, 10, 0),
                    Margin = new Thickness(8, 0, 0, 0),
                    ToolTip = "Delete report",
                    FontFamily = new FontFamily("Segoe UI, Segoe MDL2 Assets"),
                    FontSize = 11,
                    FontWeight = FontWeights.SemiBold
                };
                deleteButton.Click += DeleteAuditReport_Click;
                actionPanel.Children.Add(deleteButton);
            }

            Grid.SetColumn(actionPanel, 6);
            grid.Children.Add(actionPanel);

            return new Border
            {
                BorderBrush = (Brush)FindResource("BorderBrush2"),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Child = grid
            };
        }

        private void AddReportCell(Grid grid, string text, int column, string brushKey = "BodyTextBrush")
        {
            var block = new TextBlock
            {
                Text = text,
                Foreground = (Brush)FindResource(brushKey),
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(12, 0, 8, 0),
                TextTrimming = TextTrimming.CharacterEllipsis
            };

            Grid.SetColumn(block, column);
            grid.Children.Add(block);
        }

        private void DeleteAuditReport_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button || button.Tag is not Models.AuditReportSession report)
                return;

            if (!AuthenticationService.HasPermission(u => u.DeleteReports))
            {
                UiNotificationService.Show(
                    "You do not have permission to delete audit reports.",
                    "Delete Audit Report",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            var authWindow = new LoginWindow(
                "Administrator Verification",
                "Administrator Verification",
                "Enter the administrator username and password to delete this report.")
            {
                Owner = this
            };

            authWindow.ShowDialog();

            if (authWindow.DialogResult != true)
                return;

            var confirmation = UiNotificationService.Show(
                $"Are you sure you want to permanently delete this audit report?\n\n" +
                $"Branch / Warehouse: {report.BranchName}\n" +
                $"Date: {report.StartedAt:dd/MM/yyyy hh:mm tt}\n" +
                $"Items: {report.ItemCount:N0}",
                "Delete Audit Report",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (confirmation != MessageBoxResult.Yes)
                return;

            try
            {
                var deleted = new DatabaseService().DeleteAuditReport(report.SessionId);

                if (!deleted)
                {
                    UiNotificationService.Show(
                        "The selected audit report could not be found or was already deleted.",
                        "Delete Audit Report",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                    return;
                }

                UiNotificationService.Show(
                    "The audit report was deleted successfully.",
                    "Delete Audit Report",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                LoadAuditReports();
            }
            catch (Exception ex)
            {
                UiNotificationService.Show(
                    "The audit report could not be deleted from SQL Server.\n\n" + ex.Message,
                    "Delete Audit Report",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void EditAuditReport_Click(object sender, RoutedEventArgs e)
        {
            if (!AuthenticationService.HasPermission(u => u.EditReports))
            {
                UiNotificationService.Show(
                    "You do not have permission to edit audit reports.",
                    "Permission Required",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            if (sender is Button button && button.Tag is Models.AuditReportSession report)
            {
                var window = new AuditReportWindow(report, openInEditMode: true)
                {
                    Owner = this
                };
                window.ShowDialog();
                LoadAuditReports();
            }
        }

        private void ViewAuditReport_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is Models.AuditReportSession report)
            {
                var window = new AuditReportWindow(report, openInEditMode: false)
                {
                    Owner = this
                };
                window.ShowDialog();
            }
        }

        // ---------- Sidebar: Export to Image (renders the Detailed grid to a PNG) ----------
        private void ExportToImage_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new SaveFileDialog { Filter = "PNG Image (*.png)|*.png", FileName = "Inventory_Audit.png" };
            if (dialog.ShowDialog() != true) return;

            var element = DetailedGrid;
            var size = new Size(element.ActualWidth, element.ActualHeight);

            var bitmap = new RenderTargetBitmap((int)size.Width, (int)size.Height, 96, 96, PixelFormats.Pbgra32);
            var visual = new DrawingVisual();
            using (var context = visual.RenderOpen())
            {
                var brush = new VisualBrush(element);
                context.DrawRectangle(brush, null, new Rect(new Point(), size));
            }
            bitmap.Render(visual);

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = File.Create(dialog.FileName);
            encoder.Save(stream);

            UiNotificationService.Show("Exported the detailed table as an image.", "Export to Image", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void RestartApplication_Click(object sender, RoutedEventArgs e)
        {
            var result = UiNotificationService.Show(
                "Restart the application now?",
                "Restart Application",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes)
                return;

            _restartAfterClose = true;
            Close();
        }

        private void SwitchUser_Click(object sender, RoutedEventArgs e)
        {
            var loginWindow = new LoginWindow(
                "Switch User",
                "Switch User",
                "Sign in with another user account.")
            {
                Owner = this
            };

            Hide();
            try
            {
                var result = loginWindow.ShowDialog();
                if (result == true)
                {
                    RefreshCurrentUserDisplay();
                    ResetNavigationToHome();
                }
            }
            finally
            {
                if (!IsVisible && !IsClosing)
                    Show();
            }
        }

        private void RefreshCurrentUserDisplay()
        {
            var currentUser = AuthenticationService.CurrentUser;
            CurrentUserText.Text = currentUser == null
                ? "Signed in user"
                : string.IsNullOrWhiteSpace(currentUser.FullName)
                    ? $"@{currentUser.Username}"
                    : currentUser.FullName;

            CurrentUserRoleText.Text = currentUser == null || string.IsNullOrWhiteSpace(currentUser.Role)
                ? "Staff"
                : currentUser.Role;
        }

        // ---------- Collapsible sidebar ----------
        // Single click: toggle collapsed/expanded. Double click: always force it open (and it stays open).
        private void SidebarToggle_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount >= 2)
            {
                _sidebarCollapsed = false;
            }
            else
            {
                _sidebarCollapsed = !_sidebarCollapsed;
            }

            ApplySidebarState();
        }

        private void ApplySidebarState()
        {
            SidebarColumn.Width = new GridLength(_sidebarCollapsed ? SidebarCollapsedWidth : SidebarExpandedWidth);

            var visibility = _sidebarCollapsed ? Visibility.Collapsed : Visibility.Visible;

            HomeLabel.Visibility = visibility;
            ReportsNavLabel.Visibility = visibility;
            ItemMovementNavLabel.Visibility = visibility;
            CategoriesNavLabel.Visibility = visibility;
            UserManagerNavLabel.Visibility = visibility;
            ClearStockLabel.Visibility = visibility;
            OptionsSectionLabel.Visibility = visibility;
            ZeroStockOptionRow.Visibility = visibility;
            UncountedOptionRow.Visibility = visibility;
            SummaryCardsOptionRow.Visibility = visibility;
            ExportSectionLabel.Visibility = visibility;
            SaveAuditReportLabel.Visibility = visibility;
            ExportExcelLabel.Visibility = visibility;
            ExportPdfLabel.Visibility = visibility;
            ExportImageLabel.Visibility = visibility;

            // Flip the hamburger icon direction so it hints at the action (open vs close).
            SidebarToggleIcon.Text = _sidebarCollapsed ? "\uE700" : "\uE701";
        }
    }
}
