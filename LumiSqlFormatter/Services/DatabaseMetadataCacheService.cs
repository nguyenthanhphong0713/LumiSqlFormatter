using System;
using System.Collections.Generic;
using System.Data;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace LumiSqlFormatter.Services
{
    public class ConnectionContextInfo
    {
        public string Server { get; set; }
        public string Database { get; set; }
        public string ConnectionString { get; set; }
        public IDbConnection ActiveConnection { get; set; }
        public object ServerConnection { get; set; }
        public object UIConnectionInfo { get; set; }
    }

    /// <summary>
    /// Service responsible for inspecting database metadata (Stored Procedures & Functions)
    /// from the active SSMS connection and caching them in-memory to preserve exact canonical casing.
    /// Supports automatic re-caching on database switch and asynchronous manual refresh.
    /// </summary>
    public class DatabaseMetadataCacheService
    {
        private static readonly Lazy<DatabaseMetadataCacheService> _instance =
            new Lazy<DatabaseMetadataCacheService>(() => new DatabaseMetadataCacheService());
        public static DatabaseMetadataCacheService Instance => _instance.Value;

        private readonly object _lock = new object();
        private Dictionary<string, string> _cachedProcsAndFuncs =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public string CurrentServer { get; private set; } = string.Empty;
        public string CurrentDatabase { get; private set; } = string.Empty;
        public int CachedCount
        {
            get
            {
                lock (_lock)
                {
                    return _cachedProcsAndFuncs.Count;
                }
            }
        }
        public DateTime? LastRefreshedTime { get; private set; }
        public bool IsRefreshing { get; private set; }

        public event EventHandler CacheUpdated;

        private DateTime _lastChangeCheck = DateTime.MinValue;

        private DatabaseMetadataCacheService()
        {
        }

        /// <summary>
        /// Checks if a word is a known procedure or function in the database metadata cache.
        /// If found, outputs the canonical casing defined in the database catalog.
        /// </summary>
        public bool TryGetCanonicalName(string word, out string canonicalName)
        {
            canonicalName = null;
            if (string.IsNullOrEmpty(word))
                return false;

            lock (_lock)
            {
                return _cachedProcsAndFuncs.TryGetValue(word, out canonicalName);
            }
        }

        /// <summary>
        /// Checks if the active database or connection has changed.
        /// If changed, clears the current cache and schedules an asynchronous re-cache.
        /// Throttled to avoid inspecting SSMS connection more than once per 1.5 seconds.
        /// </summary>
        public async Task CheckAndRefreshIfDatabaseChangedAsync()
        {
            if (IsRefreshing)
                return;

            var now = DateTime.Now;
            if ((now - _lastChangeCheck).TotalMilliseconds < 1500)
                return;

            _lastChangeCheck = now;

            try
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                var connInfo = ResolveActiveConnectionInfo();
                await Task.Yield();

                if (connInfo == null || (string.IsNullOrEmpty(connInfo.Server) && string.IsNullOrEmpty(connInfo.Database)))
                    return;

                bool changed = false;
                lock (_lock)
                {
                    if (!string.Equals(connInfo.Database, CurrentDatabase, StringComparison.OrdinalIgnoreCase) ||
                        !string.Equals(connInfo.Server, CurrentServer, StringComparison.OrdinalIgnoreCase))
                    {
                        changed = true;
                    }
                }

                if (changed)
                {
                    // Database context changed -> Clear cache and re-cache
                    lock (_lock)
                    {
                        _cachedProcsAndFuncs.Clear();
                        CurrentServer = connInfo.Server ?? string.Empty;
                        CurrentDatabase = connInfo.Database ?? string.Empty;
                    }

                    CacheUpdated?.Invoke(this, EventArgs.Empty);

                    await RefreshCacheInternalAsync(connInfo, false);
                }
            }
            catch
            {
                // Ignore background inspection errors
            }
        }

        /// <summary>
        /// Performs manual or automatic refresh of the database objects cache.
        /// Queries sys.objects in background without blocking the UI.
        /// </summary>
        public async Task RefreshCacheAsync(bool isManualTrigger = false)
        {
            if (IsRefreshing)
                return;

            IsRefreshing = true;
            try
            {
                if (isManualTrigger)
                {
                    ShowStatus("Lumi SQL: Đang nạp danh sách Stored Procedures & Functions từ database...");
                }

                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                var connInfo = ResolveActiveConnectionInfo();
                await Task.Yield();

                if (connInfo == null ||
                    (string.IsNullOrEmpty(connInfo.ConnectionString) &&
                     connInfo.ActiveConnection == null &&
                     connInfo.ServerConnection == null &&
                     string.IsNullOrEmpty(connInfo.Server)))
                {
                    if (isManualTrigger)
                    {
                        ShowStatus("Lumi SQL: Không tìm thấy kết nối database active để làm mới cache.");
                    }
                    return;
                }

                await Task.Run(() => RefreshCacheInternal(connInfo, isManualTrigger));
            }
            catch (Exception ex)
            {
                if (isManualTrigger)
                {
                    ShowStatus($"Lumi SQL: Lỗi làm mới cache database: {ex.Message}");
                }
            }
            finally
            {
                IsRefreshing = false;
            }
        }

        private async Task RefreshCacheInternalAsync(ConnectionContextInfo connInfo, bool isManualTrigger)
        {
            if (IsRefreshing)
                return;

            IsRefreshing = true;
            try
            {
                await Task.Run(() => RefreshCacheInternal(connInfo, isManualTrigger));
            }
            finally
            {
                IsRefreshing = false;
            }
        }

        private void RefreshCacheInternal(ConnectionContextInfo connInfo, bool isManualTrigger)
        {
            var newCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            bool querySuccess = false;
            string lastErrorMessage = null;

            // Strategy 1: Use SMO ServerConnection if available (SSMS native, supports all auth, MFA, tokens, encryption)
            if (connInfo.ServerConnection != null)
            {
                try
                {
                    var serverConn = connInfo.ServerConnection;
                    // Check if open, if not connect
                    var isOpenProp = serverConn.GetType().GetProperty("IsOpen", BindingFlags.Public | BindingFlags.Instance);
                    bool isOpen = isOpenProp != null && (bool)isOpenProp.GetValue(serverConn);
                    if (!isOpen)
                    {
                        var connectMethod = serverConn.GetType().GetMethod("Connect", Type.EmptyTypes);
                        connectMethod?.Invoke(serverConn, null);
                    }

                    // Change database if needed
                    if (!string.IsNullOrEmpty(connInfo.Database))
                    {
                        try
                        {
                            var changeDbMethod = serverConn.GetType().GetMethod("ChangeDatabase", new[] { typeof(string) });
                            changeDbMethod?.Invoke(serverConn, new object[] { connInfo.Database });
                        }
                        catch { }
                    }

                    // Execute query via ExecuteWithResults or ExecuteReader
                    var execMethod = serverConn.GetType().GetMethod("ExecuteWithResults", new[] { typeof(string) });
                    if (execMethod != null)
                    {
                        string script = !string.IsNullOrEmpty(connInfo.Database)
                            ? $"USE [{connInfo.Database.Replace("]", "]]")}]; SELECT DB_NAME(); SELECT name FROM sys.objects WHERE type IN ('P', 'PC', 'FN', 'FS', 'FT', 'IF', 'TF', 'AF', 'X');"
                            : "SELECT DB_NAME(); SELECT name FROM sys.objects WHERE type IN ('P', 'PC', 'FN', 'FS', 'FT', 'IF', 'TF', 'AF', 'X');";

                        var ds = execMethod.Invoke(serverConn, new object[] { script }) as DataSet;
                        if (ds != null && ds.Tables.Count > 0)
                        {
                            DataTable objTable = ds.Tables[0];
                            if (ds.Tables.Count > 1)
                            {
                                if (ds.Tables[0].Rows.Count > 0 && !ds.Tables[0].Rows[0].IsNull(0))
                                {
                                    string resolvedDb = ds.Tables[0].Rows[0][0].ToString();
                                    if (!string.IsNullOrEmpty(resolvedDb))
                                    {
                                        connInfo.Database = resolvedDb;
                                    }
                                }
                                objTable = ds.Tables[1];
                            }
                            else if (ds.Tables[0].Rows.Count > 0 && ds.Tables[0].Columns.Count == 1 &&
                                     !ds.Tables[0].Columns[0].ColumnName.Equals("name", StringComparison.OrdinalIgnoreCase))
                            {
                                string resolvedDb = ds.Tables[0].Rows[0][0].ToString();
                                if (!string.IsNullOrEmpty(resolvedDb))
                                {
                                    connInfo.Database = resolvedDb;
                                }
                            }

                            foreach (DataRow row in objTable.Rows)
                            {
                                if (!row.IsNull(0))
                                {
                                    string name = row[0].ToString();
                                    if (!string.IsNullOrEmpty(name))
                                    {
                                        newCache[name] = name;
                                    }
                                }
                            }
                            querySuccess = true;
                        }
                    }

                    // If database wasn't known beforehand, read it from ServerConnection
                    if (string.IsNullOrEmpty(connInfo.Database))
                    {
                        var curDbProp = serverConn.GetType().GetProperty("CurrentDatabase", BindingFlags.Public | BindingFlags.Instance) ??
                                        serverConn.GetType().GetProperty("DatabaseName", BindingFlags.Public | BindingFlags.Instance);
                        connInfo.Database = curDbProp?.GetValue(serverConn) as string;
                    }
                }
                catch (Exception ex)
                {
                    lastErrorMessage = ex.InnerException?.Message ?? ex.Message;
                }
            }

            // Strategy 2: Use active SSMS IDbConnection directly if Strategy 1 did not succeed
            if (!querySuccess && connInfo.ActiveConnection != null)
            {
                try
                {
                    if (connInfo.ActiveConnection.State != ConnectionState.Open)
                    {
                        connInfo.ActiveConnection.Open();
                    }

                    string resolvedDb = connInfo.Database;
                    ExecuteMetadataQuery(connInfo.ActiveConnection, newCache, ref resolvedDb);
                    querySuccess = true;

                    if (!string.IsNullOrEmpty(resolvedDb))
                    {
                        connInfo.Database = resolvedDb;
                    }
                    else if (string.IsNullOrEmpty(connInfo.Database))
                    {
                        connInfo.Database = connInfo.ActiveConnection.Database;
                    }
                }
                catch (Exception ex)
                {
                    lastErrorMessage = ex.Message;
                }
            }

            // Strategy 3: Try Microsoft.Data.SqlClient (used by SSMS 19+) if available in AppDomain
            if (!querySuccess && !string.IsNullOrEmpty(connInfo.ConnectionString))
            {
                Type mdsConnType = null;
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (asm.GetName().Name.StartsWith("Microsoft.Data.SqlClient", StringComparison.OrdinalIgnoreCase))
                    {
                        mdsConnType = asm.GetType("Microsoft.Data.SqlClient.SqlConnection");
                        if (mdsConnType != null)
                            break;
                    }
                }

                if (mdsConnType != null)
                {
                    try
                    {
                        using (var mdsConn = (IDbConnection)Activator.CreateInstance(mdsConnType, connInfo.ConnectionString))
                        {
                            mdsConn.Open();
                            string resolvedDb = connInfo.Database;
                            ExecuteMetadataQuery(mdsConn, newCache, ref resolvedDb);
                            querySuccess = true;

                            if (!string.IsNullOrEmpty(resolvedDb))
                            {
                                connInfo.Database = resolvedDb;
                            }
                            else if (string.IsNullOrEmpty(connInfo.Database))
                            {
                                connInfo.Database = mdsConn.Database;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        lastErrorMessage = ex.Message;
                    }
                }
            }

            // Strategy 4: Standard System.Data.SqlClient.SqlConnection
            if (!querySuccess && !string.IsNullOrEmpty(connInfo.ConnectionString))
            {
                try
                {
                    using (var sqlConn = new System.Data.SqlClient.SqlConnection(connInfo.ConnectionString))
                    {
                        sqlConn.Open();
                        string resolvedDb = connInfo.Database;
                        ExecuteMetadataQuery(sqlConn, newCache, ref resolvedDb);
                        querySuccess = true;

                        if (!string.IsNullOrEmpty(resolvedDb))
                        {
                            connInfo.Database = resolvedDb;
                        }
                        else if (string.IsNullOrEmpty(connInfo.Database))
                        {
                            connInfo.Database = sqlConn.Database;
                        }
                    }
                }
                catch (Exception ex)
                {
                    lastErrorMessage = ex.Message;

                    // Fallback attempt: if Encrypt setting caused failure, try with Encrypt=False
                    if (!connInfo.ConnectionString.Contains("Encrypt=False"))
                    {
                        try
                        {
                            string fallbackConnStr = connInfo.ConnectionString + ";Encrypt=False;";
                            using (var sqlConn = new System.Data.SqlClient.SqlConnection(fallbackConnStr))
                            {
                                sqlConn.Open();
                                string resolvedDb = connInfo.Database;
                                ExecuteMetadataQuery(sqlConn, newCache, ref resolvedDb);
                                querySuccess = true;

                                if (!string.IsNullOrEmpty(resolvedDb))
                                {
                                    connInfo.Database = resolvedDb;
                                }
                                else if (string.IsNullOrEmpty(connInfo.Database))
                                {
                                    connInfo.Database = sqlConn.Database;
                                }
                            }
                        }
                        catch (Exception ex2)
                        {
                            lastErrorMessage = ex2.Message;
                        }
                    }
                }
            }

            if (querySuccess)
            {
                lock (_lock)
                {
                    _cachedProcsAndFuncs = newCache;
                    CurrentServer = connInfo.Server ?? string.Empty;
                    CurrentDatabase = connInfo.Database ?? string.Empty;
                    LastRefreshedTime = DateTime.Now;
                }

                if (isManualTrigger)
                {
                    string dbName = string.IsNullOrEmpty(CurrentDatabase) ? "Database" : CurrentDatabase;
                    ShowStatus($"Lumi SQL: Đã làm mới cache [{dbName}] ({newCache.Count} procedures & functions).");
                }

                CacheUpdated?.Invoke(this, EventArgs.Empty);
            }
            else if (isManualTrigger)
            {
                string detail = !string.IsNullOrEmpty(lastErrorMessage) ? $" ({lastErrorMessage})" : "";
                ShowStatus($"Lumi SQL: Không thể truy vấn sys.objects trên kết nối hiện tại{detail}.");
            }
        }

        private void ExecuteMetadataQuery(IDbConnection conn, Dictionary<string, string> targetDict, ref string databaseName)
        {
            if (!string.IsNullOrEmpty(databaseName))
            {
                try
                {
                    if (!string.Equals(conn.Database, databaseName, StringComparison.OrdinalIgnoreCase))
                    {
                        conn.ChangeDatabase(databaseName);
                    }
                }
                catch { }
            }

            string script = !string.IsNullOrEmpty(databaseName)
                ? $"USE [{databaseName.Replace("]", "]]")}]; SELECT DB_NAME(); SELECT name FROM sys.objects WHERE type IN ('P', 'PC', 'FN', 'FS', 'FT', 'IF', 'TF', 'AF', 'X');"
                : "SELECT DB_NAME(); SELECT name FROM sys.objects WHERE type IN ('P', 'PC', 'FN', 'FS', 'FT', 'IF', 'TF', 'AF', 'X');";

            try
            {
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = script;
                    cmd.CommandTimeout = 15;
                    using (var reader = cmd.ExecuteReader())
                    {
                        if (reader.Read() && !reader.IsDBNull(0))
                        {
                            string resolved = reader.GetString(0);
                            if (!string.IsNullOrEmpty(resolved))
                            {
                                databaseName = resolved;
                            }
                        }

                        if (reader.NextResult())
                        {
                            while (reader.Read())
                            {
                                if (!reader.IsDBNull(0))
                                {
                                    string name = reader.GetString(0);
                                    if (!string.IsNullOrEmpty(name))
                                    {
                                        targetDict[name] = name;
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch
            {
                // Fallback: single statement sys.objects
                string singleQuery = @"SELECT name FROM sys.objects WHERE type IN ('P', 'PC', 'FN', 'FS', 'FT', 'IF', 'TF', 'AF', 'X')";
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = singleQuery;
                    cmd.CommandTimeout = 15;
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            if (!reader.IsDBNull(0))
                            {
                                string name = reader.GetString(0);
                                if (!string.IsNullOrEmpty(name))
                                {
                                    targetDict[name] = name;
                                }
                            }
                        }
                    }
                }

                if (string.IsNullOrEmpty(databaseName))
                {
                    try { databaseName = conn.Database; } catch { }
                }
            }
        }

        /// <summary>
        /// Multi-layer resolver to extract active connection and database from SSMS.
        /// Layer 0: EnvDTE ActiveWindow Caption (extracts active tab's SERVER and Database dynamically)
        /// Layer 1: ISqlEditorService (SSqlEditorService)
        /// Layer 2: ServiceCache.ScriptFactory.CurrentlyActiveWndConnectionInfo & UIConnectionInfoUtil
        /// </summary>
        public ConnectionContextInfo ResolveActiveConnectionInfo()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var result = new ConnectionContextInfo();

            // Layer 0: Extract active editor tab caption via EnvDTE.DTE
            // SSMS query tab titles dynamically update on database change dropdown:
            // "SQLQuery1.sql - SERVER.Database (User (spid))"
            string captionServer = null;
            string captionDatabase = null;
            try
            {
                var dte = Package.GetGlobalService(typeof(EnvDTE.DTE)) as EnvDTE.DTE;
                if (dte?.ActiveWindow != null)
                {
                    string caption = dte.ActiveWindow.Caption;
                    TryParseCaption(caption, out captionServer, out captionDatabase);
                }
            }
            catch { }

            if (!string.IsNullOrEmpty(captionServer))
            {
                result.Server = captionServer;
            }
            if (!string.IsNullOrEmpty(captionDatabase))
            {
                result.Database = captionDatabase;
            }

            // Layer 1: Query ISqlEditorService (SSqlEditorService)
            try
            {
                Type ssqlEditorType = null;
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (asm.GetName().Name.StartsWith("Microsoft.SqlServer.Management.SqlStudio", StringComparison.OrdinalIgnoreCase) ||
                        asm.GetName().Name.StartsWith("SQLEditors", StringComparison.OrdinalIgnoreCase))
                    {
                        ssqlEditorType = asm.GetType("Microsoft.SqlServer.Management.UI.VSIntegration.SSqlEditorService");
                        if (ssqlEditorType != null)
                            break;
                    }
                }

                object editorService = null;
                if (ssqlEditorType != null)
                {
                    editorService = Package.GetGlobalService(ssqlEditorType);
                }

                if (editorService == null)
                {
                    var oleSp = Package.GetGlobalService(typeof(Microsoft.VisualStudio.OLE.Interop.IServiceProvider)) as Microsoft.VisualStudio.OLE.Interop.IServiceProvider;
                    if (oleSp != null)
                    {
                        Guid sid = new Guid("72bb0b14-2d2b-454a-b0ac-1890c2994a22");
                        Guid iid = new Guid("00000000-0000-0000-C000-000000000046");
                        IntPtr pUnk;
                        if (oleSp.QueryService(ref sid, ref iid, out pUnk) == 0 && pUnk != IntPtr.Zero)
                        {
                            try
                            {
                                editorService = Marshal.GetObjectForIUnknown(pUnk);
                            }
                            finally
                            {
                                Marshal.Release(pUnk);
                            }
                        }
                    }
                }

                if (editorService != null)
                {
                    MethodInfo getCurrentConnMethod = null;
                    foreach (var iface in editorService.GetType().GetInterfaces())
                    {
                        if (iface.Name.Contains("ISqlEditorService"))
                        {
                            getCurrentConnMethod = iface.GetMethod("GetCurrentConnection");
                            if (getCurrentConnMethod != null) break;
                        }
                    }
                    if (getCurrentConnMethod == null)
                    {
                        foreach (var m in editorService.GetType().GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                        {
                            if (m.Name.EndsWith("GetCurrentConnection", StringComparison.OrdinalIgnoreCase) && m.GetParameters().Length == 0)
                            {
                                getCurrentConnMethod = m;
                                break;
                            }
                        }
                    }

                    if (getCurrentConnMethod != null)
                    {
                        var activeDbConn = getCurrentConnMethod.Invoke(editorService, null) as IDbConnection;
                        if (activeDbConn != null)
                        {
                            result.ActiveConnection = activeDbConn;
                            if (!string.IsNullOrEmpty(activeDbConn.Database))
                            {
                                result.Database = activeDbConn.Database;
                            }
                            if (!string.IsNullOrEmpty(activeDbConn.ConnectionString))
                            {
                                result.ConnectionString = activeDbConn.ConnectionString;
                            }
                        }
                    }
                }
            }
            catch
            {
                // Proceed
            }

            // Layer 2: ServiceCache.ScriptFactory.CurrentlyActiveWndConnectionInfo
            try
            {
                Type serviceCacheType = null;
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    serviceCacheType = asm.GetType("Microsoft.SqlServer.Management.UI.VSIntegration.ServiceCache");
                    if (serviceCacheType != null)
                        break;
                }

                if (serviceCacheType != null)
                {
                    var sfProp = serviceCacheType.GetProperty("ScriptFactory", BindingFlags.Public | BindingFlags.Static);
                    var scriptFactory = sfProp?.GetValue(null);
                    if (scriptFactory != null)
                    {
                        var activeConnProp = scriptFactory.GetType().GetProperty("CurrentlyActiveWndConnectionInfo", BindingFlags.Public | BindingFlags.Instance);
                        var activeConn = activeConnProp?.GetValue(scriptFactory);
                        if (activeConn != null)
                        {
                            var activeDbProp = activeConn.GetType().GetProperty("Database", BindingFlags.Public | BindingFlags.Instance) ??
                                               activeConn.GetType().GetProperty("DatabaseName", BindingFlags.Public | BindingFlags.Instance) ??
                                               activeConn.GetType().GetProperty("CurrentDatabase", BindingFlags.Public | BindingFlags.Instance);
                            string activeConnDb = activeDbProp?.GetValue(activeConn) as string;
                            if (!string.IsNullOrEmpty(activeConnDb) && !string.Equals(activeConnDb, "<default>", StringComparison.OrdinalIgnoreCase))
                            {
                                if (string.IsNullOrEmpty(result.Database))
                                {
                                    result.Database = activeConnDb;
                                }
                            }

                            var uiConnProp = activeConn.GetType().GetProperty("UIConnectionInfo", BindingFlags.Public | BindingFlags.Instance);
                            var uiConn = uiConnProp?.GetValue(activeConn);
                            if (uiConn != null)
                            {
                                result.UIConnectionInfo = uiConn;

                                var serverProp = uiConn.GetType().GetProperty("ServerName", BindingFlags.Public | BindingFlags.Instance);
                                string server = serverProp?.GetValue(uiConn) as string;
                                if (!string.IsNullOrEmpty(server))
                                {
                                    result.Server = server;
                                }

                                if (string.IsNullOrEmpty(result.Database))
                                {
                                    Type uiConnUtilType = null;
                                    foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                                    {
                                        uiConnUtilType = asm.GetType("Microsoft.SqlServer.Management.UI.ConnectionDlg.UIConnectionInfoUtil");
                                        if (uiConnUtilType != null)
                                            break;
                                    }

                                    if (uiConnUtilType != null)
                                    {
                                        try
                                        {
                                            var getDbMethod = uiConnUtilType.GetMethod("GetDatabaseName", new[] { uiConn.GetType() });
                                            string dbName = getDbMethod?.Invoke(null, new object[] { uiConn }) as string;
                                            if (!string.IsNullOrEmpty(dbName) && !string.Equals(dbName, "<default>", StringComparison.OrdinalIgnoreCase))
                                            {
                                                result.Database = dbName;
                                            }
                                        }
                                        catch { }
                                    }

                                    if (string.IsNullOrEmpty(result.Database))
                                    {
                                        var advProp = uiConn.GetType().GetProperty("AdvancedOptions", BindingFlags.Public | BindingFlags.Instance);
                                        var advOptions = advProp?.GetValue(uiConn) as System.Collections.Specialized.NameValueCollection;
                                        string dbName = advOptions?["DATABASE"] ?? advOptions?["Initial Catalog"];
                                        if (!string.IsNullOrEmpty(dbName) && !string.Equals(dbName, "<default>", StringComparison.OrdinalIgnoreCase))
                                        {
                                            result.Database = dbName;
                                        }
                                    }
                                }

                                // Try to get SMO ServerConnection & Core Connection Info via UIConnectionInfoUtil
                                Type utilType = null;
                                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                                {
                                    utilType = asm.GetType("Microsoft.SqlServer.Management.UI.ConnectionDlg.UIConnectionInfoUtil");
                                    if (utilType != null)
                                        break;
                                }

                                if (utilType != null)
                                {
                                    try
                                    {
                                        var getCoreMethod = utilType.GetMethod("GetCoreConnectionInfo", new[] { uiConn.GetType() });
                                        var coreConnInfo = getCoreMethod?.Invoke(null, new object[] { uiConn });
                                        if (coreConnInfo != null)
                                        {
                                            var serverConnProp = coreConnInfo.GetType().GetProperty("ServerConnection", BindingFlags.Public | BindingFlags.Instance);
                                            var serverConn = serverConnProp?.GetValue(coreConnInfo);
                                            if (serverConn != null && result.ServerConnection == null)
                                            {
                                                result.ServerConnection = serverConn;
                                            }

                                            if (string.IsNullOrEmpty(result.Database))
                                            {
                                                var coreDbProp = coreConnInfo.GetType().GetProperty("DatabaseName", BindingFlags.Public | BindingFlags.Instance);
                                                string coreDb = coreDbProp?.GetValue(coreConnInfo) as string;
                                                if (!string.IsNullOrEmpty(coreDb) && !string.Equals(coreDb, "<default>", StringComparison.OrdinalIgnoreCase))
                                                {
                                                    result.Database = coreDb;
                                                }
                                            }

                                            try
                                            {
                                                var createConnMethod = coreConnInfo.GetType().GetMethod("CreateConnectionObject", Type.EmptyTypes);
                                                var createdDbConn = createConnMethod?.Invoke(coreConnInfo, null) as IDbConnection;
                                                if (createdDbConn != null && result.ActiveConnection == null)
                                                {
                                                    result.ActiveConnection = createdDbConn;
                                                }
                                            }
                                            catch { }
                                        }
                                    }
                                    catch { }
                                }

                                // Build connection string if not already set by ISqlEditorService
                                if (string.IsNullOrEmpty(result.ConnectionString) && !string.IsNullOrEmpty(result.Server))
                                {
                                    var authTypeProp = uiConn.GetType().GetProperty("AuthenticationType", BindingFlags.Public | BindingFlags.Instance);
                                    int authType = authTypeProp != null ? (int)authTypeProp.GetValue(uiConn) : 0;

                                    var userProp = uiConn.GetType().GetProperty("UserName", BindingFlags.Public | BindingFlags.Instance);
                                    string userName = userProp?.GetValue(uiConn) as string;

                                    string password = null;
                                    var passProp = uiConn.GetType().GetProperty("Password", BindingFlags.Public | BindingFlags.Instance);
                                    password = passProp?.GetValue(uiConn) as string;

                                    if (string.IsNullOrEmpty(password))
                                    {
                                        var inMemProp = uiConn.GetType().GetProperty("InMemoryPassword", BindingFlags.Public | BindingFlags.Instance);
                                        var secPass = inMemProp?.GetValue(uiConn) as SecureString;
                                        if (secPass != null && secPass.Length > 0)
                                        {
                                            IntPtr bstr = Marshal.SecureStringToBSTR(secPass);
                                            try
                                            {
                                                password = Marshal.PtrToStringBSTR(bstr);
                                            }
                                            finally
                                            {
                                                Marshal.ZeroFreeBSTR(bstr);
                                            }
                                        }
                                    }

                                    string dbSegment = !string.IsNullOrEmpty(result.Database) ? $"Database={result.Database};" : "";

                                    if (authType == 0) // Windows Authentication
                                    {
                                        result.ConnectionString = $"Data Source={result.Server};{dbSegment}Integrated Security=True;TrustServerCertificate=True;";
                                    }
                                    else if (authType == 1 && !string.IsNullOrEmpty(userName)) // SQL Server Authentication
                                    {
                                        string passPart = !string.IsNullOrEmpty(password) ? $"Password={password};" : "";
                                        result.ConnectionString = $"Data Source={result.Server};{dbSegment}User ID={userName};{passPart}TrustServerCertificate=True;";
                                    }
                                    else
                                    {
                                        result.ConnectionString = $"Data Source={result.Server};{dbSegment}TrustServerCertificate=True;";
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch
            {
                // Fall back
            }

            // Fallback: If result.Database was still not resolved, use captionDatabase
            if (string.IsNullOrEmpty(result.Database) && !string.IsNullOrEmpty(captionDatabase))
            {
                result.Database = captionDatabase;
            }
            if (string.IsNullOrEmpty(result.Server) && !string.IsNullOrEmpty(captionServer))
            {
                result.Server = captionServer;
            }

            return result;
        }

        public static bool TryParseCaption(string caption, out string server, out string database)
        {
            server = null;
            database = null;
            if (string.IsNullOrWhiteSpace(caption))
                return false;

            string clean = caption.Trim();
            if (clean.EndsWith("*"))
            {
                clean = clean.Substring(0, clean.Length - 1).Trim();
            }

            int dashIdx = clean.LastIndexOf(" - ", StringComparison.Ordinal);
            if (dashIdx < 0)
                return false;

            string connPart = clean.Substring(dashIdx + 3).Trim();
            if (string.IsNullOrEmpty(connPart))
                return false;

            int parenIdx = connPart.IndexOf('(');
            if (parenIdx > 0)
            {
                connPart = connPart.Substring(0, parenIdx).Trim();
            }

            int lastDot = connPart.LastIndexOf('.');
            if (lastDot > 0 && lastDot < connPart.Length - 1)
            {
                server = connPart.Substring(0, lastDot).Trim();
                database = connPart.Substring(lastDot + 1).Trim().Trim('[', ']');
                return !string.IsNullOrEmpty(database);
            }

            return false;
        }

        private static void ShowStatus(string message)
        {
            _ = Task.Run(async () =>
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                try
                {
                    var statusBar = Package.GetGlobalService(typeof(SVsStatusbar)) as IVsStatusbar;
                    if (statusBar != null)
                    {
                        statusBar.IsFrozen(out int frozen);
                        if (frozen == 0)
                        {
                            statusBar.SetText(message);
                        }
                    }
                }
                catch { }
            });
        }
    }
}
