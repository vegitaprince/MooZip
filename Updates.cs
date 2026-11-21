using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace MooZip;

public partial class MainWindow
{
    sealed record UpdateSettings(bool ApplyOnExit = false, DateTimeOffset? Checked = null);
    readonly string updateState = UpdateHandoff.StateDirectory(UpdateHandoff.AppDirectory);
    readonly CancellationTokenSource updateLifetime = new();
    UpdateSettings updateSettings = new();
    UpdateRelease? availableUpdate;
    sealed record StagedUpdate(string Directory, string ManifestHash, Version Version);
    StagedUpdate? stagedUpdate;
    string updateMessage = "최신 버전을 확인하세요.";
    int progressShown = -1;
    bool updateBusy;
    bool updateRestart;
    bool sessionEnding;
    bool menuRegistrationPending;
    UpdateWindow? updateWindow;

    internal void StartUpdates()
    {
        updateSettings = ReadUpdateSettings();
        bool showPreviousFailure = false;
        string errorPath = Path.Combine(updateState, "update-error.txt");
        try
        {
            if (File.Exists(errorPath))
            {
                updateMessage = "이전 업데이트를 적용하지 못했습니다. " + File.ReadAllText(errorPath);
                File.Delete(errorPath);
                UpdateButton.Content = "업데이트 다시 확인…";
                showPreviousFailure = true;
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        _ = Task.Run(() =>
        {
            try { UpdateHandoff.Cleanup(updateState); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        });
        Closing += CloseForUpdate;
        Closed += (_, _) =>
        {
            updateLifetime.Cancel();
            CleanupStaged();
        };
        if (showPreviousFailure) return;
        Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, async () =>
        {
            var now = DateTimeOffset.UtcNow;
            var last = updateSettings.Checked;
            if (last == null || last > now || now - last >= TimeSpan.FromHours(12))
                await CheckUpdatesAsync();
        });
    }

    internal void EndSession() => sessionEnding = true;

    UpdateSettings ReadUpdateSettings()
    {
        try
        {
            string path = Path.Combine(updateState, "settings.json");
            return File.Exists(path) ? JsonSerializer.Deserialize<UpdateSettings>(File.ReadAllText(path)) ?? new() : new();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        { return new(); }
    }

    void SaveUpdateSettings()
    {
        Directory.CreateDirectory(updateState);
        string path = Path.Combine(updateState, "settings.json");
        string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temp, JsonSerializer.Serialize(updateSettings));
            File.Move(temp, path, true);
        }
        finally { File.Delete(temp); }
    }

    void ShowUpdates(object sender, RoutedEventArgs e)
    {
        if (updateWindow != null) { updateWindow.Activate(); return; }
        updateWindow = new UpdateWindow(this, updateSettings.ApplyOnExit);
        updateWindow.Check.Click += async (_, _) => await CheckUpdatesAsync();
        updateWindow.Apply.Click += async (_, _) => await UpdateNowAsync();
        updateWindow.Automatic.Click += async (_, _) =>
        {
            var previous = updateSettings;
            updateSettings = updateSettings with { ApplyOnExit = updateWindow.Automatic.IsChecked == true };
            try { SaveUpdateSettings(); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                updateSettings = previous;
                updateWindow.Automatic.IsChecked = previous.ApplyOnExit;
                updateMessage = "업데이트 설정을 저장하지 못했습니다. " + error.Message;
                RefreshUpdateWindow();
                return;
            }
            if (updateSettings.ApplyOnExit && availableUpdate != null && stagedUpdate == null)
                await DownloadUpdateAsync();
            RefreshUpdateWindow();
        };
        updateWindow.Closed += (_, _) => updateWindow = null;
        RefreshUpdateWindow();
        updateWindow.Show();
    }

    void RefreshUpdateWindow()
    {
        if (availableUpdate != null) UpdateButton.Content = $"업데이트 {availableUpdate.Version}…";
        if (updateWindow == null) return;
        updateWindow.Message.Text = updateMessage;
        updateWindow.Check.IsEnabled = !updateBusy;
        updateWindow.Apply.IsEnabled = !updateBusy && availableUpdate != null && UpdateHandoff.IsPortable;
        updateWindow.Automatic.IsEnabled = !updateBusy && UpdateHandoff.IsPortable;
        updateWindow.Apply.Content = stagedUpdate == null ? "다운로드 및 다시 시작" : "적용 및 다시 시작";
    }

    async Task CheckUpdatesAsync()
    {
        if (updateBusy || updateLifetime.IsCancellationRequested) return;
        updateBusy = true;
        updateMessage = "GitHub에서 새 버전을 확인하는 중…";
        RefreshUpdateWindow();
        try
        {
            using var client = new UpdateClient();
            var release = await client.CheckAsync(updateLifetime.Token);
            if (release?.Version != availableUpdate?.Version || release?.Sha256 != availableUpdate?.Sha256)
                CleanupStaged();
            availableUpdate = release;
            if (release == null)
            {
                updateMessage = "최신 버전을 사용 중입니다.";
                UpdateButton.Content = "업데이트…";
                updateSettings = ReadUpdateSettings() with { Checked = DateTimeOffset.UtcNow };
                try { SaveUpdateSettings(); }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
            }
            else if (!UpdateHandoff.IsPortable)
                updateMessage = $"{release.Version} 버전이 있습니다. GitHub에서 최신 포터블 ZIP을 받아 새 폴더에 풀어 주세요.";
            else
                updateMessage = $"{release.Version} 버전을 받을 수 있습니다.";
        }
        catch (Exception error)
        {
            if (!updateLifetime.IsCancellationRequested)
            {
                updateMessage = error is OperationCanceledException ? "업데이트 확인 시간이 초과되었습니다." : "업데이트 확인에 실패했습니다. " + error.Message;
            }
        }
        finally { updateBusy = false; RefreshUpdateWindow(); }

        if (availableUpdate != null && stagedUpdate == null && UpdateHandoff.IsPortable &&
            ReadUpdateSettings().ApplyOnExit && !updateLifetime.IsCancellationRequested)
            await DownloadUpdateAsync();
    }

    async Task<bool> DownloadUpdateAsync()
    {
        if (stagedUpdate != null) return true;
        if (availableUpdate is not { } release || updateBusy || updateLifetime.IsCancellationRequested) return false;
        updateBusy = true;
        progressShown = -1;
        updateMessage = $"{release.Version} 포터블 ZIP을 받는 중…";
        RefreshUpdateWindow();
        string? work = null;
        try
        {
            work = UpdateHandoff.NewDownload();
            string archive = Path.Combine(work, "update.zip");
            string payload = Path.Combine(work, "payload");
            var progress = new Progress<double>(value =>
            {
                int step = (int)(value * 20) * 5;
                if (!updateBusy || step <= progressShown) return;
                progressShown = step;
                updateMessage = step == 100 ? "다운로드한 ZIP을 검증하는 중…" : $"업데이트 다운로드 중 · {step}%";
                RefreshUpdateWindow();
            });
            string manifestHash = await Task.Run(async () =>
            {
                using var client = new UpdateClient();
                await client.DownloadAsync(release, archive, progress, updateLifetime.Token);
                string hash = await PortablePackage.StageAsync(archive, payload, release.Version, updateLifetime.Token);
                File.Delete(archive);
                return hash;
            });
            updateLifetime.Token.ThrowIfCancellationRequested();
            stagedUpdate = new(work, manifestHash, release.Version);
            work = null;
            updateMessage = ReadUpdateSettings().ApplyOnExit
                ? "업데이트가 준비되었습니다. 같은 폴더에서 열린 MooZip 창을 모두 닫으면 적용됩니다."
                : "업데이트가 준비되었습니다. 적용 및 다시 시작을 누르세요.";
            return true;
        }
        catch (Exception error)
        {
            if (!updateLifetime.IsCancellationRequested)
                updateMessage = error is OperationCanceledException ? "업데이트 다운로드 시간이 초과되었습니다." : "업데이트 준비에 실패했습니다. " + error.Message;
            return false;
        }
        finally
        {
            if (work != null) PortablePackage.TryClean(work);
            updateBusy = false;
            RefreshUpdateWindow();
        }
    }

    async Task UpdateNowAsync()
    {
        if (job != null || menuRegistrationPending)
        {
            updateMessage = "실행 중인 작업이 끝난 뒤 업데이트하세요.";
            RefreshUpdateWindow();
            return;
        }
        if (!await DownloadUpdateAsync()) return;
        if (job != null || menuRegistrationPending)
        {
            updateMessage = "실행 중인 작업이 끝난 뒤 적용 및 다시 시작을 누르세요.";
            RefreshUpdateWindow();
            return;
        }
        updateRestart = true;
        Close();
    }

    void CloseForUpdate(object? sender, CancelEventArgs e)
    {
        if (e.Cancel || sessionEnding || menuRegistrationPending || stagedUpdate is not { } work) return;
        if (!updateRestart && !ReadUpdateSettings().ApplyOnExit) return;
        try
        {
            PortablePackage.VerifyStage(Path.Combine(work.Directory, "payload"), work.Version, work.ManifestHash);
            UpdateHandoff.Start(work.Directory, updateRestart);
            stagedUpdate = null;
        }
        catch (Exception error)
        {
            CleanupStaged();
            MessageBox.Show(this, error.Message, "MooZip · 업데이트 실행 실패", MessageBoxButton.OK, MessageBoxImage.Warning);
            if (updateRestart) e.Cancel = true;
        }
        finally { updateRestart = false; }
    }

    void CleanupStaged()
    {
        if (stagedUpdate is not { } work) return;
        stagedUpdate = null;
        _ = Task.Run(() => PortablePackage.TryClean(work.Directory));
    }
}
