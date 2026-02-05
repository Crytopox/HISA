using System.Windows;
using SMT.EVEData;

namespace SMT
{
    public partial class PasteInfrastructureUpgradesWindow : Window
    {
        public EveManager EM { get; set; }

        public PasteInfrastructureUpgradesWindow()
        {
            InitializeComponent();
            Loaded += PasteInfrastructureUpgradesWindow_Loaded;
        }

        private void PasteInfrastructureUpgradesWindow_Loaded(object sender, RoutedEventArgs e)
        {
            if (Clipboard.ContainsText(TextDataFormat.Text))
            {
                PasteTextBox.Text = Clipboard.GetText(TextDataFormat.UnicodeText);
                PasteTextBox.SelectAll();
                PasteTextBox.Focus();
            }
            else
            {
                PasteTextBox.Focus();
            }
        }

        private void Import_Click(object sender, RoutedEventArgs e)
        {
            if (EM == null)
            {
                DialogResult = false;
                return;
            }

            string text = PasteTextBox.Text;
            if (string.IsNullOrWhiteSpace(text))
            {
                DialogResult = false;
                return;
            }

            EM.LoadInfrastructureUpgradesFromText(text);
            string upgradesFilePath = System.IO.Path.Combine(EveAppConfig.StorageRoot, "InfrastructureUpgrades.txt");
            EM.SaveInfrastructureUpgrades(upgradesFilePath);
            DialogResult = true;
        }
    }
}
