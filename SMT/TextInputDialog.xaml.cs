using System.Windows;

namespace SMT
{
    public partial class TextInputDialog : Window
    {
        public string InputText => InputBox.Text;

        public TextInputDialog(string title, string prompt, string defaultValue = "")
        {
            InitializeComponent();
            Title = title;
            PromptText.Text = prompt;
            InputBox.Text = defaultValue ?? string.Empty;
            InputBox.SelectAll();
            InputBox.Focus();
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
