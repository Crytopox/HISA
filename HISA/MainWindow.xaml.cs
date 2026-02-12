using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using System.Xml;
using System.Xml.Serialization;
using Microsoft.Win32;
using Microsoft.Toolkit.Uwp.Notifications;
using NAudio.Wave;
using NHotkey;
using NHotkey.Wpf;
using HISA.EVEData;
using HISA.Helpers;
using static HISA.EVEData.ZKillRedisQ;
using AvalonDock.Layout;

namespace HISA
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public static MainWindow AppWindow;
        private LogonWindow logonBrowserWindow;
        private List<Overlay> overlayWindows = new();
        private bool overlayWindowsAreClickTrough = false;

        public bool OverlayWindowsAreClickTrough
        {
            get => overlayWindowsAreClickTrough;
        }

        private PreferencesWindow preferencesWindow;

        private int uiRefreshCounter = 0;
        private int anomRefreshCounter = 0;
        private System.Windows.Threading.DispatcherTimer uiRefreshTimer;

        private bool manualZKillFilterRefreshRequired = true;
        private string lastHoveredSystemForIntel = string.Empty;
        private int suppressRegionSelectionPersistenceCount = 0;

        private List<InfoItem> InfoLayer;

        private Dictionary<string, long> CharacterNameIDCache;
        private Dictionary<long, string> CharacterIDNameCache;

        public JumpRoute CapitalRoute { get; set; }

        public EventHandler OnSelectedCharChangedEventHandler;

        private System.Windows.Forms.NotifyIcon nIcon = new System.Windows.Forms.NotifyIcon();

        private readonly string WindowLayoutVersion = "03";

        private IWavePlayer waveOutEvent;
        private AudioFileReader audioFileReader;

        /// <summary>
        /// Main Window
        /// </summary>
        public MainWindow()
        {
            AppWindow = this;
            DataContext = this;

            this.FontFamily = new FontFamily(new Uri("pack://application:,,,/External/AtkinsonHyperlegible/"), "./#Atkinson Hyperlegible");

            Uri woopUri = new Uri(AppDomain.CurrentDomain.BaseDirectory + @"\Sounds\woop.mp3");


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


            CharacterNameIDCache = new Dictionary<string, long>();
            CharacterIDNameCache = new Dictionary<long, string>();

            InitializeComponent();

            Title = $"HISA : {EveAppConfig.HISA_TITLE} ({EveAppConfig.HISA_VERSION})";

            // Load the Dock Manager Layout file
            string dockManagerLayoutName = Path.Combine(EveAppConfig.StorageRoot, "Layout_" + WindowLayoutVersion + ".dat");
            if(File.Exists(dockManagerLayoutName) && OperatingSystem.IsWindows())
            {
                try
                {
                    AvalonDock.Layout.Serialization.XmlLayoutSerializer ls = new(dockManager);
                    using(var sr = new StreamReader(dockManagerLayoutName))
                    {
                        ls.Deserialize(sr);
                    }
                }
                catch
                {
                }
            }

            // Due to bugs in the Dock manager patch up the content id's for the 2 main views
            RegionLayoutDoc = FindDocWithContentID(dockManager.Layout, "MapRegionContentID");
            UniverseLayoutDoc = FindDocWithContentID(dockManager.Layout, "FullUniverseViewID");
            RestoreLastDockSelection();

            // load any custom map settings off disk
            string mapConfigFileName = Path.Combine(EveAppConfig.StorageRoot, "MapConfig_" + MapConfig.SaveVersion + ".dat");

            if(File.Exists(mapConfigFileName))
            {
                try
                {
                    XmlSerializer xms = new XmlSerializer(typeof(MapConfig));
                    FileStream fs = new FileStream(mapConfigFileName, FileMode.Open);
                    XmlReader xmlr = XmlReader.Create(fs);

                    MapConf = (MapConfig)xms.Deserialize(xmlr);
                    fs.Close();
                }
                catch
                {
                    MapConf = new MapConfig();
                    MapConf.SetDefaultColours();
                }
            }
            else
            {
                MapConf = new MapConfig();
                MapConf.SetDefaultColours();
            }

#if DEBUG
            MapConf.Debug_EnableMapEdit = true;
#else
            MapConf.Debug_EnableMapEdit = false;
#endif

            if(MapConf.AlwaysOnTop)
            {
                this.Topmost = true;
            }

            // Create the main EVE manager

            CapitalRoute = new JumpRoute();

            EVEManager = new EVEData.EveManager(EveAppConfig.HISA_VERSION);
            EVEData.EveManager.Instance = EVEManager;
            
            // Set up UI thread marshaling for ObservableCollection operations
            EVEData.EveManager.UIThreadInvoker = (action) =>
            {
                if(action == null)
                {
                    return;
                }

                Dispatcher dispatcher = Application.Current?.Dispatcher;
                if(dispatcher == null || dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished)
                {
                    return;
                }

                try
                {
                    if(dispatcher.CheckAccess())
                    {
                        action();
                    }
                    else
                    {
                        // Non-blocking marshal avoids shutdown races while background threads are winding down.
                        dispatcher.BeginInvoke(action, DispatcherPriority.Normal);
                    }
                }
                catch(InvalidOperationException)
                {
                    // Dispatcher/app is shutting down.
                }
                catch(TaskCanceledException)
                {
                    // Dispatcher queue was canceled during shutdown.
                }
            };
            
            EVEManager.EVELogFolder = MapConf.CustomEveLogFolderLocation;

            EVEManager.UseESIForCharacterPositions = MapConf.UseESIForCharacterPositions;

            // if we want to re-build the data as we've changed the format, recreate it all from scratch
            bool initFromScratch = false;

            if(initFromScratch)
            {
                SaveDefaultLayout();
                return;
            }
            else
            {
                Stopwatch startupDataLoad = Stopwatch.StartNew();
                EVEManager.LoadBaseDataFromDisk();
                startupDataLoad.Stop();
                Debug.WriteLine($"[StartupTiming] MainWindow base load call: {startupDataLoad.ElapsedMilliseconds} ms");
            }

            ApplyLastRegionPreference();
            string resolvedStartupRegion = RegionStartupResolver.GetStartupRegionName(
                EVEManager,
                Properties.Settings.Default.LastRegionsViewRegion,
                MapConf?.DefaultRegion);

            EVEManager.SetupIntelWatcher();
            EVEManager.SetupGameLogWatcher();
            EVEManager.SetupLogFileTriggers();

            IntelCache = new ObservableCollection<IntelData>();
            RawIntelBox.ItemsSource = IntelCache;

            GameLogCache = new ObservableCollection<GameLogData>();
            RawGameDataBox.ItemsSource = GameLogCache;

            // add test intel with debug info
            IntelData id = new IntelData("[00:00] blah.... > blah", "System");
            id.IntelString = "Intel Watcher monitoring : " + EVEManager.EVELogFolder + @"\Chatlogs\";

            IntelData idtwo = new IntelData("[00:00] blah.... > blah", "System");
            idtwo.IntelString = "Intel Filters : " + String.Join(",", EVEManager.IntelFilters);

            IntelData idthree = new IntelData("[00:00] blah... > blah", "System");
            idthree.IntelString = "Intel Alert Filters : " + String.Join(", ", EVEManager.IntelAlertFilters);

            IntelData idfour = new IntelData("[00:00] blah... > blah", "System");
            idfour.IntelString = "Intel Alert Filters Count: " + EVEManager.IntelAlertFilters.Count();

            IntelCache.Add(id);
            IntelCache.Add(idtwo);
            IntelCache.Add(idthree);
            IntelCache.Add(idfour);

            MapConf.CurrentEveLogFolderLocation = EVEManager.EVELogFolder;

            EVEManager.ZKillFeed.KillExpireTimeMinutes = MapConf.ZkillExpireTimeMinutes;

            // load jump bridge data
            EVEManager.LoadJumpBridgeData();
            EVEManager.InitNavigation();

            EVEManager.UpdateMetaliminalStorms();

            EVEManager.LocalCharacterUpdateEvent += LocalCharacters_CollectionChanged;

            CharactersList.ItemsSource = EVEManager.LocalCharacters;
            CurrentActiveCharacterCombo.ItemsSource = EVEManager.LocalCharacters;

            FleetMembersList.DataContext = this;

            TheraConnectionsList.ItemsSource = EVEManager.TheraConnections;
            EVEManager.TheraUpdateEvent += TheraConnections_CollectionChanged;

            TurnurConnectionsList.ItemsSource = EVEManager.TurnurConnections;
            EVEManager.TurnurUpdateEvent += TurnurConnections_CollectionChanged;

            MetaliminalStormList.ItemsSource = EVEManager.MetaliminalStorms;
            EVEManager.StormsUpdateEvent += Storms_CollectionChanged;

            SovCampaignList.ItemsSource = EVEManager.ActiveSovCampaigns;
            CollectionView sovCampaignView = (CollectionView)CollectionViewSource.GetDefaultView(SovCampaignList.ItemsSource);
            sovCampaignView.Filter = item => SovCampaignFilter(item);
            EVEManager.SovUpdateEvent += ActiveSovCampaigns_CollectionChanged;

            LoadInfoObjects();

            // load any custom universe view layout
            // Save any custom map Layout

            string customLayoutFile = Path.Combine(EveAppConfig.VersionStorage, "CustomUniverseLayout.txt");

            if(File.Exists(customLayoutFile))
            {
                try
                {
                    using(TextReader tr = new StreamReader(customLayoutFile))
                    {
                        string line = tr.ReadLine();

                        while(line != null)
                        {
                            string[] bits = line.Split(',');
                            string region = bits[0];
                            string system = bits[1];
                            double x = double.Parse(bits[2]);
                            double y = double.Parse(bits[3]);

                            EVEData.System sys = EVEManager.GetEveSystem(system);
                            if(sys != null)
                            {
                                sys.UniverseX = x;
                                sys.UniverseY = y;
                                sys.CustomUniverseLayout = true;
                            }

                            line = tr.ReadLine();
                        }
                    }
                }
                catch { }
            }

            // load the anom data
            string anomDataFilename = EVEManager.SaveDataVersionFolder + @"\Anoms.dat";
            if(File.Exists(anomDataFilename))
            {
                try
                {
                    XmlSerializer xms = new XmlSerializer(typeof(EVEData.AnomManager));

                    FileStream fs = new FileStream(anomDataFilename, FileMode.Open);
                    XmlReader xmlr = XmlReader.Create(fs);

                    ANOMManager = (EVEData.AnomManager)xms.Deserialize(xmlr);
                    fs.Close();
                }
                catch
                {
                    ANOMManager = new EVEData.AnomManager();
                }
            }
            else
            {
                ANOMManager = new EVEData.AnomManager();
            }

            RegionUC.MapConf = MapConf;
            RegionUC.ANOMManager = ANOMManager;
            RegionUC.Init();
            if(!string.IsNullOrWhiteSpace(resolvedStartupRegion))
            {
                SelectRegionWithoutPersist(resolvedStartupRegion);
            }

            RegionUC.RegionChanged += RegionUC_RegionChanged;
            RegionUC.UniverseSystemSelect += RegionUC_UniverseSystemSelect;

            RegionUC.SystemHoverEvent += RegionUC_SystemHoverEvent; ;

            UniverseUC.MapConf = MapConf;
            UniverseUC.CapitalRoute = CapitalRoute;
            UniverseUC.Init();
            UniverseUC.RequestRegionSystem += UniverseUC_RequestRegionSystem;

            RegionsViewUC.MapConf = MapConf;
            RegionsViewUC.Init();
            RegionsViewUC.RequestRegion += RegionsViewUC_RequestRegion;
            RestoreRegionsViewSelection();
            StartCustomRegionLoadAsync();

            AppStatusBar.DataContext = EVEManager.ServerInfo;

            List<EVEData.System> globalSystemList = new List<EVEData.System>(EVEManager.Systems);
            globalSystemList.Sort((a, b) => string.Compare(a.Name, b.Name));
            RouteSystemDropDownAC.ItemsSource = globalSystemList;
            JumpRouteSystemDropDownAC.ItemsSource = globalSystemList;
            JumpRouteAvoidSystemDropDownAC.ItemsSource = globalSystemList;

            MapConf.PropertyChanged += MapConf_PropertyChanged;

            Closing += MainWindow_Closing;
            Closed += MainWindow_Closed;
            StateChanged += MainWindow_StateChanged;

            EVEManager.IntelUpdatedEvent += OnIntelUpdated;
            EVEManager.GameLogAddedEvent += OnGamelogUpdated;
            EVEManager.ShipDecloakedEvent += OnShipDecloaked;
            EVEManager.CombatEvent += OnCombatEvent;

            uiRefreshTimer = new System.Windows.Threading.DispatcherTimer();
            uiRefreshTimer.Tick += UiRefreshTimer_Tick;
            uiRefreshTimer.Interval = new TimeSpan(0, 0, 1);
            uiRefreshTimer.Start();

            ZKBFeed.ItemsSource = EVEManager.ZKillFeed.KillStream;

            CollectionView zKBFeedview = (CollectionView)CollectionViewSource.GetDefaultView(ZKBFeed.ItemsSource);
            manualZKillFilterRefreshRequired = true;

            // Define your existing filter logic
            Predicate<object> initialFilter = item => ZKBFeedFilter(item);

            // Apply the both filters
            zKBFeedview.Filter = item =>
            {
                // copy the list before checking
                var listToFilter = EVEManager.ZKillFeed.KillStream.ToList();
                var filteredItems = listToFilter.Where(initialFilter.Invoke).Take(30).ToList();
                return filteredItems.Contains((ZKBDataSimple)item);
            };

            EVEManager.ZKillFeed.KillsAddedEvent += OnZKillsAdded;

            foreach(EVEData.LocalCharacter lc in EVEManager.LocalCharacters)
            {
                lc.Location = "";
            }

            // Listen to notification activation and select the character if clicked on
            ToastNotificationManagerCompat.OnActivated += toastArgs =>
            {
                // Obtain the arguments from the notification
                ToastArguments args = ToastArguments.Parse(toastArgs.Argument);

                if(args.Contains("character"))
                {
                    string charName = args["character"];

                    foreach(EVEData.LocalCharacter lc in EVEManager.LocalCharacters)
                    {
                        if(lc.Name == charName)
                        {
                            // Need to dispatch to UI thread if performing UI operations
                            Application.Current.Dispatcher.Invoke(delegate
                            {
                                ActivateCharacter(lc, setComboSelection: true, followCharacterOnMap: true, updateRegionsViewCharacter: false);
                            });

                            break;
                        }
                    }
                }
            };

            using(System.Diagnostics.Process currentProcess = System.Diagnostics.Process.GetCurrentProcess())
            {
                nIcon.Icon = System.Drawing.Icon.ExtractAssociatedIcon(currentProcess.MainModule.FileName);
            }
            nIcon.Visible = false;
            nIcon.Text = "HISA";
            nIcon.DoubleClick += NIcon_DClick;
            nIcon.ContextMenuStrip = new System.Windows.Forms.ContextMenuStrip();
            nIcon.ContextMenuStrip.Items.Add("Show", null, NIcon_DClick);
            nIcon.ContextMenuStrip.Items.Add("Exit", null, NIcon_Exit);

            CheckGitHubVersion();

            if(!string.IsNullOrWhiteSpace(resolvedStartupRegion))
            {
                SelectRegionWithoutPersist(resolvedStartupRegion);
            }
            else
            {
                SelectRegionWithoutPersist(MapConf.DefaultRegion);
            }
        }

        private void RegionUC_SystemHoverEvent(string system)
        {
            string hoverSystem = system ?? string.Empty;
            if(string.Equals(lastHoveredSystemForIntel, hoverSystem, StringComparison.Ordinal))
            {
                return;
            }
            lastHoveredSystemForIntel = hoverSystem;

            RawIntelBox.SelectedItem = null;

            // iterate over all of the RawIntelBox ui items
            for(int i = 0; i < RawIntelBox.Items.Count; i++)
            {
                IntelData id = RawIntelBox.Items[i] as IntelData;
                if(id != null)
                {
                    if(hoverSystem != string.Empty && id.Systems.Contains(hoverSystem))
                    {
                        // highlight the item
                        ListViewItem lvi = RawIntelBox.ItemContainerGenerator.ContainerFromItem(id) as ListViewItem;
                        if(lvi != null)
                        {
                            lvi.Background = Brushes.DarkRed;
                        }
                    }
                    else
                    {
                        // remove the highlight
                        ListViewItem lvi = RawIntelBox.ItemContainerGenerator.ContainerFromItem(id) as ListViewItem;
                        if(lvi != null)
                        {
                            lvi.Background = Brushes.Transparent;
                        }
                    }
                }
            }
        }

        private void OnGamelogUpdated(List<EVEData.GameLogData> gll)
        {
            Application.Current.Dispatcher.Invoke((Action)(() =>
            {
                List<GameLogData> removeList = new List<GameLogData>();
                List<GameLogData> addList = new List<GameLogData>();

                // remove old

                if(GameLogCache.Count > 50)
                {
                    foreach(GameLogData gl in GameLogCache)
                    {
                        if(!gll.Contains(gl))
                        {
                            removeList.Add(gl);
                        }
                    }

                    foreach(GameLogData gl in removeList)
                    {
                        GameLogCache.Remove(gl);
                    }
                }

                // add new
                foreach(GameLogData gl in gll)
                {
                    if(!GameLogCache.Contains(gl))
                    {
                        GameLogCache.Insert(0, gl);
                    }
                }
            }), DispatcherPriority.Normal);
        }

        private void Storms_CollectionChanged()
        {
            Application.Current.Dispatcher.Invoke((Action)(() =>
            {
                CollectionViewSource.GetDefaultView(MetaliminalStormList.ItemsSource).Refresh();
            }), DispatcherPriority.Normal);
        }

        private void TheraConnections_CollectionChanged()
        {
            Application.Current.Dispatcher.Invoke((Action)(() =>
            {
                CollectionViewSource.GetDefaultView(TheraConnectionsList.ItemsSource).Refresh();
            }), DispatcherPriority.Normal);
        }

        private void TurnurConnections_CollectionChanged()
        {
            Application.Current.Dispatcher.Invoke((Action)(() =>
            {
                CollectionViewSource.GetDefaultView(TurnurConnectionsList.ItemsSource).Refresh();
            }), DispatcherPriority.Normal);
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            WindowPlacement.SetPlacement(new WindowInteropHelper(this).Handle, Properties.Settings.Default.MainWindow_placement);
        }

        private void SaveDefaultLayout()
        {
            // first delete the existing
            string defaultLayoutFile = AppDomain.CurrentDomain.BaseDirectory + @"\DefaultWindowLayout.dat";

            if(File.Exists(defaultLayoutFile))
            {
                File.Delete(defaultLayoutFile);
            }

            try
            {
                if(OperatingSystem.IsWindows())
                {
                    AvalonDock.Layout.Serialization.XmlLayoutSerializer ls = new AvalonDock.Layout.Serialization.XmlLayoutSerializer(dockManager);
                    using(var sw = new StreamWriter(defaultLayoutFile))
                    {
                        ls.Serialize(sw);
                    }
                }
            }
            catch
            {
            }
        }

        /// <summary>
        /// Anom Manager
        /// </summary>
        public EVEData.AnomManager ANOMManager { get; set; }

        /// <summary>
        /// Main Region Manager
        /// </summary>
        public EVEData.EveManager EVEManager { get; set; }

        /// <summary>
        /// Main Map Config
        /// </summary>
        public MapConfig MapConf { get; }

        private AvalonDock.Layout.LayoutDocument RegionLayoutDoc { get; set; }

        private AvalonDock.Layout.LayoutDocument UniverseLayoutDoc { get; set; }
        private const string DefaultDockContentId = "MapRegionContentID";

        private void RestoreLastDockSelection()
        {
            try
            {
                string contentId = Properties.Settings.Default.LastDockContentId;
                if(string.IsNullOrWhiteSpace(contentId))
                {
                    contentId = DefaultDockContentId;
                }

                AvalonDock.Layout.LayoutDocument doc = FindDocWithContentID(dockManager.Layout, contentId);
                if(doc != null)
                {
                    doc.IsSelected = true;
                }
            }
            catch
            {
            }
        }

        private void SaveLastDockSelection()
        {
            try
            {
                var docs = dockManager.Layout?.Descendents().OfType<AvalonDock.Layout.LayoutDocument>();
                AvalonDock.Layout.LayoutDocument selected = docs?.FirstOrDefault(d => d.IsSelected);
                if(selected != null && !string.IsNullOrWhiteSpace(selected.ContentId))
                {
                    Properties.Settings.Default.LastDockContentId = selected.ContentId;
                }
                else
                {
                    Properties.Settings.Default.LastDockContentId = DefaultDockContentId;
                }
                Properties.Settings.Default.Save();
            }
            catch
            {
            }
        }

        private void RestoreRegionsViewSelection()
        {
            try
            {
                string regionName = RegionStartupResolver.GetStartupRegionName(
                    EVEManager,
                    Properties.Settings.Default.LastRegionsViewRegion,
                    MapConf?.DefaultRegion);
                if(!string.IsNullOrWhiteSpace(regionName))
                {
                    RegionsViewUC?.SetSelectedRegion(regionName);
                }
            }
            catch
            {
            }
        }

        private void SaveRegionsViewSelection(string regionName)
        {
            if(string.IsNullOrWhiteSpace(regionName))
            {
                return;
            }

            if(suppressRegionSelectionPersistenceCount > 0)
            {
                return;
            }

            try
            {
                Properties.Settings.Default.LastRegionsViewRegion = regionName;
                Properties.Settings.Default.Save();
            }
            catch
            {
            }
        }

        private void ApplyLastRegionPreference()
        {
            try
            {
                string startupRegion = RegionStartupResolver.GetStartupRegionName(
                    EVEManager,
                    Properties.Settings.Default.LastRegionsViewRegion,
                    MapConf?.DefaultRegion);
                if(!string.IsNullOrWhiteSpace(startupRegion))
                {
                    MapConf.DefaultRegion = startupRegion;
                }
            }
            catch
            {
            }
        }

        private void SelectRegionWithoutPersist(string regionName)
        {
            if(string.IsNullOrWhiteSpace(regionName) || RegionUC == null)
            {
                return;
            }

            suppressRegionSelectionPersistenceCount++;
            try
            {
                RegionUC.SelectRegion(regionName);
            }
            finally
            {
                suppressRegionSelectionPersistenceCount--;
            }
        }

        private async void StartCustomRegionLoadAsync()
        {
            try
            {
                Stopwatch sw = Stopwatch.StartNew();
                List<MapRegion> customSnapshot = await Task.Run(() => EVEManager.LoadCustomRegionsSnapshotFromDisk());

                EVEManager.ApplyCustomRegionSnapshot(customSnapshot);

                RegionUC?.RefreshRegionList();
                RegionsViewUC?.Redraw(true);
                UniverseUC?.ReDrawMap(true, true, false);

                if(RegionUC?.Region != null)
                {
                    SelectRegionWithoutPersist(RegionUC.Region.Name);
                }

                string preferredRegion = RegionStartupResolver.GetPreferredRegionName(
                    Properties.Settings.Default.LastRegionsViewRegion,
                    MapConf?.DefaultRegion);
                string startupRegion = RegionStartupResolver.ResolveKnownRegionName(EVEManager, preferredRegion)
                    ?? RegionStartupResolver.GetStartupRegionName(
                        EVEManager,
                        Properties.Settings.Default.LastRegionsViewRegion,
                        MapConf?.DefaultRegion);
                if(!string.IsNullOrWhiteSpace(startupRegion) && RegionUC?.Region?.Name != startupRegion && EVEManager.GetRegion(startupRegion) != null)
                {
                    SelectRegionWithoutPersist(startupRegion);
                }

                sw.Stop();
                Debug.WriteLine($"[StartupTiming] Async custom region load + apply: {sw.ElapsedMilliseconds} ms");
            }
            catch(Exception ex)
            {
                Debug.WriteLine($"[StartupTiming] Async custom region load failed: {ex.Message}");
            }
        }

        private void ActiveSovCampaigns_CollectionChanged()
        {
            Application.Current.Dispatcher.Invoke((Action)(() =>
            {
                CollectionViewSource.GetDefaultView(SovCampaignList.ItemsSource).Refresh();
            }), DispatcherPriority.Normal);
        }

        private void LocalCharacters_CollectionChanged()
        {
            Application.Current.Dispatcher.Invoke((Action)(() =>
            {
                CollectionViewSource.GetDefaultView(CharactersList.ItemsSource).Refresh();
            }), DispatcherPriority.Normal);
        }

        private void Exit_MenuItem_Click(object sender, RoutedEventArgs e)
        {
            System.Windows.Application.Current.Shutdown();
        }

        private AvalonDock.Layout.LayoutDocument FindDocWithContentID(AvalonDock.Layout.ILayoutElement root, string contentID)
        {
            AvalonDock.Layout.LayoutDocument content = null;

            if(root is AvalonDock.Layout.ILayoutContainer)
            {
                AvalonDock.Layout.ILayoutContainer ic = root as AvalonDock.Layout.ILayoutContainer;
                foreach(AvalonDock.Layout.ILayoutElement ie in ic.Children)
                {
                    AvalonDock.Layout.LayoutDocument f = FindDocWithContentID(ie, contentID);
                    if(f != null)
                    {
                        content = f;
                        break;
                    }
                }
            }
            else
            {
                if(root is AvalonDock.Layout.LayoutDocument)
                {
                    AvalonDock.Layout.LayoutDocument i = root as AvalonDock.Layout.LayoutDocument;
                    if(i.ContentId == contentID)
                    {
                        content = i;
                    }
                }
            }
            return content;
        }

        private void MainWindow_Closing(object sender, CancelEventArgs e)
        {
            if(MapConf.CloseToTray && MapConf.MinimizeToTray && AppWindow.ShowInTaskbar != false)
            {
                e.Cancel = true;
                AppWindow.Hide();
                AppWindow.ShowInTaskbar = false;
                nIcon.Visible = true;
                return;
            }

            // Store the main window position and size

            Properties.Settings.Default.MainWindow_placement = WindowPlacement.GetPlacement(new WindowInteropHelper(AppWindow).Handle);
            Properties.Settings.Default.Save();

            EVEManager.ZKillFeed.KillsAddedEvent -= OnZKillsAdded;
        }

        private void MainWindow_Closed(object sender, EventArgs e)
        {
            SaveLastDockSelection();
            if(RegionUC?.Region != null && !string.IsNullOrWhiteSpace(RegionUC.Region.Name))
            {
                SaveRegionsViewSelection(RegionUC.Region.Name);
            }

            // save off the dockmanager layout
            string dockManagerLayoutName = Path.Combine(EveAppConfig.StorageRoot, "Layout_" + WindowLayoutVersion + ".dat");

            try
            {
                AvalonDock.Layout.Serialization.XmlLayoutSerializer ls = new AvalonDock.Layout.Serialization.XmlLayoutSerializer(dockManager);
                using(var sw = new StreamWriter(dockManagerLayoutName))
                {
                    ls.Serialize(sw);
                }
            }
            catch
            {
            }

            try
            {
                // Save off any explicit items
                MapConf.UseESIForCharacterPositions = EVEManager.UseESIForCharacterPositions;
                if(RegionUC?.Region != null && !string.IsNullOrWhiteSpace(RegionUC.Region.Name))
                {
                    MapConf.DefaultRegion = RegionUC.Region.Name;
                }

                // Save the Map Colours
                string mapConfigFileName = Path.Combine(EveAppConfig.StorageRoot, "MapConfig_" + MapConfig.SaveVersion + ".dat");

                // save off the toolbar setup
                MapConf.ToolBox_ShowJumpBridges = RegionUC.ShowJumpBridges;
                MapConf.ToolBox_ShowNPCKills = RegionUC.ShowNPCKills;
                MapConf.ToolBox_ShowPodKills = RegionUC.ShowPodKills;
                MapConf.ToolBox_ShowShipJumps = RegionUC.ShowShipJumps;
                MapConf.ToolBox_ShowShipKills = RegionUC.ShowShipKills;
                MapConf.ToolBox_ShowSovOwner = RegionUC.ShowSovOwner;
                MapConf.ToolBox_ShowStandings = RegionUC.ShowStandings;
                MapConf.ToolBox_ShowSystemADM = RegionUC.ShowSystemADM;
                MapConf.ToolBox_ShowSystemSecurity = RegionUC.ShowSystemSecurity;
                MapConf.ToolBox_ShowSystemTimers = RegionUC.ShowSystemTimers;
                MapConf.ToolBox_ShowInfrastructureUpgrades = RegionUC.ShowInfrastructureUpgrades;
                MapConf.ToolBox_ESIOverlayScale = RegionUC.ESIOverlayScale;

                // now serialise the class to disk
                XmlSerializer xms = new XmlSerializer(typeof(MapConfig));
                using(TextWriter tw = new StreamWriter(mapConfigFileName))
                {
                    xms.Serialize(tw, MapConf);
                }

                // Save any custom map Layout
                string customLayoutFile = Path.Combine(EveAppConfig.VersionStorage, "CustomUniverseLayout.txt");

                using(TextWriter tw = new StreamWriter(customLayoutFile))
                {
                    foreach(EVEData.System s in EVEManager.Systems)
                    {
                        if(s.CustomUniverseLayout)
                        {
                            tw.WriteLine($"{s.Region},{s.Name},{s.UniverseX},{s.UniverseY}");
                        }
                    }
                }
            }
            catch
            {
            }

            try
            {
                // save the Anom Data
                // now serialise the class to disk
                XmlSerializer anomxms = new XmlSerializer(typeof(EVEData.AnomManager));
                string anomDataFilename = EVEManager.SaveDataVersionFolder + @"\Anoms.dat";

                using(TextWriter tw = new StreamWriter(anomDataFilename))
                {
                    anomxms.Serialize(tw, ANOMManager);
                }
            }
            catch
            {
            }

            // save the character data
            EVEManager.SaveData();
            EVEManager.ShutDown();
        }

        private void MainWindow_StateChanged(object sender, EventArgs e)
        {
            if(this.WindowState == WindowState.Minimized)
            {
                if(MapConf.MinimizeToTray)
                {
                    AppWindow.Hide();
                    AppWindow.ShowInTaskbar = false;
                    nIcon.Visible = true;
                }
            }
        }

        private void NIcon_DClick(object sender, EventArgs e)
        {
            this.ShowInTaskbar = true;
            this.Visibility = Visibility.Visible;
            this.Show();
            this.WindowState = WindowState.Normal;
            nIcon.Visible = false;
            this.Activate();
        }

        private void NIcon_Exit(object sender, EventArgs e)
        {
            System.Windows.Application.Current.Shutdown();
        }

        private void SovCampaignList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if(sender != null)
            {
                DataGrid grid = sender as DataGrid;
                if(grid != null && grid.SelectedItems != null && grid.SelectedItems.Count == 1)
                {
                    DataGridRow dgr = grid.ItemContainerGenerator.ContainerFromItem(grid.SelectedItem) as DataGridRow;

                    EVEData.SOVCampaign sc = dgr.Item as EVEData.SOVCampaign;

                    if(sc != null)
                    {
                        SelectSystemOnRegionMap(sc.System, focusRegionDocument: true);
                    }
                }
            }
        }

        private void UiRefreshTimer_Tick(object sender, EventArgs e)
        {
            if(WindowState == WindowState.Minimized || !IsVisible)
            {
                return;
            }

            uiRefreshCounter++;
            if(uiRefreshCounter == 5)
            {
                uiRefreshCounter = 0;

                if(FleetMembersList.ItemsSource != null)
                {
                    Application.Current.Dispatcher.Invoke((Action)(() =>
                    {
                        CollectionViewSource.GetDefaultView(FleetMembersList.ItemsSource).Refresh();
                    }), DispatcherPriority.Normal);
                }
            }
            if(MapConf.SyncActiveCharacterBasedOnActiveEVEClient)
            {
                UpdateCharacterSelectionBasedOnActiveWindow();
            }

            if(manualZKillFilterRefreshRequired)
            {
                if(TryRefreshZKillFeedView())
                {
                    manualZKillFilterRefreshRequired = false;
                }
            }

            // refresh the anomalies datagrid every 60 seconds to update the "since" column
            anomRefreshCounter++;
            if(anomRefreshCounter == 60)
            {
                anomRefreshCounter = 0;
                AnomSigList.Items.Refresh();
            }
        }

        #region RegionsView Control

        private void RegionsViewUC_RequestRegion(object sender, RoutedEventArgs e)
        {
            string regionName = e.OriginalSource as string;
            RegionUC.FollowCharacter = false;
            RegionUC.SelectRegion(regionName);
            SaveRegionsViewSelection(regionName);
            RegionsViewUC?.SetSelectedRegion(regionName);

            if(RegionLayoutDoc != null)
            {
                RegionLayoutDoc.IsSelected = true;
            }
        }

        #endregion RegionsView Control

        #region Region Control

        private void SelectSystemOnRegionMap(string systemName, bool focusRegionDocument)
        {
            if(string.IsNullOrWhiteSpace(systemName))
            {
                return;
            }

            RegionUC.SelectSystem(systemName, true);

            if(focusRegionDocument && RegionLayoutDoc != null)
            {
                RegionLayoutDoc.IsSelected = true;
            }
        }

        private void MapConf_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if(e.PropertyName == "AlwaysOnTop")
            {
                if(MapConf.AlwaysOnTop)
                {
                    this.Topmost = true;
                }
                else
                {
                    this.Topmost = false;
                }
            }

            if(e.PropertyName == "ShowZKillData")
            {
                EVEManager.ZKillFeed.PauseUpdate = !MapConf.ShowZKillData;
            }

            RegionUC.ReDrawMap(true);

            if(e.PropertyName == "ShowRegionStandings")
            {
                RegionsViewUC.Redraw(true);
            }

            if(e.PropertyName == "ShowUniverseRats")
            {
                RegionsViewUC.Redraw(true);
            }

            if(e.PropertyName == "ShowUniversePods")
            {
                RegionsViewUC.Redraw(true);
            }

            if(e.PropertyName == "ShowUniverseKills")
            {
                RegionsViewUC.Redraw(true);
            }

            if(e.PropertyName == "UniverseDataScale")
            {
                RegionsViewUC.Redraw(true);
            }
        }

        public void OnCharacterSelectionChanged()
        {
            manualZKillFilterRefreshRequired = true;
        }

        private void RegionUC_RegionChanged(object sender, PropertyChangedEventArgs e)
        {
            if(RegionLayoutDoc != null)
            {
                RegionLayoutDoc.Title = RegionUC.Region.Name;
            }

            manualZKillFilterRefreshRequired = true;

            if(SovCampaignList != null && SovCampaignList.ItemsSource != null)
            {
                CollectionViewSource.GetDefaultView(SovCampaignList.ItemsSource).Refresh();
            }

            if(RegionUC?.Region != null && !string.IsNullOrWhiteSpace(RegionUC.Region.Name))
            {
                SaveRegionsViewSelection(RegionUC.Region.Name);
                RegionsViewUC?.SetSelectedRegion(RegionUC.Region.Name);
            }
        }

        private void RegionUC_UniverseSystemSelect(object sender, RoutedEventArgs e)
        {
            string sysName = e.OriginalSource as string;
            UniverseUC.ShowSystem(sysName);

            if(UniverseLayoutDoc != null)
            {
                UniverseLayoutDoc.IsSelected = true;
            }
        }

        #endregion Region Control

        #region Universe Control

        private void UniverseUC_RequestRegionSystem(object sender, RoutedEventArgs e)
        {
            string sysName = e.OriginalSource as string;
            RegionUC.FollowCharacter = false;
            SelectSystemOnRegionMap(sysName, focusRegionDocument: true);
        }

        #endregion Universe Control

        #region Preferences & Options

        private void ForceESIUpdate_MenuItem_Click(object sender, RoutedEventArgs e)
        {
            EVEManager.UpdateESIUniverseData();
        }

        private void ClearOldEVELogs_Click(object sender, RoutedEventArgs e)
        {
            string EVEGameLogFolder = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments) + @"\EVE\logs\Gamelogs";
            {
                DirectoryInfo di = new DirectoryInfo(EVEGameLogFolder);
                FileInfo[] files = di.GetFiles("*.txt");
                foreach(FileInfo file in files)
                {
                    // keep only recent files
                    if(file.CreationTime < DateTime.Now.AddDays(-1))
                    {
                        try
                        {
                            File.Delete(file.FullName);
                        }
                        catch
                        {
                        }
                    }
                }
            }

            string EVEChatLogFolder = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments) + @"\EVE\logs\Chatlogs";
            {
                DirectoryInfo di = new DirectoryInfo(EVEChatLogFolder);
                FileInfo[] files = di.GetFiles("*.txt");
                foreach(FileInfo file in files)
                {
                    // keep only recent files
                    if(file.CreationTime < DateTime.Now.AddDays(-1))
                    {
                        try
                        {
                            File.Delete(file.FullName);
                        }
                        catch
                        {
                        }
                    }
                }
            }
        }

        private void ManageInfrastructureUpgrades_Click(object sender, RoutedEventArgs e)
        {
            InfrastructureUpgradeWindow upgradeWindow = new InfrastructureUpgradeWindow();
            upgradeWindow.EM = EVEManager;
            upgradeWindow.Owner = this;
            upgradeWindow.ShowDialog();

            // Refresh the current region view after closing the window
            if(RegionUC != null)
            {
                RegionUC.ReDrawMap(false);
            }
        }

        private void LoadInfrastructureUpgrades_Click(object sender, RoutedEventArgs e)
        {
            Microsoft.Win32.OpenFileDialog dlg = new Microsoft.Win32.OpenFileDialog();
            dlg.DefaultExt = ".txt";
            dlg.Filter = "Text Files (*.txt)|*.txt|All Files (*.*)|*.*";
            dlg.Title = "Load Infrastructure Upgrades";

            string saveRoot = !string.IsNullOrWhiteSpace(EVEManager?.SaveDataRootFolder)
                ? EVEManager.SaveDataRootFolder
                : EVEData.EveAppConfig.StorageRoot;

            // Default to the auto-load file location
            string defaultPath = System.IO.Path.Combine(saveRoot, "InfrastructureUpgrades.txt");
            if(System.IO.File.Exists(defaultPath))
            {
                dlg.FileName = defaultPath;
            }
            else
            {
                dlg.InitialDirectory = saveRoot;
            }

            bool? result = dlg.ShowDialog();

            if(result == true)
            {
                string filename = dlg.FileName;
                EVEManager.LoadInfrastructureUpgrades(filename);
                // Persist loaded content to canonical startup file.
                EVEManager.SaveInfrastructureUpgrades(defaultPath);

                // Refresh the current region view to show the loaded upgrades
                if(RegionUC != null)
                {
                    RegionUC.ReDrawMap(false);
                }
            }
        }

        private void SaveInfrastructureUpgrades_Click(object sender, RoutedEventArgs e)
        {
            Microsoft.Win32.SaveFileDialog dlg = new Microsoft.Win32.SaveFileDialog();
            dlg.DefaultExt = ".txt";
            dlg.Filter = "Text Files (*.txt)|*.txt|All Files (*.*)|*.*";
            dlg.Title = "Save Infrastructure Upgrades";

            string saveRoot = !string.IsNullOrWhiteSpace(EVEManager?.SaveDataRootFolder)
                ? EVEManager.SaveDataRootFolder
                : EVEData.EveAppConfig.StorageRoot;

            // Default to the auto-load file location
            string defaultPath = System.IO.Path.Combine(saveRoot, "InfrastructureUpgrades.txt");
            dlg.FileName = defaultPath;

            bool? result = dlg.ShowDialog();

            if(result == true)
            {
                string filename = dlg.FileName;
                EVEManager.SaveInfrastructureUpgrades(filename);
            }
        }

        private void PasteInfrastructureUpgrades_Click(object sender, RoutedEventArgs e)
        {
            PasteInfrastructureUpgradesWindow pasteWindow = new PasteInfrastructureUpgradesWindow();
            pasteWindow.EM = EVEManager;
            pasteWindow.Owner = this;
            bool? result = pasteWindow.ShowDialog();

            if (result == true && RegionUC != null)
            {
                RegionUC.ReDrawMap(false);
            }
        }

        private void ManageNavStructures_Click(object sender, RoutedEventArgs e)
        {
            NavStructuresWindow navWindow = new NavStructuresWindow();
            navWindow.EM = EVEManager;
            navWindow.Owner = this;
            navWindow.ShowDialog();

            if (RegionUC != null)
            {
                RegionUC.ReDrawMap(false);
            }
        }

        private void ImportCustomRegions_MenuItem_Click(object sender, RoutedEventArgs e)
        {
            RegionUC?.ImportCustomRegionsFromDialog();
        }

        private void ManageCustomRegions_MenuItem_Click(object sender, RoutedEventArgs e)
        {
            CustomRegionsWindow win = new CustomRegionsWindow(EVEManager, RegionUC);
            win.Owner = this;
            win.ShowDialog();
        }

        private void CreateCustomRegion_MenuItem_Click(object sender, RoutedEventArgs e)
        {
            if(EVEManager == null)
            {
                return;
            }

            List<MapRegion> allRegions = EVEManager.Regions.ToList();
            CustomRegionCreateWindow dlg = new CustomRegionCreateWindow(EVEManager, allRegions);
            dlg.Owner = this;
            if(dlg.ShowDialog() != true)
            {
                return;
            }

            string name = dlg.RegionName;
            List<MapRegion> selected = dlg.SelectedRegions;
            MapRegion created = EVEManager.CreateCustomRegionFromRegions(selected, name, out string error);
            if(created == null)
            {
                MessageBox.Show("Create failed: " + error, "Custom Regions", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            RegionUC.RefreshRegionList();
            RegionUC.SelectRegion(created.Name);
        }

        private void ExportCustomRegion_MenuItem_Click(object sender, RoutedEventArgs e)
        {
            if(RegionUC?.Region == null)
            {
                return;
            }

            SaveFileDialog dlg = new SaveFileDialog
            {
                Title = "Export Region",
                Filter = "HISA Region Pack (*.hisaregion)|*.hisaregion|Zip (*.zip)|*.zip|XML (*.xml)|*.xml|All files (*.*)|*.*",
                FileName = RegionUC.Region.Name + ".hisaregion"
            };

            if(dlg.ShowDialog() != true)
            {
                return;
            }

            if(!EVEManager.ExportRegion(RegionUC.Region, dlg.FileName, out string error))
            {
                MessageBox.Show("Export failed: " + error, "Custom Regions", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void RegionColorLegend_MenuItem_Click(object sender, RoutedEventArgs e)
        {
            if(RegionUC == null || RegionUC.Region == null || !RegionUC.Region.IsCustom)
            {
                MessageBox.Show("Open a custom region to view its color legend.", "Region Colors", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            List<RegionControl.RegionColorItem> items = RegionUC.GetRegionColorLegend();
            RegionColorLegendWindow win = new RegionColorLegendWindow(items);
            win.Owner = this;
            win.ShowDialog();
        }

        private void FullScreenToggle_MenuItem_Click(object sender, RoutedEventArgs e)
        {
            if(miFullScreenToggle.IsChecked)
            {
                WindowStyle = WindowStyle.None;
                WindowState = WindowState.Maximized;
            }
            else
            {
                WindowStyle = WindowStyle.SingleBorderWindow;
                WindowState = WindowState.Normal;
            }
        }

        private void Preferences_MenuItem_Click(object sender, RoutedEventArgs e)
        {
            if(preferencesWindow != null)
            {
                preferencesWindow.Close();
            }

            preferencesWindow = new PreferencesWindow();
            preferencesWindow.Closed += PreferencesWindow_Closed;
            preferencesWindow.Owner = this;
            preferencesWindow.DataContext = MapConf;
            preferencesWindow.MapConf = MapConf;
            preferencesWindow.EM = EVEManager;
            preferencesWindow.ShowDialog();
        }

        private void PreferencesWindow_Closed(object sender, EventArgs e)
        {
            RegionUC.ReDrawMap(true);
            UniverseUC.ReDrawMap(true, true, false);

            // recalculate the route if required
            if(ActiveCharacter != null && ActiveCharacter.Waypoints.Count > 0)
            {
                ActiveCharacter.RecalcRoute();
            }
        }

        #endregion Preferences & Options

        #region NewVersion

        private async void CheckGitHubVersion()
        {
            string url = @"https://api.github.com/repos/slazanger/HISA/releases/latest";
            string strContent = string.Empty;

            try
            {
                HttpClient hc = new HttpClient();
                hc.DefaultRequestHeaders.UserAgent.Add(new System.Net.Http.Headers.ProductInfoHeaderValue("HISA", EveAppConfig.HISA_VERSION));
                var response = await hc.GetAsync(url);
                response.EnsureSuccessStatusCode();
                strContent = await response.Content.ReadAsStringAsync();
            }
            catch
            {
                return;
            }

            GitHubRelease.Release releaseInfo = GitHubRelease.Release.FromJson(strContent);

            if(releaseInfo != null)
            {
                if(releaseInfo.TagName != EveAppConfig.HISA_VERSION)
                {
                    Application.Current.Dispatcher.Invoke((Action)(() =>
                    {
                        NewVersionWindow nw = new NewVersionWindow();
                        nw.ReleaseInfo = releaseInfo.Body;
                        nw.CurrentVersion = EveAppConfig.HISA_VERSION;
                        nw.NewVersion = releaseInfo.TagName;
                        nw.ReleaseURL = releaseInfo.HtmlUrl.ToString();
                        nw.Owner = this;
                        nw.ShowDialog();
                    }), DispatcherPriority.Normal);
                }
            }
        }

        #endregion NewVersion

        #region Characters

        // Property now automatically fires an event when the active character changes.
        private EVEData.LocalCharacter activeCharacter;

        public EVEData.LocalCharacter ActiveCharacter
        { get => activeCharacter; set { activeCharacter = value; OnSelectedCharChangedEventHandler?.Invoke(this, EventArgs.Empty); } }

        /// <summary>
        ///  Add Character Button Clicked
        /// </summary>
        private void btn_AddCharacter_Click(object sender, RoutedEventArgs e)
        {
            AddCharacter();
        }

        public void AddCharacter()
        {
            if(logonBrowserWindow != null)
            {
                logonBrowserWindow.Close();
            }

            logonBrowserWindow = new LogonWindow();
            logonBrowserWindow.Owner = this;
            logonBrowserWindow.ShowDialog();
        }

        private void ActivateCharacter(EVEData.LocalCharacter character, bool setComboSelection, bool followCharacterOnMap, bool updateRegionsViewCharacter)
        {
            if(character == null)
            {
                return;
            }

            ActiveCharacter = character;

            if(setComboSelection)
            {
                CurrentActiveCharacterCombo.SelectedItem = character;
            }

            FleetMembersList.ItemsSource = character.FleetInfo.Members;
            CollectionViewSource.GetDefaultView(FleetMembersList.ItemsSource).Refresh();

            character.FleetUpdatedEvent -= OnFleetMemebersUpdate;
            character.FleetUpdatedEvent += OnFleetMemebersUpdate;

            character.RouteUpdatedEvent -= OnCharacterRouteUpdate;
            character.RouteUpdatedEvent += OnCharacterRouteUpdate;

            if(updateRegionsViewCharacter)
            {
                RegionsViewUC.ActiveCharacter = character;
            }

            RegionUC.UpdateActiveCharacter(character);
            UniverseUC.UpdateActiveCharacter(character);

            if(followCharacterOnMap)
            {
                RegionUC.FollowCharacter = true;
                RegionUC.SelectSystem(character.Location, true);

                UniverseUC.FollowCharacter = true;
            }
        }

        private void CharactersList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if(sender != null)
            {
                DataGrid grid = sender as DataGrid;
                if(grid != null && grid.SelectedItems != null && grid.SelectedItems.Count == 1)
                {
                    DataGridRow dgr = grid.ItemContainerGenerator.ContainerFromItem(grid.SelectedItem) as DataGridRow;
                    EVEData.LocalCharacter lc = dgr.Item as EVEData.LocalCharacter;

                    if(lc != null)
                    {
                        ActivateCharacter(lc, setComboSelection: true, followCharacterOnMap: true, updateRegionsViewCharacter: false);
                    }
                }
            }
        }

        private void OnCharacterRouteUpdate()
        {
            Application.Current.Dispatcher.Invoke((Action)(() =>
            {
                CollectionViewSource.GetDefaultView(dgActiveCharacterRoute.ItemsSource).Refresh();
                CollectionViewSource.GetDefaultView(lbActiveCharacterWaypoints.ItemsSource).Refresh();
            }), DispatcherPriority.Normal);
        }

        private void CharactersListMenuItemDelete_Click(object sender, RoutedEventArgs e)
        {
            if(CharactersList.SelectedIndex == -1)
            {
                return;
            }

            EVEData.LocalCharacter lc = CharactersList.SelectedItem as EVEData.LocalCharacter;

            lc.FleetUpdatedEvent -= OnFleetMemebersUpdate;
            lc.RouteUpdatedEvent -= OnCharacterRouteUpdate;

            ActiveCharacter = null;
            FleetMembersList.ItemsSource = null;

            CurrentActiveCharacterCombo.SelectedIndex = -1;
            RegionsViewUC.ActiveCharacter = null;
            RegionUC.ActiveCharacter = null;
            RegionUC.UpdateActiveCharacter();
            UniverseUC.ActiveCharacter = null;
            OnCharacterSelectionChanged();

            EVEManager.RemoveCharacter(lc);
        }

        private void CurrentActiveCharacterCombo_Selected(object sender, SelectionChangedEventArgs e)
        {
            if(CurrentActiveCharacterCombo.SelectedIndex == -1)
            {
                RegionsViewUC.ActiveCharacter = null;
                RegionUC.ActiveCharacter = null;
                FleetMembersList.ItemsSource = null;
                RegionUC.UpdateActiveCharacter();
                UniverseUC.UpdateActiveCharacter(null);
            }
            else
            {
                EVEData.LocalCharacter lc = CurrentActiveCharacterCombo.SelectedItem as EVEData.LocalCharacter;
                ActivateCharacter(lc, setComboSelection: false, followCharacterOnMap: false, updateRegionsViewCharacter: true);
            }

            OnCharacterSelectionChanged();
        }

        private void UpdateCharacterSelectionBasedOnActiveWindow()
        {
            string ActiveWindowText = Utils.Misc.GetCaptionOfActiveWindow();

            if(ActiveWindowText.Contains("EVE - "))
            {
                string characterName = ActiveWindowText.Substring(6);
                foreach(EVEData.LocalCharacter lc in EVEManager.LocalCharacters)
                {
                    if(lc.Name == characterName)
                    {
                        ActivateCharacter(lc, setComboSelection: true, followCharacterOnMap: false, updateRegionsViewCharacter: false);

                        break;
                    }
                }
            }
        }

        public void OnFleetMemebersUpdate(LocalCharacter c)
        {
            Application.Current.Dispatcher.Invoke((Action)(() =>
            {
                CollectionViewSource.GetDefaultView(FleetMembersList.ItemsSource).Refresh();
            }), DispatcherPriority.Normal);
        }

        #endregion Characters

        #region intel

        private ObservableCollection<EVEData.IntelData> IntelCache;

        private ObservableCollection<EVEData.GameLogData> GameLogCache;

        private void ClearIntelBtn_Click(object sender, RoutedEventArgs e)
        {
            EVEManager.IntelDataList.ClearAll();
            IntelCache.Clear();
        }

        private void ClearGameLogBtn_Click(object sender, RoutedEventArgs e)
        {
            EVEManager.GameLogList.ClearAll();
            GameLogCache.Clear();
        }

        private void OnIntelUpdated(List<IntelData> idl)
        {
            Application.Current.Dispatcher.Invoke((Action)(() =>
            {
                IntelCacheHelper.SyncRecent(IntelCache, idl, 250);
            }), DispatcherPriority.Normal);

            // Always refresh map intel visuals (ring/background) when intel updates,
            // including clear notifications.
            Application.Current.Dispatcher.Invoke((Action)(() =>
            {
                if(RegionUC != null)
                {
                    bool fullRedrawForEnemyBackground = RegionUC.SelectedBackgroundTintMode == RegionControl.SystemBackgroundTintMode.Enemies;
                    RegionUC.ReDrawMap(fullRedrawForEnemyBackground);
                }
            }), DispatcherPriority.Normal);

            if(IntelCache.Count == 0)
            {
                return;
            }

            IntelData id = IntelCache[0];
            if(id.ClearNotification)
            {
                // do nothing for now
                return;
            }

            HashSet<string> warningSystems = null;
            if(MapConf.PlaySoundOnlyInDangerZone || MapConf.FlashWindowOnlyInDangerZone)
            {
                warningSystems = DangerZoneHelper.GetDangerZoneSystems(MapConf, EVEManager);
            }

            IntelAlertDecision alertDecision = IntelAlertDecisionHelper.Evaluate(
                id,
                MapConf,
                EVEManager.IntelAlertFilters,
                warningSystems);

            Application.Current.Dispatcher.Invoke((Action)(() =>
            {
                if(alertDecision.ShouldPlaySound)
                {
                    try
                    {
                        waveOutEvent.Stop();
                        waveOutEvent.Volume = MapConf.IntelSoundVolume;
                        audioFileReader.Position = 0;
                        waveOutEvent.Play();
                    }
                    catch(Exception ex)
                    {
                        // if the sound fails to play
                        // seen this after wake up from sleep
                        Debug.WriteLine("Failed to play intel sound: " + ex.Message);
                    }
                }
                if(alertDecision.ShouldFlashWindow)
                {
                    FlashWindow.Flash(AppWindow, 5);
                }
            }), DispatcherPriority.Normal);
        }

        private void OnShipDecloaked(string character, string text)
        {
            foreach(LocalCharacter lc in EVEManager.LocalCharacters)
            {
                if(lc.Name == character)
                {
                    bool triggerAlert = lc.DecloakWarningEnabled;

                    if(text.Contains("Mobile Observatory"))
                    {
                        triggerAlert = lc.ObservatoryDecloakWarningEnabled;
                    }

                    if(text.Contains("Stargate"))
                    {
                        triggerAlert = lc.GateDecloakWarningEnabled;
                    }

                    if(triggerAlert)
                    {
                        if(OperatingSystem.IsWindows() && OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763, 0))
                        {
                            Application.Current.Dispatcher.Invoke((Action)(() =>
                            {
                                try
                                {
                                    // Requires Microsoft.Toolkit.Uwp.Notifications NuGet package version 7.0 or greater
                                    ToastContentBuilder tb = new ToastContentBuilder();
                                    tb.AddText("HISA Alert");
                                    tb.AddText("Character : " + character + "(" + lc.Location + ")");

                                    // add the character portrait if we have one
                                    if(lc.PortraitLocation != null)
                                    {
                                        tb.AddInlineImage(lc.PortraitLocation);
                                    }

                                    tb.AddText(text);
                                    tb.AddArgument("character", character);
                                    tb.SetToastScenario(ToastScenario.Alarm);
                                    tb.SetToastDuration(ToastDuration.Long);
                                    Uri woopUri = new Uri(AppDomain.CurrentDomain.BaseDirectory + @"\Sounds\woop.mp3");
                                    tb.AddAudio(woopUri);
                                    tb.Show();
                                }
                                catch
                                {
                                    // sometimes caused by this :
                                    // https://github.com/CommunityToolkit/WindowsCommunityToolkit/issues/4858
                                }
                            }), DispatcherPriority.Normal, null);
                        }
                    }

                    break;
                }
            }
        }

        private void OnCombatEvent(string character, string text)
        {
            foreach(LocalCharacter lc in EVEManager.LocalCharacters)
            {
                if(lc.Name == character)
                {
                    if(lc.CombatWarningEnabled)
                    {
                        if(OperatingSystem.IsWindows() && OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763, 0))
                        {
                            Application.Current.Dispatcher.Invoke((Action)(() =>
                            {
                                try
                                {
                                    // Requires Microsoft.Toolkit.Uwp.Notifications NuGet package version 7.0 or greater
                                    ToastContentBuilder tb = new ToastContentBuilder();
                                    tb.AddText("HISA Alert");
                                    tb.AddText("Character : " + character + "(" + lc.Location + ")");

                                    // add the character portrait if we have one
                                    if(lc.PortraitLocation != null)
                                    {
                                        tb.AddInlineImage(lc.PortraitLocation);
                                    }

                                    tb.AddText(text);
                                    tb.AddArgument("character", character);
                                    tb.SetToastScenario(ToastScenario.Alarm);
                                    tb.SetToastDuration(ToastDuration.Long);
                                    Uri woopUri = new Uri(AppDomain.CurrentDomain.BaseDirectory + @"\Sounds\woop.mp3");
                                    tb.AddAudio(woopUri);
                                    tb.Show();
                                }
                                catch
                                {
                                    // sometimes caused by this :
                                    // https://github.com/CommunityToolkit/WindowsCommunityToolkit/issues/4858
                                }
                            }), DispatcherPriority.Normal, null);
                        }
                    }

                    break;
                }
            }
        }

        private void OnZKillsAdded()
        {
            manualZKillFilterRefreshRequired = true;
        }

        private bool TryRefreshZKillFeedView()
        {
            if(ZKBFeed == null || ZKBFeed.ItemsSource == null)
            {
                return false;
            }

            try
            {
                ICollectionView view = CollectionViewSource.GetDefaultView(ZKBFeed.ItemsSource);
                if(view == null)
                {
                    return false;
                }

                view.Refresh();
                return true;
            }
            catch(ArgumentOutOfRangeException)
            {
                // Transient list/view race; keep refresh flag set and retry next tick.
                return false;
            }
            catch(InvalidOperationException)
            {
                // View can be temporarily unavailable while source is changing.
                return false;
            }
        }

        private void RawIntelBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if(RawIntelBox.SelectedItem == null)
            {
                return;
            }

            EVEData.IntelData chat = RawIntelBox.SelectedItem as EVEData.IntelData;

            bool selectedSystem = false;

            foreach(string s in chat.IntelString.Split(' '))
            {
                if(s == "")
                {
                    continue;
                }
                var linkParser = new Regex(@"\b(?:https?://|www\.)\S+\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
                foreach(Match m in linkParser.Matches(s))
                {
                    string url = m.Value;
                    if(!Uri.IsWellFormedUriString(url, UriKind.Absolute))
                    {
                        url = "http://" + url;
                    }
                    if(Uri.IsWellFormedUriString(url, UriKind.Absolute))
                    {
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
                    }
                }
                // only select the first system
                if(!selectedSystem)
                {
                    foreach(EVEData.System sys in EVEManager.Systems)
                    {
                        if(s.IndexOf(sys.Name, StringComparison.OrdinalIgnoreCase) == 0)
                        {
                            if(RegionUC.Region.Name != sys.Region)
                            {
                                RegionUC.SelectRegion(sys.Region);
                            }

                            RegionUC.SelectSystem(s, true);
                            selectedSystem = true;
                        }
                    }
                }
            }
        }

        #endregion intel

        #region Thera / Turnur

        /// <summary>
        /// Update Thera Button Clicked
        /// </summary>
        private void btn_UpdateThera_Click(object sender, RoutedEventArgs e)
        {
            EVEManager.UpdateTheraConnections();
        }

        private void TheraConnectionsList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if(sender != null)
            {
                DataGrid grid = sender as DataGrid;
                if(grid != null && grid.SelectedItems != null && grid.SelectedItems.Count == 1)
                {
                    DataGridRow dgr = grid.ItemContainerGenerator.ContainerFromItem(grid.SelectedItem) as DataGridRow;
                    EVEData.TheraConnection tc = dgr.Item as EVEData.TheraConnection;

                    if(tc != null)
                    {
                        SelectSystemOnRegionMap(tc.System, focusRegionDocument: false);
                    }
                }
            }
        }

        private void btn_UpdateTurnur_Click(object sender, RoutedEventArgs e)
        {
            EVEManager.UpdateTurnurConnections();
        }

        private void TurnurConnectionsList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if(sender != null)
            {
                DataGrid grid = sender as DataGrid;
                if(grid != null && grid.SelectedItems != null && grid.SelectedItems.Count == 1)
                {
                    DataGridRow dgr = grid.ItemContainerGenerator.ContainerFromItem(grid.SelectedItem) as DataGridRow;
                    EVEData.TurnurConnection tc = dgr.Item as EVEData.TurnurConnection;

                    if(tc != null)
                    {
                        SelectSystemOnRegionMap(tc.System, focusRegionDocument: false);
                    }
                }
            }
        }

        #endregion Thera / Turnur

        #region Route

        private void refreshJumpRouteUI()
        {
            Application.Current.Dispatcher.Invoke((Action)(() =>
            {
                if(capitalRouteWaypointsLB.ItemsSource != null)
                {
                    CollectionViewSource.GetDefaultView(capitalRouteWaypointsLB.ItemsSource).Refresh();
                }

                if(capitalRouteAvoidLB.ItemsSource != null)
                {
                    CollectionViewSource.GetDefaultView(capitalRouteAvoidLB.ItemsSource).Refresh();
                }

                if(dgCapitalRouteCurrentRoute.ItemsSource != null)
                {
                    CollectionViewSource.GetDefaultView(dgCapitalRouteCurrentRoute.ItemsSource).Refresh();
                }

                // lbAlternateMids could be null, need to check..

                if(lbAlternateMids.ItemsSource != null)
                {
                    CollectionViewSource.GetDefaultView(lbAlternateMids.ItemsSource).Refresh();
                }
            }), DispatcherPriority.Normal, null);
        }

        private void RecalculateJumpRoute(bool updateSummary)
        {
            CapitalRoute.Recalculate();
            if(updateSummary)
            {
                lblCapitalRouteSummary.Content = JumpRouteSummaryHelper.BuildSummaryText(CapitalRoute);
            }

            refreshJumpRouteUI();
        }

        private void AddWaypointsBtn_Click(object sender, RoutedEventArgs e)
        {
            if(RegionUC.ActiveCharacter == null)
            {
                return;
            }

            if(RouteSystemDropDownAC.SelectedItem == null)
            {
                return;
            }
            EVEData.System s = RouteSystemDropDownAC.SelectedItem as EVEData.System;

            if(s != null)
            {
                RegionUC.ActiveCharacter.AddDestination(s.ID, false);
            }
        }

        private void AddJumpWaypointsBtn_Click(object sender, RoutedEventArgs e)
        {
            if(JumpRouteSystemDropDownAC.SelectedItem == null)
            {
                return;
            }
            EVEData.System s = JumpRouteSystemDropDownAC.SelectedItem as EVEData.System;

            if(s != null)
            {
                CapitalRoute.WayPoints.Add(s.Name);
                RecalculateJumpRoute(updateSummary: true);
            }
        }

        private void AddJumpAvoidSystemsBtn_Click(object sender, RoutedEventArgs e)
        {
            if(JumpRouteAvoidSystemDropDownAC.SelectedItem == null)
            {
                return;
            }
            EVEData.System s = JumpRouteAvoidSystemDropDownAC.SelectedItem as EVEData.System;

            if(s != null)
            {
                CapitalRoute.AvoidSystems.Add(s.Name);
                RecalculateJumpRoute(updateSummary: true);
            }
        }

        private void ClearWaypointsBtn_Click(object sender, RoutedEventArgs e)
        {
            EVEData.LocalCharacter c = RegionUC.ActiveCharacter as EVEData.LocalCharacter;
            if(c != null)
            {
                c.ClearAllWaypoints();
            }
        }

        private void ClearJumpWaypointsBtn_Click(object sender, RoutedEventArgs e)
        {
            CapitalRoute.WayPoints.Clear();
            CapitalRoute.CurrentRoute.Clear();
            lbAlternateMids.ItemsSource = null;
            lblAlternateMids.Content = "";

            refreshJumpRouteUI();
        }

        private void ClearJumpAvoidSystemsBtn_Click(object sender, RoutedEventArgs e)
        {
            CapitalRoute.AvoidSystems.Clear();
            refreshJumpRouteUI();
        }

        private void CopyRouteBtn_Click(object sender, RoutedEventArgs e)
        {
            EVEData.LocalCharacter c = RegionUC.ActiveCharacter as EVEData.LocalCharacter;
            if(c != null)
            {
                string WPT = c.GetWayPointText();

                try
                {
                    Clipboard.SetText(WPT);
                }
                catch { }
            }
        }

        private void ReCalculateRouteBtn_Click(object sender, RoutedEventArgs e)
        {
            EVEData.LocalCharacter c = RegionUC.ActiveCharacter as EVEData.LocalCharacter;
            if(c != null && c.Waypoints.Count > 0)
            {
                c.RecalcRoute();
            }
        }

        #endregion Route

        #region ZKillBoard

        private bool zkbFilterByRegion = true;

        private EVEData.ZKillRedisQ.ZKBDataSimple GetSelectedZkbEntry()
        {
            if(ZKBFeed.SelectedIndex == -1)
            {
                return null;
            }

            return ZKBFeed.SelectedItem as EVEData.ZKillRedisQ.ZKBDataSimple;
        }

        private void ShowZkbSystemOnMap(EVEData.ZKillRedisQ.ZKBDataSimple zkbEntry)
        {
            if(zkbEntry == null)
            {
                return;
            }

            SelectSystemOnRegionMap(zkbEntry.SystemName, focusRegionDocument: true);
        }

        private static void OpenZkbKillInBrowser(EVEData.ZKillRedisQ.ZKBDataSimple zkbEntry)
        {
            if(zkbEntry == null)
            {
                return;
            }

            string killUrl = $"https://zkillboard.com/kill/{zkbEntry.KillID}/";
            Process.Start(new ProcessStartInfo(killUrl) { UseShellExecute = true });
        }

        private void ZKBContexMenu_ShowSystem_Click(object sender, RoutedEventArgs e)
        {
            ShowZkbSystemOnMap(GetSelectedZkbEntry());
        }

        private void ZKBContexMenu_ShowZKB_Click(object sender, RoutedEventArgs e)
        {
            OpenZkbKillInBrowser(GetSelectedZkbEntry());
        }

        private void ZKBFeed_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            OpenZkbKillInBrowser(GetSelectedZkbEntry());
        }

        private void ZKBFeed_MouseDoubleClick_1(object sender, MouseButtonEventArgs e)
        {
            OpenZkbKillInBrowser(GetSelectedZkbEntry());
        }

        private bool ZKBFeedFilter(object item)
        {
            // Define the new filter logic to show only the last 50 items Predicate<object> newFilter = item => allItems.IndexOf((YourItemType)item) >= Math.Max(0, allItems.Count - 50);

            if(zkbFilterByRegion == false)
            {
                return true;
            }

            EVEData.ZKillRedisQ.ZKBDataSimple zs = item as EVEData.ZKillRedisQ.ZKBDataSimple;
            if(zs == null)
            {
                return false;
            }

            if(RegionUC.Region.IsSystemOnMap(zs.SystemName))
            {
                return true;
            }

            return false;
        }

        private void ZKBFeedFilterViewChk_Checked(object sender, RoutedEventArgs e)
        {
            zkbFilterByRegion = (bool)ZKBFeedFilterViewChk.IsChecked;

            if(ZKBFeed != null)
            {
                manualZKillFilterRefreshRequired = true;
            }
        }


        #endregion ZKillBoard

        #region Anoms

        /// <summary>
        /// The anomalies grid is clicked
        /// </summary>
        private void AnomSigList_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            // set focus to enable the grid to receive the PreviewKeyDown events
            AnomSigList.Focus();
        }

        /// <summary>
        /// Keys pressed while the anomalies grid is in focus
        /// </summary>
        private void AnomSigList_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            // handle paste operation
            if(Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.V)
            {
                updateAnomListFromClipboard();
                e.Handled = true;
            }
            // handle copy operation
            else if(Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.C)
            {
                copyAnomListToClipboard();
                e.Handled = true;
            }
            // handle delete
            else if(Keyboard.Modifiers == ModifierKeys.None && e.Key == Key.Delete)
            {
                deleteSelectedAnoms();
                e.Handled = true;
            }
        }

        /// <summary>
        /// Clear system Anoms button clicked
        /// </summary>
        private void btnClearAnomList_Click(object sender, RoutedEventArgs e)
        {
            EVEData.AnomData ad = ANOMManager.ActiveSystem;
            if(ad != null)
            {
                ad.Anoms.Clear();
                RefreshAnomListUI();
            }
        }

        /// <summary>
        /// Delete selected Anoms button clicked
        /// </summary>
        private void btnDeleteSelectedAnoms_Click(object sender, RoutedEventArgs e)
        {
            deleteSelectedAnoms();
        }

        /// <summary>
        /// Update Anoms clicked
        /// </summary>
        private void btnUpdateAnomList_Click(object sender, RoutedEventArgs e)
        {
            updateAnomListFromClipboard();
        }

        private void RefreshAnomListUI()
        {
            AnomSigList.Items.Refresh();
            AnomSigList.UpdateLayout();
            CollectionViewSource.GetDefaultView(AnomSigList.ItemsSource).Refresh();
        }

        private void updateAnomListFromClipboard()
        {
            EVEData.AnomData ad = ANOMManager.ActiveSystem;
            string pasteData = Clipboard.GetText();

            if(ad == null || string.IsNullOrEmpty(pasteData))
                return;

            HashSet<string> missingSignatures = ad.UpdateFromPaste(pasteData);

            // Clear current selection
            AnomSigList.SelectedItems.Clear();

            foreach(var item in AnomSigList.Items)
            {
                var anom = item as Anom;
                if(anom != null && missingSignatures.Contains(anom.Signature))
                    AnomSigList.SelectedItems.Add(item);
            }

            if(AnomSigList.SelectedItems.Count > 0)
                AnomSigList.ScrollIntoView(AnomSigList.SelectedItems[0]);

            RefreshAnomListUI();
        }

        private void copyAnomListToClipboard()
        {
            EVEData.AnomData ad = ANOMManager.ActiveSystem;
            if(ad == null)
                return;

            string str = AnomClipboardHelper.BuildClipboardText(
                AnomSigList.SelectedItems.Cast<object>().OfType<Anom>(),
                ad.Anoms.Values);

            Clipboard.SetText(str);
        }

        private void deleteSelectedAnoms()
        {
            EVEData.AnomData ad = ANOMManager.ActiveSystem;
            if(ad == null || AnomSigList.SelectedItems.Count == 0)
                return;

            HashSet<string> selectedSignatures = AnomClipboardHelper.GetSelectedSignatures(
                AnomSigList.SelectedItems.Cast<object>().OfType<Anom>());

            var keysToRemove = ad.Anoms
                .Where(kvp => selectedSignatures.Contains(kvp.Value.Signature))
                .Select(kvp => kvp.Key)
                .ToList();

            foreach(var key in keysToRemove)
                ad.Anoms.Remove(key);

            RefreshAnomListUI();
        }

        #endregion Anoms


        private bool sovCampaignFilterByRegion = false;

        private void SovCampaignFilterViewChk_Checked(object sender, RoutedEventArgs e)
        {
            sovCampaignFilterByRegion = (bool)SovCampaignFilterViewChk.IsChecked;

            if(SovCampaignList != null && SovCampaignList.ItemsSource != null)
            {
                CollectionViewSource.GetDefaultView(SovCampaignList.ItemsSource).Refresh();
            }
        }

        private bool SovCampaignFilter(object item)
        {
            if(!sovCampaignFilterByRegion)
                return true;

            if(item is not EVEData.SOVCampaign sc)
                return false;

            string currentRegion = RegionUC?.Region?.Name;
            if(string.IsNullOrEmpty(currentRegion))
                return true; // no region selected => don't filter

            return string.Equals(sc.Region, currentRegion, StringComparison.OrdinalIgnoreCase);
        }



        private void Characters_MenuItem_Click(object sender, RoutedEventArgs e)
        {
            CharactersWindow charactersWindow = new CharactersWindow();
            charactersWindow.characterLV.ItemsSource = EVEManager.LocalCharacters;

            charactersWindow.Owner = this;

            charactersWindow.ShowDialog();
        }

        private void LoadInfoObjects()
        {
            InfoLayer = new List<InfoItem>();

            // now add the beacons
            string infoObjectsFile = Path.Combine(EveAppConfig.StorageRoot, "InfoObjects.txt");
            if(File.Exists(infoObjectsFile))
            {
                StreamReader file = new StreamReader(infoObjectsFile);

                string line;
                while((line = file.ReadLine()) != null)
                {
                    line = line.Trim();
                    if(line.StartsWith("#"))
                    {
                        continue;
                    }

                    string[] parts = line.Split(',');

                    if(parts.Length == 0)
                    {
                        continue;
                    }

                    string region = parts[0];

                    EVEData.MapRegion mr = EVEManager.GetRegion(region);
                    if(mr == null)
                    {
                        continue;
                    }

                    if(parts[1] == "SYSLINK" || parts[1] == "SYSLINKARC")
                    {
                        if(parts.Length != 7)
                        {
                            continue;
                        }
                        // REGION SYSLINK FROM TO SOLID/DASHED size #FFFFFF
                        string from = parts[2];
                        string to = parts[3];
                        string lineStyle = parts[4];
                        string size = parts[5];
                        string colour = parts[6];

                        if(!mr.MapSystems.ContainsKey(from))
                        {
                            continue;
                        }

                        if(!mr.MapSystems.ContainsKey(to))
                        {
                            continue;
                        }

                        EVEData.MapSystem fromMS = mr.MapSystems[from];
                        EVEData.MapSystem toMS = mr.MapSystems[to];

                        InfoItem.LineType lt = InfoItem.LineType.Solid;
                        if(lineStyle == "DASHED")
                        {
                            lt = InfoItem.LineType.Dashed;
                        }

                        if(lineStyle == "LIGHTDASHED")
                        {
                            lt = InfoItem.LineType.LightDashed;
                        }

                        Color c = (Color)ColorConverter.ConvertFromString(colour);

                        int lineThickness = int.Parse(size);

                        InfoItem ii = new InfoItem();
                        ii.DrawType = InfoItem.ShapeType.Line;

                        if(parts[1] == "SYSLINKARC")
                        {
                            ii.DrawType = InfoItem.ShapeType.ArcLine;
                        }

                        ii.X1 = (int)fromMS.Layout.X;
                        ii.Y1 = (int)fromMS.Layout.Y;
                        ii.X2 = (int)toMS.Layout.X;
                        ii.Y2 = (int)toMS.Layout.Y;
                        ii.Size = lineThickness;
                        ii.Region = region;
                        ii.Fill = c;
                        ii.LineStyle = lt;
                        InfoLayer.Add(ii);
                    }

                    if(parts[1] == "SYSMARKER")
                    {
                        if(parts.Length != 5)
                        {
                            continue;
                        }
                        // REGION SYSMARKER FROM SIZE #FFFFFF
                        string from = parts[2];
                        string size = parts[3];
                        string colour = parts[4];

                        if(!mr.MapSystems.ContainsKey(from))
                        {
                            continue;
                        }

                        EVEData.MapSystem fromMS = mr.MapSystems[from];

                        Color c = (Color)ColorConverter.ConvertFromString(colour);

                        int radius = int.Parse(size);

                        InfoItem ii = new InfoItem();
                        ii.DrawType = InfoItem.ShapeType.Circle;
                        ii.X1 = (int)fromMS.Layout.X;
                        ii.Y1 = (int)fromMS.Layout.Y;
                        ii.Size = radius;
                        ii.Region = region;
                        ii.Fill = c;
                        InfoLayer.Add(ii);
                    }
                }
            }

            RegionUC.InfoLayer = InfoLayer;
        }

        private void TestMenuItem_Click(object sender, RoutedEventArgs e)
        {
            string characterName = ActiveCharacter.Name;

            LocalCharacter lc = ActiveCharacter;

            string line = "Your cloak deactivates due to a pulse from a Mobile Observatory deployed by Slazanger.";

            lc.GameLogWarningText = line;

            if(OperatingSystem.IsWindows() && OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763, 0))
            {
                ToastContentBuilder tb = new ToastContentBuilder();
                tb.AddText("HISA Alert");
                tb.AddText("Character : " + characterName + "(" + lc.Location + ")");

                // add the character portrait if we have one
                if(lc.PortraitLocation != null)
                {
                    tb.AddInlineImage(lc.PortraitLocation);
                }

                tb.AddText(line);
                tb.AddArgument("character", characterName);
                tb.SetToastScenario(ToastScenario.Alarm);
                tb.SetToastDuration(ToastDuration.Long);
                Uri woopUri = new Uri(AppDomain.CurrentDomain.BaseDirectory + @"\Sounds\woop.mp3");
                tb.AddAudio(woopUri);
                tb.Show();
            }
        }

        private void miResetLayout_Click(object sender, RoutedEventArgs e)
        {
            string defaultLayoutFile = AppDomain.CurrentDomain.BaseDirectory + @"\DefaultWindowLayout.dat";
            if(File.Exists(defaultLayoutFile))
            {
                try
                {
                    AvalonDock.Layout.Serialization.XmlLayoutSerializer ls = new AvalonDock.Layout.Serialization.XmlLayoutSerializer(dockManager);
                    using(var sr = new StreamReader(defaultLayoutFile))
                    {
                        ls.Deserialize(sr);
                    }
                }
                catch
                {
                }
            }

            // Due to bugs in the Dock manager patch up the content id's for the 2 main views
            RegionLayoutDoc = FindDocWithContentID(dockManager.Layout, "MapRegionContentID");
            UniverseLayoutDoc = FindDocWithContentID(dockManager.Layout, "FullUniverseViewID");

            dockManager.UpdateLayout();
        }

        private void JumpPlannerShipType_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if(CapitalRoute != null)
            {
                ComboBox cb = sender as ComboBox;
                ComboBoxItem cbi = cb.SelectedItem as ComboBoxItem;
                CapitalRoute.MaxLY = double.Parse(cbi.DataContext as string);
                RecalculateJumpRoute(updateSummary: true);
            }
        }

        private void JumpPlannerJDC_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if(CapitalRoute != null)
            {
                ComboBox cb = sender as ComboBox;
                ComboBoxItem cbi = cb.SelectedItem as ComboBoxItem;
                CapitalRoute.JDC = int.Parse(cbi.DataContext as string);
                RecalculateJumpRoute(updateSummary: true);
            }
        }

        private void CurrentCapitalRouteItem_Selected(object sender, RoutedEventArgs e)
        {
            if(dgCapitalRouteCurrentRoute.SelectedItem != null)
            {
                Navigation.RoutePoint rp = dgCapitalRouteCurrentRoute.SelectedItem as Navigation.RoutePoint;
                string sel = rp.SystemName;

                UniverseUC.ShowSystem(sel);

                lblAlternateMids.Content = sel;
                if(CapitalRoute.AlternateMids.ContainsKey(sel))
                {
                    lbAlternateMids.ItemsSource = CapitalRoute.AlternateMids[sel];
                }
                else
                {
                    lbAlternateMids.ItemsSource = null;
                }
            }
        }

        private void CapitalWaypointsContextMenuMoveUp_Click(object sender, RoutedEventArgs e)
        {
            if(capitalRouteWaypointsLB.SelectedItem != null && capitalRouteWaypointsLB.SelectedIndex != 0)
            {
                string sys = CapitalRoute.WayPoints[capitalRouteWaypointsLB.SelectedIndex];
                CapitalRoute.WayPoints.RemoveAt(capitalRouteWaypointsLB.SelectedIndex);
                CapitalRoute.WayPoints.Insert(capitalRouteWaypointsLB.SelectedIndex - 1, sys);

                RecalculateJumpRoute(updateSummary: false);
            }
        }

        private void CapitalWaypointsContextMenuMoveDown_Click(object sender, RoutedEventArgs e)
        {
            if(capitalRouteWaypointsLB.SelectedItem != null && capitalRouteWaypointsLB.SelectedIndex != CapitalRoute.WayPoints.Count - 1)
            {
                string sys = CapitalRoute.WayPoints[capitalRouteWaypointsLB.SelectedIndex];
                CapitalRoute.WayPoints.RemoveAt(capitalRouteWaypointsLB.SelectedIndex);
                CapitalRoute.WayPoints.Insert(capitalRouteWaypointsLB.SelectedIndex + 1, sys);

                RecalculateJumpRoute(updateSummary: false);
            }
        }

        private void CapitalWaypointsContextMenuDelete_Click(object sender, RoutedEventArgs e)
        {
            if(capitalRouteWaypointsLB.SelectedItem != null)
            {
                CapitalRoute.WayPoints.RemoveAt(capitalRouteWaypointsLB.SelectedIndex);
                RecalculateJumpRoute(updateSummary: false);
            }
        }

        private void CapitalRouteContextMenuUseAlt_Click(object sender, RoutedEventArgs e)
        {
            if(lbAlternateMids.SelectedItem != null)
            {
                string selectedAlt = lbAlternateMids.SelectedItem as string;

                // need to find where to insert the new waypoint
                int waypointIndex = -1;
                foreach(Navigation.RoutePoint rp in CapitalRoute.CurrentRoute)
                {
                    if(rp.SystemName == CapitalRoute.WayPoints[waypointIndex + 1])
                    {
                        waypointIndex++;
                    }
                    if(CapitalRoute.AlternateMids.ContainsKey(rp.SystemName))
                    {
                        foreach(string alt in CapitalRoute.AlternateMids[rp.SystemName])
                        {
                            if(alt == selectedAlt)
                            {
                                CapitalRoute.WayPoints.Insert(waypointIndex + 1, selectedAlt);
                                break;
                            }
                        }
                    }
                }

                RecalculateJumpRoute(updateSummary: false);
            }
        }

        private void btnUnseenFits_Click(object sender, RoutedEventArgs e)
        {
            string KillURL = "https://zkillboard.com/character/93280351/losses/";
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(KillURL) { UseShellExecute = true });
        }

        private void OverlayWindow_MenuItem_Click(object sender, RoutedEventArgs e)
        {
            if(!OverlayWindowManager.CanOpenOverlay(MapConf, activeCharacter, overlayWindows))
            {
                return;
            }

            OverlayWindowManager.EnsureToggleHotkeyRegistered(OverlayWindows_ToggleClicktrough_HotkeyTrigger);

            Overlay newOverlayWindow = new Overlay(this);
            newOverlayWindow.Closing += OnOverlayWindowClosing;
            newOverlayWindow.Show();
            overlayWindows.Add(newOverlayWindow);
        }

        private void OverlayClickTroughToggle_MenuItem_Click(object sender, RoutedEventArgs e)
        {
            OverlayWindow_ToggleClickTrough();
        }

        private void OverlayWindows_ToggleClicktrough_HotkeyTrigger(object sender, HotkeyEventArgs eventArgs)
        {
            OverlayWindow_ToggleClickTrough();
        }

        public void OverlayWindow_ToggleClickTrough()
        {
            overlayWindowsAreClickTrough = OverlayWindowManager.ToggleClickThrough(overlayWindows, overlayWindowsAreClickTrough);
        }

        public void OnOverlayWindowClosing(object sender, CancelEventArgs e)
        {
            OverlayWindowManager.HandleOverlayClosing(overlayWindows, sender as Overlay);
        }

        private void MetaliminalStormList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if(MetaliminalStormList.SelectedIndex != -1)
            {
                EVEData.Storm selectedStorm = MetaliminalStormList.SelectedItem as EVEData.Storm;
                if(selectedStorm != null)
                {
                    SelectSystemOnRegionMap(selectedStorm.System, focusRegionDocument: true);
                }
            }
        }
    }

    /// <summary>
    /// TimeSpanConverter Sec statuc colour converter
    /// </summary>
    public class TimeSpanConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            TimeSpan ts = (TimeSpan)value;

            string Output = "";

            if(ts.Ticks < 0)
            {
                Output += "-";
            }

            if(ts.Days != 0)
            {
                Output += Math.Abs(ts.Days) + "d ";
            }

            if(ts.Hours != 0)
            {
                Output += Math.Abs(ts.Hours) + "h ";
            }

            Output += Math.Abs(ts.Minutes) + "m ";

            return Output;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return null;
        }
    }

    /// <summary>
    /// ZKillboard Sec statuc colour converter
    /// </summary>
    public class ZKBBackgroundConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            EVEData.ZKillRedisQ.ZKBDataSimple zs = value as EVEData.ZKillRedisQ.ZKBDataSimple;
            Color rowCol = (Color)ColorConverter.ConvertFromString("#FF333333");
            if(zs != null)
            {
                float Standing = 0.0f;

                EVEData.LocalCharacter c = MainWindow.AppWindow.RegionUC.ActiveCharacter;
                if(c != null && c.ESILinked)
                {
                    if(c.AllianceID != 0 && c.AllianceID == zs.VictimAllianceID)
                    {
                        Standing = 10.0f;
                    }

                    if(c.Standings.Keys.Contains(zs.VictimAllianceID))
                    {
                        Standing = c.Standings[zs.VictimAllianceID];
                    }

                    if(Standing == -10.0)
                    {
                        rowCol = Colors.Red;
                    }

                    if(Standing == -5.0)
                    {
                        rowCol = Colors.Orange;
                    }

                    if(Standing == 5.0)
                    {
                        rowCol = Colors.LightBlue;
                    }

                    if(Standing == 10.0)
                    {
                        rowCol = Colors.Blue;
                    }
                }

                // Do the conversion from bool to visibility
            }

            return new SolidColorBrush(rowCol);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return null;
        }
    }

    /// <summary>
    /// ZKillboard Sec statuc colour converter
    /// </summary>
    public class ZKBForegroundConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            EVEData.ZKillRedisQ.ZKBDataSimple zs = value as EVEData.ZKillRedisQ.ZKBDataSimple;
            Color rowCol = Colors.White;
            if(zs != null)
            {
                float Standing = 0.0f;

                EVEData.LocalCharacter c = MainWindow.AppWindow.RegionUC.ActiveCharacter;
                if(c != null && c.ESILinked)
                {
                    if(c.AllianceID != 0 && c.AllianceID == zs.VictimAllianceID)
                    {
                        Standing = 10.0f;
                    }

                    if(c.Standings.Keys.Contains(zs.VictimAllianceID))
                    {
                        Standing = c.Standings[zs.VictimAllianceID];
                    }

                    if(Standing == -10.0)
                    {
                        rowCol = Colors.Black;
                    }

                    if(Standing == -5.0)
                    {
                        rowCol = Colors.Black;
                    }

                    if(Standing == 5.0)
                    {
                        rowCol = Colors.Black;
                    }

                    if(Standing == 10.0)
                    {
                        rowCol = Colors.White;
                    }
                }

                // Do the conversion from bool to visibility
            }

            return new SolidColorBrush(rowCol);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return null;
        }
    }

    /// <summary>
    /// Convert time elapsed to simple human format
    /// </summary>
    public class TimeSinceConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if(value is not DateTime timeFound)
                return "Unknown";

            var now = DateTime.Now;
            var elapsed = now - timeFound;

            if(elapsed.TotalDays >= 1.0 && elapsed.TotalDays < 2.0)
                return $"{(int)elapsed.TotalDays} day";
            else if(elapsed.TotalDays >= 2.0)
                return $"{(int)elapsed.TotalDays} days";

            if(elapsed.TotalHours >= 1.0 && elapsed.TotalHours < 2.0)
                return $"{(int)elapsed.TotalHours} hour";
            else if(elapsed.TotalHours >= 2.0)
                return $"{(int)elapsed.TotalHours} hours";

            if(elapsed.TotalMinutes < 1.0)
                return "Now";
            else if(elapsed.TotalMinutes < 2.0)
                return $"{(int)elapsed.TotalMinutes} minute";
            else
                return $"{(int)elapsed.TotalMinutes} minutes";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    ///  Window Flashing helper
    /// </summary>
    public static class FlashWindow
    {
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
        private static extern bool FlashWindowEx(ref FLASHWINFO pwfi);

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct FLASHWINFO
        {
            /// <summary>
            /// The size of the structure in bytes.
            /// </summary>
            public uint cbSize;

            /// <summary>
            /// A Handle to the Window to be Flashed. The window can be either opened or minimized.
            /// </summary>
            public IntPtr hwnd;

            /// <summary>
            /// The Flash Status.
            /// </summary>
            public uint dwFlags;

            /// <summary>
            /// The number of times to Flash the window.
            /// </summary>
            public uint uCount;

            /// <summary>
            /// The rate at which the Window is to be flashed, in milliseconds. If Zero, the function uses the default cursor blink rate.
            /// </summary>
            public uint dwTimeout;
        }

        /// <summary>
        /// Flash both the window caption and taskbar button.
        /// This is equivalent to setting the FLASHW_CAPTION | FLASHW_TRAY flags.
        /// </summary>
        public const uint FLASHW_ALL = 3;

        private static FLASHWINFO Create_FLASHWINFO(IntPtr handle, uint flags, uint count, uint timeout)
        {
            FLASHWINFO fi = new FLASHWINFO();
            fi.cbSize = Convert.ToUInt32(System.Runtime.InteropServices.Marshal.SizeOf(fi));
            fi.hwnd = handle;
            fi.dwFlags = flags;
            fi.uCount = count;
            fi.dwTimeout = timeout;
            return fi;
        }

        /// <summary>
        /// Flash the specified Window for the specified number of times
        /// </summary>
        /// <param name="window">The Window to Flash.</param>
        /// <param name="count">The number of times to Flash.</param>
        /// <returns></returns>
        public static bool Flash(System.Windows.Window window, uint count)
        {
            if(Win2000OrLater)
            {
                FLASHWINFO fi = Create_FLASHWINFO(new System.Windows.Interop.WindowInteropHelper(window).Handle, FLASHW_ALL, count, 0);
                return FlashWindowEx(ref fi);
            }
            return false;
        }

        /// <summary>
        /// A boolean value indicating whether the application is running on Windows 2000 or later.
        /// </summary>
        private static bool Win2000OrLater
        {
            get { return System.Environment.OSVersion.Version.Major >= 5; }
        }
    }
}


