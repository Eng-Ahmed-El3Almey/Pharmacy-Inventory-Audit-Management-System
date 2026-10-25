using System;
using System.Collections.Generic;
using System.Linq;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using System.Windows.Interop;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using PharmacyInventorySystem.Models;
using PharmacyInventorySystem.Services;

namespace PharmacyInventorySystem
{
    public partial class AuditReportWindow : Window
    {
        private readonly DatabaseService _databaseService = new();
        private readonly AuditReportSession _session;
        private readonly List<AuditReportItem> _items;
        private string _filter = "All";
        private bool _editMode;
        private bool _hasUnsavedChanges;
        private readonly Dictionary<int, decimal> _originalActualQuantities = new();

        private const int DWMWA_NCRENDERING_POLICY = 2;
        private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
        private const int DWMNCRP_ENABLED = 2;
        private const int DWMWCP_ROUND = 2;

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int valueSize);

        public AuditReportWindow(AuditReportSession session, bool openInEditMode = false)
        {
            InitializeComponent();
            ThemeManager.ThemeChanged += ThemeManager_ThemeChanged;
            ThemeSelector.SelectedIndex = ThemeManager.GetCurrentSelectorIndex();
            Closed += AuditReportWindow_Closed;
            _session = session;
            _items = _databaseService.GetAuditReportDetails(session.SessionId);
            foreach (var item in _items)
                item.PropertyChanged += ReportItem_PropertyChanged;

            ReportSubtitleText.Text =
                $"{session.BranchName}   |   {session.StartedAt:dd/MM/yyyy HH:mm}";
            UpdateSummaryCards();

            DetailsGrid.ItemsSource = _items;

            if (openInEditMode && AuthenticationService.HasPermission(u => u.EditReports))
            {
                EnterEditMode();
            }
            else
            {
                _editMode = false;
                DetailsGrid.IsReadOnly = true;
                CaptureOriginalActualQuantities();
                // View mode never exposes editing controls. The Reports list
                // is the place where a permitted user chooses Edit.
                EditReportButton.Visibility = Visibility.Collapsed;
                SaveReportChangesButton.Visibility = Visibility.Collapsed;
            }

            UpdateFooter();
        }

        private List<AuditReportItem> GetVisibleItems()
        {
            var search = SearchBox.Text?.Trim() ?? string.Empty;
            IEnumerable<AuditReportItem> query = _items;

            switch (_filter)
            {
                case "Shortage":
                    query = query.Where(x => x.Difference < 0);
                    break;
                case "Surplus":
                    query = query.Where(x => x.Difference > 0);
                    break;
                case "Variance":
                    query = query.Where(x => x.Difference != 0);
                    break;
                case "Exact":
                    query = query.Where(x => x.Difference == 0);
                    break;
            }

            if (!string.IsNullOrWhiteSpace(search))
            {
                query = query.Where(x =>
                    x.Code.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                    x.ProductName.Contains(search, StringComparison.OrdinalIgnoreCase));
            }

            return query.ToList();
        }

        private void ApplyFilter()
        {
            var visible = GetVisibleItems();
            DetailsGrid.ItemsSource = visible;
            UpdateFooter(visible.Count);
        }

        private void UpdateFooter(int? visibleCount = null)
        {
            int count = visibleCount ?? _items.Count;
            int shortage = _items.Count(x => x.Difference < 0);
            int surplus = _items.Count(x => x.Difference > 0);
            int exact = _items.Count(x => x.Difference == 0);

            FooterText.Text =
                $"Total Items: {_items.Count:N0}   |   Shortage: {shortage:N0}   |   Surplus: {surplus:N0}   |   Exact: {exact:N0}" +
                (count != _items.Count ? $"   |   Showing: {count:N0}" : string.Empty);
        }

        private void SearchBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
            => ApplyFilter();

