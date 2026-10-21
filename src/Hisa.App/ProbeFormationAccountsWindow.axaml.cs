using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Hisa.Core.Models;

namespace Hisa.App;

public partial class ProbeFormationAccountsWindow : Window
{
    private ProbeFormationAccountsViewModel? Vm=>DataContext as ProbeFormationAccountsViewModel;
    private readonly DispatcherTimer clientTimer=new(){Interval=TimeSpan.FromSeconds(3)};
    private bool ownerWasEnabled=true;
    public ProbeFormationAccountsWindow(){InitializeComponent();}
    public ProbeFormationAccountsWindow(ProbeFormationDesignerViewModel source):this(source.CreateAccountsViewModel()){}
    public ProbeFormationAccountsWindow(ProbeFormationAccountsViewModel vm):this()
    {
        DataContext=vm;vm.PropertyChanged+=OnViewModelChanged;
        Opened+=async(_,_)=>{ownerWasEnabled=Owner?.IsEnabled??true;clientTimer.Start();await vm.RunAsync(vm.InitializeAsync);};
        clientTimer.Tick+=(_,_)=>vm.RefreshClientStatus();
        Closing+=(_,e)=>{if(vm.IsWriting){e.Cancel=true;vm.Status="Wait for the current settings write to finish.";}};
        Closed+=(_,_)=>{clientTimer.Stop();if(Owner is { } owner)owner.IsEnabled=ownerWasEnabled;vm.PropertyChanged-=OnViewModelChanged;vm.Dispose();};
    }
    private void OnViewModelChanged(object? sender,PropertyChangedEventArgs e)
    {
        if(e.PropertyName==nameof(ProbeFormationAccountsViewModel.IsWriting)&&Owner is { } owner)owner.IsEnabled=Vm?.IsWriting==true?false:ownerWasEnabled;
    }
    private async void OnPrimaryAction(object? sender,RoutedEventArgs e){if(Vm is not { } vm)return;if(vm.IsResults){Close();return;}await vm.RunAsync(vm.NextAsync);}
    private void OnBack(object? sender,RoutedEventArgs e)=>Vm?.Back();
    private void OnSelectVisible(object? sender,RoutedEventArgs e)=>Vm?.SelectVisible();
    private void OnClearTargets(object? sender,RoutedEventArgs e)=>Vm?.ClearTargets();
    private void OnResetFilters(object? sender,RoutedEventArgs e)=>Vm?.ResetFilters();
    private async void OnRefresh(object? sender,RoutedEventArgs e){if(Vm is { } vm)await vm.RunAsync(vm.DiscoverAsync);}
    private async void OnTargetDetails(object? sender,RoutedEventArgs e){if(Vm is { } vm&&sender is Button{Tag:TransferTargetRow row})await vm.RunAsync(()=>vm.SelectTargetAsync(row));}
    private async void OnSaveAlias(object? sender,RoutedEventArgs e){if(Vm is { } vm)await vm.RunAsync(vm.SaveAliasAsync);}
    private void OnCopyFormation(object? sender,RoutedEventArgs e){if(Vm is { } vm&&sender is Button{Tag:EveStoredFormation row})vm.Run(()=>vm.CopyFormation(row));}
    private async void OnRetryFailed(object? sender,RoutedEventArgs e){if(Vm is { } vm)await vm.RunAsync(vm.RetryFailedAsync);}
    private void OnBackToAccounts(object? sender,RoutedEventArgs e)=>Vm?.BackToAccounts();
    private async void OnCharacterVisible(object? sender,VisualTreeAttachmentEventArgs e){if(Vm is { } vm&&sender is Control{DataContext:TransferCharacterRow row})await vm.LoadPortraitAsync(row);}
    private async void OnLinkCharacters(object? sender,RoutedEventArgs e)
    {
        if(Vm is not { } vm)return;
        try{vm.OpenLinkPicker();await new ProbeCharacterLinksWindow(vm).ShowDialog(this);}catch(Exception ex){vm.Status=ex.Message;}
    }
    private async void OnLocations(object? sender,RoutedEventArgs e)
    {
        if(Vm is not { } vm)return;
        var roots=new ListBox{ItemsSource=vm.Locations,MinHeight=100,MaxHeight=220};
        var panel=new StackPanel{Spacing=10,Children={new TextBlock{Text="Standard EVE and Steam locations are discovered automatically. Add custom settings folders, Wine prefixes, or an account file.",TextWrapping=Avalonia.Media.TextWrapping.Wrap},roots}};
        var actions=new WrapPanel();var dialog=CreateDialog("EVE settings locations",panel,650);
        AddButton(actions,"Add folder",async()=>await vm.RunAsync(async()=>{var folders=await dialog.StorageProvider.OpenFolderPickerAsync(new(){Title="Select EVE root, profile, or Wine prefix"});if(folders.FirstOrDefault()?.TryGetLocalPath() is { } path)await vm.AddLocationAsync(path);roots.ItemsSource=vm.Locations;}));
        AddButton(actions,"Add account file",async()=>await vm.RunAsync(async()=>{var files=await dialog.StorageProvider.OpenFilePickerAsync(new(){Title="Select core_user account file",FileTypeFilter=[new("EVE settings"){Patterns=["core_user_*.dat"]}]});if(files.FirstOrDefault()?.TryGetLocalPath() is { } path)await vm.AddLocationAsync(path);roots.ItemsSource=vm.Locations;}));
        AddButton(actions,"Remove selected",async()=>await vm.RunAsync(async()=>{if(roots.SelectedItem is string path)await vm.RemoveLocationAsync(path);roots.ItemsSource=vm.Locations;}));
        AddButton(actions,"Done",()=>{dialog.Close();return Task.CompletedTask;});panel.Children.Add(actions);await dialog.ShowDialog(this);
    }
    private async void OnBackups(object? sender,RoutedEventArgs e)
    {
        if(Vm is not { } vm)return;await vm.RunAsync(vm.LoadBackupsAsync);
        var list=new ListBox{ItemsSource=vm.Backups,MinHeight=160,MaxHeight=300,ItemTemplate=new FuncDataTemplate<EveSettingsBackup>((backup,_)=>new StackPanel{Children={new TextBlock{Text=backup?.CreatedUtc.ToLocalTime().ToString("g")},new TextBlock{Text=backup?.Path,FontSize=11,TextWrapping=Avalonia.Media.TextWrapping.Wrap}}})};
        var ack=new CheckBox{Content="I confirm all EVE clients are closed",IsChecked=vm.Acknowledged};
        var panel=new StackPanel{Spacing=10,Children={new TextBlock{Text=vm.TargetPath,TextWrapping=Avalonia.Media.TextWrapping.Wrap},new TextBlock{Text="Restoring replaces the complete settings file and backs up its current contents first.",TextWrapping=Avalonia.Media.TextWrapping.Wrap},list,ack}};
        var dialog=CreateDialog("Account settings backups",panel,720);var actions=new WrapPanel();
        AddButton(actions,"Restore selected…",async()=>{
            if(list.SelectedItem is not EveSettingsBackup backup)return;
            var confirmation=new StackPanel{Spacing=12,Children={new TextBlock{Text=$"Restore the complete settings file from {backup.CreatedUtc.ToLocalTime():g}?",TextWrapping=Avalonia.Media.TextWrapping.Wrap}}};
            var ask=CreateDialog("Restore settings",confirmation,520);var buttons=new WrapPanel();AddButton(buttons,"Restore",()=>{ask.Close(true);return Task.CompletedTask;});AddButton(buttons,"Cancel",()=>{ask.Close(false);return Task.CompletedTask;});confirmation.Children.Add(buttons);
            if(await ask.ShowDialog<bool>(dialog)){vm.Acknowledged=ack.IsChecked==true;await vm.RunAsync(()=>vm.RestoreAsync(backup));}
        });
        AddButton(actions,"Close",()=>{dialog.Close();return Task.CompletedTask;});panel.Children.Add(actions);await dialog.ShowDialog(this);
    }
    private Window CreateDialog(string title,StackPanel content,double width)
    {
        content.Margin=new Thickness(18);var dialog=new Window{Title=title,Width=width,MaxHeight=650,SizeToContent=SizeToContent.Height,Content=new ScrollViewer{Content=content},WindowStartupLocation=WindowStartupLocation.CenterOwner,Background=Avalonia.Media.Brush.Parse("#141F2F")};
        dialog.Closing+=(_,e)=>{if(Vm?.IsWriting==true)e.Cancel=true;};return dialog;
    }
    private static void AddButton(Panel panel,string title,Func<Task> action)
    {
        var button=new Button{Content=title,Margin=new Thickness(0,0,6,4),Padding=new Thickness(10,6)};
        button.Click+=async(_,_)=>{button.IsEnabled=false;try{await action();}finally{button.IsEnabled=true;}};panel.Children.Add(button);
    }
}
