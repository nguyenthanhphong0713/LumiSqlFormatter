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
        /// Queries sys.all_objects / sys.objects in background without blocking the UI.
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
                        var curDbProp = serverConn.GetType().GetProperty("CurrentDatabase", BindingFlags.Public | BindingFlags.Instance) ??
                                        serverConn.GetType().GetProperty("DatabaseName", BindingFlags.Public | BindingFlags.Instance);
                        string curDb = curDbProp?.GetValue(serverConn) as string;
                        if (!string.IsNullOrEmpty(curDb) && !string.Equals(curDb, connInfo.Database, StringComparison.OrdinalIgnoreCase))
                        {
                            var changeDbMethod = serverConn.GetType().GetMethod("ChangeDatabase", new[] { typeof(string) });
                            changeDbMethod?.Invoke(serverConn, new object[] { connInfo.Database });
                        }
                    }

                    // Execute query via ExecuteWithResults or ExecuteReader
                    var execMethod = serverConn.GetType().GetMethod("ExecuteWithResults", new[] { typeof(string) });
                    if (execMethod != null)
                    {
                        string query = @"SELECT name FROM sys.all_objects WHERE type IN ('P', 'PC', 'FN', 'FS', 'FT', 'IF', 'TF', 'AF', 'X')";
                        var ds = execMethod.Invoke(serverConn, new object[] { query }) as DataSet;
                        if (ds != null && ds.Tables.Count > 0)
                        {
                            foreach (DataRow row in ds.Tables[0].Rows)
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

                    if (!string.IsNullOrEmpty(connInfo.Database) &&
                        !string.Equals(connInfo.ActiveConnection.Database, connInfo.Database, StringComparison.OrdinalIgnoreCase))
                    {
                        try { connInfo.ActiveConnection.ChangeDatabase(connInfo.Database); } catch { }
                    }

                    ExecuteMetadataQuery(connInfo.ActiveConnection, newCache);
                    querySuccess = true;

                    if (string.IsNullOrEmpty(connInfo.Database))
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
                            if (!string.IsNullOrEmpty(connInfo.Database) &&
                                !string.Equals(mdsConn.Database, connInfo.Database, StringComparison.OrdinalIgnoreCase))
                            {
                                try { mdsConn.ChangeDatabase(connInfo.Database); } catch { }
                            }

                            ExecuteMetadataQuery(mdsConn, newCache);
                            querySuccess = true;

                            if (string.IsNullOrEmpty(connInfo.Database))
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
                        if (!string.IsNullOrEmpty(connInfo.Database) &&
                            !string.Equals(sqlConn.Database, connInfo.Database, StringComparison.OrdinalIgnoreCase))
                        {
                            try { sqlConn.ChangeDatabase(connInfo.Database); } catch { }
                        }

                        ExecuteMetadataQuery(sqlConn, newCache);
                        querySuccess = true;

                        if (string.IsNullOrEmpty(connInfo.Database))
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
                                if (!string.IsNullOrEmpty(connInfo.Database) &&
                                    !string.Equals(sqlConn.Database, connInfo.Database, StringComparison.OrdinalIgnoreCase))
                                {
                                    try { sqlConn.ChangeDatabase(connInfo.Database); } catch { }
                                }

                                ExecuteMetadataQuery(sqlConn, newCache);
                                querySuccess = true;

                                if (string.IsNullOrEmpty(connInfo.Database))
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

        private void ExecuteMetadataQuery(IDbConnection conn, Dictionary<string, string> targetDict)
        {
            // Query sys.all_objects first to capture all user objects and system objects in the current database
            string query = @"
                SELECT name 
                FROM sys.all_objects 
                WHERE type IN ('P', 'PC', 'FN', 'FS', 'FT', 'IF', 'TF', 'AF', 'X')";

            try
            {
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = query;
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
            }
            catch
            {
                // Fall back to sys.objects in case of restricted permissions
                string fallbackQuery = @"
                    SELECT name 
                    FROM sys.objects 
                    WHERE type IN ('P', 'PC', 'FN', 'FS', 'FT', 'IF', 'TF', 'AF', 'X')";

                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = fallbackQuery;
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
            }
        }

        /// <summary>
        /// Multi-layer resolver to extract active connection and database from SSMS.
        /// Layer 1: ServiceCache.ScriptFactory.CurrentlyActiveWndConnectionInfo & UIConnectionInfoUtil
        /// Layer 2: ISqlEditorService (SSqlEditorService)
        /// </summary>
        public ConnectionContextInfo ResolveActiveConnectionInfo()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var result = new ConnectionContextInfo();

            // Layer 1: ServiceCache.ScriptFactory.CurrentlyActiveWndConnectionInfo
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

                                // 1. Try to resolve Database using UIConnectionInfoUtil.GetDatabaseName(uiConn)
                                string dbName = null;
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
                                        dbName = getDbMethod?.Invoke(null, new object[] { uiConn }) as string;
                                    }
                                    catch { }
                                }

                                // 2. Fall back to AdvancedOptions for Database
                                if (string.IsNullOrEmpty(dbName))
                                {
                                    var advProp = uiConn.GetType().GetProperty("AdvancedOptions", BindingFlags.Public | BindingFlags.Instance);
                                    var advOptions = advProp?.GetValue(uiConn) as System.Collections.Specialized.NameValueCollection;
                                    dbName = advOptions?["DATABASE"] ?? advOptions?["Initial Catalog"];
                                }

                                if (!string.IsNullOrEmpty(dbName) && !string.Equals(dbName, "<default>", StringComparison.OrdinalIgnoreCase))
                                {
                                    result.Database = dbName;
                                }

                                // 3. Try to get SMO ServerConnection & Core Connection Info via UIConnectionInfoUtil
                                if (uiConnUtilType != null)
                                {
                                    try
                                    {
                                        var getCoreMethod = uiConnUtilType.GetMethod("GetCoreConnectionInfo", new[] { uiConn.GetType() });
                                        var coreConnInfo = getCoreMethod?.Invoke(null, new object[] { uiConn });
                                        if (coreConnInfo != null)
                                        {
                                            var serverConnProp = coreConnInfo.GetType().GetProperty("ServerConnection", BindingFlags.Public | BindingFlags.Instance);
                                            var serverConn = serverConnProp?.GetValue(coreConnInfo);
                                            if (serverConn != null)
                                            {
                                                result.ServerConnection = serverConn;
                                            }

                                            // Check database name from core connection info if not set
                                            if (string.IsNullOrEmpty(result.Database))
                                            {
                                                var coreDbProp = coreConnInfo.GetType().GetProperty("DatabaseName", BindingFlags.Public | BindingFlags.Instance);
                                                string coreDb = coreDbProp?.GetValue(coreConnInfo) as string;
                                                if (!string.IsNullOrEmpty(coreDb) && !string.Equals(coreDb, "<default>", StringComparison.OrdinalIgnoreCase))
                                                {
                                                    result.Database = coreDb;
                                                }
                                            }

                                            // Check CreateConnectionObject
                                            try
                                            {
                                                var createConnMethod = coreConnInfo.GetType().GetMethod("CreateConnectionObject", Type.EmptyTypes);
                                                var createdDbConn = createConnMethod?.Invoke(coreConnInfo, null) as IDbConnection;
                                                if (createdDbConn != null)
                                                {
                                                    result.ActiveConnection = createdDbConn;
                                                }
                                            }
                                            catch { }
                                        }
                                    }
                                    catch { }
                                }

                                // 4. Build connection string with full credential extraction (handling InMemoryPassword)
                                if (!string.IsNullOrEmpty(server))
                                {
                                    var authTypeProp = uiConn.GetType().GetProperty("AuthenticationType", BindingFlags.Public | BindingFlags.Instance);
                                    int authType = authTypeProp != null ? (int)authTypeProp.GetValue(uiConn) : 0;

                                    var userProp = uiConn.GetType().GetProperty("UserName", BindingFlags.Public | BindingFlags.Instance);
                                    string userName = userProp?.GetValue(uiConn) as string;

                                    string password = null;
                                    var passProp = uiConn.GetType().GetProperty("Password", BindingFlags.Public | BindingFlags.Instance);
                                    password = passProp?.GetValue(uiConn) as string;

                                    // If password is empty (normal for SSMS SQL Auth), decrypt InMemoryPassword
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
                                        result.ConnectionString = $"Data Source={server};{dbSegment}Integrated Security=True;TrustServerCertificate=True;";
                                    }
                                    else if (authType == 1 && !string.IsNullOrEmpty(userName)) // SQL Server Authentication
                                    {
                                        string passPart = !string.IsNullOrEmpty(password) ? $"Password={password};" : "";
                                        result.ConnectionString = $"Data Source={server};{dbSegment}User ID={userName};{passPart}TrustServerCertificate=True;";
                                    }
                                    else
                                    {
                                        // Azure Active Directory / Entra / Others
                                        result.ConnectionString = $"Data Source={server};{dbSegment}TrustServerCertificate=True;";
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch
            {
                // Proceed to Layer 2
            }

            // Layer 2: Query ISqlEditorService as fallback
            try
            {
                Type ssqlEditorType = null;
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    ssqlEditorType = asm.GetType("Microsoft.SqlServer.Management.UI.VSIntegration.SSqlEditorService");
                    if (ssqlEditorType != null)
                        break;
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
                    var getCurrentConnMethod = editorService.GetType().GetMethod("GetCurrentConnection", BindingFlags.Public | BindingFlags.Instance);
                    if (getCurrentConnMethod != null)
                    {
                        var activeDbConn = getCurrentConnMethod.Invoke(editorService, null) as IDbConnection;
                        if (activeDbConn != null)
                        {
                            if (result.ActiveConnection == null)
                            {
                                result.ActiveConnection = activeDbConn;
                            }
                            if (string.IsNullOrEmpty(result.Database))
                            {
                                result.Database = activeDbConn.Database;
                            }
                            if (string.IsNullOrEmpty(result.ConnectionString))
                            {
                                result.ConnectionString = activeDbConn.ConnectionString;
                            }
                        }
                    }
                }
            }
            catch
            {
                // Fall back
            }

            return result;
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
