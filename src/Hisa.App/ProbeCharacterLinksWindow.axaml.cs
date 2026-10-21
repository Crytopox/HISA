using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Hisa.App;

public partial class ProbeCharacterLinksWindow:Window
{
    private ProbeFormationAccountsViewModel Vm=>(ProbeFormationAccountsViewModel)DataContext!;
    public ProbeCharacterLinksWindow(){InitializeComponent();}
    public ProbeCharacterLinksWindow(ProbeFormationAccountsViewModel vm):this(){DataContext=vm;}
    private async void OnCharacterVisible(object? sender,VisualTreeAttachmentEventArgs e){if(sender is Control{DataContext:TransferCharacterRow row})await Vm.LoadPortraitAsync(row);}
    private async Task RunLinkActionAsync(Func<Task> action)=>await Vm.RunAsync(async()=>{Vm.LinkStatus="";try{await action();}catch(Exception ex){Vm.LinkStatus=ex.Message;}});
    private async void OnAdd(object? sender,RoutedEventArgs e)=>await RunLinkActionAsync(async()=>{await Vm.AddManualCharacterAsync();Vm.LinkStatus="Character added. Save local links to keep it.";});
    private async void OnSave(object? sender,RoutedEventArgs e){var saved=false;await RunLinkActionAsync(async()=>{await Vm.SaveLinksAsync();saved=true;});if(saved)Close();}
    private void OnCancel(object? sender,RoutedEventArgs e)=>Close();
}
