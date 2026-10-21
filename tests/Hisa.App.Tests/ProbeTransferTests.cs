using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using Hisa.App;
using Hisa.Core.Abstractions;
using Hisa.Core.Models;
using Hisa.Services.Probes;

namespace Hisa.App.Tests;

public sealed class ProbeTransferTests
{
    [AvaloniaTheory]
    [InlineData(1200,800)] [InlineData(1000,620)]
    public async Task CompactAccountDetailsAndCharacterPickerPreserveExplicitTargetSelection(int width,int height)
    {
        var fixture=await Fixture.Create();using var vm=fixture.Vm;await vm.InitializeAsync();await vm.NextAsync();
        var window=new ProbeFormationAccountsWindow{DataContext=vm,Width=width,Height=height};window.Show();
        try {
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Assert.False(window.FindControl<Expander>("AccountFilters")!.IsExpanded);
            var target=vm.AllTargets.First();
            var inspect=window.GetVisualDescendants().OfType<Button>().First(b=>ReferenceEquals(b.Tag,target));
            inspect.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Same(target,vm.SelectedTarget);Assert.All(vm.AllTargets,t=>Assert.False(t.Selected));
            Assert.Equal("0 files selected",vm.DestinationSummary);
            var tabs=window.FindControl<TabControl>("AccountDetailTabs")!;
            Assert.Equal(new[]{"Characters","Formations","File"},tabs.Items.OfType<TabItem>().Select(t=>t.Header!.ToString()));
            tabs.SelectedIndex=2;Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Assert.Contains(window.GetVisualDescendants().OfType<SelectableTextBlock>(),t=>t.Text==target.Path);
            vm.ServerFilter="tranquility";vm.ProfileFilter="Default";Assert.True(vm.HasActiveFilters);Assert.Single(vm.VisibleGroups);
            vm.ResetFilters();Assert.False(vm.HasActiveFilters);Assert.Equal(2,vm.VisibleGroups.Count);
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();window.UpdateLayout();
            var check=window.GetVisualDescendants().OfType<CheckBox>().First(c=>ReferenceEquals(c.DataContext,target));
            check.IsChecked=true;Assert.Equal("1 file selected",vm.DestinationSummary);Assert.Single(vm.AllTargets,t=>t.Selected);
            tabs.SelectedIndex=0;vm.OpenLinkPicker();
            var picker=new ProbeCharacterLinksWindow(vm){Width=width==1000?520:700,Height=height==620?420:650};picker.Show(window);
            try {
                vm.LinkCandidates[0].Identity=new("tranquility",vm.LinkCandidates[0].CharacterId,"A long character name that should wrap cleanly in the picker","Offline cached name");
                vm.LinkCandidates[0].Linked=true;
                Render(picker,$"probe-character-picker-{width}.png");
                vm.CharacterSearch="no such character";Assert.False(vm.HasMatchingLinkCandidates);Render(picker,$"probe-character-picker-empty-{width}.png");
                vm.CharacterSearch="";Assert.True(vm.HasMatchingLinkCandidates);
                Assert.Single(vm.AllTargets,t=>t.Selected);Assert.Equal(0,fixture.Eve.ApplyCalls);
            }finally{picker.Close();}
            Assert.False(vm.HasLinkedCharacters); // Closing the picker did not persist its draft.
            Render(window,$"probe-transfer-details-{width}.png");
        }finally{window.Close();}
        static void Render(Window view,string name){Avalonia.Threading.Dispatcher.UIThread.RunJobs();using var image=new RenderTargetBitmap(new((int)view.Bounds.Width,(int)view.Bounds.Height));image.Render(view);image.Save(Path.Combine(AppContext.BaseDirectory,name));}
    }
    [Fact]
    public async Task IdentityLinksAndAliasesNeverSelectOrRedirectWriteTargets()
    {
        var fixture=await Fixture.Create();using var vm=fixture.Vm;await vm.InitializeAsync();Assert.True(vm.IsFormations);Assert.Single(vm.Formations,f=>f.Selected);Assert.All(vm.AllTargets,t=>Assert.False(t.Selected));
        await vm.NextAsync();var target=vm.AllTargets.First();await vm.SelectTargetAsync(target);vm.Alias="My main account";await vm.SaveAliasAsync();
        vm.OpenLinkPicker();vm.ManualCharacterId="90000002";await vm.AddManualCharacterAsync();await vm.SaveLinksAsync();
        Assert.Single(vm.Groups.First().Characters);Assert.All(vm.AllTargets,t=>Assert.False(t.Selected));Assert.Equal(0,fixture.Eve.ApplyCalls);
        vm.Search="Pilot 90000002";vm.SelectVisible();Assert.Equal(2,vm.AllTargets.Count(t=>t.Selected));Assert.All(vm.AllTargets.Where(t=>t.Selected),t=>Assert.Equal("tq_tranquility",t.Account.Installation));
        vm.ClearTargets();await vm.DiscoverAsync();Assert.All(vm.AllTargets,t=>Assert.False(t.Selected));Assert.Equal("My main account",vm.AllTargets.First().Alias);
        Assert.Single((await fixture.Identity.LoadLinksAsync()));Assert.Equal(0,fixture.Eve.ApplyCalls);
    }
    [Fact]
    public async Task InlineConflictsBackNavigationAndUnchangedResultsFollowTheCapturedReview()
    {
        var fixture=await Fixture.Create();using var vm=fixture.Vm;await vm.InitializeAsync();await vm.NextAsync();
        foreach(var target in vm.Groups.First(g=>g.Group.Server=="tranquility").Targets)target.Selected=true;
        var first=vm.AllTargets.First().Path;fixture.Eve.Stored[first]=[new(7,fixture.Source.Current! with{Probes=[new(default,10)]})];
        await vm.NextAsync();Assert.True(vm.IsReview);Assert.False(vm.CanApply);
        var conflict=vm.Reviews.First(r=>r.Target.Path==first).Formations[0];conflict.ActionIndex=2;Assert.True(vm.CanApply);Assert.Equal(7,vm.Reviews.First().Preview!.Entries[0].TargetId);
        vm.Back();Assert.True(vm.IsAccounts);await vm.NextAsync();conflict=vm.Reviews.First().Formations[0];Assert.Equal(2,conflict.ActionIndex);
        conflict.ActionIndex=3;conflict.NewName=conflict.Name;Assert.False(vm.CanApply);conflict.NewName="A separate copy";Assert.True(vm.CanApply);
        conflict.ActionIndex=1;await vm.ApplyAsync();Assert.True(vm.IsResults);Assert.Equal("Unchanged",vm.Results.First().State);Assert.Equal("Updated",vm.Results.Last().State);
    }
    [Fact]
    public async Task SourceEditsAndPendingDraftsInvalidateReviewAndRetryReadsFreshFiles()
    {
        var fixture=await Fixture.Create();using var vm=fixture.Vm;await vm.InitializeAsync();await vm.NextAsync();var target=vm.AllTargets.First();target.Selected=true;await vm.NextAsync();Assert.True(vm.CanApply);
        fixture.Source.TransformSelected(f=>ProbeFormationMath.Translate(f,new(1,2,3)));Assert.False(vm.CanApply);await Assert.ThrowsAsync<ArgumentException>(vm.ApplyAsync);
        fixture.Source.ProbeRows[0].X.Text="invalid";await Assert.ThrowsAsync<ArgumentException>(vm.BuildReviewAsync);fixture.Source.ProbeRows[0].X.Cancel();
        await vm.BuildReviewAsync();fixture.Eve.Version++;await vm.ApplyAsync();Assert.True(vm.HasFailed);
        var before=fixture.Eve.ReadCalls;await vm.RetryFailedAsync();Assert.True(fixture.Eve.ReadCalls>before);Assert.True(vm.CanApply);await vm.ApplyAsync();Assert.False(vm.HasFailed);
    }
    [Fact]
    public async Task PartialFailureRetryIncludesOnlyFailedTargetsAndDoesNotReuseOldPreviews()
    {
        var fixture=await Fixture.Create();using var vm=fixture.Vm;await vm.InitializeAsync();await vm.NextAsync();var targets=vm.AllTargets.Take(2).ToArray();foreach(var target in targets)target.Selected=true;fixture.Eve.FailPaths.Add(targets[1].Path);
        await vm.NextAsync();await vm.ApplyAsync();Assert.Equal(2,vm.Results.Count);Assert.Single(vm.Results,r=>r.Failed);Assert.Contains("Backup",vm.Results[0].BackupPath);
        fixture.Eve.FailPaths.Clear();var reads=fixture.Eve.ReadCalls;await vm.RetryFailedAsync();Assert.Single(vm.Reviews);Assert.Equal(targets[1].Path,vm.Reviews[0].Target.Path);Assert.True(fixture.Eve.ReadCalls>reads);
        await vm.ApplyAsync();Assert.Equal("Updated",Assert.Single(vm.Results).State);
    }
    [Fact]
    public async Task NameResolutionDoesNotBlockNavigationAndClosingCancelsIt()
    {
        var fixture=await Fixture.Create();fixture.Resolver.WaitForCancellation=true;using var vm=fixture.Vm;await vm.InitializeAsync();await vm.NextAsync();
        Assert.True(vm.IsResolving);Assert.True(vm.IsIdle);Assert.True(fixture.Source.IsIdle);Assert.All(vm.AllTargets,t=>Assert.False(t.Selected));vm.Dispose();
        await fixture.Resolver.Canceled.Task.WaitAsync(TimeSpan.FromSeconds(2));
    }
    [Fact]
    public async Task ADisappearingReplacementTargetRequiresANewExplicitChoice()
    {
        var fixture=await Fixture.Create();using var vm=fixture.Vm;await vm.InitializeAsync();await vm.NextAsync();var target=vm.AllTargets.First();target.Selected=true;
        var formation=fixture.Source.Current!;fixture.Eve.Stored[target.Path]=[new(7,formation with{Probes=[new(default,10)]})];
        await vm.NextAsync();vm.Reviews[0].Formations[0].ActionIndex=2;Assert.True(vm.CanApply);
        fixture.Eve.Stored[target.Path]=[new(9,formation with{Probes=[new(default,20)]})];await vm.BuildReviewAsync();
        Assert.Equal(0,vm.Reviews[0].Formations[0].ActionIndex);Assert.False(vm.CanApply);
    }
    [Fact]
    public async Task WriteLocksTargetsAndSourceChangesStopRemainingFiles()
    {
        var fixture=await Fixture.Create();using var vm=fixture.Vm;await vm.InitializeAsync();await vm.NextAsync();vm.SelectVisible();await vm.NextAsync();
        var targets=vm.AllTargets.ToArray();fixture.Eve.BeforeApply=()=>{Assert.True(vm.IsWriting);targets[0].Selected=false;Assert.True(targets[0].Selected);fixture.Source.TransformSelected(f=>ProbeFormationMath.Translate(f,new(1,0,0)));};
        await vm.ApplyAsync();Assert.Equal(1,fixture.Eve.ApplyCalls);Assert.Equal(2,vm.Results.Count(r=>r.Failed));
    }
    [Fact]
    public async Task ClientStatusAndAcknowledgmentControlApplyAvailability()
    {
        var fixture=await Fixture.Create();using var vm=fixture.Vm;await vm.InitializeAsync();await vm.NextAsync();vm.AllTargets.First().Selected=true;
        fixture.Eve.Status=EveClientStatus.Unknown;await vm.NextAsync();Assert.False(vm.CanApply);vm.Acknowledged=true;Assert.True(vm.CanApply);
        fixture.Eve.Status=EveClientStatus.Running;vm.RefreshClientStatus();Assert.False(vm.CanApply);await Assert.ThrowsAsync<ArgumentException>(vm.ApplyAsync);Assert.Equal(0,fixture.Eve.ApplyCalls);
    }
    [AvaloniaFact]
    public async Task EmptyDiscoveryAndLongConflictsRemainUsable()
    {
        var fixture=await Fixture.Create();fixture.Source.FormationName="A deliberately long formation name for exploring account transfer conflicts and reviewing exactly which settings file will receive it";fixture.Source.Rename();
        using var vm=fixture.Vm;await vm.InitializeAsync();var window=new ProbeFormationAccountsWindow{DataContext=vm,Width=1000,Height=620};window.Show();
        try{
            Render("long-formation");await vm.NextAsync();var first=vm.AllTargets.First();first.Selected=true;fixture.Eve.Stored[first.Path]=[new(1,fixture.Source.Current!),new(2,fixture.Source.Current!)];
            await vm.NextAsync();Assert.False(vm.CanApply);Render("conflicts");
            vm.Reviews[0].Formations[0].ActionIndex=3;vm.Reviews[0].Formations[0].NewName="Renamed copy";Render("renamed-copy");
            fixture.Eve.Empty=true;await vm.DiscoverAsync();vm.BackToAccounts();Assert.True(vm.HasNoVisibleGroups);Render("empty-accounts");
            void Render(string name){Avalonia.Threading.Dispatcher.UIThread.RunJobs();using var image=new RenderTargetBitmap(new(1000,620));image.Render(window);image.Save(Path.Combine(AppContext.BaseDirectory,$"probe-transfer-{name}.png"));}
        }finally{window.Close();}
    }
    [AvaloniaTheory]
    [InlineData(1200,800)]
    [InlineData(1000,620)]
    public async Task GuidedScreensRenderAtDefaultAndMinimumSizes(int width,int height)
    {
        var fixture=await Fixture.Create();using var vm=fixture.Vm;await vm.InitializeAsync();
        var window=new ProbeFormationAccountsWindow{DataContext=vm,Width=width,Height=height};window.Show();
        try{
            Render("formations");await vm.NextAsync();await vm.SelectTargetAsync(vm.AllTargets.First());vm.OpenLinkPicker();vm.LinkCandidates[0].Linked=true;await vm.SaveLinksAsync();Render("accounts");
            vm.AllTargets.First().Selected=true;await vm.NextAsync();Render("review");await vm.ApplyAsync();Render("results");
            void Render(string step){Avalonia.Threading.Dispatcher.UIThread.RunJobs();using var image=new RenderTargetBitmap(new((int)window.Bounds.Width,(int)window.Bounds.Height));image.Render(window);image.Save(Path.Combine(AppContext.BaseDirectory,$"probe-transfer-{width}-{step}.png"));}
        }finally{window.Close();}
    }
    private sealed record Fixture(ProbeFormationDesignerViewModel Source,ProbeFormationAccountsViewModel Vm,FakeEve Eve,FakeResolver Resolver,EveSettingsIdentityService Identity)
    {
        public static async Task<Fixture> Create()
        {
            var settings=new MemorySettings();var eve=new FakeEve();var identity=new FakeIdentities(settings);var resolver=new FakeResolver();
            var source=new ProbeFormationDesignerViewModel(new ProbeFormationLibrary(settings),eve,settings,identity,resolver);await source.InitializeAsync();source.Create();
            return new(source,new(source,eve,settings,identity,resolver),eve,resolver,identity.Storage);
        }
    }
    private sealed class MemorySettings:ISettingsService
    {
        private readonly Dictionary<string,string> values=[];
        public Task<T?> GetAsync<T>(string key,CancellationToken cancellationToken=default)=>Task.FromResult(values.TryGetValue(key,out var value)?System.Text.Json.JsonSerializer.Deserialize<T>(value):default);
        public Task SetAsync<T>(string key,T value,CancellationToken cancellationToken=default){values[key]=System.Text.Json.JsonSerializer.Serialize(value);return Task.CompletedTask;}
    }
    private sealed class FakeIdentities(MemorySettings settings):IEveSettingsIdentityService
    {
        public EveSettingsIdentityService Storage {get;}=new(settings);
        public Task<IReadOnlyList<EveCharacterFile>> DiscoverCharactersAsync(IEnumerable<EveSettingsAccount> accounts,CancellationToken cancellationToken=default)=>Task.FromResult<IReadOnlyList<EveCharacterFile>>([new("unused.dat","tranquility","Default",90000001)]);
        public IReadOnlyList<EveAccountGroup> GroupAccounts(IEnumerable<EveSettingsAccount> accounts)=>Storage.GroupAccounts(accounts);
        public Task<IReadOnlyList<EveLocalCharacterLink>> LoadLinksAsync(CancellationToken cancellationToken=default)=>Storage.LoadLinksAsync(cancellationToken);
        public Task SaveLinksAsync(string key,string server,IEnumerable<long> ids,CancellationToken cancellationToken=default)=>Storage.SaveLinksAsync(key,server,ids,cancellationToken);
    }
    private sealed class FakeResolver:IEveCharacterResolver
    {
        public bool WaitForCancellation;public TaskCompletionSource Canceled {get;}=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<EveCharacterIdentity> ResolveAsync(string server,long id,CancellationToken cancellationToken=default)
        {
            if(WaitForCancellation)try{await Task.Delay(Timeout.Infinite,cancellationToken);}catch(OperationCanceledException){Canceled.TrySetResult();throw;}
            return new(server,id,$"Pilot {id}","Resolved");
        }
        public Task<byte[]?> GetPortraitAsync(string server,long id,CancellationToken cancellationToken=default)=>Task.FromResult<byte[]?>(null);
    }
    private sealed class FakeEve:IEveProbeSettingsService
    {
        public int ReadCalls,ApplyCalls,Version;public bool Empty;public EveClientStatus Status=EveClientStatus.Closed;public Action? BeforeApply;public HashSet<string> FailPaths {get;}=[];public Dictionary<string,IReadOnlyList<EveStoredFormation>> Stored {get;}=[];
        private readonly EveProbeSettingsService merge=new();
        private static readonly string Root=Path.Combine(Path.GetTempPath(),"hisa-fake-transfer");
        private readonly EveSettingsAccount[] accounts=[new(Path.Combine(Root,"tq","settings_Default","core_user_123.dat"),"tq_tranquility","Default",123),new(Path.Combine(Root,"tq","settings_Exploration","core_user_123.dat"),"tq_tranquility","Exploration",123),new(Path.Combine(Root,"sisi","settings_Default","core_user_123.dat"),"sisi_singularity","Default",123)];
        public Task<IReadOnlyList<EveSettingsAccount>> DiscoverAsync(IEnumerable<string> roots,CancellationToken cancellationToken=default)=>Task.FromResult<IReadOnlyList<EveSettingsAccount>>(Empty?[]:accounts);
        public Task<EveFormationSnapshot> ReadAsync(EveSettingsAccount account,CancellationToken cancellationToken=default){ReadCalls++;return Task.FromResult(new EveFormationSnapshot(account,Version.ToString(),Stored.GetValueOrDefault(account.Path)??[],[]));}
        public Task<string> GetFingerprintAsync(EveSettingsAccount account,CancellationToken cancellationToken=default)=>Task.FromResult(Version.ToString());
        public FormationMergePreview Preview(EveFormationSnapshot snapshot,IReadOnlyList<ProbeFormation> formations,IReadOnlyList<FormationMergeChoice> choices)=>merge.Preview(snapshot,formations,choices);
        public EveClientStatus GetClientStatus()=>Status;
        public Task<EveSettingsApplyResult> ApplyAsync(FormationMergePreview preview,bool clientClosedAcknowledged,CancellationToken cancellationToken=default){ApplyCalls++;BeforeApply?.Invoke();var failed=FailPaths.Contains(preview.Snapshot.Account.Path)||preview.Snapshot.Fingerprint!=Version.ToString();return Task.FromResult(new EveSettingsApplyResult(preview.Snapshot.Account.Path,!failed,failed?"Refresh this file and retry":"Applied",failed?null:"Backup/path.bak"));}
        public Task<IReadOnlyList<EveSettingsBackup>> ListBackupsAsync(EveSettingsAccount account,CancellationToken cancellationToken=default)=>Task.FromResult<IReadOnlyList<EveSettingsBackup>>([]);
        public Task<EveSettingsApplyResult> RestoreAsync(EveSettingsAccount account,EveSettingsBackup backup,string expectedFingerprint,bool clientClosedAcknowledged,CancellationToken cancellationToken=default)=>Task.FromResult(new EveSettingsApplyResult(account.Path,true,"Restored"));
    }
}
