using System;
using System.ComponentModel.Composition;
using Microsoft.VisualStudio.Editor;
using Microsoft.VisualStudio.OLE.Interop;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.TextManager.Interop;
using Microsoft.VisualStudio.Utilities;

namespace LumiSqlFormatter.Editor
{
    [Export(typeof(IVsTextViewCreationListener))]
    [ContentType("SQL Server Tools")]
    [ContentType("SQL")]
    [ContentType("code")]
    [ContentType("text")]
    [TextViewRole(PredefinedTextViewRoles.Editable)]
    public class TextViewCreationListener : IVsTextViewCreationListener
    {
        [Import]
        internal IVsEditorAdaptersFactoryService AdapterService { get; set; }

        public void VsTextViewCreated(IVsTextView textViewAdapter)
        {
            if (textViewAdapter == null || AdapterService == null)
                return;

            var wpfTextView = AdapterService.GetWpfTextView(textViewAdapter);
            if (wpfTextView == null)
                return;

            // Attach our command filter
            var commandFilter = new KeyCommandFilter(wpfTextView, textViewAdapter);

            int hr = textViewAdapter.AddCommandFilter(commandFilter, out IOleCommandTarget nextCommandHandler);
            if (hr == Microsoft.VisualStudio.VSConstants.S_OK)
            {
                commandFilter.NextCommandHandler = nextCommandHandler;
            }

            // Check active database context and refresh cache in background if needed
            _ = LumiSqlFormatter.Services.DatabaseMetadataCacheService.Instance.CheckAndRefreshIfDatabaseChangedAsync();
        }
    }
}
