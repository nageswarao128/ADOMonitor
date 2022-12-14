using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using System;
using System.Diagnostics.CodeAnalysis;
using System.Reactive.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using AzureDevOpsStatusMonitor.Entities;
using AzureDevOpsStatusMonitor.Helpers;
using AzureDevOpsStatusMonitor.Service;
using AzureDevOpsStatusMonitor.UI;
using Task = System.Threading.Tasks.Task;

namespace AzureDevOpsStatusMonitor
{
    [SuppressMessage("StyleCop.CSharp.DocumentationRules", "SA1650:ElementDocumentationMustBeSpelledCorrectly",
        Justification = "pkgdef, VS and vsixmanifest are valid VS terms")]
    [PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
    [InstalledProductRegistration("#110", "#112", "1.4", IconResourceID = 400)]
    [Guid(GuidList.guidVSOStatusPkgString)]
    [ProvideAutoLoad(UIContextGuids80.NoSolution, PackageAutoLoadFlags.BackgroundLoad)]
    [ProvideAutoLoad(UIContextGuids80.SolutionExists, PackageAutoLoadFlags.BackgroundLoad)]
    [ProvideBindingPath]
    [ProvideOptionPage(typeof(Options), Vsix.Name, "General", 0, 0, true)]
    public class AzureDevOpsStatusMonitorPackage : AsyncPackage
    {
        private static Options _options;
        private IVsOutputWindow _outputWindow;
        private static readonly object _syncRoot = new object();
        AzDevOpsStatusMonitor _monitor = new AzDevOpsStatusMonitor();
        private IDisposable _subscription;
        private AzDevOpsStatusControl _azDevOpsStatusControl;
        private StatusBarManager _statusBarManager;
        public event EventHandler<VSTSStatusResponse> OnStatusChanged;

        protected void OnOnStatusChanged(VSTSStatusResponse response)
        {
            if (OnStatusChanged != null)
            {
                OnStatusChanged(this, response);
            }
        }

        public static Options Options
        {
            get
            {
                if (_options == null)
                {
                    lock (_syncRoot)
                    {
                        if (_options == null)
                        {
                            LoadPackage();
                        }
                    }
                }

                return _options;
            }
        }


        protected override async Task InitializeAsync(CancellationToken cancellationToken,
            IProgress<ServiceProgressData> progress)
        {
            // Switches to the UI thread in order to consume some services used in command initialization
            await JoinableTaskFactory.SwitchToMainThreadAsync();

            //get interval from options
            _options = (Options)GetDialogPage(typeof(Options));

            Logger.Initialize(this, Vsix.Name);

            if (Options != null)
            {
                Options.OnOptionsChanged += OnOptionsChanged;
            }

            _azDevOpsStatusControl = new AzDevOpsStatusControl(this);
            _statusBarManager = new StatusBarManager(Application.Current.MainWindow);
            _statusBarManager.InjectControl(_azDevOpsStatusControl);

            PollForStatus();
        }

        private void PollForStatus()
        {
            var interval = Options?.Interval ?? 5;

            // https://github.com/LeeCampbell/RxCookbook/blob/master/Repository/Polling.md
            _subscription = _monitor
                .GetStatusAsync()
                .Timeout(TimeSpan.FromSeconds(30))
                .Poll(TimeSpan.FromSeconds(interval))
                .Subscribe(async res =>
                {
                    await res.Switch(async r =>
                        {
                            await JoinableTaskFactory.SwitchToMainThreadAsync();
                            OnOnStatusChanged(r);
                            Logger.Log(r.Status.Message);
                        },
                        async e =>
                        {
                            await JoinableTaskFactory.SwitchToMainThreadAsync();
                            OnOnStatusChanged(null);
                        });
                });
        }

        private void OnOptionsChanged(object sender, OptionsChangedEventArgs e)
        {
            if (_subscription != null)
            {
                _subscription.Dispose();
            }

            PollForStatus();
        }

        public IVsOutputWindow OutputWindow
        {
            get
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                if (_outputWindow == null)
                {
                    _outputWindow = (IVsOutputWindow)GetService(typeof(SVsOutputWindow));
                    return _outputWindow;
                }

                return _outputWindow;
            }
        }

        private static void LoadPackage()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var shell = (IVsShell)GetGlobalService(typeof(SVsShell));

            if (shell.IsPackageLoaded(ref GuidList.guidVSOStatusPkg, out IVsPackage package) != VSConstants.S_OK)
            {
                ErrorHandler.Succeeded(shell.LoadPackage(ref GuidList.guidVSOStatusPkg, out package));
            }
        }
    }
}