        private void ReportItem_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(AuditReportItem.ActualQuantity) ||
                e.PropertyName == nameof(AuditReportItem.Difference) ||
                e.PropertyName == nameof(AuditReportItem.TotalValue))
            {
                if (_editMode)
                    UpdateUnsavedChangesState();

                UpdateSummaryCards();
                UpdateFooter();
            }
        }

        private void UpdateUnsavedChangesState()
        {
            _hasUnsavedChanges = _items.Any(item =>
                _originalActualQuantities.TryGetValue(item.CountId, out var original) &&
                original != item.ActualQuantity);
        }

        private void CaptureOriginalActualQuantities()
        {
            _originalActualQuantities.Clear();
            foreach (var item in _items)
                _originalActualQuantities[item.CountId] = item.ActualQuantity;
            _hasUnsavedChanges = false;
        }

        private void FilterAll_Click(object sender, RoutedEventArgs e)
        {
            _filter = "All";
            ApplyFilter();
        }

        private void FilterShortage_Click(object sender, RoutedEventArgs e)
        {
            _filter = "Shortage";
            ApplyFilter();
        }

        private void FilterSurplus_Click(object sender, RoutedEventArgs e)
        {
            _filter = "Surplus";
            ApplyFilter();
        }

        private void FilterVariance_Click(object sender, RoutedEventArgs e)
        {
            _filter = "Variance";
            ApplyFilter();
        }

        private void FilterExact_Click(object sender, RoutedEventArgs e)
        {
            _filter = "Exact";
            ApplyFilter();
        }

        private DataGridColumn? GetActualQuantityColumn()
        {
            return DetailsGrid.Columns.FirstOrDefault(c =>
                string.Equals(c.Header?.ToString(), "Actual Quantity", StringComparison.OrdinalIgnoreCase));
        }

        private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            if (parent == null)
                return null;

            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T typed)
                    return typed;

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

        private void FocusActualQuantityEditor()
        {
            var column = GetActualQuantityColumn();
            if (column == null || DetailsGrid.CurrentCell.Column != column || DetailsGrid.CurrentCell.Item == null)
                return;

            var row = DetailsGrid.ItemContainerGenerator.ContainerFromItem(DetailsGrid.CurrentCell.Item) as DataGridRow;
            if (row == null)
                return;

            var cell = FindVisualChild<DataGridCell>(row);
            if (cell == null || cell.Column != column)
                return;

            var editor = FindVisualChild<TextBox>(cell);
            if (editor == null)
                return;

            editor.Focus();
            editor.SelectAll();
        }

        private void DetailsGrid_BeginningEdit(object sender, DataGridBeginningEditEventArgs e)
        {
            var column = GetActualQuantityColumn();
            if (column == null || e.Column != column)
                return;

            Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(FocusActualQuantityEditor));
        }

        private void DetailsGrid_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (!_editMode)
                return;

            var cell = FindVisualParent<DataGridCell>(e.OriginalSource as DependencyObject);
            var column = GetActualQuantityColumn();
            if (cell == null || column == null || cell.Column != column)
                return;

            Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
            {
                DetailsGrid.SelectedItem = cell.DataContext;
                DetailsGrid.CurrentCell = new DataGridCellInfo(cell.DataContext, column);
                DetailsGrid.BeginEdit();
                FocusActualQuantityEditor();
            }));
        }

        private void ActualQuantityEditor_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is TextBox editor)
            {
                e.Handled = true;
                editor.Focus();
                editor.SelectAll();
            }
        }

        private void DetailsGrid_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (!_editMode || e.Key != Key.Enter)
                return;

            var column = GetActualQuantityColumn();
            var currentItem = DetailsGrid.CurrentCell.Item as AuditReportItem;
            if (column == null || DetailsGrid.CurrentCell.Column != column || currentItem == null)
                return;

            e.Handled = true;
            DetailsGrid.CommitEdit(DataGridEditingUnit.Cell, true);
            DetailsGrid.CommitEdit(DataGridEditingUnit.Row, true);

            var currentIndex = DetailsGrid.Items.IndexOf(currentItem);
            var nextIndex = currentIndex + 1;
            if (currentIndex < 0 || nextIndex >= DetailsGrid.Items.Count)
                return;

            var nextItem = DetailsGrid.Items[nextIndex];
            DetailsGrid.SelectedItem = nextItem;
            DetailsGrid.SelectedIndex = nextIndex;
            DetailsGrid.CurrentCell = new DataGridCellInfo(nextItem, column);
            DetailsGrid.ScrollIntoView(nextItem, column);

            Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
            {
                DetailsGrid.BeginEdit();
                FocusActualQuantityEditor();
            }));
        }

        private void EditReport_Click(object sender, RoutedEventArgs e)
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

            EnterEditMode();
        }

        private void EnterEditMode()
        {
            _editMode = true;
            DetailsGrid.IsReadOnly = false;
            EditReportButton.Visibility = Visibility.Collapsed;
            SaveReportChangesButton.Visibility = Visibility.Visible;
            CaptureOriginalActualQuantities();
            DetailsGrid.Focus();
        }

        private void SaveReportChanges_Click(object sender, RoutedEventArgs e)
        {
            if (!AuthenticationService.HasPermission(u => u.EditReports))
                return;

            try
            {
                DetailsGrid.CommitEdit(System.Windows.Controls.DataGridEditingUnit.Cell, true);
                DetailsGrid.CommitEdit(System.Windows.Controls.DataGridEditingUnit.Row, true);

                if (!_databaseService.UpdateAuditReportItems(_session.SessionId, _items))
                {
                    UiNotificationService.Show(
                        "No report changes were saved.",
                        "Edit Report",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }

                _editMode = false;
                _hasUnsavedChanges = false;
                DetailsGrid.IsReadOnly = true;
                EditReportButton.Visibility = Visibility.Visible;
                SaveReportChangesButton.Visibility = Visibility.Collapsed;
                UpdateSummaryCards();
                ApplyFilter();

                UiNotificationService.Show(
                    "The audit report was updated successfully.",
                    "Report Updated",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                UiNotificationService.Show(
                    "The audit report could not be updated.\n\n" + ex.Message,
                    "Edit Report",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void UpdateSummaryCards()
        {
            decimal shortage = _items.Where(x => x.Difference < 0).Sum(x => Math.Abs(x.TotalValue));
            decimal surplus = _items.Where(x => x.Difference > 0).Sum(x => x.TotalValue);
            decimal net = surplus - shortage;
            ShortageValueText.Text = $"{shortage:N2} EGP";
            SurplusValueText.Text = $"+ {surplus:N2} EGP";
            NetDifferenceText.Text = $"{net:+0.00;-0.00;0.00} EGP";
        }

        private bool ConfirmLeaveEditMode()
        {
            if (!_editMode || !_hasUnsavedChanges)
                return true;

            var result = UiNotificationService.Show(
                "Do you want to save the changes to this audit report before closing?",
                "Save Report Changes",
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Cancel)
                return false;

            if (result == MessageBoxResult.No)
                return true;

            SaveReportChanges_Click(this, new RoutedEventArgs());
            return !_hasUnsavedChanges;
        }

        private void Back_Click(object sender, RoutedEventArgs e)
        {
            if (!ConfirmLeaveEditMode())
                return;
            Close();
        }

        private void AuditReportWindow_Closing(object? sender, CancelEventArgs e)
        {
            if (!ConfirmLeaveEditMode())
            {
                e.Cancel = true;
                return;
            }
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                MaximizeRestore_Click(sender, e);
                return;
            }

            if (e.ButtonState == MouseButtonState.Pressed)
                DragMove();
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

        private void AuditReportWindow_Closed(object? sender, EventArgs e)
        {
            ThemeManager.ThemeChanged -= ThemeManager_ThemeChanged;
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

        private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

        private void MaximizeRestore_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
            MaximizeButton.Content = WindowState == WindowState.Maximized ? "\uE923" : "\uE922";
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();

        private void AuditReportWindow_SourceInitialized(object? sender, EventArgs e)
        {
            try
            {
                var hwnd = new WindowInteropHelper(this).Handle;
                var renderingPolicy = DWMNCRP_ENABLED;
                var cornerPreference = DWMWCP_ROUND;
                DwmSetWindowAttribute(hwnd, DWMWA_NCRENDERING_POLICY, ref renderingPolicy, sizeof(int));
                DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref cornerPreference, sizeof(int));
            }
            catch { }
        }

        private void ExportExcel_Click(object sender, RoutedEventArgs e)
        {
            ExportFiltered("Excel");
        }

        private void ExportPdf_Click(object sender, RoutedEventArgs e)
        {
            ExportFiltered("PDF");
        }

        private void ExportFiltered(string format)
        {
            var visibleItems = GetVisibleItems();
            if (visibleItems.Count == 0)
            {
                UiNotificationService.Show(
                    "There are no items matching the current filter and search.",
                    "Export Report",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            var safeBranch = string.Concat(_session.BranchName.Select(c =>
                System.IO.Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
            var filterName = _filter switch
            {
                "Shortage" => "Shortage",
                "Surplus" => "Surplus",
                "Variance" => "ShortagePlusSurplus",
                "Exact" => "Exact",
                _ => "All"
            };

            var dialog = new SaveFileDialog
            {
                Filter = format == "Excel"
                    ? "Excel Workbook (*.xlsx)|*.xlsx"
                    : "PDF Document (*.pdf)|*.pdf",
                FileName = $"Inventory_Audit_{safeBranch}_{filterName}_{_session.StartedAt:yyyyMMdd_HHmm}"
            };

            if (dialog.ShowDialog() != true)
                return;

            try
            {
                var products = visibleItems.Select(x => new Product
                {
                    Code = x.Code,
                    Name = x.ProductName,
                    System = x.SystemQuantity,
                    Court = x.ActualQuantity,
                    Price = x.Price
                }).ToList();

                if (format == "Excel")
                {
                    ExportService.ExportAuditToExcel(products, _session.BranchName, dialog.FileName);
                }
                else
                {
                    ExportService.ExportAuditToPdf(
                        products,
                        _session.BranchName,
                        dialog.FileName,
                        visibleItems.Count(x => x.Difference == 0),
                        visibleItems.Count(x => x.Difference > 0),
                        visibleItems.Count(x => x.Difference < 0));
                }

                UiNotificationService.Show(
                    $"The {format} audit report was exported successfully.\n\nExported items: {visibleItems.Count:N0}",
                    "Export Complete",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                UiNotificationService.Show(
                    "The audit report could not be exported.\n\n" + ex.Message,
                    "Export Report",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }
    }
}
