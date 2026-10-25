using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Data;
using System.Threading.Tasks;
using System.ComponentModel;
using Microsoft.Win32;
using PharmacyInventorySystem.Models;
using PharmacyInventorySystem.Services;

namespace PharmacyInventorySystem.ViewModels
{
    public enum SearchMode
    {
        Code,
        ProductName,
        Price
    }

    public class MainViewModel : ViewModelBase
    {
        private const string AllBranchesOption = "All Branches";
        private const string IndependentWarehousesName = "مخازن مستقلة";

        private readonly BulkObservableCollection<Product> _allProducts =
            new BulkObservableCollection<Product>();

        private readonly Dictionary<string, string> _branchDisplayToValue =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        private readonly DatabaseService _databaseService = new DatabaseService();

        private BranchNode _selectedBranchNode;
        private string _selectedBranch = AllBranchesOption;
        private string _searchText = string.Empty;
        private SearchMode _searchMode = SearchMode.Code;
        private string _loadedFileName = string.Empty;
        private bool _hasInventoryData;
        private bool _showZeroStock;
        private bool _showUncountedOnly;
        private bool _showSummaryCards = true;
        private bool _isLoading;
        private bool _requiresBranchSelection;
        private int _importProgress;
        private string _importProgressText = string.Empty;
        private bool _hasUnsavedAuditChanges;

        public ObservableCollection<Product> AllProducts => _allProducts;

        public ICollectionView ProductsView { get; }

        public ObservableCollection<BranchNode> BranchTree { get; } =
            new ObservableCollection<BranchNode>();

        public BranchNode SelectedBranchNode
        {
            get => _selectedBranchNode;
            set
            {
                if (SetField(ref _selectedBranchNode, value))
                {
                    if (value == null || value.IsGroup)
                        return;

                    SelectedBranch = value.Value;
                }
            }
        }

        public string SelectedBranch
        {
            get => _selectedBranch;
            set
            {
                if (!SetField(ref _selectedBranch, value))
                    return;

                if (!string.IsNullOrWhiteSpace(value))
                    _requiresBranchSelection = false;

                RefreshFilteredView();
                ApplyBranchToVisibleProducts();
            }
        }

        // Kept for compatibility with existing UI/code.
        // The new branch UI can use BranchTree instead.
        public ObservableCollection<string> Branches { get; } =
            new ObservableCollection<string>();

        /// <summary>
        /// Returns the actual branch or warehouse value represented by the
        /// selected item in the branch ComboBox.
        /// </summary>
        private string? GetSelectedBranchValue()
        {
            if (string.IsNullOrWhiteSpace(SelectedBranch) ||
                SelectedBranch == AllBranchesOption)
            {
                return null;
            }

            if (_branchDisplayToValue.TryGetValue(SelectedBranch, out var value))
                return value;

            return SelectedBranch;
        }

        public string SearchText
        {
            get => _searchText;
            set
            {
                if (SetField(ref _searchText, value))
                    RefreshFilteredView();
            }
        }

        public SearchMode SearchMode
        {
            get => _searchMode;
            set
            {
                if (SetField(ref _searchMode, value))
                    RefreshFilteredView();
            }
        }

        // Dark Mode is the only active theme for this release.
        // Light Mode is reserved for a future update.
        public bool IsDarkTheme
        {
            get => true;
            set { }
        }

        public bool HasUnsavedAuditChanges
        {
            get => _hasUnsavedAuditChanges;
            private set => SetField(ref _hasUnsavedAuditChanges, value);
        }

        public string LoadedFileName
        {
            get => _loadedFileName;
            private set => SetField(ref _loadedFileName, value);
        }

        public bool ShowZeroStock
        {
            get => _showZeroStock;
            set
            {
                if (SetField(ref _showZeroStock, value))
                    RefreshFilteredView();
            }
        }

        public bool ShowUncountedOnly
        {
            get => _showUncountedOnly;
            set
            {
                if (SetField(ref _showUncountedOnly, value))
                    RefreshFilteredView();
            }
        }

