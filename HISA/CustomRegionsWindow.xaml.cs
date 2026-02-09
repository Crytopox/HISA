using System.Collections.Generic;
using System.Linq;
using System.Windows;
using Microsoft.Win32;
using HISA.EVEData;

namespace HISA
{
    public partial class CustomRegionsWindow : Window
    {
        private readonly EveManager m_Manager;
        private readonly RegionControl m_RegionControl;

        public CustomRegionsWindow(EveManager manager, RegionControl regionControl)
        {
            InitializeComponent();
            m_Manager = manager;
            m_RegionControl = regionControl;
            ReloadList();
        }

        private void ReloadList()
        {
            if(m_Manager == null)
            {
                RegionsList.ItemsSource = null;
                return;
            }

            List<MapRegion> list = m_Manager.Regions
                .Where(r => r.IsCustom)
                .OrderBy(r => r.Name)
                .ToList();

            RegionsList.ItemsSource = list;
        }

        private MapRegion GetSelectedRegion()
        {
            return RegionsList.SelectedItem as MapRegion;
        }

        private void AllowEditChk_Changed(object sender, RoutedEventArgs e)
        {
            if(m_Manager == null)
            {
                return;
            }

            if(sender is FrameworkElement fe && fe.DataContext is MapRegion region)
            {
                m_Manager.SaveCustomRegion(region);
            }
        }

        private void ExportBtn_Click(object sender, RoutedEventArgs e)
        {
            MapRegion region = GetSelectedRegion();
            if(region == null)
            {
                return;
            }

            SaveFileDialog dlg = new SaveFileDialog
            {
                Title = "Export Region",
                Filter = "HISA Region Pack (*.hisaregion)|*.hisaregion|Zip (*.zip)|*.zip|XML (*.xml)|*.xml|All files (*.*)|*.*",
                FileName = region.Name + ".hisaregion"
            };

            if(dlg.ShowDialog() != true)
            {
                return;
            }

            if(!m_Manager.ExportRegion(region, dlg.FileName, out string error))
            {
                MessageBox.Show("Export failed: " + error, "Custom Regions", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void RemoveBtn_Click(object sender, RoutedEventArgs e)
        {
            MapRegion region = GetSelectedRegion();
            if(region == null)
            {
                return;
            }

            MessageBoxResult confirm = MessageBox.Show(
                $"Remove custom region '{region.Name}'?",
                "Custom Regions",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if(confirm != MessageBoxResult.Yes)
            {
                return;
            }

            m_Manager.DeleteCustomRegion(region.Name);
            ReloadList();
            m_RegionControl?.RefreshRegionList();

            if(m_RegionControl?.Region != null && m_RegionControl.Region.Name == region.Name)
            {
                MapRegion next = m_Manager.Regions.FirstOrDefault(r => !r.IsCustom) ?? m_Manager.Regions.FirstOrDefault();
                if(next != null)
                {
                    m_RegionControl.SelectRegion(next.Name);
                }
            }
        }

        private void CloseBtn_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}


