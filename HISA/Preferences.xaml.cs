using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Data;

//using System.Windows.Forms;
using NAudio.Wave;
using HISA.EVEData;
using MessageBox = System.Windows.MessageBox;

namespace HISA
{
    /// <summary>
    /// Interaction logic for PreferencesWindow.xaml
    /// </summary>
    public partial class PreferencesWindow : Window
    {
        private const string AnyCharacterOption = "Any character";

        public HISA.MapConfig MapConf;
        public HISA.EVEData.EveManager EM { get; set; }

        private IWavePlayer waveOutEvent;
        private AudioFileReader audioFileReader;

        private bool isInitialLoad = true; // Flag to track initial load of preferences window

        public PreferencesWindow()
        {
            InitializeComponent();

            syncESIPositionChk.IsChecked = EveManager.Instance.UseESIForCharacterPositions;

            waveOutEvent = new WaveOutEvent { DeviceNumber = -1 };

            audioFileReader = new AudioFileReader(AppDomain.CurrentDomain.BaseDirectory + @"\Sounds\woop.mp3");

            try
            {
                waveOutEvent.Init(audioFileReader);
            }
            catch
            {
                // wave output fails on some devices; try falling back to dsound
                waveOutEvent = new DirectSoundOut();
                waveOutEvent.Init(audioFileReader);
            }

        }

        private void Prefs_OK_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void Prefs_Default_Click(object sender, RoutedEventArgs e)
        {
            if(MapConf != null)
            {
                MapConf.SetDefaults();
            }
        }


        private void Prefs_OpenFolder_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                System.Diagnostics.Process.Start("explorer.exe", EVEData.EveAppConfig.StorageRoot);
            }
            catch(Exception ex)
            {
                //MessageBox.Show("Error opening folder: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        


        private void syncESIPositionChk_Checked(object sender, RoutedEventArgs e)
        {
            EveManager.Instance.UseESIForCharacterPositions = (bool)syncESIPositionChk.IsChecked;
        }

        private void zkilltime_ValueChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            EveManager.Instance.ZKillFeed.KillExpireTimeMinutes = MapConf.ZkillExpireTimeMinutes;
        }

        private void ResetColourData_Click(object sender, RoutedEventArgs e)
        {
            MapConf.SetDefaultColours();
            ColoursPropertyGrid.SelectedObject = MapConf.ActiveColourScheme;
            MainWindow.AppWindow.RegionUC.ReDrawMap(true);
            MainWindow.AppWindow.UniverseUC.ReDrawMap(true, true, true);
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            RefreshDangerZoneCharacterOptions();

            ColoursPropertyGrid.SelectedObject = MapConf.ActiveColourScheme;
            ColoursPropertyGrid.CollapseAllProperties();
            ColoursPropertyGrid.Update();
            ColoursPropertyGrid.PropertyValueChanged += ColoursPropertyGrid_PropertyValueChanged;

            intelVolumeSlider.ValueChanged += IntelVolumeChanged_ValueChanged;
        }

        private void ColoursPropertyGrid_PropertyValueChanged(object sender, Xceed.Wpf.Toolkit.PropertyGrid.PropertyValueChangedEventArgs e)
        {
            MainWindow.AppWindow.RegionUC.ReDrawMap(true);
            MainWindow.AppWindow.UniverseUC.ReDrawMap(true, true, true);
        }

        private void IntelVolumeChanged_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if(isInitialLoad)
            {
                isInitialLoad = false;
                return; // Skip sound playback on initial load
            }

            waveOutEvent.Volume = MapConf.IntelSoundVolume;

            if(waveOutEvent.PlaybackState != PlaybackState.Playing)
            {
                try
                {
                    audioFileReader.Position = 0; // Reset position to the beginning
                    waveOutEvent.Play(); // Play the sound
                }
                catch(Exception ex)
                {
                    Debug.WriteLine("Error playing sound: " + ex.Message);
                }
            }
        }

        private void SetLogLocation_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new System.Windows.Forms.FolderBrowserDialog();
            if(dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                MapConf.CustomEveLogFolderLocation = dialog.SelectedPath;
            }
            MessageBoxResult result = MessageBox.Show("Restart HISA for the log folder location to take effect", "Please Restart HISA", MessageBoxButton.OK);
        }

        private void DefaultLogLocation_Click(object sender, RoutedEventArgs e)
        {
            MapConf.CustomEveLogFolderLocation = string.Empty;
            MessageBoxResult result = MessageBox.Show("Restart HISA for the log folder location to take effect", "Please Restart HISA", MessageBoxButton.OK);
        }

        private void RefreshDangerZoneCharacterOptions()
        {
            if(dangerZoneCharacterCombo == null || MapConf == null || EM == null)
            {
                return;
            }

            List<string> characterOptions = new List<string> { AnyCharacterOption };
            characterOptions.AddRange(
                EM.LocalCharacters
                    .Where(c => !string.IsNullOrWhiteSpace(c?.Name))
                    .Select(c => c.Name)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(n => n, StringComparer.OrdinalIgnoreCase));

            string selectedCharacter = string.IsNullOrWhiteSpace(MapConf.DangerZoneCharacter)
                ? AnyCharacterOption
                : MapConf.DangerZoneCharacter;

            string matchedOption = characterOptions.FirstOrDefault(o => string.Equals(o, selectedCharacter, StringComparison.OrdinalIgnoreCase));
            if(string.IsNullOrWhiteSpace(matchedOption))
            {
                if(!string.IsNullOrWhiteSpace(MapConf.DangerZoneCharacter))
                {
                    characterOptions.Add(MapConf.DangerZoneCharacter);
                    selectedCharacter = MapConf.DangerZoneCharacter;
                }
                else
                {
                    selectedCharacter = AnyCharacterOption;
                }
            }
            else
            {
                selectedCharacter = matchedOption;
            }

            dangerZoneCharacterCombo.ItemsSource = characterOptions;
            dangerZoneCharacterCombo.SelectedItem = selectedCharacter;
        }

        private void DangerZoneCharacterCombo_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if(MapConf == null || dangerZoneCharacterCombo?.SelectedItem == null)
            {
                return;
            }

            string selectedCharacter = dangerZoneCharacterCombo.SelectedItem.ToString();
            MapConf.DangerZoneCharacter = string.Equals(selectedCharacter, AnyCharacterOption, StringComparison.Ordinal)
                ? string.Empty
                : selectedCharacter;

            MainWindow.AppWindow?.RegionUC?.ReDrawMap(false);
        }

    }

    public class JoinStringConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var lines = value as IEnumerable<string>;
            return lines is null ? null : string.Join(Environment.NewLine, lines);
        }

        public object ConvertBack(object value, Type targetTypes, object parameter, CultureInfo culture)
        {
            string inputstr = (string)value;
            string[] lines = inputstr.Split(new string[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries);
            List<string> oc = new List<string>(lines);
            return oc;
        }
    }

    public class NegateBooleanConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if(value is bool boolValue)
            {
                if(boolValue)
                {
                    return "True";
                }
                else
                {
                    return "False";
                }
            }

            return System.Windows.Data.Binding.DoNothing;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}