        public bool ShowSummaryCards
        {
            get => _showSummaryCards;
            set
            {
                if (SetField(ref _showSummaryCards, value))
                    OnPropertyChanged(nameof(SummaryCardsVisibility));
            }
        }

        public Visibility SummaryCardsVisibility =>
            ShowSummaryCards ? Visibility.Visible : Visibility.Collapsed;

        public bool IsLoading
        {
            get => _isLoading;
            private set
            {
                if (SetField(ref _isLoading, value))
                {
                    OnPropertyChanged(nameof(LoadingVisibility));
                    OnPropertyChanged(nameof(MainContentVisibility));
                    ImportExcelCommand?.RaiseCanExecuteChanged();
                    ClearInventoryCommand?.RaiseCanExecuteChanged();
                    SaveAuditReportCommand?.RaiseCanExecuteChanged();
                }
            }
        }

        public int ImportProgress
        {
            get => _importProgress;
            private set
            {
                if (SetField(ref _importProgress, value))
                    OnPropertyChanged(nameof(ImportProgressFraction));
            }
        }

        // Kept between 0 and 1 so the ProgressBar can never receive a value
        // outside its default RangeBase limits during XAML initialization.
        public double ImportProgressFraction =>
            Math.Max(0.0, Math.Min(1.0, ImportProgress / 100.0));

        public string ImportProgressText
        {
            get => _importProgressText;
            private set => SetField(ref _importProgressText, value);
        }

        public Visibility LoadingVisibility =>
            IsLoading ? Visibility.Visible : Visibility.Collapsed;

        // Import progress is informational. Keep the application navigable while
        // Excel/SQL processing is running so the user can move between screens.
        public Visibility MainContentVisibility => Visibility.Visible;

        public bool HasInventoryData
        {
            get => _hasInventoryData;
            private set
            {
                if (SetField(ref _hasInventoryData, value))
                {
                    OnPropertyChanged(nameof(EmptyStateVisibility));
                    OnPropertyChanged(nameof(InventoryVisibility));
                    OnPropertyChanged(nameof(BranchSelectionEnabled));
                    OnPropertyChanged(nameof(ImportButtonText));
                    ClearInventoryCommand?.RaiseCanExecuteChanged();
                    SaveAuditReportCommand?.RaiseCanExecuteChanged();
                }
            }
        }

        public string ImportButtonText =>
            HasInventoryData ? "Replace Stock via Excel" : "Add Stock via Excel";

        public Visibility EmptyStateVisibility =>
            HasInventoryData ? Visibility.Collapsed : Visibility.Visible;

        public Visibility InventoryVisibility =>
            HasInventoryData ? Visibility.Visible : Visibility.Collapsed;

        public bool BranchSelectionEnabled => HasInventoryData;

        public int TotalItems => _allProducts.Count;

        public int FilteredCount =>
            ProductsView.Cast<Product>().Count();

        public int PerfectCount =>
            ProductsView.Cast<Product>().Count(p => p.Diff == 0);

        public int SurplusCount =>
            ProductsView.Cast<Product>().Count(p => p.Diff > 0);

        public int DeficitCount =>
            ProductsView.Cast<Product>().Count(p => p.Diff < 0);

        public decimal SurplusValue =>
            ProductsView.Cast<Product>()
                .Where(p => p.Diff > 0)
                .Sum(p => p.Diff * p.Price);

        public decimal DeficitValue =>
            ProductsView.Cast<Product>()
                .Where(p => p.Diff < 0)
                .Sum(p => Math.Abs(p.Diff * p.Price));

        public string PerfectPercent =>
            FormatPercent(PerfectCount);

        public string SurplusPercent =>
            FormatPercent(SurplusCount);

        public string DeficitPercent =>
            FormatPercent(DeficitCount);

        private string FormatPercent(int count)
        {
            return FilteredCount == 0
                ? "0%"
                : $"{Math.Round(count * 100.0 / FilteredCount)}%";
        }

