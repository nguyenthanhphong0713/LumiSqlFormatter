# Lumi SQL Formatter & Smart Snippets (PHONGNT)

[![SSMS Support](https://img.shields.io/badge/SSMS-18%20%7C%2019%20%7C%2020%20%7C%2022+-007ACC.svg)](https://learn.microsoft.com/en-us/sql/ssms/download-sql-server-management-studio-ssms)
[![VSIX Version](https://img.shields.io/badge/version-1.1.4-2D7D9A.svg)](d:/LumiSqlFormatter/LumiSqlFormatter/bin/Release/LumiSqlFormatter.vsix)
[![Author](https://img.shields.io/badge/author-PHONGNT-107C41.svg)](https://github.com)

**[English](#english) | [Tiếng Việt](#tiếng-việt)**

---

<a name="english"></a>
## English

An enterprise-grade extension for **SQL Server Management Studio (SSMS 18, 19, 20, 22+)** built on the 64-bit Visual Studio Isolated Shell, inspired by the core productivity features of **Redgate SQL Prompt**:

### Key Features

1. **Real-Time Auto-Capitalization:**
   - Automatically formats keywords, data types, and built-in functions as you type upon pressing trigger keys (`Space`, `Enter`, `Tab`, `,`, `;`, `)`).
   - Configurable casing styles: `UPPERCASE`, `lowercase`, or `PascalCase`.

2. **Comprehensive Dictionary (510+ T-SQL Keywords & Functions):**
   - Full T-SQL DQL, DML, and DDL syntax coverage (including `COLUMN`).
   - Window Functions, PIVOT/UNPIVOT, Transactions, Query & Table Hints, and SET Options.
   - 37 Data Types and 180+ T-SQL Built-in Functions.

3. **Strict Multi-Line Lexical Exclusions:**
   - Built on a snapshot-based Lexical State Machine (Microsoft ScriptDom Lexer integration).
   - Safely ignores: multi-line string literals (`'...'`, `N'...'`), escaped quotes (`''`), nested comments (`/* ... */`, `--`), and bracketed or double-quoted identifiers (`[...]`, `"..."`).

4. **T-SQL Variable & Temporary Table Protection:**
   - User-defined local variables (`@Year`, `@Date`, `@Status`, etc.) and temporary tables (`#Year`, `#Temp`) are strictly protected from accidental capitalization.

5. **System & User-Defined Stored Procedures and Functions Protection:**
   - **Pre-loaded System Catalog:** Over 150 standard system stored procedures (`sp_executesql`, `sp_send_dbmail`, `sp_helptext`...), extended procedures (`xp_cmdshell`...), and system functions (`fn_my_permissions`, `fn_virtualfilestats`...) are pre-loaded with their canonical system definition casing, remaining unaltered by Upper or Lower batch formatting or auto-capitalization.
   - **Dynamic Script Scanner:** Automatically strips comments and string literals to accurately scan and preserve the declared casing of procedures and functions (`CREATE/ALTER PROCEDURE/FUNCTION`, `EXEC/EXECUTE`, and schema calls like `dbo.MyFunc(...)`).
   - **Database Metadata Cache & Automatic Re-caching:** Background worker inspects `sys.all_objects` / `sys.objects` from the active SSMS connection into an in-memory cache. Automatically invalidates and re-caches whenever the active database context switches.
   - **Manual Cache Refresh:** Dedicated **Refresh Database Objects Cache** button in Options and context menu command allows on-demand background reloading of metadata without UI interruption.

6. **Smart Snippet Manager:**
   - **Instant Expansion:** Expand code templates immediately using `Tab` or `Enter` after typing the snippet prefix (e.g., `ssf`, `adrop`, `ct`, `ii`...). Supports final cursor positioning via `$CURSOR$`.
   - **Custom Snippet Directory:** Point your snippets library to any custom directory (OneDrive, Git repository, network share). Saved as individual `{prefix}.json` files (Redgate style) for seamless version control and team sharing.
   - **Quick Snippet Creation (`Ctrl+Alt+S`):** Highlight any query in the Query Editor and press `Ctrl+Alt+S` to open an instant snippet creator modal.
   - **IntelliSense Suggestion Popup:** Floating suggestion box appears next to the cursor showing matching snippets with live template preview.

7. **Native Enterprise Options Page:**
   - Deeply integrated into SSMS at **Tools -> Options -> Lumi SQL Formatter & Snippets (PHONGNT)**.
   - Clean, professional styling matching Visual Studio / SSMS design standards (no distracting AI motifs).
   - Responsive Dark / Light theme support for live syntax preview and snippet editors.
   - High-DPI scaling compatible layout with compact, resizable 3-row DataGrid.

8. **Context Menu Batch Formatting, Database Cache & Custom Shortcuts:**
   - **Lumi Format Uppercase (`Ctrl+Alt+U`):** Right-click on selected text (or whole document) -> select **Lumi Format Uppercase (Ctrl+Alt+U)** to convert all keywords, data types, and functions to UPPERCASE, safely preserving comments, string literals, variables, temp tables, and system/user procedures and functions.
   - **Lumi Format Lowercase (`Ctrl+Alt+L`):** Right-click on selected text (or whole document) -> select **Lumi Format Lowercase (Ctrl+Alt+L)** to convert all code text to lowercase, while strictly keeping the original format of `@variables`, table variables (`@TempTable`), `#temp` tables, and system/user procedures and functions.
   - **Refresh Database Objects Cache (`Ctrl+Shift+R`):** Right-click -> select **Lumi: Refresh Database Objects Cache (Ctrl+Shift+R)** to inspect and cache all procedures & functions from the active database in background.
   - **Full Shortcut Explanations in Context Menu:** Context menu items always explicitly display full hotkey descriptions next to each command name, dynamically updating when hotkeys are reconfigured.
   - **Configurable Shortcuts:** View and rebind custom key combinations for Uppercase, Lowercase, and Refresh Cache directly in **Tools -> Options**.

---

### Project Structure

```text
d:\LumiSqlFormatter\
├── LumiSqlFormatter.sln                        # Visual Studio Solution
├── HUONG_DAN_SU_DUNG.md                        # Comprehensive user guide (Vietnamese)
├── README.md                                   # Project overview (EN & VI)
└── LumiSqlFormatter\
    ├── LumiSqlFormatter.csproj                 # VSSDK & SSMS project file
    ├── source.extension.vsixmanifest           # VSIX Manifest (v1.1.4 - PHONGNT)
    ├── ExtensionPackage.cs                     # AsyncPackage registering Top-Menu, ToolWindow, OptionsPage
    ├── ExtensionPackage.vsct                   # Top-Level Menu "Lumi SQL" definitions
    ├── PostProcessVsix.ps1                     # SSMS VsixV2 packaging automation script
    ├── Models/                                 # CasingStyle, FormatterSettings, SnippetItem models
    ├── Services/                               # SettingsService, SqlTokenParsingService, SnippetService
    ├── Editor/                                 # TextViewCreationListener, KeyCommandFilter
    ├── UI/                                     # SettingsToolWindow, OptionsPage, Views, ViewModels
    └── Properties/                             # AssemblyInfo.cs (v1.1.4.0)
```

---

### Installation via .VSIX

The extension is distributed as a standalone **`.vsix`** installation package:

1. Close all running instances of SQL Server Management Studio.
2. Locate the `.vsix` file at:
   ```text
   d:\LumiSqlFormatter\LumiSqlFormatter\bin\Release\LumiSqlFormatter.vsix
   ```
3. Double-click the file to open **VSIX Installer** -> Click **Install** (or **Modify/Update**).
4. Restart SSMS to start using the extension.

> [!NOTE]
> For in-depth technical documentation, keyword catalogs, and version changelogs, please refer to [HUONG_DAN_SU_DUNG.md](file:///d:/LumiSqlFormatter/HUONG_DAN_SU_DUNG.md).

---

<a name="tiếng-việt"></a>
## Tiếng Việt

Extension chuyên nghiệp cho **SQL Server Management Studio (SSMS 18, 19, 20, 22+)** (Visual Studio Shell x64), được thiết kế lấy cảm hứng từ các tính năng cốt lõi của **Redgate SQL Prompt**:

### Tính Năng Nổi Bật

1. **Auto-Capitalization thời gian thực:** Tự động chuẩn hóa từ khóa khi kết thúc từ (`Space`, `Enter`, `Tab`, `,`, `;`, `)`) theo các chuẩn `UPPERCASE`, `lowercase`, hoặc `PascalCase`.
2. **Bộ từ điển 510+ Từ Khóa & Hàm T-SQL:** Bao gồm đầy đủ câu lệnh DQL/DML/DDL (kèm `COLUMN`), Window Functions, Pivot, Transaction, Hints, SET Options, 37 Data Types và 180+ Built-in Functions.
3. **Strict Multi-line Exclusions:** Bộ phân tích từ vựng (Lexical State Machine) trên Buffer Snapshot loại trừ an toàn: chuỗi ký tự nhiều dòng (`'...'`, `N'...'`), escape nháy kép (`''`), comment lồng nhau (`/* ... */`), và định danh trong ngoặc vuông (`[...]`, `"..."`).
4. **Bảo vệ biến T-SQL & Bảng tạm:** Các biến cục bộ `@Year`, `@Date`, `@Status`... và bảng tạm `#Year` không bao giờ bị viết hoa nhầm thành từ khóa SQL.
5. **Quét & Nạp sẵn Stored Procedures & Functions (Database Metadata Cache):**
   - **Nạp sẵn 150+ SP/Func hệ thống:** Nạp sẵn các thủ tục và hàm chuẩn (`sp_executesql`, `sp_send_dbmail`, `xp_cmdshell`, `fn_my_permissions`...) với định dạng canonical của hệ thống; không bị tác động sai lệch khi thực hiện Upper hay Lower.
   - **Quét động thủ tục/hàm trong file (Script Scanner):** Tự động bóc tách và loại bỏ chú thích/chuỗi ký tự, nhận diện khai báo `CREATE/ALTER PROCEDURE`, `CREATE/ALTER FUNCTION`, lời gọi `EXEC/EXECUTE` hoặc `dbo.Func(...)` để bảo toàn format khai báo trên toàn bộ câu lệnh.
   - **Cơ chế Cache Database Metadata & Tự động Re-cache:** Tự động truy vấn ngầm `sys.all_objects` / `sys.objects` từ phiên kết nối SSMS hiện tại; tự động làm mới và xóa cache cũ khi người dùng chuyển sang database khác.
   - **Làm mới Cache chủ động (Manual Refresh):** Bổ sung nút **"Làm Mới Cache Database"** trong Options và lệnh **"Lumi: Refresh Database Objects Cache"** trên Menu chuột phải và menu Lumi SQL.
6. **Smart Snippet Manager:**
   - Kích hoạt nhanh template bằng phím `Tab` hoặc `Enter` khi gõ prefix (ví dụ: `ssf`, `adrop`, `ct`, `ii`...). Hỗ trợ vị trí đặt con trỏ cuối cùng `$CURSOR$`.
   - **Tùy chỉnh thư mục Snippet (Custom Directory):** Cho phép trỏ tới bất kỳ thư mục nào (OneDrive, Git repo, Shared drive...). Lưu dưới dạng từng file `{prefix}.json` độc lập (Redgate style).
   - **Khung nhập nhanh Snippet từ Editor (`Ctrl+Alt+S`):** Bôi đen đoạn code bất kỳ trong Query Editor, nhấn `Ctrl+Alt+S` để mở modal tạo nhanh template ngay lập tức.
   - **Suggestion Popup khi gõ:** Popup IntelliSense nổi lên ngay cạnh con trỏ soạn thảo hiển thị danh sách gợi ý và xem trước code template.
7. **Giao diện quản trị chuẩn mực:** Tích hợp trực tiếp vào **Tools -> Options -> Lumi SQL Formatter**, giao diện thiết kế theo phong cách native enterprise của Visual Studio / SSMS, hỗ trợ lưới 3 dòng thu gọn tối ưu không gian, giao diện Dark Mode / Light Mode sắc nét và nút Làm mới.
8. **Định dạng hàng loạt, Nạp Cache Database & Phím tắt cấu hình được:**
   - **Lumi Format Uppercase (`Ctrl+Alt+U`):** Chuột phải vào vùng chọn (hoặc toàn bộ file) -> chọn **Lumi Format Uppercase (Ctrl+Alt+U)** để viết hoa toàn bộ từ khóa, hàm, kiểu dữ liệu (tương tự auto-capitalization), bảo vệ chuỗi, chú thích, biến, bảng tạm, thủ tục/hàm hệ thống và tự định nghĩa.
   - **Lumi Format Lowercase (`Ctrl+Alt+L`):** Chuột phải vào vùng chọn -> chọn **Lumi Format Lowercase (Ctrl+Alt+L)** để chuyển mã sang chữ thường, đồng thời giữ nguyên định dạng của `@biến`, biến bảng tạm (`@TempTable`), `#bảng tạm` (`#TempTable`), thủ tục/hàm hệ thống và hàm tự khai báo.
   - **Làm mới Cache Database (`Ctrl+Shift+R`):** Chuột phải -> chọn **Lumi: Refresh Database Objects Cache (Ctrl+Shift+R)** để quét ngầm toàn bộ metadata thủ tục & hàm của database active.
   - **Diễn giải phím tắt đầy đủ trên Context Menu:** Mọi mục menu chuột phải đều hiển thị rõ ràng tổ hợp phím tắt đi kèm ngay cạnh tên lệnh, tự động đồng bộ khi người dùng đổi phím tắt.
   - **Cấu hình phím tắt linh hoạt:** Cho phép xem và gán tổ hợp phím tắt tùy ý cho cả 3 thao tác trực tiếp tại **Tools -> Options**.

---

### Hướng Dẫn Cài Đặt Qua File .VSIX

Gói extension được phân phối trực tiếp dưới dạng file cài đặt chuẩn **`.vsix`**:

1. Đóng hoàn toàn các cửa sổ SSMS đang chạy.
2. Mở file `.vsix` tại đường dẫn:
   ```text
   d:\LumiSqlFormatter\LumiSqlFormatter\bin\Release\LumiSqlFormatter.vsix
   ```
3. Nhấp đúp chuột vào file để khởi chạy **VSIX Installer** -> Chọn **Install** (hoặc **Modify/Update**).
4. Khởi động lại SSMS để bắt đầu sử dụng.

> [!NOTE]
> Để xem chi tiết hướng dẫn sử dụng tất cả tính năng, danh mục 510+ từ khóa, và nhật ký các phiên bản, vui lòng xem tại: [HUONG_DAN_SU_DUNG.md](file:///d:/LumiSqlFormatter/HUONG_DAN_SU_DUNG.md).
