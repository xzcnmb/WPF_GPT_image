using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using Clipboard = System.Windows.Clipboard;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Gpt2Image.Core.Agent;
using Gpt2Image.Core.Api;
using Gpt2Image.Core.Models;
using Gpt2Image.Core.Storage.Repositories;
using Gpt2Image.Wpf.Services;

namespace Gpt2Image.Wpf.ViewModels;

public sealed partial class CodingPageViewModel : ObservableObject
{
    private const int MaxFileChanges = 8;
    private const int MaxCommands = 4;
    private const string DefaultCodingModel = "gpt-4o-mini";

    private readonly BackendProfileRepository _profiles;
    private readonly CodingAgentRepository _repository;
    private readonly ICodingAgentClient _client;
    private readonly ICodingWorkspaceService _workspace;
    private readonly IFileChangeService _fileChanges;
    private readonly IProcessRunner _processRunner;
    private readonly CommandSafetyClassifier _commandSafety;
    private readonly IFolderPickerService _folderPicker;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartRunCommand))]
    private string _workspacePath = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartRunCommand))]
    private string _goal = "";

    [ObservableProperty]
    private string _status = "选择工作区并输入目标后开始。";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartRunCommand))]
    private bool _isRunning;

    [ObservableProperty]
    private CodingRunItemViewModel? _selectedRun;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApproveFileChangeCommand))]
    [NotifyCanExecuteChangedFor(nameof(RejectFileChangeCommand))]
    [NotifyCanExecuteChangedFor(nameof(CopyDiffCommand))]
    private CodingFileChangeProposalViewModel? _selectedFileChange;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApproveCommandCommand))]
    [NotifyCanExecuteChangedFor(nameof(RejectCommandCommand))]
    [NotifyCanExecuteChangedFor(nameof(CopyCommandCommand))]
    private CodingCommandProposalViewModel? _selectedCommand;

    [ObservableProperty]
    private CodingSessionModeOptionViewModel _selectedSessionMode = CodingSessionModeOptionViewModel.Default;

    [ObservableProperty]
    private BackendProfileItemViewModel? _selectedCodingProfile;

    [ObservableProperty]
    private string _workspaceSearchQuery = "";

    [ObservableProperty]
    private CodingWorkspaceFileItemViewModel? _selectedWorkspaceFile;

    [ObservableProperty]
    private CodingWorkspaceSearchResultViewModel? _selectedSearchResult;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CopyWorkspacePreviewCommand))]
    private string _workspacePreviewText = "选择工作区后刷新项目树，或搜索/打开文件。";

    public ObservableCollection<CodingSessionModeOptionViewModel> SessionModes { get; } = new();
    public ObservableCollection<BackendProfileItemViewModel> AvailableCodingProfiles { get; } = new();
    public ObservableCollection<CodingWorkspaceFileItemViewModel> WorkspaceFiles { get; } = new();
    public ObservableCollection<CodingWorkspaceSearchResultViewModel> WorkspaceSearchResults { get; } = new();
    public ObservableCollection<CodingPlanItemViewModel> PlanItems { get; } = new();
    public ObservableCollection<CodingRunItemViewModel> Runs { get; } = new();
    public ObservableCollection<CodingRunEventViewModel> Events { get; } = new();
    public ObservableCollection<CodingFileChangeProposalViewModel> FileChanges { get; } = new();
    public ObservableCollection<CodingCommandProposalViewModel> Commands { get; } = new();

    public CodingPageViewModel(
        BackendProfileRepository profiles,
        CodingAgentRepository repository,
        ICodingAgentClient client,
        ICodingWorkspaceService workspace,
        IFileChangeService fileChanges,
        IProcessRunner processRunner,
        CommandSafetyClassifier commandSafety,
        IFolderPickerService folderPicker)
    {
        _profiles = profiles;
        _repository = repository;
        _client = client;
        _workspace = workspace;
        _fileChanges = fileChanges;
        _processRunner = processRunner;
        _commandSafety = commandSafety;
        _folderPicker = folderPicker;
        foreach (var mode in CodingSessionModeOptionViewModel.CreateDefaults())
        {
            SessionModes.Add(mode);
        }

        SelectedSessionMode = SessionModes.First(item => item.Value == CodingSessionMode.Cowork);
        RefreshCodingProfiles();
        RefreshRuns();
    }

    [RelayCommand]
    private void RefreshCodingProfiles()
    {
        var selectedId = SelectedCodingProfile?.Id;
        AvailableCodingProfiles.Clear();
        foreach (var profile in _profiles.ListEnabledForRole(BackendProfileRole.Coding)
                     .Concat(_profiles.ListEnabledForRole(BackendProfileRole.Chat))
                     .GroupBy(profile => profile.Id)
                     .Select(group => group.First()))
        {
            AvailableCodingProfiles.Add(BackendProfileItemViewModel.FromProfile(profile));
        }

        SelectedCodingProfile = !string.IsNullOrWhiteSpace(selectedId)
            ? AvailableCodingProfiles.FirstOrDefault(item => item.Id == selectedId) ?? AvailableCodingProfiles.FirstOrDefault()
            : AvailableCodingProfiles.FirstOrDefault();
    }

    [RelayCommand]
    private void SelectWorkspace()
    {
        var selected = _folderPicker.PickFolder(WorkspacePath);
        if (!string.IsNullOrWhiteSpace(selected))
        {
            WorkspacePath = selected;
            RefreshWorkspaceExplorer();
            Status = $"已选择工作区：{selected}";
        }
    }

    [RelayCommand]
    private void RefreshWorkspaceExplorer()
    {
        try
        {
            var normalized = _workspace.NormalizeWorkspacePath(WorkspacePath);
            WorkspaceFiles.Clear();
            WorkspaceSearchResults.Clear();
            foreach (var relativePath in _workspace.ListSafeFileTree(normalized, 500))
            {
                WorkspaceFiles.Add(new CodingWorkspaceFileItemViewModel(relativePath));
            }

            WorkspacePreviewText = $"已加载 {WorkspaceFiles.Count} 个安全文本候选文件。双击/点击文件可预览，敏感文件不会显示。";
            Status = $"项目树已刷新：{WorkspaceFiles.Count} 个文件。";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            WorkspaceFiles.Clear();
            WorkspacePreviewText = ex.Message;
            Status = ex.Message;
        }
    }

    [RelayCommand]
    private void ReadWorkspaceFile(CodingWorkspaceFileItemViewModel? file)
    {
        if (file is null)
        {
            return;
        }

        SelectedWorkspaceFile = file;
        var preview = _workspace.ReadTextFile(WorkspacePath, file.RelativePath);
        if (preview is null)
        {
            WorkspacePreviewText = $"无法预览：{file.RelativePath}。可能是非文本、大文件、敏感路径或工作区外路径。";
            return;
        }

        WorkspacePreviewText = $"--- {preview.RelativePath} sha256={preview.Sha256} bytes={preview.ByteLength} ---{Environment.NewLine}{preview.Content}";
        Status = $"已预览：{preview.RelativePath}";
    }

    [RelayCommand]
    private void SearchWorkspace()
    {
        try
        {
            WorkspaceSearchResults.Clear();
            foreach (var result in _workspace.SearchText(WorkspacePath, WorkspaceSearchQuery))
            {
                WorkspaceSearchResults.Add(CodingWorkspaceSearchResultViewModel.FromRecord(result));
            }

            WorkspacePreviewText = WorkspaceSearchResults.Count == 0
                ? "没有搜索结果。"
                : $"找到 {WorkspaceSearchResults.Count} 条匹配；选择结果可预览对应文件。";
            Status = $"搜索完成：{WorkspaceSearchResults.Count} 条结果。";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            WorkspacePreviewText = ex.Message;
            Status = ex.Message;
        }
    }

    [RelayCommand]
    private void OpenSearchResult(CodingWorkspaceSearchResultViewModel? result)
    {
        if (result is null)
        {
            return;
        }

        SelectedSearchResult = result;
        ReadWorkspaceFile(new CodingWorkspaceFileItemViewModel(result.RelativePath));
    }

    [RelayCommand(CanExecute = nameof(CanCopyWorkspacePreview))]
    private void CopyWorkspacePreview()
    {
        if (!string.IsNullOrWhiteSpace(WorkspacePreviewText))
        {
            Clipboard.SetText(WorkspacePreviewText);
            Status = "项目预览内容已复制。";
        }
    }

    [RelayCommand(CanExecute = nameof(CanStartRun))]
    private async Task StartRunAsync(CancellationToken cancellationToken)
    {
        BackendProfile? profile = null;
        string normalizedWorkspace;
        try
        {
            normalizedWorkspace = _workspace.NormalizeWorkspacePath(WorkspacePath);
            RefreshCodingProfiles();
            if (!string.IsNullOrWhiteSpace(SelectedCodingProfile?.Id))
            {
                profile = _profiles.GetById(SelectedCodingProfile.Id);
            }

            profile ??= _profiles.GetFirstEnabledForRole(BackendProfileRole.Coding)
                       ?? _profiles.GetFirstEnabledForRole(BackendProfileRole.Chat);
            if (profile is null)
            {
                Status = "缺少编码或聊天后端配置，请先到设置页添加。";
                return;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            Status = ex.Message;
            return;
        }

        IsRunning = true;
        Events.Clear();
        PlanItems.Clear();
        FileChanges.Clear();
        Commands.Clear();
        SelectedFileChange = null;
        SelectedCommand = null;
        var now = DateTimeOffset.UtcNow;
        var runId = $"coding-{Guid.NewGuid():N}";
        var title = BuildTitle(Goal);
        var model = NormalizeCodingModel(profile.MainlineModel);
        _repository.CreateRun(new CodingRunRecord
        {
            Id = runId,
            WorkspacePath = normalizedWorkspace,
            Title = title,
            Goal = Goal.Trim(),
            Status = CodingRunStatus.Running,
            BackendProfileId = profile.Id,
            Model = model,
            CreatedAt = now,
            UpdatedAt = now
        });
        AddEvent(runId, CodingEventKind.Plan, "会话模式", $"{SelectedSessionMode.Label}：{SelectedSessionMode.Description}", CodingProposalStatus.Applied);
        AddLocalEvent(runId, _repository.AddEvent(new CodingRunEventRecord
        {
            CodingRunId = runId,
            Kind = CodingEventKind.UserMessage,
            Title = "用户目标",
            Detail = Goal.Trim(),
            Status = CodingProposalStatus.Applied,
            CreatedAt = now
        }));

        try
        {
            Status = "正在读取工作区摘要...";
            var snapshot = _workspace.BuildSnapshot(normalizedWorkspace);
            AddEvent(runId, CodingEventKind.FileRead, "工作区摘要", $"已提供 {snapshot.Files.Count} 个文件，跳过 {snapshot.SkippedFiles.Count} 个文件。", CodingProposalStatus.Applied);

            Status = "正在生成编码方案...";
            var response = await _client.GenerateProposalAsync(WithMainlineModel(profile, model), new CodingAgentRequest
            {
                Goal = Goal.Trim(),
                WorkspacePath = normalizedWorkspace,
                SessionMode = SelectedSessionMode.Value,
                WorkspaceSnapshot = snapshot,
                MaxFileChanges = MaxFileChanges,
                MaxCommands = MaxCommands
            }, cancellationToken).ConfigureAwait(true);

            if (!string.IsNullOrWhiteSpace(response.Message))
            {
                AddEvent(runId, CodingEventKind.AssistantMessage, "模型回复", response.Message, CodingProposalStatus.Applied, response.RawJson);
            }

            if (response.Plan.Count > 0)
            {
                foreach (var item in response.Plan.Select((text, index) => new CodingPlanItemViewModel(index + 1, text, "pending")))
                {
                    PlanItems.Add(item);
                }

                AddEvent(runId, CodingEventKind.Plan, "执行计划", string.Join(Environment.NewLine, response.Plan.Select((item, index) => $"{index + 1}. {item}")), CodingProposalStatus.Applied);
            }

            if (!string.IsNullOrWhiteSpace(response.Error))
            {
                AddEvent(runId, CodingEventKind.Error, "请求失败", response.Error, CodingProposalStatus.Failed, response.RawJson);
                _repository.UpdateRunStatus(runId, CodingRunStatus.Failed, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
                Status = response.Error;
                RefreshRuns();
                return;
            }

            foreach (var draft in response.FileChanges.Take(MaxFileChanges))
            {
                var validation = _fileChanges.Validate(normalizedWorkspace, draft);
                var eventId = AddEvent(
                    runId,
                    validation.IsAllowed ? CodingEventKind.PatchProposed : CodingEventKind.Error,
                    validation.IsAllowed ? $"文件变更：{draft.RelativePath}" : $"文件变更被拒绝：{draft.RelativePath}",
                    validation.Message,
                    validation.IsAllowed ? CodingProposalStatus.Pending : CodingProposalStatus.Failed);
                if (!validation.IsAllowed)
                {
                    continue;
                }

                var proposal = new CodingFileChangeProposalRecord
                {
                    CodingRunId = runId,
                    EventId = eventId,
                    RelativePath = draft.RelativePath,
                    ChangeType = draft.ChangeType,
                    OriginalSha256 = draft.OriginalSha256,
                    ProposedContent = draft.ProposedContent,
                    DiffText = validation.DiffText,
                    Summary = string.IsNullOrWhiteSpace(draft.Summary) ? draft.RelativePath : draft.Summary,
                    Status = CodingProposalStatus.Pending,
                    CreatedAt = DateTimeOffset.UtcNow
                };
                var proposalId = _repository.AddFileChangeProposal(proposal);
                var viewModel = CodingFileChangeProposalViewModel.FromRecord(new CodingFileChangeProposalRecord
                {
                    Id = proposalId,
                    CodingRunId = proposal.CodingRunId,
                    EventId = proposal.EventId,
                    RelativePath = proposal.RelativePath,
                    ChangeType = proposal.ChangeType,
                    OriginalSha256 = proposal.OriginalSha256,
                    ProposedContent = proposal.ProposedContent,
                    DiffText = proposal.DiffText,
                    Summary = proposal.Summary,
                    Status = proposal.Status,
                    CreatedAt = proposal.CreatedAt
                });
                FileChanges.Add(viewModel);
                SelectedFileChange ??= viewModel;
            }

            foreach (var command in response.Commands.Take(MaxCommands))
            {
                var safety = _commandSafety.Classify(command.Command);
                var eventId = AddEvent(
                    runId,
                    CodingEventKind.CommandProposed,
                    $"命令建议：{command.Command}",
                    safety.Message,
                    safety.IsAllowed ? CodingProposalStatus.Pending : CodingProposalStatus.Rejected);
                var record = new CodingCommandProposalRecord
                {
                    CodingRunId = runId,
                    EventId = eventId,
                    Command = command.Command,
                    WorkingDirectory = string.IsNullOrWhiteSpace(command.WorkingDirectory) ? "." : command.WorkingDirectory,
                    Reason = command.Reason,
                    RiskLevel = safety.RiskLevel,
                    Status = safety.IsAllowed ? CodingProposalStatus.Pending : CodingProposalStatus.Rejected,
                    CreatedAt = DateTimeOffset.UtcNow
                };
                var proposalId = _repository.AddCommandProposal(record);
                var viewModel = CodingCommandProposalViewModel.FromRecord(new CodingCommandProposalRecord
                {
                    Id = proposalId,
                    CodingRunId = record.CodingRunId,
                    EventId = record.EventId,
                    Command = record.Command,
                    WorkingDirectory = record.WorkingDirectory,
                    Reason = record.Reason,
                    RiskLevel = record.RiskLevel,
                    Status = record.Status,
                    CreatedAt = record.CreatedAt
                });
                Commands.Add(viewModel);
                SelectedCommand ??= viewModel;
            }

            var waiting = FileChanges.Any(item => item.Status == CodingProposalStatus.Pending)
                          || Commands.Any(item => item.Status == CodingProposalStatus.Pending);
            foreach (var item in PlanItems)
            {
                item.Status = waiting ? "等待审批" : "已完成";
            }

            _repository.UpdateRunStatus(runId, waiting ? CodingRunStatus.WaitingForApproval : CodingRunStatus.Completed, DateTimeOffset.UtcNow, waiting ? null : DateTimeOffset.UtcNow);
            if (!waiting)
            {
                AddEvent(runId, CodingEventKind.AssistantMessage, "运行完成", "没有待审批的文件变更或命令。", CodingProposalStatus.Applied);
            }

            Status = waiting ? "已生成方案，等待你审批文件变更或命令。" : "分析完成，未生成可执行变更。";
            RefreshRuns();
        }
        catch (OperationCanceledException)
        {
            _repository.UpdateRunStatus(runId, CodingRunStatus.Canceled, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
            Status = "已取消。";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            AddEvent(runId, CodingEventKind.Error, "运行失败", ex.Message, CodingProposalStatus.Failed);
            _repository.UpdateRunStatus(runId, CodingRunStatus.Failed, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
            Status = $"运行失败：{ex.Message}";
        }
        finally
        {
            IsRunning = false;
        }
    }

    [RelayCommand]
    private void RefreshRuns()
    {
        Runs.Clear();
        foreach (var run in _repository.ListRecentRuns())
        {
            Runs.Add(CodingRunItemViewModel.FromRecord(run));
        }
    }

    [RelayCommand]
    private void LoadRun(CodingRunItemViewModel? run)
    {
        if (run is null)
        {
            return;
        }

        SelectedRun = run;
        WorkspacePath = run.WorkspacePath;
        Goal = run.Goal;
        Events.Clear();
        PlanItems.Clear();
        FileChanges.Clear();
        Commands.Clear();
        foreach (var item in _repository.ListEvents(run.Id))
        {
            var eventViewModel = CodingRunEventViewModel.FromRecord(item);
            Events.Add(eventViewModel);
            if (item.Kind == CodingEventKind.Plan && item.Title == "执行计划" && !string.IsNullOrWhiteSpace(item.Detail))
            {
                foreach (var plan in CodingPlanItemViewModel.FromPlanText(item.Detail))
                {
                    PlanItems.Add(plan);
                }
            }
        }

        foreach (var item in _repository.ListFileChangeProposals(run.Id))
        {
            FileChanges.Add(CodingFileChangeProposalViewModel.FromRecord(item));
        }

        foreach (var item in _repository.ListCommandProposals(run.Id))
        {
            Commands.Add(CodingCommandProposalViewModel.FromRecord(item));
        }

        SelectedFileChange = FileChanges.FirstOrDefault();
        SelectedCommand = Commands.FirstOrDefault();
        Status = $"已加载：{run.Title}";
    }

    [RelayCommand(CanExecute = nameof(CanApproveFileChange))]
    private void ApproveFileChange()
    {
        if (SelectedFileChange is null)
        {
            return;
        }

        var record = SelectedFileChange.ToRecord();
        var result = _fileChanges.Apply(GetRunWorkspacePath(record.CodingRunId), record);
        var status = result.IsSuccess ? CodingProposalStatus.Applied : CodingProposalStatus.Failed;
        _repository.UpdateFileChangeStatus(record.Id, status, result.IsSuccess ? DateTimeOffset.UtcNow : null);
        SelectedFileChange.Status = status;
        AddEvent(record.CodingRunId, result.IsSuccess ? CodingEventKind.PatchApplied : CodingEventKind.Error, SelectedFileChange.RelativePath, result.Message, status);
        CompleteRunIfReady(record.CodingRunId);
        RefreshApprovalCommandStates();
        Status = result.Message;
    }

    [RelayCommand(CanExecute = nameof(CanRejectFileChange))]
    private void RejectFileChange()
    {
        if (SelectedFileChange is null)
        {
            return;
        }

        var record = SelectedFileChange.ToRecord();
        _repository.UpdateFileChangeStatus(record.Id, CodingProposalStatus.Rejected);
        SelectedFileChange.Status = CodingProposalStatus.Rejected;
        AddEvent(record.CodingRunId, CodingEventKind.ApprovalRequired, $"拒绝文件变更：{record.RelativePath}", "用户拒绝了该文件变更。", CodingProposalStatus.Rejected);
        CompleteRunIfReady(record.CodingRunId);
        RefreshApprovalCommandStates();
        Status = "已拒绝文件变更。";
    }

    [RelayCommand(CanExecute = nameof(CanCopyDiff))]
    private void CopyDiff()
    {
        if (!string.IsNullOrWhiteSpace(SelectedFileChange?.DiffText))
        {
            Clipboard.SetText(SelectedFileChange.DiffText);
            Status = "Diff 已复制。";
        }
    }

    [RelayCommand(CanExecute = nameof(CanCopyCommand))]
    private void CopyCommand()
    {
        if (!string.IsNullOrWhiteSpace(SelectedCommand?.Command))
        {
            Clipboard.SetText(SelectedCommand.Command);
            Status = "命令已复制。";
        }
    }

    [RelayCommand(CanExecute = nameof(CanApproveCommand))]
    private async Task ApproveCommandAsync(CancellationToken cancellationToken)
    {
        if (SelectedCommand is null)
        {
            return;
        }

        var record = SelectedCommand.ToRecord();
        var safety = _commandSafety.Classify(record.Command);
        if (!safety.IsAllowed)
        {
            _repository.UpdateCommandResult(record.Id, CodingProposalStatus.Rejected, "", safety.Message, null, DateTimeOffset.UtcNow);
            SelectedCommand.Status = CodingProposalStatus.Rejected;
            SelectedCommand.Stderr = safety.Message;
            AddEvent(record.CodingRunId, CodingEventKind.Error, $"命令被阻止：{record.Command}", safety.Message, CodingProposalStatus.Rejected);
            CompleteRunIfReady(record.CodingRunId);
            RefreshApprovalCommandStates();
            Status = safety.Message;
            return;
        }

        SelectedCommand.Status = CodingProposalStatus.Approved;
        RefreshApprovalCommandStates();
        AddEvent(record.CodingRunId, CodingEventKind.CommandStarted, record.Command, $"用户已批准，开始运行。工作目录：{record.WorkingDirectory}", CodingProposalStatus.Approved);
        Status = $"正在运行：{record.Command}";
        var result = await _processRunner.RunAsync(GetRunWorkspacePath(record.CodingRunId), record.WorkingDirectory, record.Command, TimeSpan.FromMinutes(5), cancellationToken).ConfigureAwait(true);
        var status = result.ExitCode == 0 ? CodingProposalStatus.Applied : CodingProposalStatus.Failed;
        _repository.UpdateCommandResult(record.Id, status, result.Stdout, result.Stderr, result.ExitCode, DateTimeOffset.UtcNow);
        SelectedCommand.Status = status;
        SelectedCommand.Stdout = result.Stdout;
        SelectedCommand.Stderr = result.Stderr;
        SelectedCommand.ExitCode = result.ExitCode;
        AddEvent(record.CodingRunId, CodingEventKind.CommandCompleted, record.Command, $"ExitCode: {result.ExitCode?.ToString() ?? "n/a"}", status);
        CompleteRunIfReady(record.CodingRunId);
        RefreshApprovalCommandStates();
        Status = result.ExitCode == 0 ? "命令运行成功。" : "命令运行失败，请查看输出。";
    }

    [RelayCommand(CanExecute = nameof(CanRejectCommand))]
    private void RejectCommand()
    {
        if (SelectedCommand is null)
        {
            return;
        }

        var record = SelectedCommand.ToRecord();
        var stderr = record.Stderr ?? "用户拒绝运行该命令。";
        _repository.UpdateCommandResult(record.Id, CodingProposalStatus.Rejected, record.Stdout ?? "", stderr, null, DateTimeOffset.UtcNow);
        SelectedCommand.Status = CodingProposalStatus.Rejected;
        SelectedCommand.Stderr = stderr;
        AddEvent(record.CodingRunId, CodingEventKind.ApprovalRequired, $"拒绝命令：{record.Command}", stderr, CodingProposalStatus.Rejected);
        CompleteRunIfReady(record.CodingRunId);
        RefreshApprovalCommandStates();
        Status = "已拒绝命令。";
    }

    private bool CanStartRun() => !IsRunning && !string.IsNullOrWhiteSpace(WorkspacePath) && !string.IsNullOrWhiteSpace(Goal);

    private bool CanCopyWorkspacePreview() => !string.IsNullOrWhiteSpace(WorkspacePreviewText);

    private bool CanApproveFileChange() => SelectedFileChange?.Status == CodingProposalStatus.Pending;

    private bool CanRejectFileChange() => SelectedFileChange?.Status == CodingProposalStatus.Pending;

    private bool CanCopyDiff() => !string.IsNullOrWhiteSpace(SelectedFileChange?.DiffText);

    private bool CanApproveCommand() => SelectedCommand?.Status == CodingProposalStatus.Pending && SelectedCommand.RiskLevel == CodingCommandRiskLevel.Low;

    private bool CanRejectCommand() => SelectedCommand?.Status == CodingProposalStatus.Pending;

    private bool CanCopyCommand() => !string.IsNullOrWhiteSpace(SelectedCommand?.Command);

    private string GetRunWorkspacePath(string runId) => _repository.GetRun(runId)?.WorkspacePath ?? WorkspacePath;

    private void CompleteRunIfReady(string runId)
    {
        if (_repository.TryCompleteRunIfNoPendingProposals(runId, DateTimeOffset.UtcNow))
        {
            AddEvent(runId, CodingEventKind.AssistantMessage, "运行完成", "所有待审批项目已处理。", CodingProposalStatus.Applied);
            RefreshRuns();
        }
    }

    private void RefreshApprovalCommandStates()
    {
        ApproveFileChangeCommand.NotifyCanExecuteChanged();
        RejectFileChangeCommand.NotifyCanExecuteChanged();
        ApproveCommandCommand.NotifyCanExecuteChanged();
        RejectCommandCommand.NotifyCanExecuteChanged();
        CopyCommandCommand.NotifyCanExecuteChanged();
    }

    private long AddEvent(string runId, string kind, string title, string? detail, string status, string? rawJson = null)
    {
        var id = _repository.AddEvent(new CodingRunEventRecord
        {
            CodingRunId = runId,
            Kind = kind,
            Title = title,
            Detail = detail,
            Status = status,
            RawJson = rawJson,
            CreatedAt = DateTimeOffset.UtcNow
        });
        AddLocalEvent(runId, id);
        return id;
    }

    private void AddLocalEvent(string runId, long id)
    {
        var item = _repository.ListEvents(runId).FirstOrDefault(e => e.Id == id);
        if (item is not null)
        {
            Events.Add(CodingRunEventViewModel.FromRecord(item));
        }
    }

    private static BackendProfile WithMainlineModel(BackendProfile profile, string model) => new()
    {
        Id = profile.Id,
        Name = profile.Name,
        BaseUrl = profile.BaseUrl,
        ApiKey = profile.ApiKey,
        Protocol = profile.Protocol,
        MainlineModel = model,
        ImageModel = profile.ImageModel,
        VideoModel = profile.VideoModel,
        Concurrency = profile.Concurrency,
        Priority = profile.Priority,
        IsEnabled = profile.IsEnabled,
        SupportsPromptOptimization = profile.SupportsPromptOptimization,
        SupportsChat = profile.SupportsChat,
        SupportsImageGeneration = profile.SupportsImageGeneration,
        SupportsVideoGeneration = profile.SupportsVideoGeneration,
        SupportsAgent = profile.SupportsAgent,
        FailureCooldownUntil = profile.FailureCooldownUntil
    };

    private static string NormalizeCodingModel(string? model)
    {
        var value = model?.Trim();
        if (string.IsNullOrWhiteSpace(value)
            || string.Equals(value, "auto", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "gpt-5.5", StringComparison.OrdinalIgnoreCase))
        {
            return DefaultCodingModel;
        }

        return value;
    }

    private static string BuildTitle(string goal)
    {
        var trimmed = goal.Trim().Replace("\r", " ").Replace("\n", " ");
        return trimmed.Length <= 32 ? trimmed : trimmed[..32] + "...";
    }
}

public sealed class CodingSessionModeOptionViewModel
{
    public static CodingSessionModeOptionViewModel Default { get; } = new(CodingSessionMode.Cowork, "Cowork", "可搜索、改文件、建议验证命令");

    public CodingSessionModeOptionViewModel(string value, string label, string description)
    {
        Value = value;
        Label = label;
        Description = description;
    }

    public string Value { get; }

    public string Label { get; }

    public string Description { get; }

    public string DisplayText => $"{Label} · {Description}";

    public static IReadOnlyList<CodingSessionModeOptionViewModel> CreateDefaults() => new[]
    {
        new CodingSessionModeOptionViewModel(CodingSessionMode.Chat, "Chat", "只问答，不建议本地工具"),
        new CodingSessionModeOptionViewModel(CodingSessionMode.Clarify, "Clarify", "先澄清需求和风险"),
        Default,
        new CodingSessionModeOptionViewModel(CodingSessionMode.Code, "Code", "专注生成/修改代码"),
        new CodingSessionModeOptionViewModel(CodingSessionMode.AutonomousCodingPipeline, "ACP", "计划-实现-审查流程")
    };
}

public sealed class CodingWorkspaceFileItemViewModel
{
    public CodingWorkspaceFileItemViewModel(string relativePath)
    {
        RelativePath = relativePath;
        FileName = Path.GetFileName(relativePath);
        DirectoryName = Path.GetDirectoryName(relativePath)?.Replace('\\', '/') ?? ".";
    }

    public string RelativePath { get; }

    public string FileName { get; }

    public string DirectoryName { get; }
}

public sealed class CodingWorkspaceSearchResultViewModel
{
    public string RelativePath { get; init; } = "";

    public int LineNumber { get; init; }

    public string LineText { get; init; } = "";

    public string DisplayText => $"{RelativePath}:{LineNumber}";

    public static CodingWorkspaceSearchResultViewModel FromRecord(CodingWorkspaceSearchResult result) => new()
    {
        RelativePath = result.RelativePath,
        LineNumber = result.LineNumber,
        LineText = result.LineText
    };
}

public sealed partial class CodingPlanItemViewModel : ObservableObject
{
    public CodingPlanItemViewModel(int index, string text, string status)
    {
        Index = index;
        Text = text;
        Status = status;
    }

    public int Index { get; }

    public string Text { get; }

    [ObservableProperty]
    private string _status;

    public static IReadOnlyList<CodingPlanItemViewModel> FromPlanText(string detail)
    {
        return detail.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select((line, index) => new CodingPlanItemViewModel(index + 1, StripPlanPrefix(line), "已记录"))
            .ToList();
    }

    private static string StripPlanPrefix(string value)
    {
        var trimmed = value.Trim();
        var dotIndex = trimmed.IndexOf('.');
        if (dotIndex > 0 && int.TryParse(trimmed[..dotIndex], out _))
        {
            return trimmed[(dotIndex + 1)..].Trim();
        }

        return trimmed;
    }
}

public sealed partial class CodingRunItemViewModel : ObservableObject
{
    public string Id { get; init; } = "";
    public string WorkspacePath { get; init; } = "";
    public string Title { get; init; } = "";
    public string Goal { get; init; } = "";
    public string Status { get; init; } = "";
    public string Model { get; init; } = "";
    public string UpdatedAtText { get; init; } = "";

    public static CodingRunItemViewModel FromRecord(CodingRunRecord record) => new()
    {
        Id = record.Id,
        WorkspacePath = record.WorkspacePath,
        Title = record.Title,
        Goal = record.Goal,
        Status = record.Status,
        Model = record.Model,
        UpdatedAtText = record.UpdatedAt.LocalDateTime.ToString("yyyy-MM-dd HH:mm")
    };
}

public sealed class CodingRunEventViewModel
{
    public long Id { get; init; }
    public int Sequence { get; init; }
    public string Kind { get; init; } = "";
    public string Title { get; init; } = "";
    public string? Detail { get; init; }
    public string Status { get; init; } = "";
    public string CreatedAtText { get; init; } = "";

    public static CodingRunEventViewModel FromRecord(CodingRunEventRecord record) => new()
    {
        Id = record.Id,
        Sequence = record.Sequence,
        Kind = record.Kind,
        Title = record.Title,
        Detail = record.Detail,
        Status = record.Status,
        CreatedAtText = record.CreatedAt.LocalDateTime.ToString("HH:mm:ss")
    };
}

public sealed partial class CodingFileChangeProposalViewModel : ObservableObject
{
    public long Id { get; init; }
    public string CodingRunId { get; init; } = "";
    public long? EventId { get; init; }
    public string RelativePath { get; init; } = "";
    public string ChangeType { get; init; } = "";
    public string? OriginalSha256 { get; init; }
    public string ProposedContent { get; init; } = "";
    public string DiffText { get; init; } = "";
    public string Summary { get; init; } = "";

    [ObservableProperty]
    private string _status = "";

    public static CodingFileChangeProposalViewModel FromRecord(CodingFileChangeProposalRecord record) => new()
    {
        Id = record.Id,
        CodingRunId = record.CodingRunId,
        EventId = record.EventId,
        RelativePath = record.RelativePath,
        ChangeType = record.ChangeType,
        OriginalSha256 = record.OriginalSha256,
        ProposedContent = record.ProposedContent,
        DiffText = record.DiffText,
        Summary = record.Summary,
        Status = record.Status
    };

    public CodingFileChangeProposalRecord ToRecord() => new()
    {
        Id = Id,
        CodingRunId = CodingRunId,
        EventId = EventId,
        RelativePath = RelativePath,
        ChangeType = ChangeType,
        OriginalSha256 = OriginalSha256,
        ProposedContent = ProposedContent,
        DiffText = DiffText,
        Summary = Summary,
        Status = Status
    };
}

public sealed partial class CodingCommandProposalViewModel : ObservableObject
{
    public long Id { get; init; }
    public string CodingRunId { get; init; } = "";
    public long? EventId { get; init; }
    public string Command { get; init; } = "";
    public string WorkingDirectory { get; init; } = "";
    public string Reason { get; init; } = "";
    public string RiskLevel { get; init; } = "";

    [ObservableProperty]
    private string _status = "";

    [ObservableProperty]
    private string? _stdout;

    [ObservableProperty]
    private string? _stderr;

    [ObservableProperty]
    private int? _exitCode;

    public string DisplayOutput
    {
        get
        {
            var lines = new List<string>
            {
                $"状态：{Status}",
                $"工作目录：{(string.IsNullOrWhiteSpace(WorkingDirectory) ? "." : WorkingDirectory)}"
            };
            if (!string.IsNullOrWhiteSpace(Reason))
            {
                lines.Add($"原因：{Reason}");
            }

            lines.Add($"ExitCode：{(ExitCode.HasValue ? ExitCode.Value.ToString() : "n/a")}");
            if (!string.IsNullOrWhiteSpace(Stdout))
            {
                lines.Add("");
                lines.Add("[stdout]");
                lines.Add(Stdout!);
            }

            if (!string.IsNullOrWhiteSpace(Stderr))
            {
                lines.Add("");
                lines.Add("[stderr]");
                lines.Add(Stderr!);
            }

            return string.Join(Environment.NewLine, lines);
        }
    }

    partial void OnStatusChanged(string value) => OnPropertyChanged(nameof(DisplayOutput));

    partial void OnStdoutChanged(string? value) => OnPropertyChanged(nameof(DisplayOutput));

    partial void OnStderrChanged(string? value) => OnPropertyChanged(nameof(DisplayOutput));

    partial void OnExitCodeChanged(int? value) => OnPropertyChanged(nameof(DisplayOutput));

    public static CodingCommandProposalViewModel FromRecord(CodingCommandProposalRecord record) => new()
    {
        Id = record.Id,
        CodingRunId = record.CodingRunId,
        EventId = record.EventId,
        Command = record.Command,
        WorkingDirectory = record.WorkingDirectory,
        Reason = record.Reason,
        RiskLevel = record.RiskLevel,
        Status = record.Status,
        Stdout = record.Stdout,
        Stderr = record.Stderr,
        ExitCode = record.ExitCode
    };

    public CodingCommandProposalRecord ToRecord() => new()
    {
        Id = Id,
        CodingRunId = CodingRunId,
        EventId = EventId,
        Command = Command,
        WorkingDirectory = WorkingDirectory,
        Reason = Reason,
        RiskLevel = RiskLevel,
        Status = Status,
        Stdout = Stdout,
        Stderr = Stderr,
        ExitCode = ExitCode
    };
}
