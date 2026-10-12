#if ANDROID
using System.IO;
using Android.App;
using Android.Content.PM;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Storage;
using PKForge.App.Services;
using PKForge.Domain;

[assembly: UsesPermission(Android.Manifest.Permission.RequestInstallPackages)]
[assembly: UsesPermission(Android.Manifest.Permission.UpdatePackagesWithoutUserAction)]

namespace PKForge.App.Platforms.Android
{
    public sealed class AndroidUpdateInstaller : IUpdateInstaller
    {
        private static readonly HttpClient Http = new(new SocketsHttpHandler
        {
            ConnectTimeout = TimeSpan.FromSeconds(15),
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        })
        { Timeout = TimeSpan.FromMinutes(10) };

        public bool CanInstallUpdates => CanRequestInstalls();

#pragma warning disable CA1416 // Every guarded call checks Build.VERSION explicitly.
        public void OpenInstallPermissionSettings()
        {
            var context = Platform.CurrentActivity ?? Platform.AppContext;
            if (global::Android.OS.Build.VERSION.SdkInt < global::Android.OS.BuildVersionCodes.O) return;
            var package = global::Android.Net.Uri.FromParts("package", context.PackageName, null);
            var intent = new global::Android.Content.Intent(global::Android.Provider.Settings.ActionManageUnknownAppSources, package);
            if (context != Platform.CurrentActivity)
                intent.AddFlags(global::Android.Content.ActivityFlags.NewTask);
            context.StartActivity(intent);
        }

        public async Task<AppUpdateInstallResult> DownloadAndInstallAsync(
            AvailableAppUpdate update,
            Action<long, long> onProgress,
            CancellationToken cancellationToken)
        {
            if (!CanRequestInstalls())
            {
                return AppUpdateInstallResult.InstallPermissionRequired;
            }

            var directory = Path.Combine(FileSystem.CacheDirectory, "updates");
            Directory.CreateDirectory(directory);
            foreach (var old in Directory.EnumerateFiles(directory, "pkforge-*.apk*"))
                File.Delete(old);

            using var request = new HttpRequestMessage(HttpMethod.Get, update.DownloadUrl);
            request.Headers.UserAgent.ParseAdd($"PKForge/{AppInfo.Current.VersionString}");
            using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            var total = response.Content.Headers.ContentLength ?? update.SizeBytes;
            var temporary = Path.Combine(directory, $"pkforge-{update.Version}.apk.part");
            var destination = Path.Combine(directory, $"pkforge-{update.Version}.apk");

            await using var download = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            await using (var output = File.Create(temporary))
            {
                var buffer = new byte[1024 * 128];
                long received = 0;
                onProgress(0, total);
                while (true)
                {
                    var read = await download.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                    if (read == 0) break;
                    await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                    received += read;
                    onProgress(received, total);
                }

                if (received < 1024 * 1024)
                    throw new InvalidDataException("The downloaded update was not a complete APK.");
                if (total > 0 && received != total)
                    throw new InvalidDataException($"The update stopped at {received} of {total} bytes.");
            }

            File.Move(temporary, destination, overwrite: true);
            StartAndroidInstall(destination);
            return AppUpdateInstallResult.InstallerOpened;
        }

        public static bool CanRequestInstalls() =>
            global::Android.OS.Build.VERSION.SdkInt < global::Android.OS.BuildVersionCodes.O
            || (Platform.CurrentActivity?.PackageManager ?? Platform.AppContext.PackageManager)?.CanRequestPackageInstalls() == true;

        /// <summary>
        /// Installs the downloaded APK through a package-installer session. Same-package,
        /// same-signature updates may install silently once the user has granted PKForge the
        /// install-unknown-apps switch, but Xiaomi's HyperOS/MIUI refuse a silent install from an
        /// installer they have not allowlisted ("INSTALL_FAILED_ABORTED: Permission denied"), so
        /// there, and on a retry after any failed silent attempt, Android shows its own prompt.
        /// </summary>
        public static void StartAndroidInstall(string apkPath, bool askUser = false)
        {
            var context = Platform.CurrentActivity ?? Platform.AppContext;
            var installer = context.PackageManager!.PackageInstaller!;
            var parameters = new global::Android.Content.PM.PackageInstaller.SessionParams(global::Android.Content.PM.PackageInstallMode.FullInstall);
            parameters.SetAppPackageName(context.PackageName!);
            askUser |= IsXiaomi();
            if (global::Android.OS.Build.VERSION.SdkInt >= global::Android.OS.BuildVersionCodes.S)
                parameters.SetRequireUserAction((int)(askUser
                    ? global::Android.Content.PM.PackageInstallUserAction.Required
                    : global::Android.Content.PM.PackageInstallUserAction.NotRequired));
            global::Android.Util.Log.Info("PKForgeUpdate", $"Creating install session for {context.PackageName}, APK {apkPath}");
            var sessionId = installer.CreateSession(parameters);
            using var session = installer.OpenSession(sessionId);
            using (var input = File.OpenRead(apkPath))
            using (var output = session.OpenWrite("pkforge-update", 0, input.Length))
            {
                input.CopyTo(output);
                session.Fsync(output);
            }

            var callback = new global::Android.Content.Intent(context, Java.Lang.Class.FromType(typeof(UpdateInstallReceiver)))
                .SetAction(UpdateInstallReceiver.ActionName)!
                .PutExtra(UpdateInstallReceiver.ExtraApkPath, apkPath)!
                .PutExtra(UpdateInstallReceiver.ExtraAskedUser, askUser);
            var flags = global::Android.App.PendingIntentFlags.UpdateCurrent;
            if (global::Android.OS.Build.VERSION.SdkInt >= global::Android.OS.BuildVersionCodes.S)
                flags |= global::Android.App.PendingIntentFlags.Mutable;
            var pending = global::Android.App.PendingIntent.GetBroadcast(
                context,
                UpdateInstallReceiver.RequestCode,
                callback,
                flags);
            var sender = pending?.IntentSender ?? throw new InvalidOperationException("Android did not create the update callback.");
            session.Commit(sender);
            global::Android.Util.Log.Info("PKForgeUpdate", $"Committed install session {sessionId} (prompt: {askUser})");
        }

