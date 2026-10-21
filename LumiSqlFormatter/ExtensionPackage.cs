using System;
using System.ComponentModel.Design;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.ComponentModelHost;
using Microsoft.VisualStudio.Editor;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.TextManager.Interop;
using Task = System.Threading.Tasks.Task;
using LumiSqlFormatter.Editor;
using LumiSqlFormatter.Models;
using LumiSqlFormatter.UI;

namespace LumiSqlFormatter
{
    [PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
    [InstalledProductRegistration("Lumi SQL Formatter (PHONGNT)", "Real-time SQL Auto-Capitalization & Smart Snippets for SSMS 22", "1.1.4")]
    [ProvideMenuResource("Menus.ctmenu", 1)]
    [ProvideAutoLoad(VSConstants.UICONTEXT.ShellInitialized_string, PackageAutoLoadFlags.BackgroundLoad)]
    [ProvideAutoLoad(VSConstants.UICONTEXT.NoSolution_string, PackageAutoLoadFlags.BackgroundLoad)]
    [ProvideToolWindow(typeof(SettingsToolWindow), Style = VsDockStyle.Tabbed, Window = ToolWindowGuids.Outputwindow)]
    [ProvideOptionPage(typeof(OptionsPage), "Lumi SQL Formatter", "General", 0, 0, true)]
    [Guid(GuidList.guidLumiSqlPackageString)]
    public sealed class ExtensionPackage : AsyncPackage
    {
        protected override async Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
        {
            await this.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

            // Register OleMenuCommandService
            var commandService = await GetServiceAsync(typeof(IMenuCommandService)) as OleMenuCommandService;
            if (commandService != null)
            {
                // Settings command
                var menuCommandID = new CommandID(GuidList.guidLumiSqlCmdSet, (int)PkgCmdIDList.cmdidOpenSettings);
                var menuItem = new MenuCommand(ShowSettingsToolWindow, menuCommandID);
                commandService.AddCommand(menuItem);

                // Format Uppercase command
                var upperCmdID = new CommandID(GuidList.guidLumiSqlCmdSet, (int)PkgCmdIDList.cmdidFormatUppercase);
                var upperItem = new OleMenuCommand(OnExecuteFormatUppercase, upperCmdID);
                upperItem.BeforeQueryStatus += OnBeforeQueryStatusFormatCommand;
                commandService.AddCommand(upperItem);

                // Format Lowercase command
                var lowerCmdID = new CommandID(GuidList.guidLumiSqlCmdSet, (int)PkgCmdIDList.cmdidFormatLowercase);
                var lowerItem = new OleMenuCommand(OnExecuteFormatLowercase, lowerCmdID);
                lowerItem.BeforeQueryStatus += OnBeforeQueryStatusFormatCommand;
                commandService.AddCommand(lowerItem);
            }
        }

        private void OnBeforeQueryStatusFormatCommand(object sender, EventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (sender is OleMenuCommand menuCommand)
            {
                menuCommand.Visible = true;
                menuCommand.Enabled = true;
            }
        }

        private void OnExecuteFormatUppercase(object sender, EventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            ExecuteFormatCommand(BatchFormatMode.UppercaseKeywords);
        }

        private void OnExecuteFormatLowercase(object sender, EventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            ExecuteFormatCommand(BatchFormatMode.LowercaseAllExceptPreserved);
        }

        private void ExecuteFormatCommand(BatchFormatMode mode)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            // 1. First check if we have a current active filter from recent editor interaction
            var activeTextView = KeyCommandFilter.CurrentActiveTextView;
            if (activeTextView != null)
            {
                KeyCommandFilter.FormatTextView(activeTextView, mode);
                return;
            }

            // 2. Fallback to SVsTextManager to find active view
            try
            {
                var textManager = GetService(typeof(SVsTextManager)) as IVsTextManager;
                if (textManager != null)
                {
                    textManager.GetActiveView(0, null, out IVsTextView activeView);
                    if (activeView != null)
                    {
                        var componentModel = GetService(typeof(SComponentModel)) as IComponentModel;
                        var adapterService = componentModel?.GetService<IVsEditorAdaptersFactoryService>();
                        var wpfTextView = adapterService?.GetWpfTextView(activeView);
                        if (wpfTextView != null)
                        {
                            KeyCommandFilter.FormatTextView(wpfTextView, mode);
                        }
                    }
                }
            }
            catch (Exception)
            {
                // Silently ignore
            }
        }

        private void ShowSettingsToolWindow(object sender, EventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            ToolWindowPane window = this.FindToolWindow(typeof(SettingsToolWindow), 0, true);
            if (window?.Frame == null)
            {
                throw new NotSupportedException("Cannot create tool window");
            }

            var windowFrame = (IVsWindowFrame)window.Frame;
            Microsoft.VisualStudio.ErrorHandler.ThrowOnFailure(windowFrame.Show());
        }
    }
}
