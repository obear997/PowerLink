using System.Diagnostics;
using System.Reflection;
using Velopack;
using Velopack.Sources;

namespace PowerLink.App.Services;

public sealed class UpdateService
{
    // Gitea 仓库地址（Velopack 的 GiteaSource 会据此调用 /api/v1 接口）。
    private const string RepoUrl = "https://gitea.obear.top/obear/PowerLink";

    private readonly UpdateManager _manager;

    public UpdateService()
    {
        var source = new GiteaSource(RepoUrl, accessToken: null, prerelease: false);
        _manager = new UpdateManager(source);
    }

    // True for both flavors that ship from CI (Setup.exe install or Velopack
    // Portable.zip extracted). False for ad-hoc builds run out of bin/Debug
    // or any layout missing Update.exe in the parent folder.
    public bool IsVelopackInstalled => _manager.IsInstalled;

    public string CurrentVersionText
    {
        get
        {
            var v = _manager.CurrentVersion;
            if (v != null) return v.ToString();
            return Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";
        }
    }

    public string ReleasesPageUrl => $"{RepoUrl}/releases/latest";

    // No CancellationToken: Velopack's CheckForUpdatesAsync / DownloadUpdatesAsync
    // don't accept one, so we'd be lying about cancellation.
    public async Task<UpdateCheckResult> CheckAsync()
    {
        try
        {
            var info = await _manager.CheckForUpdatesAsync().ConfigureAwait(false);
            if (info is null)
                return new UpdateCheckResult(UpdateAvailability.UpToDate, null, null);

            var version = info.TargetFullRelease.Version.ToString();
            return new UpdateCheckResult(UpdateAvailability.Available, version, info);
        }
        catch (Exception ex)
        {
            return new UpdateCheckResult(UpdateAvailability.Failed, null, null, ex.Message);
        }
    }

    public async Task ApplyAsync(UpdateInfo info)
    {
        ArgumentNullException.ThrowIfNull(info);
        if (!IsVelopackInstalled)
            throw new InvalidOperationException(
                "ApplyAsync 需要应用运行在 Velopack 管理的安装目录中（Setup.exe 或 Portable.zip）。"
                + "bin/ 下的开发构建无法自动更新。");

        await _manager.DownloadUpdatesAsync(info).ConfigureAwait(false);
        _manager.ApplyUpdatesAndRestart(info);
    }

    public void OpenReleasesPage()
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = ReleasesPageUrl,
            UseShellExecute = true,
        });
    }
}

public enum UpdateAvailability
{
    UpToDate,
    Available,
    Failed,
}

public sealed record UpdateCheckResult(
    UpdateAvailability Availability,
    string? AvailableVersion,
    UpdateInfo? VelopackInfo,
    string? ErrorMessage = null);