        private static bool IsXiaomi() =>
            global::Android.OS.Build.Manufacturer is { } maker
            && (maker.Equals("Xiaomi", StringComparison.OrdinalIgnoreCase)
                || maker.Equals("POCO", StringComparison.OrdinalIgnoreCase)
                || maker.Equals("Redmi", StringComparison.OrdinalIgnoreCase));
#pragma warning restore CA1416
    }
}

namespace PKForge.App.Services
{
    [global::Android.Content.BroadcastReceiver(Enabled = true, Exported = false)]
    public sealed class UpdateInstallReceiver : global::Android.Content.BroadcastReceiver
    {
        public const string ActionName = "org.pkforge.app.UPDATE_INSTALL_RESULT";
        public const string ExtraApkPath = "org.pkforge.app.UPDATE_APK";
        public const string ExtraAskedUser = "org.pkforge.app.UPDATE_ASKED_USER";
        public const int RequestCode = 4701;

        /// <summary>Raised on the main thread when Android refused the update even with its own
        /// prompt, with Android's reason; the Home screen says so instead of staying silent.</summary>
        public static event Action<string>? Failed;

        public override void OnReceive(global::Android.Content.Context? context, global::Android.Content.Intent? intent)
        {
            var failure = (int)global::Android.Content.PM.PackageInstallStatus.Failure;
            var status = intent?.GetIntExtra(global::Android.Content.PM.PackageInstaller.ExtraStatus, failure) ?? failure;
            var message = intent?.GetStringExtra(global::Android.Content.PM.PackageInstaller.ExtraStatusMessage);
            global::Android.Content.Intent? userAction = null;
            if (status == (int)global::Android.Content.PM.PackageInstallStatus.PendingUserAction && intent is not null)
            {
                if (global::Android.OS.Build.VERSION.SdkInt >= global::Android.OS.BuildVersionCodes.Tiramisu)
#pragma warning disable CA1416 // Guarded by the API 33 check above.
                    userAction = intent.GetParcelableExtra(
                        global::Android.Content.Intent.ExtraIntent,
                        Java.Lang.Class.FromType(typeof(global::Android.Content.Intent))) as global::Android.Content.Intent;
#pragma warning restore CA1416
#pragma warning disable CA1422 // Pre-33 fallback is paired with the runtime version guard.
                else
                    userAction = intent.GetParcelableExtra(global::Android.Content.Intent.ExtraIntent) as global::Android.Content.Intent;
#pragma warning restore CA1422
            }
            if (userAction is not null && context is not null)
            {
                userAction.AddFlags(global::Android.Content.ActivityFlags.NewTask);
                context.StartActivity(userAction);
                return;
            }
            global::Android.Util.Log.Info("PKForgeUpdate", status == (int)global::Android.Content.PM.PackageInstallStatus.Success
                ? "Update install confirmed."
                : $"Update install status {status}: {message}");
            if (status == (int)global::Android.Content.PM.PackageInstallStatus.Success) return;

            var apk = intent?.GetStringExtra(ExtraApkPath);
            var askedUser = intent?.GetBooleanExtra(ExtraAskedUser, false) ?? false;
            if (askedUser && status == (int)global::Android.Content.PM.PackageInstallStatus.FailureAborted) return;
            if (string.IsNullOrWhiteSpace(message)) message = null;
            Microsoft.Maui.ApplicationModel.MainThread.BeginInvokeOnMainThread(() =>
            {
                try
                {
                    if (!askedUser && apk is not null && File.Exists(apk))
                    {
                        Platforms.Android.AndroidUpdateInstaller.StartAndroidInstall(apk, askUser: true);
                        return;
                    }
                }
                catch (Exception error) when (error is Java.Lang.Exception or IOException or InvalidOperationException)
                {
                    message = error.Message;
                }
                Failed?.Invoke(message ?? $"Android stopped the installation (status {status}).");
            });
        }
    }
}
#endif
