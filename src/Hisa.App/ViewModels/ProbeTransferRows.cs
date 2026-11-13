using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Avalonia.Media.Imaging;
using Hisa.Core.Models;

namespace Hisa.App;

public abstract class ProbeTransferRow : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    protected bool Set<T>(ref T field,T value,[CallerMemberName]string? name=null) {if(EqualityComparer<T>.Default.Equals(field,value))return false;field=value;Raise(name);return true;}
    protected internal void Raise([CallerMemberName]string? name=null)=>PropertyChanged?.Invoke(this,new(name));
}
public sealed class TransferFormationRow(ProbeFormation formation,Action changed) : ProbeTransferRow
{
    public ProbeFormation Formation {get;internal set;}=formation;
    public string Name=>Formation.Name;
    public string Description=>$"{Formation.Probes.Count} probes";
    private bool selected;
    public bool Selected {get=>selected;set{if(Set(ref selected,value))changed();}}
}
public sealed class TransferCharacterRow(string server,long id) : ProbeTransferRow,IDisposable
{
    public string Server {get;}=server;
    public long CharacterId {get;}=id;
    public string Key=>$"{Server}:{CharacterId}";
    private EveCharacterIdentity identity=new(server,id,null,"Not resolved yet");
    public EveCharacterIdentity Identity {get=>identity;set{identity=value;Raise();Raise(nameof(Name));Raise(nameof(Detail));}}
    public string Name=>Identity.DisplayName;
    public string Detail=>$"{CharacterId} · {Server} · {Identity.Status}";
    private bool linked;
    public bool Linked {get=>linked;set=>Set(ref linked,value);}
    private Bitmap? portrait;
    public Bitmap? Portrait {get=>portrait;private set{var old=portrait;portrait=value;Raise();Raise(nameof(HasPortrait));old?.Dispose();}}
    public bool HasPortrait=>Portrait is not null;
    internal bool PortraitAttempted {get;set;}
    public void SetPortrait(byte[] bytes) {try{using var stream=new MemoryStream(bytes);Portrait=new Bitmap(stream);}catch(Exception ex)when(ex is ArgumentException or IOException or NotSupportedException){Portrait=null;}}
    public void Dispose(){Portrait?.Dispose();portrait=null;}
}
public sealed class TransferTargetRow(EveSettingsAccount account,Action changed,Func<bool> locked) : ProbeTransferRow
{
    public EveSettingsAccount Account {get;internal set;}=account;
    public string Path=>Account.Path;
    public string Profile=>Account.Profile;
    public string Alias=>Account.Alias??"";
    public string Label=>string.IsNullOrWhiteSpace(Alias)?Profile:$"{Profile} · {Alias}";
    public DateTimeOffset? Modified {get{try{return File.Exists(Path)?File.GetLastWriteTimeUtc(Path):null;}catch(IOException){return null;}}}
    private bool selected;
    public bool Selected {get=>selected;set{if(locked())return;if(Set(ref selected,value))changed();}}
}
public sealed class TransferAccountGroupRow(EveAccountGroup group) : ProbeTransferRow
{
    public EveAccountGroup Group {get;}=group;
    public string Title=>$"Account {Group.AccountId}";
    public string Detail=>$"{Group.Server} · {Targets.Count} settings files";
    public ObservableCollection<TransferTargetRow> Targets {get;}=[];
    public IReadOnlyList<TransferTargetRow> VisibleTargets {get;internal set;}=[];
    public ObservableCollection<TransferCharacterRow> Characters {get;}=[];
    public string Names=>Characters.Count==0?"No characters linked locally":string.Join(", ",Characters.Select(c=>c.Name));
    public string DisplayTitle=>Characters.Count==0?Title:Names;
    public string AccountContext=>Characters.Count==0?$"{Group.Server} · {Targets.Count} profile file{(Targets.Count==1?"":"s")}":$"Account {Group.AccountId} · {Group.Server}";
}
public sealed class FormationReviewRow(ProbeFormation formation, IReadOnlyList<EveStoredFormation> matches,
    bool replacementMissing, Action changed) : ProbeTransferRow
{
    public ProbeFormation Formation {get;}=formation;
    public IReadOnlyList<EveStoredFormation> Matches {get;}=matches;
    public bool IsConflict=>Matches.Count>0;
    public bool ReplacementMissing => replacementMissing;
    public string Name=>Formation.Name;
    public bool RequiresAction => IsConflict || replacementMissing;
    public bool IsUnresolved => RequiresAction && (ActionIndex <= 0 || ActionIndex == 2 && IsConflict && Target is null);
    public string Description => replacementMissing ? "Previously selected replacement is unavailable."
        : Matches.Count switch { 0 => "New formation", 1 => "Matching name", _ => $"{Matches.Count} matching names" };
    public string[] ActionOptions { get; } = replacementMissing && matches.Count == 0
        ? ["Choose action…", "Skip", "Add as new"]
        : ["Choose action…", "Keep existing", "Replace", "Create renamed copy"];
    private int actionIndex;
    public int ActionIndex {get=>actionIndex;set{if(Set(ref actionIndex,value)){Raise(nameof(NeedsName));Raise(nameof(NeedsTarget));changed();}}}
    private EveStoredFormation? target = matches.Count == 1 ? matches[0] : null;
    public EveStoredFormation? Target {get=>target;set{if(Set(ref target,value))changed();}}
    private string newName=formation.Name+" (HISA)";
    public string NewName {get=>newName;set{if(Set(ref newName,value))changed();}}
    public bool NeedsName=>IsConflict&&ActionIndex==3;
    public bool NeedsTarget => Matches.Count > 1 && ActionIndex == 2;
    public FormationMergeChoice? Choice => ActionIndex == 1 && RequiresAction
        ? new(Formation.Id, FormationConflictAction.KeepExisting)
        : !IsConflict ? null : new(Formation.Id, ActionIndex switch
        {
            2 => FormationConflictAction.Replace,
            3 => FormationConflictAction.RenamedCopy,
            _ => FormationConflictAction.Unresolved
        }, Target?.EveId, NewName);

    private string outcome = "";
    public string Outcome { get => outcome; internal set => Set(ref outcome, value); }
}
public sealed class FileReviewRow(TransferTargetRow target,EveFormationSnapshot? snapshot,string? error=null) : ProbeTransferRow
{
    public TransferTargetRow Target {get;}=target;
    public EveFormationSnapshot? Snapshot {get;}=snapshot;
    public bool HasSnapshot => Snapshot is not null;
    public ObservableCollection<FormationReviewRow> Formations {get;}=[];
    public FormationMergePreview? Preview {get;internal set;}
    private bool expanded;
    public bool IsExpanded { get => expanded; set => Set(ref expanded, value); }
    private string message=error??"";
    public string Message {get=>message;internal set=>Set(ref message,value);}
    private bool hasError = error is not null;
    public bool HasError { get => hasError; internal set => Set(ref hasError, value); }
    public string Title => $"Account {Target.Account.AccountId} · {EveSettingsIdentity.Server(Target.Account.Installation)} · {Target.Label}";
}
public sealed record TransferResultRow(string Path,string State,string Message,string? BackupPath)
{
    public bool Failed=>State=="Failed";
    public bool HasBackup => BackupPath is not null;
}
