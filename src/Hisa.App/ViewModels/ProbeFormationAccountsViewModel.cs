using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text.Json;
using Hisa.Core.Abstractions;
using Hisa.Core.Models;

namespace Hisa.App;

public sealed class ProbeFormationAccountsViewModel : ProbeTransferRow,IDisposable
{
    private readonly ProbeFormationDesignerViewModel source;
    private readonly IEveProbeSettingsService eve;
    private readonly ISettingsService settings;
    private readonly IEveSettingsIdentityService identities;
    private readonly IEveCharacterResolver resolver;
    private readonly CancellationTokenSource lifetime=new();
    private readonly Dictionary<string,TransferCharacterRow> characters=[];
    private IReadOnlyList<EveCharacterFile> characterFiles=[];
    private IReadOnlyList<EveLocalCharacterLink> links=[];
    private EveDiscoveryPreferences preferences=new([],new Dictionary<string,string>());
    private string sourceSignature="",search="",serverFilter="All servers",profileFilter="All profiles",status="Choose formations to send.",alias="",characterSearch="";
    private int step,detailGeneration,revision;
    private bool working,writing,acknowledged,reviewCurrent,disposed,resolving;
    private TransferTargetRow? selectedTarget;
    public ObservableCollection<TransferFormationRow> Formations {get;}=[];
    public ObservableCollection<TransferAccountGroupRow> Groups {get;}=[];
    public ObservableCollection<FileReviewRow> Reviews {get;}=[];
    public ObservableCollection<TransferResultRow> Results {get;}=[];
    public ObservableCollection<EveStoredFormation> ExistingFormations {get;}=[];
    public ObservableCollection<EveSettingsBackup> Backups {get;}=[];
    public ObservableCollection<TransferCharacterRow> LinkCandidates {get;}=[];
    public IEnumerable<TransferCharacterRow> FilteredLinkCandidates=>LinkCandidates.Where(c=>c.Name.Contains(CharacterSearch,StringComparison.OrdinalIgnoreCase)||c.CharacterId.ToString().Contains(CharacterSearch,StringComparison.Ordinal));
    public bool HasMatchingLinkCandidates=>FilteredLinkCandidates.Any();
    public string CharacterSearch {get=>characterSearch;set{if(Set(ref characterSearch,value)){Raise(nameof(FilteredLinkCandidates));Raise(nameof(HasMatchingLinkCandidates));}}}
    public string ManualCharacterId {get;set;}="";
    private string linkStatus="";
    public string LinkStatus {get=>linkStatus;set=>Set(ref linkStatus,value);}
    public string Alias {get=>alias;set=>Set(ref alias,value);}
    public string Status {get=>status;set=>Set(ref status,value);}
    public bool IsWorking {get=>working;private set{working=value;RefreshState();}}
    public bool IsWriting {get=>writing;private set{writing=value;RefreshState();}}
    public bool IsIdle=>!IsWorking&&!IsWriting;
    public bool IsResolving {get=>resolving;private set=>Set(ref resolving,value);}
    public int Step {get=>step;private set{step=value;RefreshState();}}
    public bool IsFormations=>Step==0;
    public bool IsAccounts=>Step==1;
    public bool IsReview=>Step==2;
    public bool IsResults=>Step==3;
    public string StepTitle=>new[]{"1  Formations","2  Accounts","3  Review","4  Results"}[Step];
    public string PrimaryAction=>Step switch{0=>"Choose accounts →",1=>"Review changes →",2=>"Apply to EVE",_=>"Done"};
    public string Summary {get{var f=Formations.Count(f=>f.Selected);var t=AllTargets.Count(t=>t.Selected);return $"{f} formation{(f==1?"":"s")} · {t} destination file{(t==1?"":"s")}";}}
    public bool CanBack=>IsIdle&&Step is 1 or 2;
    public bool CanContinue=>IsIdle&&(Step switch{0=>Formations.Any(f=>f.Selected)&&!SourceHasDrafts,1=>AllTargets.Any(t=>t.Selected)&&Formations.Any(f=>f.Selected)&&!SourceHasDrafts,2=>CanApply,_=>true});
    public bool CanApply=>IsIdle&&reviewCurrent&&Reviews.Count>0&&Reviews.All(r=>r.Preview is not null)&&ClientStatus!=EveClientStatus.Running&&(ClientStatus!=EveClientStatus.Unknown||Acknowledged)&&!SourceHasDrafts;
    public bool HasFailed=>Results.Any(r=>r.Failed);
    public bool HasNoFormations=>Formations.Count==0;
    public bool Acknowledged {get=>acknowledged;set{if(Set(ref acknowledged,value))RefreshState();}}
    public EveClientStatus ClientStatus {get;private set;}
    public string ClientStatusText=>ClientStatus switch{EveClientStatus.Running=>"EVE is running — close all clients before applying.",EveClientStatus.Unknown=>"Client detection is inconclusive. Confirm all clients are closed.",_=>"No running EVE client detected."};
    public IReadOnlyList<string> Locations=>preferences.Roots;
    public IEnumerable<TransferTargetRow> AllTargets=>Groups.SelectMany(g=>g.Targets);
    public ObservableCollection<TransferAccountGroupRow> VisibleGroups {get;}=[];
    public bool HasNoVisibleGroups=>VisibleGroups.Count==0;
    public string VisibleAccountSummary=>$"{VisibleGroups.Count} accounts · {VisibleGroups.Sum(g=>g.VisibleTargets.Count)} profile files shown";
    public string DestinationSummary {get{var count=AllTargets.Count(t=>t.Selected);return $"{count} file{(count==1?"":"s")} selected";}}
    public bool HasActiveFilters=>ServerFilter!="All servers"||ProfileFilter!="All profiles";
    public string Search {get=>search;set{if(Set(ref search,value))RefreshFilters();}}
    public string ServerFilter {get=>serverFilter;set{if(Set(ref serverFilter,value))RefreshFilters();}}
    public string ProfileFilter {get=>profileFilter;set{if(Set(ref profileFilter,value))RefreshFilters();}}
    public string[] ServerOptions=>new[]{"All servers"}.Concat(Groups.Select(g=>g.Group.Server).Distinct()).ToArray();
    public string[] ProfileOptions=>new[]{"All profiles"}.Concat(AllTargets.Select(t=>t.Profile).Distinct()).ToArray();
    public TransferTargetRow? SelectedTarget {get=>selectedTarget;private set{selectedTarget=value;Alias=value?.Alias??"";Raise();Raise(nameof(HasTarget));Raise(nameof(TargetTitle));Raise(nameof(TargetContext));Raise(nameof(TargetPath));Raise(nameof(TargetModified));Raise(nameof(SelectedCharacters));Raise(nameof(HasLinkedCharacters));}}
    public bool HasTarget=>SelectedTarget is not null;
    public string TargetTitle=>SelectedTarget?.Label??"Account details";
    public string TargetContext=>SelectedTarget is { } t?$"Account {t.Account.AccountId} · {EveSettingsIdentity.Server(t.Account.Installation)}":"";
    public bool HasLinkedCharacters=>SelectedCharacters.Any();
    public bool HasExistingFormations=>ExistingFormations.Count>0;
    public string TargetPath=>SelectedTarget?.Path??"Select Details on an account file.";
    public string TargetModified=>SelectedTarget?.Modified is { } time?$"Modified {time.ToLocalTime():g}":"";
    public IEnumerable<TransferCharacterRow> SelectedCharacters=>SelectedTarget is { } t?Groups.First(g=>g.Group.Key==EveSettingsIdentity.GroupKey(t.Account)).Characters:[];
    public bool SourceHasDrafts=>source.HasPendingEdits||source.Current is { } f&&source.FormationName!=f.Name;
    public ProbeFormationAccountsViewModel(ProbeFormationDesignerViewModel source,IEveProbeSettingsService eve,ISettingsService settings,IEveSettingsIdentityService identities,IEveCharacterResolver resolver)
    {
        this.source=source;this.eve=eve;this.settings=settings;this.identities=identities;this.resolver=resolver;
        RefreshSource(true);source.PropertyChanged+=OnSourceChanged;
    }
    public async Task RunAsync(Func<Task> action)
    {
        if(!IsIdle)return;IsWorking=true;
        try{await action();}catch(OperationCanceledException)when(lifetime.IsCancellationRequested){}catch(Exception ex){Status=ex.Message;}finally{IsWorking=false;}
    }
    public void Run(Action action){try{action();}catch(Exception ex){Status=ex.Message;}}
    public async Task InitializeAsync()
    {
        preferences=await settings.GetAsync<EveDiscoveryPreferences>("probes.discovery.v1",lifetime.Token)??preferences;
        await DiscoverAsync();
    }
    public async Task DiscoverAsync()
    {
        InvalidateReview();var chosen=AllTargets.Where(t=>t.Selected).Select(t=>t.Path).ToHashSet();
        var accounts=await eve.DiscoverAsync(preferences.Roots,lifetime.Token);
        characterFiles=await identities.DiscoverCharactersAsync(accounts,lifetime.Token);links=await identities.LoadLinksAsync(lifetime.Token);
        foreach(var file in characterFiles)GetCharacter(file.Server,file.CharacterId);
        foreach(var link in links)GetCharacter(link.Server,link.CharacterId);
        Groups.Clear();SelectedTarget=null;ExistingFormations.Clear();Backups.Clear();
        foreach(var group in identities.GroupAccounts(accounts)) {
            var row=new TransferAccountGroupRow(group);
            foreach(var account in group.Files){var target=new TransferTargetRow(account with{Alias=preferences.Aliases.GetValueOrDefault(account.Path)},SelectionChanged,()=>IsWriting);row.Targets.Add(target);target.Selected=chosen.Contains(target.Path);}
            Groups.Add(row);
        }
        if(!ServerOptions.Contains(ServerFilter))serverFilter="All servers";
        if(!ProfileOptions.Contains(ProfileFilter))profileFilter="All profiles";
        Raise(nameof(ServerFilter));Raise(nameof(ProfileFilter));
        UpdateLinks();Raise(nameof(ServerOptions));Raise(nameof(ProfileOptions));Raise(nameof(Locations));RefreshFilters();RefreshClientStatus();
        Status=$"Found {accounts.Count} account files and {characterFiles.Count} character files. Select exact profile targets.";
        if(Step==1)StartIdentityResolution();
    }
    public async Task AddLocationAsync(string path)
    {
        preferences=preferences with{Roots=preferences.Roots.Append(path).Distinct().ToArray()};await settings.SetAsync("probes.discovery.v1",preferences,lifetime.Token);await DiscoverAsync();
    }
    public async Task RemoveLocationAsync(string path)
    {
        preferences=preferences with{Roots=preferences.Roots.Where(p=>p!=path).ToArray()};await settings.SetAsync("probes.discovery.v1",preferences,lifetime.Token);await DiscoverAsync();
    }
    private void RefreshSource(bool initial=false)
    {
        var signature=JsonSerializer.Serialize(source.Library.Select(r=>r.Formation));
        if(signature==sourceSignature&&!initial){RefreshState();return;}
        sourceSignature=signature;var selected=Formations.Where(f=>f.Selected).Select(f=>f.Formation.Id).ToHashSet();
        Formations.Clear();foreach(var row in source.Library)Formations.Add(new(row.Formation,SelectionChanged){Selected=initial?row.Formation.Id==source.Current?.Id:selected.Contains(row.Formation.Id)});
        InvalidateReview();
    }
    private void OnSourceChanged(object? sender,PropertyChangedEventArgs e)
    {
        if(e.PropertyName is nameof(source.Current) or nameof(source.FilteredLibrary))RefreshSource();
        if(e.PropertyName is nameof(source.HasPendingEdits) or nameof(source.IsDirty)){if(SourceHasDrafts)InvalidateReview();RefreshState();}
    }
    private void SelectionChanged(){InvalidateReview();RefreshState();}
    private void InvalidateReview()
    {
        revision++;
        reviewCurrent=false;foreach(var row in Reviews){row.Preview=null;row.Message="Selection or formation changed. Return to Accounts and review again.";}RefreshState();
    }
    private void RefreshFilters()
    {
        foreach(var group in Groups){var visible=group.Targets.Where(t=>(ServerFilter=="All servers"||group.Group.Server==ServerFilter)&&(ProfileFilter=="All profiles"||t.Profile==ProfileFilter)&&($"{group.Names} {group.Title} {t.Alias} {t.Path}".Contains(Search,StringComparison.OrdinalIgnoreCase))).ToArray();if(!group.VisibleTargets.SequenceEqual(visible)){group.VisibleTargets=visible;group.Raise(nameof(group.VisibleTargets));}}
        var desired=Groups.Where(g=>g.VisibleTargets.Count>0).ToArray();
        for(var i=0;i<desired.Length;i++){if(i<VisibleGroups.Count&&ReferenceEquals(VisibleGroups[i],desired[i]))continue;var ix=VisibleGroups.IndexOf(desired[i]);if(ix>=0)VisibleGroups.Move(ix,i);else VisibleGroups.Insert(i,desired[i]);}
        while(VisibleGroups.Count>desired.Length)VisibleGroups.RemoveAt(VisibleGroups.Count-1);
        Raise(nameof(HasNoVisibleGroups));Raise(nameof(VisibleAccountSummary));Raise(nameof(HasActiveFilters));
    }
    public void ResetFilters(){ServerFilter="All servers";ProfileFilter="All profiles";}
    public void SelectVisible(){if(IsWriting)return;foreach(var target in VisibleGroups.SelectMany(g=>g.VisibleTargets))target.Selected=true;}
    public void ClearTargets(){if(IsWriting)return;foreach(var target in AllTargets)target.Selected=false;}
    public void Back(){if(!CanBack)return;Step--;}
    public async Task NextAsync()
    {
        if(SourceHasDrafts)throw new ArgumentException("Finish or cancel pending editor changes before transferring.");
        if(Step==0){if(!Formations.Any(f=>f.Selected))throw new ArgumentException("Select at least one formation.");Step=1;StartIdentityResolution();}
        else if(Step==1)await BuildReviewAsync();
        else if(Step==2)await ApplyAsync();
    }
    public async Task BuildReviewAsync()
    {
        if(SourceHasDrafts)throw new ArgumentException("Finish or cancel pending editor changes first.");
        var formations=Formations.Where(f=>f.Selected).Select(f=>f.Formation).ToArray();var targets=AllTargets.Where(t=>t.Selected).ToArray();
        if(formations.Length==0||targets.Length==0)throw new ArgumentException("Select formations and at least one target file.");
        var old=Reviews.ToDictionary(r=>r.Target.Path,r=>r.Formations.ToDictionary(f=>f.Formation.Id,f=>f.Choice));
        Reviews.Clear();reviewCurrent=false;acknowledged=false;
        foreach(var target in targets){
            FileReviewRow row;
            try{var snapshot=await eve.ReadAsync(target.Account,lifetime.Token);row=new(target,snapshot);
                foreach(var f in formations){var review=new FormationReviewRow(f,snapshot.Formations.Where(e=>e.Formation.Name.Equals(f.Name,StringComparison.OrdinalIgnoreCase)).ToArray(),RebuildPreviews);
                    if(old.TryGetValue(target.Path,out var previous)&&previous.GetValueOrDefault(f.Id) is { } choice){
                        var matchingTarget=review.Matches.FirstOrDefault(m=>m.EveId==choice.TargetId);
                        review.ActionIndex=choice.Action==FormationConflictAction.Replace&&matchingTarget is null?0:(int)choice.Action;
                        review.Target=matchingTarget??review.Matches.FirstOrDefault();review.NewName=choice.NewName??review.NewName;
                    }
                    row.Formations.Add(review);}
            }catch(Exception ex)when(ex is not OperationCanceledException){row=new(target,null,ex.Message);}
            Reviews.Add(row);
        }
        reviewCurrent=true;RebuildPreviews();RefreshClientStatus();Step=2;Status="Review each destination and resolve name conflicts before applying.";
    }
    private void RebuildPreviews()
    {
        if(!reviewCurrent)return;
        foreach(var row in Reviews){if(row.Snapshot is null)continue;row.Preview=null;
            try{row.Preview=eve.Preview(row.Snapshot,row.Formations.Select(f=>f.Formation).ToArray(),row.Formations.Select(f=>f.Choice).OfType<FormationMergeChoice>().ToArray());row.Message=$"{row.Preview.Entries.Count(e=>!e.ReplacesExisting)} additions · {row.Preview.Entries.Count(e=>e.ReplacesExisting)} replacements · {row.Formations.Count(f=>f.Choice?.Action==FormationConflictAction.KeepExisting)} skipped";}
            catch(Exception ex){row.Message=ex.Message;}
        }RefreshState();
    }
    public void RefreshClientStatus(){ClientStatus=eve.GetClientStatus();Raise(nameof(ClientStatus));Raise(nameof(ClientStatusText));RefreshState();}
    public async Task ApplyAsync()
    {
        RefreshClientStatus();
        // RunAsync marks navigation busy; validity itself remains independent of that flag.
        if(!reviewCurrent||Reviews.Count==0||Reviews.Any(r=>r.Preview is null)||SourceHasDrafts)throw new ArgumentException("Refresh and complete the review before applying.");
        if(ClientStatus==EveClientStatus.Running||ClientStatus==EveClientStatus.Unknown&&!Acknowledged)throw new ArgumentException(ClientStatusText);
        var captured=Reviews.Select(r=>r.Preview!).ToArray();var capturedRevision=revision;IsWriting=true;Results.Clear();
        try{foreach(var preview in captured){
            Status=$"Applying {Results.Count+1} of {captured.Length}: {preview.Snapshot.Account.Path}";
            EveSettingsApplyResult result;
            if(capturedRevision!=revision||SourceHasDrafts)result=new(preview.Snapshot.Account.Path,false,"Formation selection changed during transfer. Review again.");
            else try{result=await eve.ApplyAsync(preview,Acknowledged,lifetime.Token);}catch(Exception ex)when(ex is not OperationCanceledException){result=new(preview.Snapshot.Account.Path,false,ex.Message);}
            Results.Add(new(result.Path,result.Success?(preview.Entries.Count==0?"Unchanged":"Updated"):"Failed",result.Message,result.BackupPath));
        }}
        finally{IsWriting=false;reviewCurrent=false;Step=3;RefreshState();}
        Status=$"{Results.Count(r=>!r.Failed)} succeeded or unchanged · {Results.Count(r=>r.Failed)} failed.";
    }
    public async Task RetryFailedAsync()
    {
        var failed=Results.Where(r=>r.Failed).Select(r=>r.Path).ToHashSet();if(failed.Count==0)return;
        foreach(var target in AllTargets)target.Selected=failed.Contains(target.Path);
        await BuildReviewAsync();
    }
    public void BackToAccounts(){if(IsWriting)return;Step=1;InvalidateReview();}
    public async Task SelectTargetAsync(TransferTargetRow target)
    {
        var generation=++detailGeneration;SelectedTarget=target;ExistingFormations.Clear();Backups.Clear();
        try{var snapshot=await eve.ReadAsync(target.Account,lifetime.Token);if(generation!=detailGeneration)return;foreach(var formation in snapshot.Formations)ExistingFormations.Add(formation);if(snapshot.Errors.Count>0)Status=string.Join("; ",snapshot.Errors);}
        catch(Exception ex)when(ex is not OperationCanceledException){if(generation==detailGeneration)Status=ex.Message;}
        if(generation==detailGeneration)Raise(nameof(HasExistingFormations));
    }
    public async Task SaveAliasAsync()
    {
        if(SelectedTarget is not { } target)return;
        var aliases=preferences.Aliases.ToDictionary(p=>p.Key,p=>p.Value);aliases[target.Path]=Alias.Trim();preferences=preferences with{Aliases=aliases};await settings.SetAsync("probes.discovery.v1",preferences,lifetime.Token);
        target.Account=target.Account with{Alias=Alias.Trim()};target.Raise(nameof(target.Alias));target.Raise(nameof(target.Label));Raise(nameof(TargetTitle));RefreshFilters();
    }
    public void CopyFormation(EveStoredFormation stored)=>source.AddTransferredFormation(stored.Formation);
    public async Task LoadBackupsAsync(){Backups.Clear();if(SelectedTarget is { } target)foreach(var b in await eve.ListBackupsAsync(target.Account,lifetime.Token))Backups.Add(b);}
    public async Task RestoreAsync(EveSettingsBackup backup)
    {
        if(SelectedTarget is not { } target)return;var fingerprint=await eve.GetFingerprintAsync(target.Account,lifetime.Token);IsWriting=true;
        try{var result=await eve.RestoreAsync(target.Account,backup,fingerprint,Acknowledged,lifetime.Token);Status=result.Message+(result.BackupPath is null?"":$" Backup: {result.BackupPath}");InvalidateReview();}
        finally{IsWriting=false;}
        await SelectTargetAsync(target);await LoadBackupsAsync();
    }
    public void OpenLinkPicker()
    {
        if(SelectedTarget is not { } target)throw new ArgumentException("Select an account file first.");var server=EveSettingsIdentity.Server(target.Account.Installation);var key=EveSettingsIdentity.GroupKey(target.Account);
        LinkCandidates.Clear();foreach(var row in characters.Values.Where(c=>c.Server==server).OrderBy(c=>c.Name)){row.Linked=links.Any(l=>l.GroupKey==key&&l.Server==server&&l.CharacterId==row.CharacterId);LinkCandidates.Add(row);}
        CharacterSearch="";LinkStatus="";Raise(nameof(HasMatchingLinkCandidates));StartIdentityResolution();
    }
    public async Task AddManualCharacterAsync()
    {
        if(SelectedTarget is not { } target||!long.TryParse(ManualCharacterId,out var id)||id<=0)throw new ArgumentException("Enter a positive character ID.");
        var row=GetCharacter(EveSettingsIdentity.Server(target.Account.Installation),id);if(!LinkCandidates.Contains(row))LinkCandidates.Add(row);row.Linked=true;Raise(nameof(FilteredLinkCandidates));Raise(nameof(HasMatchingLinkCandidates));await ResolveRowAsync(row);
    }
    public async Task SaveLinksAsync()
    {
        if(SelectedTarget is not { } target)return;await identities.SaveLinksAsync(EveSettingsIdentity.GroupKey(target.Account),EveSettingsIdentity.Server(target.Account.Installation),LinkCandidates.Where(c=>c.Linked).Select(c=>c.CharacterId),lifetime.Token);
        links=await identities.LoadLinksAsync(lifetime.Token);UpdateLinks();Status="Character labels linked locally. Destination selection is unchanged.";
    }
    private TransferCharacterRow GetCharacter(string server,long id)
    {
        var key=$"{server}:{id}";if(!characters.TryGetValue(key,out var row))characters[key]=row=new(server,id);return row;
    }
    private void UpdateLinks()
    {
        foreach(var group in Groups){group.Characters.Clear();foreach(var link in links.Where(l=>l.GroupKey==group.Group.Key&&l.Server==group.Group.Server))group.Characters.Add(GetCharacter(link.Server,link.CharacterId));group.Raise(nameof(group.Names));group.Raise(nameof(group.DisplayTitle));group.Raise(nameof(group.AccountContext));}
        Raise(nameof(SelectedCharacters));Raise(nameof(HasLinkedCharacters));RefreshFilters();
    }
    private void StartIdentityResolution(){if(resolving||disposed)return;_ = ResolveAllAsync();}
    private async Task ResolveAllAsync()
    {
        IsResolving=true;
        try{foreach(var batch in characters.Values.Where(c=>c.Identity.Name is null).ToArray().Chunk(4)){if(lifetime.IsCancellationRequested)break;await Task.WhenAll(batch.Select(ResolveRowAsync));}}
        catch(OperationCanceledException)when(lifetime.IsCancellationRequested){}finally{IsResolving=false;}
    }
    private async Task ResolveRowAsync(TransferCharacterRow row)
    {
        try{var identity=await resolver.ResolveAsync(row.Server,row.CharacterId,lifetime.Token);if(disposed)return;row.Identity=identity;foreach(var group in Groups){group.Raise(nameof(group.Names));group.Raise(nameof(group.DisplayTitle));}RefreshFilters();Raise(nameof(FilteredLinkCandidates));Raise(nameof(HasMatchingLinkCandidates));}
        catch(OperationCanceledException)when(lifetime.IsCancellationRequested){}catch(Exception ex){if(!disposed)row.Identity=new(row.Server,row.CharacterId,null,ex.Message);}
    }
    public async Task LoadPortraitAsync(TransferCharacterRow row)
    {
        if(row.PortraitAttempted||disposed)return;row.PortraitAttempted=true;
        try{var bytes=await resolver.GetPortraitAsync(row.Server,row.CharacterId,lifetime.Token);if(!disposed&&bytes is not null)row.SetPortrait(bytes);}catch(OperationCanceledException)when(lifetime.IsCancellationRequested){}catch(Exception){/* Portraits are optional; keep the placeholder. */}
    }
    private void RefreshState()
    {
        foreach(var name in new[]{nameof(IsWorking),nameof(IsWriting),nameof(IsIdle),nameof(Step),nameof(IsFormations),nameof(IsAccounts),nameof(IsReview),nameof(IsResults),nameof(StepTitle),nameof(PrimaryAction),nameof(Summary),nameof(DestinationSummary),nameof(CanBack),nameof(CanContinue),nameof(CanApply),nameof(HasFailed),nameof(SourceHasDrafts),nameof(HasNoFormations)})Raise(name);
    }
    public void Dispose(){if(disposed)return;disposed=true;source.PropertyChanged-=OnSourceChanged;lifetime.Cancel();foreach(var row in characters.Values)row.Dispose();}
}
