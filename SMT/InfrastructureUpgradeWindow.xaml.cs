using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using SMT.EVEData;

namespace SMT
{
    public partial class InfrastructureUpgradeWindow : Window
    {
        public EveManager EM { get; set; }

        private ObservableCollection<InfrastructureUpgrade> currentUpgrades;
        private ObservableCollection<InfrastructureUpgradeSummary> allUpgrades;
        private string selectedSystemName;
        private string upgradesFilePath;

        public InfrastructureUpgradeWindow()
        {
            InitializeComponent();
            currentUpgrades = new ObservableCollection<InfrastructureUpgrade>();
            UpgradesDataGrid.ItemsSource = currentUpgrades;
            allUpgrades = new ObservableCollection<InfrastructureUpgradeSummary>();
            AllUpgradesDataGrid.ItemsSource = allUpgrades;
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            if (EM != null)
            {
                // Set up the auto-save file path
                upgradesFilePath = Path.Combine(EveAppConfig.StorageRoot, "InfrastructureUpgrades.txt");

                // Populate system combo box with all null sec systems
                var nullSecSystems = EM.Systems
                    .Where(s => s.TrueSec < 0.0)
                    .OrderBy(s => s.Name)
                    .Select(s => s.Name)
                    .ToList();

                SystemComboBox.ItemsSource = nullSecSystems;

                RefreshAllUpgrades();
            }
        }

        private void AutoSave()
        {
            if (EM != null && !string.IsNullOrEmpty(upgradesFilePath))
            {
                EM.SaveInfrastructureUpgrades(upgradesFilePath);

                // Immediately refresh the map to show changes
                RefreshOwnerMap();
            }
        }

        private void RefreshOwnerMap()
        {
            // Get the MainWindow owner and refresh its map
            if (Owner is MainWindow mainWindow && mainWindow.RegionUC != null)
            {
                mainWindow.RegionUC.ReDrawMap(false);
            }
        }

        private void SystemComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            selectedSystemName = SystemComboBox.SelectedItem as string;

            if (selectedSystemName != null && EM != null)
            {
                LoadUpgradesForSystem(selectedSystemName);
            }
        }

        private void LoadUpgradesForSystem(string systemName)
        {
            currentUpgrades.Clear();

            if (EM != null)
            {
                EVEData.System sys = EM.GetEveSystem(systemName);
                if (sys != null)
                {
                    foreach (var upgrade in sys.InfrastructureUpgrades.OrderBy(u => u.SlotNumber))
                    {
                        currentUpgrades.Add(upgrade);
                    }
                }
            }
        }

        private void RefreshAllUpgrades()
        {
            allUpgrades.Clear();

            if (EM == null)
            {
                return;
            }

            foreach (var sys in EM.Systems)
            {
                if (sys.InfrastructureUpgrades == null || sys.InfrastructureUpgrades.Count == 0)
                {
                    continue;
                }

                foreach (var upgrade in sys.InfrastructureUpgrades.OrderBy(u => u.SlotNumber))
                {
                    allUpgrades.Add(new InfrastructureUpgradeSummary
                    {
                        SystemName = sys.Name,
                        SlotNumber = upgrade.SlotNumber,
                        UpgradeName = upgrade.UpgradeName,
                        Level = upgrade.Level,
                        IsOnline = upgrade.IsOnline
                    });
                }
            }
        }

        private void AddUpdateButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(selectedSystemName))
            {
                MessageBox.Show("Please select a system first.", "No System Selected", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (UpgradeTypeComboBox.SelectedItem == null)
            {
                MessageBox.Show("Please select an upgrade type.", "No Type Selected", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }


            if(SlotComboBox.SelectedIndex == -1)
            {
                MessageBox.Show("Please select a valid slot number (1-10)..", "Invalid Slot", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            int slotNumber = SlotComboBox.SelectedIndex + 1;



            if(LevelComboBox.SelectedIndex == -1)
            {
                MessageBox.Show("Please select a valid level (0-3).", "Invalid Level", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            int level = LevelComboBox.SelectedIndex;

            string upgradeName = (UpgradeTypeComboBox.SelectedItem as ComboBoxItem).Content.ToString();
            bool isOnline = OnlineCheckBox.IsChecked ?? false;

            if (EM != null)
            {
                EM.SetInfrastructureUpgrade(selectedSystemName, slotNumber, upgradeName, level, isOnline);
                LoadUpgradesForSystem(selectedSystemName);
                RefreshAllUpgrades();

                // Auto-save after adding
                AutoSave();

                // Clear the form
                SlotComboBox.SelectedIndex = -1;
                LevelComboBox.SelectedIndex = -1;
                UpgradeTypeComboBox.SelectedIndex = -1;
                OnlineCheckBox.IsChecked = true;
            }
        }

        private void DeleteUpgradeButton_Click(object sender, RoutedEventArgs e)
        {
            if (UpgradesDataGrid.SelectedItem is InfrastructureUpgrade selectedUpgrade)
            {
                if (EM != null && !string.IsNullOrEmpty(selectedSystemName))
                {
                    EM.RemoveInfrastructureUpgrade(selectedSystemName, selectedUpgrade.SlotNumber);
                    LoadUpgradesForSystem(selectedSystemName);
                    RefreshAllUpgrades();

                    // Auto-save after deleting
                    AutoSave();
                }
            }
            else
            {
                MessageBox.Show("Please select an upgrade to delete.", "No Upgrade Selected", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            // Final save before closing
            AutoSave();
            this.Close();
        }

        private void ClearAllUpgradesButton_Click(object sender, RoutedEventArgs e)
        {
            if (EM == null)
            {
                return;
            }

            MessageBoxResult result = MessageBox.Show(
                "Clear all infrastructure upgrades for all systems?",
                "Confirm Clear All",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes)
            {
                return;
            }

            foreach (var sys in EM.Systems)
            {
                sys.InfrastructureUpgrades?.Clear();
            }

            currentUpgrades.Clear();
            RefreshAllUpgrades();
            AutoSave();
        }

        public class InfrastructureUpgradeSummary
        {
            public string SystemName { get; set; }
            public int SlotNumber { get; set; }
            public string UpgradeName { get; set; }
            public int Level { get; set; }
            public bool IsOnline { get; set; }
        }
    }
}
