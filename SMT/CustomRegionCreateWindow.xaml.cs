using System.Collections.Generic;
using System.Linq;
using System.Windows;
using SMT.EVEData;

namespace SMT
{
    public partial class CustomRegionCreateWindow : Window
    {
        public class RegionPick
        {
            public string Name { get; set; }
            public MapRegion Region { get; set; }
            public bool IsSelected { get; set; }
            public bool IsCustom { get; set; }
            public string TypeLabel { get; set; }
        }

        private readonly List<RegionPick> m_Items;
        private readonly List<MapRegion> m_CustomRegions;
        private readonly EveManager m_Manager;

        public string RegionName => RegionNameBox.Text.Trim();

        public List<MapRegion> SelectedRegions => m_Items.Where(i => i.IsSelected).Select(i => i.Region).ToList();

        public CustomRegionCreateWindow(EveManager manager, IEnumerable<MapRegion> regions)
        {
            InitializeComponent();

            m_Manager = manager;
            m_Items = regions
                .Where(r => r != null)
                .OrderBy(r => r.Name)
                .Select(r => new RegionPick
                {
                    Name = r.Name,
                    Region = r,
                    IsSelected = false,
                    IsCustom = r.IsCustom,
                    TypeLabel = r.IsCustom ? "Custom" : "Region"
                })
                .ToList();

            RegionList.ItemsSource = m_Items;

            m_CustomRegions = regions.Where(r => r != null && r.IsCustom).OrderBy(r => r.Name).ToList();
            TemplateCustomRegionBox.ItemsSource = m_CustomRegions;
            TemplateCustomRegionBox.DisplayMemberPath = "Name";
        }

        private void Create_Click(object sender, RoutedEventArgs e)
        {
            if(string.IsNullOrWhiteSpace(RegionName))
            {
                MessageBox.Show("Region name is required.", "Create Custom Region", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if(SelectedRegions.Count == 0)
            {
                MessageBox.Show("Select at least one region to include.", "Create Custom Region", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void ApplyTemplate_Click(object sender, RoutedEventArgs e)
        {
            MapRegion template = TemplateCustomRegionBox.SelectedItem as MapRegion;
            if(template == null)
            {
                return;
            }

            foreach(RegionPick pick in m_Items)
            {
                pick.IsSelected = pick.Region.Name == template.Name;
            }

            RegionList.Items.Refresh();
        }
    }
}