        public RelayCommand ExportAuditExcelCommand { get; }
        public RelayCommand ExportAuditPdfCommand { get; }
        public RelayCommand SaveAuditReportCommand { get; }
        public RelayCommand ImportExcelCommand { get; }
        public RelayCommand ClearInventoryCommand { get; }

        public MainViewModel()
        {
            // IMPORTANT:
            // The application starts completely empty.
            // No inventory Excel file is loaded automatically.
            ProductsView =
                CollectionViewSource.GetDefaultView(_allProducts);

            ProductsView.Filter = FilterProduct;

            // Start with a clean in-memory state. No inventory is restored
            // automatically; the user explicitly loads an Excel workbook.
            ClearInventoryState();
            //TryLoadInventoryFromDatabase();

            ExportAuditExcelCommand =
                new RelayCommand(_ =>
                    ExportAuditExcel());

            ExportAuditPdfCommand =
                new RelayCommand(_ =>
                    ExportAuditPdf());

            SaveAuditReportCommand =
                new RelayCommand(
                    _ => SaveAuditReport(),
                    _ => HasInventoryData && !IsLoading);

            // "Add Stock via Excel" is the initial inventory loader.
            ImportExcelCommand =
                new RelayCommand(
                    async _ => await OpenInventoryFileDialogAndLoadAsync(),
                    _ => !IsLoading);

            ClearInventoryCommand =
                new RelayCommand(
                    _ => ClearInventory(),
                    _ => HasInventoryData && !IsLoading);

            RefreshFilteredView();
        }

        private void TryLoadInventoryFromDatabase()
        {
            try
            {
                if (!_databaseService.HasInventory())
                    return;

                var loaded = _databaseService.LoadCurrentInventory();
                if (loaded.Products.Count == 0 || loaded.BranchNames.Count == 0)
                    return;

                foreach (var product in loaded.Products)
                    product.PropertyChanged += Product_PropertyChanged;

                _allProducts.AddRange(loaded.Products);
                BuildBranchHierarchy(loaded.BranchNames);

                var firstRealBranch = FindFirstRealBranch();
                if (firstRealBranch != null)
                {
                    _selectedBranchNode = firstRealBranch;
                    _selectedBranch = firstRealBranch.Value;
                }

                OnPropertyChanged(nameof(SelectedBranchNode));
                OnPropertyChanged(nameof(SelectedBranch));

                HasInventoryData = true;
                HasUnsavedAuditChanges = false;
                LoadedFileName = _databaseService.GetLatestImportFileName();

                // Refresh the filter first so we only update products that are
                // actually visible for the selected branch. Updating all 57k+
                // products with multiple PropertyChanged events was causing the
                // branch selector to appear frozen.
                RefreshFilteredView();
                ApplyBranchToVisibleProducts();
            }
            catch (Exception ex)
            {
                // Keep the application usable if SQL Server is temporarily unavailable.
                // Excel import can still be used and will report a database error when saving.
                System.Diagnostics.Debug.WriteLine("Database startup load failed: " + ex);
            }
        }

        private void ClearInventoryState()
        {
            foreach (var product in _allProducts)
                product.PropertyChanged -= Product_PropertyChanged;

            _allProducts.Clear();
            BranchTree.Clear();
            Branches.Clear();
            _branchDisplayToValue.Clear();

            _selectedBranchNode = null;
            _selectedBranch = AllBranchesOption;
            _requiresBranchSelection = false;
            _loadedFileName = string.Empty;
            _searchText = string.Empty;

            OnPropertyChanged(nameof(SelectedBranchNode));
            OnPropertyChanged(nameof(SelectedBranch));
            OnPropertyChanged(nameof(LoadedFileName));
            OnPropertyChanged(nameof(SearchText));

            HasInventoryData = false;
            HasUnsavedAuditChanges = false;
            ShowZeroStock = false;
            ShowUncountedOnly = false;
            ShowSummaryCards = true;
            IsLoading = false;
            ImportProgress = 0;
            ImportProgressText = string.Empty;
        }

