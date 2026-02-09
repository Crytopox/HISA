using System.Collections.Generic;
using System.Windows;

namespace HISA
{
    public partial class RegionColorLegendWindow : Window
    {
        public RegionColorLegendWindow(IEnumerable<RegionControl.RegionColorItem> items)
        {
            InitializeComponent();
            LegendList.ItemsSource = items;
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}


