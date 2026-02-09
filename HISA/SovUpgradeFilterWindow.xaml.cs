using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using HISA.ResourceUsage;

namespace HISA
{
    public partial class SovUpgradeFilterWindow : Window, INotifyPropertyChanged
    {
        public MapConfig MapConf { get; set; }

        public bool ShowUpgrades { get; private set; } = true;

        public ObservableCollection<UpgradeCategory> UpgradeCategories { get; } = new ObservableCollection<UpgradeCategory>();

        public event PropertyChangedEventHandler PropertyChanged;

        public SovUpgradeFilterWindow()
        {
            InitializeComponent();
            DataContext = this;
            Loaded += SovUpgradeFilterWindow_Loaded;
        }

        private void SovUpgradeFilterWindow_Loaded(object sender, RoutedEventArgs e)
        {
            UpgradeCategories.Clear();

            var selected = MapConf?.InfrastructureUpgradeIconFilter;
            bool hasSelection = selected != null && selected.Count > 0;

            foreach(var category in BuildCategories(selected, hasSelection))
            {
                UpgradeCategories.Add(category);
            }
        }

        private void SelectAll_Click(object sender, RoutedEventArgs e)
        {
            foreach(var item in UpgradeCategories.SelectMany(c => c.Items).Concat(UpgradeCategories.SelectMany(c => c.SubGroups).SelectMany(s => s.Items)))
            {
                item.IsChecked = true;
            }
        }

        private void ClearAll_Click(object sender, RoutedEventArgs e)
        {
            foreach(var item in UpgradeCategories.SelectMany(c => c.Items).Concat(UpgradeCategories.SelectMany(c => c.SubGroups).SelectMany(s => s.Items)))
            {
                item.IsChecked = false;
            }
        }

        private void Apply_Click(object sender, RoutedEventArgs e)
        {
            if(MapConf == null)
            {
                DialogResult = false;
                return;
            }

            var selected = UpgradeCategories
                .SelectMany(c => c.Items)
                .Concat(UpgradeCategories.SelectMany(c => c.SubGroups).SelectMany(s => s.Items))
                .Where(i => i.IsChecked)
                .Select(i => i.DisplayName)
                .ToList();

            if(selected.Count == 0)
            {
                ShowUpgrades = false;
                MapConf.InfrastructureUpgradeIconFilter = new System.Collections.Generic.List<string>();
            }
            else if(selected.Count == UpgradeCategories.SelectMany(c => c.Items).Concat(UpgradeCategories.SelectMany(c => c.SubGroups).SelectMany(s => s.Items)).Count())
            {
                ShowUpgrades = true;
                MapConf.InfrastructureUpgradeIconFilter = new System.Collections.Generic.List<string>();
            }
            else
            {
                ShowUpgrades = true;
                MapConf.InfrastructureUpgradeIconFilter = selected;
            }

            DialogResult = true;
        }

        private static ObservableCollection<UpgradeCategory> BuildCategories(System.Collections.Generic.List<string> selected, bool hasSelection)
        {
            ObservableCollection<UpgradeCategory> categories = new ObservableCollection<UpgradeCategory>();

            categories.Add(new UpgradeCategory("Threat Detection Arrays", BuildThreatDetectionSubGroups(selected, hasSelection)));

            categories.Add(new UpgradeCategory("Exploration Detectors", BuildItems(name =>
                name.StartsWith("Exploration Detector"),
                selected, hasSelection)));

            categories.Add(new UpgradeCategory("Prospecting Arrays", BuildProspectingSubGroups(selected, hasSelection)));

            categories.Add(new UpgradeCategory("Stability Generators", BuildItems(name =>
                name.EndsWith("Stability Generator"),
                selected, hasSelection)));

            return categories;
        }

        private static ObservableCollection<UpgradeSubCategory> BuildProspectingSubGroups(System.Collections.Generic.List<string> selected, bool hasSelection)
        {
            var subGroups = new ObservableCollection<UpgradeSubCategory>
            {
                BuildSubGroup("Tritanium", "Tritanium Prospecting Array", selected, hasSelection),
                BuildSubGroup("Pyerite", "Pyerite Prospecting Array", selected, hasSelection),
                BuildSubGroup("Mexallon", "Mexallon Prospecting Array", selected, hasSelection),
                BuildSubGroup("Isogen", "Isogen Prospecting Array", selected, hasSelection),
                BuildSubGroup("Nocxium", "Nocxium Prospecting Array", selected, hasSelection),
                BuildSubGroup("Zydrine", "Zydrine Prospecting Array", selected, hasSelection),
                BuildSubGroup("Megacyte", "Megacyte Prospecting Array", selected, hasSelection)
            };

            return subGroups;
        }

        private static ObservableCollection<UpgradeSubCategory> BuildThreatDetectionSubGroups(System.Collections.Generic.List<string> selected, bool hasSelection)
        {
            var subGroups = new ObservableCollection<UpgradeSubCategory>
            {
                BuildSubGroup("Major", "Major Threat Detection Array", selected, hasSelection),
                BuildSubGroup("Minor", "Minor Threat Detection Array", selected, hasSelection)
            };

            return subGroups;
        }

        private static UpgradeSubCategory BuildSubGroup(string name, string prefix, System.Collections.Generic.List<string> selected, bool hasSelection)
        {
            return new UpgradeSubCategory(name, BuildItems(displayName => displayName.StartsWith(prefix), selected, hasSelection));
        }

        private static ObservableCollection<UpgradeFilterItem> BuildItems(System.Func<string, bool> matcher, System.Collections.Generic.List<string> selected, bool hasSelection)
        {
            var items = new ObservableCollection<UpgradeFilterItem>();

            foreach(string displayName in SovUpgradeIconCatalog.DisplayNames.Where(matcher).OrderBy(n => n))
            {
                bool isChecked = !hasSelection || selected.Any(s => string.Equals(s, displayName, System.StringComparison.OrdinalIgnoreCase));
                var icon = ResourceLoader.LoadBitmapFromResource(SovUpgradeIconCatalog.TryGetIconPath(displayName, out string path) ? path : string.Empty);

                items.Add(new UpgradeFilterItem
                {
                    DisplayName = displayName,
                    Icon = icon,
                    IsChecked = isChecked
                });
            }

            return items;
        }
    }

    public class UpgradeCategory
    {
        public UpgradeCategory(string name, ObservableCollection<UpgradeFilterItem> items)
        {
            Name = name;
            Items = items;
            SubGroups = new ObservableCollection<UpgradeSubCategory>();
        }

        public UpgradeCategory(string name, ObservableCollection<UpgradeSubCategory> subGroups)
        {
            Name = name;
            Items = new ObservableCollection<UpgradeFilterItem>();
            SubGroups = subGroups;
        }

        public string Name { get; }
        public ObservableCollection<UpgradeFilterItem> Items { get; }
        public ObservableCollection<UpgradeSubCategory> SubGroups { get; }
    }

    public class UpgradeSubCategory
    {
        public UpgradeSubCategory(string name, ObservableCollection<UpgradeFilterItem> items)
        {
            Name = name;
            Items = items;
        }

        public string Name { get; }
        public ObservableCollection<UpgradeFilterItem> Items { get; }
    }
    public class UpgradeFilterItem : INotifyPropertyChanged
    {
        private bool m_IsChecked;

        public string DisplayName { get; set; }

        public System.Windows.Media.Imaging.BitmapImage Icon { get; set; }

        public bool IsChecked
        {
            get => m_IsChecked;
            set
            {
                if(m_IsChecked == value)
                {
                    return;
                }
                m_IsChecked = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsChecked)));
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
    }
}