        private void ClearInventory()
        {
            var result = UiNotificationService.Show(
                "This will remove the current stock from the application and SQL Server. The Excel file itself will not be deleted.\n\nDo you want to clear the loaded stock?",
                "Clear Stock",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes)
                return;

            try
            {
                _databaseService.ClearCurrentInventory();
                ClearInventoryState();
                RefreshFilteredView();
            }
            catch (Exception ex)
            {
                UiNotificationService.Show(
                    "The current stock could not be cleared from SQL Server.\n\n" + ex.Message,
                    "Clear Stock",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private async Task OpenInventoryFileDialogAndLoadAsync()
        {
            var dialog = new OpenFileDialog
            {
                Title = "Select Inventory Excel File",
                Filter =
                    "Excel Workbook (*.xlsx;*.xlsm)|*.xlsx;*.xlsm|" +
                    "All Files (*.*)|*.*",
                CheckFileExists = true,
                Multiselect = false
            };

            if (dialog.ShowDialog() != true)
                return;

            await ConfirmAndLoadInventoryAsync(dialog.FileName);
        }

        public async Task ConfirmAndLoadInventoryAsync(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !System.IO.File.Exists(filePath))
                return;

            if (_databaseService.HasInventory())
            {
                var result = UiNotificationService.Show(
                    "There is existing stock saved in the database.\n\n" +
                    "Do you want to replace the existing stock with this new Excel file?\n\n" +
                    "Yes = Replace existing stock\n" +
                    "No = Keep existing stock",
                    "Existing Stock Found",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result != MessageBoxResult.Yes)
                    return;
            }

            await LoadInventoryFromExcelAsync(filePath);
        }

        public async Task LoadInventoryFromExcelAsync(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !System.IO.File.Exists(filePath))
                return;

            IsLoading = true;
            ImportProgress = 0;
            ImportProgressText = "Opening Excel file...";

            try
            {
                var progress = new Progress<int>(value =>
                {
                    ImportProgress = Math.Max(0, Math.Min(100, value));
                    ImportProgressText = $"Loading inventory... {ImportProgress}%";
                });

                InventoryLoadResult loaded = await Task.Run(
                    () => ExcelInventoryLoader.Load(filePath, progress));

                if (loaded.Products.Count == 0)
                {
                    UiNotificationService.Show(
                        "The selected Excel file does not contain any inventory items.",
                        "Load Inventory",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }

                if (loaded.BranchNames.Count == 0)
                {
                    UiNotificationService.Show(
                        "The selected Excel file does not contain any branch or warehouse columns.",
                        "Load Inventory",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }

                // The Excel file is completely parsed first. Only after that do we
                // replace the database stock. DatabaseService performs the whole
                // replacement inside one SQL transaction, so a failed import leaves
                // the previous database stock untouched.
                ImportProgress = 92;
                ImportProgressText = "Saving inventory to SQL Server...";

                var databaseProgress = new Progress<int>(value =>
                {
                    ImportProgress = 92 + (int)Math.Round(Math.Max(0, Math.Min(100, value)) * 0.07);
                    ImportProgressText = $"Saving inventory to SQL Server... {ImportProgress}%";
                });

                await Task.Run(() =>
                    _databaseService.ImportInventory(
                        loaded,
                        System.IO.Path.GetFileName(filePath),
                        databaseProgress));

                // Replace the current in-memory inventory only after the new
                // file has been fully loaded and validated.
                // AddRange raises one Reset notification instead of thousands of
                // individual collection changes. This also avoids changing the
                // CollectionView while its refresh is deferred.
                foreach (var product in _allProducts)
                    product.PropertyChanged -= Product_PropertyChanged;

                _allProducts.Clear();

                foreach (var product in loaded.Products)
                    product.PropertyChanged += Product_PropertyChanged;

                _allProducts.AddRange(loaded.Products);

                ImportProgress = 94;
                ImportProgressText = "Building branches and warehouses...";

                BuildBranchHierarchy(loaded.BranchNames);

                // Automatic branch selection:
                // - One branch/location in the uploaded workbook: select it and show its stock.
                // - Multiple branches/locations: select nothing and keep the grid empty
                //   until the user chooses a branch.
                var uploadedBranches = loaded.BranchNames
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Select(x => x.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (uploadedBranches.Count == 1)
                {
                    var singleBranch = uploadedBranches[0];
                    _selectedBranchNode = new BranchNode
                    {
                        Name = singleBranch,
                        Value = singleBranch,
                        IsGroup = false
                    };
                    _selectedBranch = singleBranch;
                    _requiresBranchSelection = false;
                }
                else
                {
                    _selectedBranchNode = null;
                    _selectedBranch = null;
                    _requiresBranchSelection = true;
                }

                OnPropertyChanged(nameof(SelectedBranchNode));
                OnPropertyChanged(nameof(SelectedBranch));

                LoadedFileName = filePath;
                HasInventoryData = true;
                HasUnsavedAuditChanges = true;

                ImportProgress = 97;
                ImportProgressText = "Preparing inventory view...";

                // Refresh before applying the selected branch so a multi-branch
                // workbook stays visually empty until the user chooses a branch.
                RefreshFilteredView();
                ApplyBranchToVisibleProducts();

                ImportProgress = 100;
                ImportProgressText = $"Loaded {loaded.Products.Count:N0} items successfully.";

                UiNotificationService.PlayFinishedSound();

                string successMessage;
                if (uploadedBranches.Count == 1)
                {
                    successMessage =
                        "Inventory uploaded successfully.\n\n" +
                        $"Branch selected automatically: {uploadedBranches[0]}";
                }
                else
                {
                    successMessage =
                        "Inventory uploaded successfully.\n\n" +
                        "The file contains multiple branches. Please select a branch to view its inventory.";
                }

                UiNotificationService.ShowWindowsNotification(
                    "Inventory Upload Complete",
                    $"{loaded.Products.Count:N0} items were uploaded successfully.");

                // If the user left the application minimized, the Windows notification
                // is the completion message and should not force the application back
                // to the foreground with a modal dialog.
                var mainWindow = Application.Current?.MainWindow;
                if (mainWindow == null || mainWindow.WindowState != WindowState.Minimized)
                {
                    UiNotificationService.Show(
                        successMessage,
                        "Inventory Uploaded",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }

                // Give WPF one short UI frame so the user can actually see 100%.
                await Task.Delay(120);
            }
            catch (Exception ex)
            {
                UiNotificationService.Show(
                    $"Could not load the selected inventory Excel file.\n\n" +
                    $"{ex.Message}",
                    "Load Inventory",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                IsLoading = false;
                ImportProgressText = string.Empty;
            }
        }

        private void Product_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(Product.Court))
                HasUnsavedAuditChanges = true;

            if (e.PropertyName == nameof(Product.Court) ||
                e.PropertyName == nameof(Product.System) ||
                e.PropertyName == nameof(Product.Diff) ||
                e.PropertyName == nameof(Product.Total))
            {
                RaiseStatsChanged();
            }
        }

        private void BuildBranchHierarchy(IEnumerable<string> branchNames)
        {
            BranchTree.Clear();
            Branches.Clear();
            _branchDisplayToValue.Clear();

            if (branchNames == null)
                return;

            var uniqueNames = branchNames
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var normalBranches = new List<string>();
            var internalWarehouses = new List<string>();
            var independentWarehouses = new List<string>();

            foreach (var name in uniqueNames)
            {
                if (IsIndependentWarehouse(name))
                    independentWarehouses.Add(name);
                else if (IsInternalWarehouse(name))
                    internalWarehouses.Add(name);
                else
                    normalBranches.Add(name);
            }

            // Root group for branches.
            var branchesGroup = new BranchNode
            {
                Name = "الفروع",
                Value = string.Empty,
                IsGroup = true
            };

            foreach (var branch in normalBranches)
            {
                var branchNode = new BranchNode
                {
                    Name = branch,
                    Value = branch,
                    IsGroup = false
                };

                foreach (var warehouse in internalWarehouses)
                {
                    if (FindParentBranch(
                            warehouse,
                            new[] { branch }) != null)
                    {
                        branchNode.Children.Add(
                            new BranchNode
                            {
                                Name = warehouse,
                                Value = warehouse,
                                IsGroup = false
                            });
                    }
                }

                branchesGroup.Children.Add(branchNode);

                Branches.Add(branch);
                _branchDisplayToValue[branch] = branch;

                foreach (var child in branchNode.Children)
                {
                    Branches.Add(child.Name);
                    _branchDisplayToValue[child.Name] = child.Value;
                }
            }

            if (branchesGroup.Children.Count > 0)
                BranchTree.Add(branchesGroup);

            // Internal warehouses that could not be matched to a branch
            // are independent warehouses.
            var assignedInternalWarehouses =
                new HashSet<string>(
                    StringComparer.OrdinalIgnoreCase);

            foreach (var branchNode in branchesGroup.Children)
            {
                foreach (var child in branchNode.Children)
                    assignedInternalWarehouses.Add(child.Value);
            }

            foreach (var warehouse in internalWarehouses)
            {
                if (!assignedInternalWarehouses.Contains(warehouse))
                    independentWarehouses.Add(warehouse);
            }

            independentWarehouses =
                independentWarehouses
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

            if (independentWarehouses.Count > 0)
            {
                var independentGroup = new BranchNode
                {
                    Name = IndependentWarehousesName,
                    Value = string.Empty,
                    IsGroup = true
                };

                foreach (var warehouse in independentWarehouses)
                {
                    independentGroup.Children.Add(
                        new BranchNode
                        {
                            Name = warehouse,
                            Value = warehouse,
                            IsGroup = false
                        });

                    Branches.Add(warehouse);
                    _branchDisplayToValue[warehouse] = warehouse;
                }

                BranchTree.Add(independentGroup);
            }
        }

        private BranchNode? FindFirstRealBranch()
        {
            foreach (var node in BranchTree)
            {
                var child = node.IsGroup
                    ? FindFirstRealChild(node)
                    : node;

                if (child != null)
                    return child;
            }

            return null;
        }

        private BranchNode? FindFirstRealChild(BranchNode node)
        {
            foreach (var child in node.Children)
            {
                if (!child.IsGroup)
                    return child;

                var nested = FindFirstRealChild(child);

                if (nested != null)
                    return nested;
            }

            return null;
        }

        private bool IsIndependentWarehouse(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return false;

            var value = name.Trim();

            return value.StartsWith(
                       "م ",
                       StringComparison.OrdinalIgnoreCase)
                   ||
                   value.StartsWith(
                       "م-",
                       StringComparison.OrdinalIgnoreCase)
                   ||
                   value.StartsWith(
                       "م_",
                       StringComparison.OrdinalIgnoreCase)
                   ||
                   value.Equals(
                       "م",
                       StringComparison.OrdinalIgnoreCase);
        }

        private bool IsInternalWarehouse(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return false;

            var value = name.Trim();

            return value.StartsWith(
                       "Internal ",
                       StringComparison.OrdinalIgnoreCase)
                   ||
                   value.StartsWith(
                       "Internal-",
                       StringComparison.OrdinalIgnoreCase)
                   ||
                   value.StartsWith(
                       "Internal_",
                       StringComparison.OrdinalIgnoreCase)
                   ||
                   value.StartsWith(
                       "داخلي ",
                       StringComparison.OrdinalIgnoreCase)
                   ||
                   value.StartsWith(
                       "داخلي-",
                       StringComparison.OrdinalIgnoreCase)
                   ||
                   value.StartsWith(
                       "داخلي_",
                       StringComparison.OrdinalIgnoreCase);
        }

        private string FindParentBranch(
            string warehouseName,
            IEnumerable<string> branches)
        {
            var warehouse = warehouseName.Trim();
            var baseName = warehouse;

            if (warehouse.StartsWith(
                    "Internal ",
                    StringComparison.OrdinalIgnoreCase))
            {
                baseName = warehouse.Substring(9).Trim();
            }
            else if (warehouse.StartsWith(
                         "Internal-",
                         StringComparison.OrdinalIgnoreCase))
            {
                baseName = warehouse.Substring(9).Trim();
            }
            else if (warehouse.StartsWith(
                         "Internal_",
                         StringComparison.OrdinalIgnoreCase))
            {
                baseName = warehouse.Substring(9).Trim();
            }
            else if (warehouse.StartsWith(
                         "داخلي ",
                         StringComparison.OrdinalIgnoreCase))
            {
                baseName = warehouse.Substring(6).Trim();
            }
            else if (warehouse.StartsWith(
                         "داخلي-",
                         StringComparison.OrdinalIgnoreCase))
            {
                baseName = warehouse.Substring(6).Trim();
            }
            else if (warehouse.StartsWith(
                         "داخلي_",
                         StringComparison.OrdinalIgnoreCase))
            {
                baseName = warehouse.Substring(6).Trim();
            }

            var exact = branches.FirstOrDefault(
                b => string.Equals(
                    b.Trim(),
                    baseName,
                    StringComparison.OrdinalIgnoreCase));

            if (exact != null)
                return exact;

            var normalizedBase = NormalizeName(baseName);

            return branches.FirstOrDefault(
                b => NormalizeName(b) == normalizedBase);
        }

        private string NormalizeName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            return new string(
                value
                    .Trim()
                    .Where(c => !char.IsWhiteSpace(c))
                    .ToArray())
                .ToLowerInvariant();
        }

        private void ApplyBranchToVisibleProducts()
        {
            if (ProductsView == null)
                return;

            var branch =
                string.IsNullOrWhiteSpace(SelectedBranch) ||
                SelectedBranch == AllBranchesOption
                    ? null
                    : SelectedBranch;

            // Snapshot the filtered view before changing product properties.
            // Product notifications can otherwise cause repeated collection-view
            // work while the DataGrid is changing branches.
            var visibleProducts = ProductsView.Cast<Product>().ToList();

            foreach (var product in visibleProducts)
                product.PropertyChanged -= Product_PropertyChanged;

            foreach (var product in visibleProducts)
                product.ApplyBranch(branch);

            foreach (var product in visibleProducts)
                product.PropertyChanged += Product_PropertyChanged;

            RaiseStatsChanged();
        }

        private bool FilterProduct(object obj)
        {
            if (!(obj is Product p))
                return false;

            if (_requiresBranchSelection && string.IsNullOrWhiteSpace(SelectedBranch))
                return false;

            var selectedBranch =
                string.IsNullOrWhiteSpace(SelectedBranch) ||
                SelectedBranch == AllBranchesOption
                    ? null
                    : SelectedBranch;

            if (selectedBranch != null)
            {
                var quantity = p.BranchStock.TryGetValue(
                    selectedBranch,
                    out var branchQuantity)
                    ? branchQuantity
                    : 0m;

                if (!ShowZeroStock && quantity <= 0)
                    return false;
            }

            if (ShowUncountedOnly && p.IsCounted)
                return false;

            if (string.IsNullOrWhiteSpace(SearchText))
                return true;

            var term = SearchText.Trim();

            switch (SearchMode)
            {
                case SearchMode.Code:
                    return p.Code != null &&
                           p.Code.Contains(
                               term,
                               StringComparison.OrdinalIgnoreCase);

                case SearchMode.ProductName:
                    return p.Name != null &&
                           p.Name.Contains(
                               term,
                               StringComparison.OrdinalIgnoreCase);

                case SearchMode.Price:
                    return p.Price
                        .ToString("0.##")
                        .Contains(
                            term,
                            StringComparison.OrdinalIgnoreCase);

                default:
                    return true;
            }
        }


        public void RefreshFilteredView()
        {
            if (ProductsView == null)
                return;

            ProductsView.Refresh();
            RaiseStatsChanged();
        }

        private void RaiseStatsChanged()
        {
            OnPropertyChanged(nameof(TotalItems));
            OnPropertyChanged(nameof(FilteredCount));
            OnPropertyChanged(nameof(PerfectCount));
            OnPropertyChanged(nameof(SurplusCount));
            OnPropertyChanged(nameof(DeficitCount));
            OnPropertyChanged(nameof(SurplusValue));
            OnPropertyChanged(nameof(DeficitValue));
            OnPropertyChanged(nameof(PerfectPercent));
            OnPropertyChanged(nameof(SurplusPercent));
            OnPropertyChanged(nameof(DeficitPercent));
        }

        public bool TrySaveCurrentAuditReport(out string message)
        {
            message = string.Empty;

            if (!HasInventoryData)
            {
                message = "There is no inventory data to save.";
                return false;
            }

            string branchName = GetSelectedBranchValue() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(branchName))
            {
                message = "Please select a specific branch or warehouse before saving the audit report.";
                return false;
            }

            try
            {
                var products = ProductsView.Cast<Product>().ToList();
                if (products.Count == 0)
                {
                    message = "There are no visible audit items to save.";
                    return false;
                }

                int savedCount = _databaseService.SaveInventoryAudit(products, branchName);
                HasUnsavedAuditChanges = false;
                message = $"Audit report saved successfully for {branchName}.\n\n{savedCount:N0} item(s) were saved to the database.";
                return true;
            }
            catch (Exception ex)
            {
                message = "The audit report could not be saved to SQL Server.\n\n" + ex.Message;
                return false;
            }
        }

        private void SaveAuditReport()
        {
            if (TrySaveCurrentAuditReport(out var message))
            {
                UiNotificationService.Show(
                    message,
                    "Audit Saved",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            else
            {
                UiNotificationService.Show(
                    message,
                    "Save Audit Report",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        private void ExportAuditExcel()
        {
            if (!HasInventoryData)
            {
                UiNotificationService.Show(
                    "Please add an inventory Excel file first.",
                    "Export to Excel",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                return;
            }

            var branchName =
                string.IsNullOrWhiteSpace(SelectedBranch) ||
                SelectedBranch == AllBranchesOption
                    ? AllBranchesOption
                    : SelectedBranch;

            var dialog = new SaveFileDialog
            {
                Filter = "Excel Workbook (*.xlsx)|*.xlsx",
                FileName = $"Audit_{branchName}.xlsx"
            };

            if (dialog.ShowDialog() == true)
            {
                ExportService.ExportAuditToExcel(
                    ProductsView.Cast<Product>(),
                    branchName,
                    dialog.FileName);

                UiNotificationService.Show(
                    "Exported to Excel successfully.",
                    "Export to Excel",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
        }

        private void ExportAuditPdf()
        {
            if (!HasInventoryData)
            {
                UiNotificationService.Show(
                    "Please add an inventory Excel file first.",
                    "Export to PDF",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                return;
            }

            var branchName =
                string.IsNullOrWhiteSpace(SelectedBranch) ||
                SelectedBranch == AllBranchesOption
                    ? AllBranchesOption
                    : SelectedBranch;

            var dialog = new SaveFileDialog
            {
                Filter = "PDF Document (*.pdf)|*.pdf",
                FileName = $"Audit_{branchName}.pdf"
            };

            if (dialog.ShowDialog() == true)
            {
                ExportService.ExportAuditToPdf(
                    ProductsView.Cast<Product>(),
                    branchName,
                    dialog.FileName,
                    PerfectCount,
                    SurplusCount,
                    DeficitCount);

                UiNotificationService.Show(
                    "Exported to PDF successfully.",
                    "Export to PDF",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
        }
    }
}
