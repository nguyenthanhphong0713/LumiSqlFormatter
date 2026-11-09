# HƯỚNG DẪN SỬ DỤNG & TÀI LIỆU KỸ THUẬT TOÀN DIỆN
# LUMI SQL FORMATTER & SMART SNIPPETS (PHONGNT)

> **Phiên bản:** 1.1.5  
> **Tác giả:** PHONGNT  
> **Dự án:** `LumiSqlFormatter.Extension.2026`  
> **Môi trường hỗ trợ:** SQL Server Management Studio (SSMS 18, 19, 20, 22+ x64)

---

## MỤC LỤC
1. [Giới thiệu tổng quan](#1-giới-thiệu-tổng-quan)
2. [Hướng dẫn cài đặt qua file .VSIX](#2-hướng-dẫn-cài-đặt-qua-file-vsix)
3. [Tự động viết hoa từ khóa (Real-time Auto-Capitalize)](#3-tự-động-viết-hoa-từ-khóa-real-time-auto-capitalize)
   - 3.1. Các chế độ định dạng Casing
   - 3.2. Cơ chế loại trừ đa dòng thông minh (Strict Multi-line Exclusions)
   - 3.3. Bảo vệ biến và bảng tạm (`@Year`, `#TempTable`)
   - 3.4. Danh mục hơn 510 từ khóa, kiểu dữ liệu và hàm T-SQL
4. [Quản lý & Sử dụng Snippets (Smart Snippet Manager)](#4-quản-lý--sử-dụng-snippets-smart-snippet-manager)
   - 4.1. Thư mục Snippets tùy biến & Định dạng JSON độc lập
   - 4.2. Gợi ý tự động khi gõ (IntelliSense Suggestion Popup)
   - 4.3. Tạo nhanh Snippet từ Editor (`Ctrl + Alt + S`)
   - 4.4. Trình quản lý chi tiết trong Tools -> Options (Lưới 3 dòng, Theme Sáng/Tối)
5. [Hướng dẫn gỡ cài đặt (Uninstall)](#5-hướng-dẫn-gỡ-cài-đặt-uninstall)
6. [Cấu trúc kỹ thuật của dự án](#6-cấu-trúc-kỹ-thuật-của-dự-án)
7. [Nhật ký cập nhật các phiên bản (Changelog)](#7-nhật-ký-cập-nhật-các-phiên-bản-changelog)

---

## 1. GIỚI THIỆU TỔNG QUAN

**Lumi SQL Formatter (PHONGNT)** là tiện ích mở rộng (extension) chuyên nghiệp dành cho SQL Server Management Studio (SSMS), mang lại trải nghiệm viết mã T-SQL hiện đại, chuẩn chỉ, lấy cảm hứng từ các tính năng cốt lõi của **Redgate SQL Prompt**:

- **Tự động viết hoa từ khóa thời gian thực (Real-time Auto-Capitalize):** Tự động chuẩn hóa chữ hoa/chữ thường cho Keywords, Data Types và Built-in Functions ngay khi gõ phím cách (`Space`), `Enter`, `Tab`, phẩy (`,`), chấm phẩy (`;`), hoặc đóng ngoặc (`)`).
- **Loại trừ ngữ cảnh đa dòng chính xác tuyệt đối (Strict Multi-line Exclusions):** Sử dụng bộ phân tích từ vựng (Lexical State Machine) quét trực tiếp trên Buffer Snapshot, nhận diện chính xác chuỗi ký tự nhiều dòng, chú thích lồng nhau, định danh trong ngoặc vuông và nháy kép.
- **Bảo vệ biến T-SQL:** Biến cục bộ có tiền tố `@` (như `@Year`, `@Date`, `@Status`) và bảng tạm `#` (như `#Year`) không bao giờ bị viết hoa nhầm thành từ khóa SQL.
- **Snippet Engine thông minh:** Gợi ý tức thì (suggestion popup), mở rộng mã mẫu bằng phím `Tab` hoặc `Enter`, hỗ trợ con trỏ `$CURSOR$`.
- **Thư mục Snippet tùy biến (Redgate-style):** Mỗi snippet được lưu thành file `{prefix}.json` riêng lẻ trong thư mục bạn chỉ định, dễ dàng đồng bộ qua Git/OneDrive.
- **Tích hợp sâu vào SSMS:** Quản trị cấu hình toàn diện tại menu **Tools -> Options -> Lumi SQL Formatter** (hỗ trợ full-screen, lưới 3 dòng tối ưu không gian, giao diện Dark/Light mode, phong cách enterprise chuẩn mực).

---

## 2. HƯỚNG DẪN CÀI ĐẶT QUA FILE .VSIX

Gói cài đặt VSIX được đóng gói chuẩn **VsixV2 (SSMS Isolated Shell)**, tương thích hoàn toàn với SSMS 18, 19, 20 và 22+.

### Các bước cài đặt:
1. Đóng hoàn toàn các cửa sổ SSMS đang chạy.
2. Mở thư mục chứa file cài đặt:
   ```text
   d:\LumiSqlFormatter\LumiSqlFormatter\bin\Release\LumiSqlFormatter.vsix
   ```
3. **Nhấp đúp chuột** vào file `LumiSqlFormatter.vsix`.
4. Trình cài đặt `VSIX Installer` sẽ hiển thị: **Lumi SQL Formatter & Snippets (PHONGNT)** -> Nhấn **Install** (hoặc **Modify/Update** nếu cài đè).
5. Mở lại SSMS để bắt đầu sử dụng.

---

## 3. TỰ ĐỘNG VIẾT HOA TỪ KHÓA (REAL-TIME AUTO-CAPITALIZE)

### 3.1. Các chế độ định dạng Casing
Vào menu **Tools** -> **Options** -> **Lumi SQL Formatter** -> tab **Formatting & Casing**:

| Tùy chọn | Mô tả | Giá trị hỗ trợ |
| :--- | :--- | :--- |
| **Bật Tự động Viết hoa** | Bật/Tắt tính năng tự động viết hoa khi gõ phím | `Bật` / `Tắt` |
| **Keywords Casing** | Kiểu chữ cho từ khóa lệnh SQL (`SELECT`, `FROM`, `WHERE`, `JOIN`...) | `UPPERCASE`, `lowercase`, `PascalCase` |
| **Data Types Casing** | Kiểu chữ cho kiểu dữ liệu (`INT`, `VARCHAR`, `DATETIME2`...) | `UPPERCASE`, `lowercase`, `PascalCase` |
| **Functions Casing** | Kiểu chữ cho hàm có sẵn (`COUNT`, `ISNULL`, `GETDATE`, `STRING_AGG`...) | `UPPERCASE`, `lowercase`, `PascalCase` |

> [!TIP]
> **Các phím kích hoạt AutoUpperCase:**  
> Hệ thống sẽ kích hoạt kiểm tra và định dạng từ ngay trước con trỏ khi bạn gõ: **Phím cách (`Space`)**, **Phím Enter**, **Phím Tab**, **Dấu phẩy (`,`)**, **Dấu chấm phẩy (`;`)**, hoặc **Dấu đóng ngoặc (`)`)**.

---

### 3.2. Cơ chế loại trừ đa dòng thông minh (Strict Multi-line Exclusions)
Trong các phiên bản trước, việc chỉ phân tích dòng hiện tại dẫn đến lỗi: khi xuống dòng trong chuỗi ký tự nhiều dòng, từ khóa như `and` bị viết hoa sai thành `AND`.

Từ phiên bản 1.1.1+, Lumi SQL Formatter sử dụng **Lexical State Machine** chuyên biệt cho T-SQL quét trên toàn bộ Buffer Snapshot:
- **Chuỗi ký tự nhiều dòng (`'...'`, `N'...'`):** Nhận biết chuỗi mở kéo dài qua nhiều dòng (`\r\n`) cho đến khi gặp dấu nháy đóng hợp lệ.
- **Escape nháy kép (`''`):** Cặp nháy kép thoát bên trong chuỗi (ví dụ: `''''`) không làm ngắt chuỗi.
- **Chú thích nhiều dòng (`/* ... */`):** Theo dõi độ sâu chú thích, hỗ trợ cả chú thích lồng nhau (`/* /* nested */ */`).
- **Định danh trong ngoặc vuông (`[...]`) & nháy kép (`"..."`):** Không can thiệp vào tên cột/bảng đặt trong ngoặc.
- **Hiệu năng:** Quét $O(N)$ bằng C# thuần, xử lý tài liệu 500.000 ký tự chỉ mất **< 1 mili-giây** (hoàn toàn mượt mà, không giật lag).

---

### 3.3. Bảo vệ biến và bảng tạm (`@Year`, `#TempTable`)
- **Biến T-SQL (`@variable`):** Khi bạn khai báo hoặc sử dụng biến có tên trùng với từ khóa/hàm (ví dụ: `@Year`, `@Date`, `@Month`, `@Status`, `@Type`, `@Count`), hệ thống phát hiện tiền tố `@` và **giữ nguyên đúng chữ bạn gõ, không tự động in hoa**.
- **Hàm chuẩn:** Khi viết `YEAR(GetDate())`, do không có tiền tố `@`, chữ `YEAR` vẫn được tự động viết hoa chuẩn xác.
- **Bảng tạm (`#table`):** Tên bảng tạm bắt đầu bằng `#` (như `#Year`) cũng được bảo vệ an toàn.

---

### 3.4. Danh mục hơn 510 từ khóa, kiểu dữ liệu và hàm T-SQL
Hệ thống tích hợp sẵn từ điển **hơn 510 từ khóa độc nhất**, phân loại thành 3 nhóm:

1. **Cú pháp, Mệnh đề & Hints (280+ từ):**
   - **DQL / DML / DDL:** `SELECT`, `FROM`, `WHERE`, `GROUP`, `BY`, `HAVING`, `ORDER`, `ASC`, `DESC`, `INSERT`, `INTO`, `VALUES`, `UPDATE`, `SET`, `DELETE`, `MERGE`, `MATCHED`, `TARGET`, `SOURCE`, `USING`, `OUTPUT`, `DELETED`, `INSERTED`, `ACTION`, `TOP`, `DISTINCT`, `PERCENT`, `TIES`...
   - **Mệnh đề DDL nâng cao:** `CREATE`, `ALTER`, `DROP`, `TRUNCATE`, `TABLE`, `COLUMN`, `VIEW`, `PROCEDURE`, `PROC`, `FUNCTION`, `TRIGGER`, `INDEX`, `DATABASE`, `SCHEMA`, `SEQUENCE`, `SYNONYM`, `TYPE`...
   - **Joins & Set Operators:** `JOIN`, `INNER`, `LEFT`, `RIGHT`, `FULL`, `CROSS`, `OUTER`, `APPLY`, `ON`, `UNION`, `ALL`, `EXCEPT`, `INTERSECT`...
   - **Logic & Điều kiện:** `CASE`, `WHEN`, `THEN`, `ELSE`, `END`, `AND`, `OR`, `NOT`, `IN`, `IS`, `NULL`, `LIKE`, `BETWEEN`, `EXISTS`, `SOME`, `ANY`, `COLLATE`, `ESCAPE`...
   - **Phân trang & Windowing:** `OVER`, `PARTITION`, `ROWS`, `RANGE`, `UNBOUNDED`, `PRECEDING`, `FOLLOWING`, `CURRENT`, `ROW`, `OFFSET`, `FETCH`, `NEXT`, `ONLY`, `FIRST`, `LAST`...
   - **Pivot & Transform:** `PIVOT`, `UNPIVOT`, `TABLESAMPLE`...
   - **Giao dịch & Luồng:** `BEGIN`, `TRAN`, `TRANSACTION`, `COMMIT`, `ROLLBACK`, `SAVE`, `EXEC`, `EXECUTE`, `DECLARE`, `AS`, `WITH`, `IF`, `WHILE`, `BREAK`, `CONTINUE`, `RETURN`, `RETURNS`, `GOTO`, `WAITFOR`, `DELAY`...
   - **Xử lý lỗi:** `TRY`, `CATCH`, `THROW`, `RAISERROR`, `PRINT`...
   - **Ràng buộc & Chỉ mục:** `PRIMARY`, `FOREIGN`, `REFERENCES`, `CONSTRAINT`, `CHECK`, `UNIQUE`, `CLUSTERED`, `NONCLUSTERED`, `COLUMNSTORE`, `CASCADE`, `RESTRICT`, `NOCHECK`, `INCLUDE`, `FILLFACTOR`, `PAD_INDEX`, `DATA_COMPRESSION`, `ONLINE`, `OFFLINE`, `PERSISTED`, `SPARSE`, `IDENTITY`, `IDENTITY_INSERT`...
   - **Lock & Query Hints:** `OPTION`, `RECOMPILE`, `MAXRECURSION`, `OPTIMIZE`, `FAST`, `FORCE`, `LOOP`, `HASH`, `NOLOCK`, `READUNCOMMITTED`, `READCOMMITTED`, `REPEATABLEREAD`, `SERIALIZABLE`, `READPAST`, `XLOCK`, `UPDLOCK`, `ROWLOCK`, `PAGLOCK`, `TABLOCK`, `TABLOCKX`, `HOLDLOCK`, `NOWAIT`...
   - **Thiết lập phiên làm việc (SET):** `NOCOUNT`, `XACT_ABORT`, `ANSI_NULLS`, `QUOTED_IDENTIFIER`, `ARITHABORT`, `NUMERIC_ROUNDABORT`, `ANSI_PADDING`, `ANSI_WARNINGS`, `ISOLATION`, `LEVEL`, `SNAPSHOT`, `STATISTICS`, `IO`, `PROFILE`, `OFF`, `ON`, `GO`, `USE`...
   - **Cursor, XML, JSON & Full-Text:** `CURSOR`, `OPEN`, `CLOSE`, `DEALLOCATE`, `PATH`, `AUTO`, `EXPLICIT`, `RAW`, `ELEMENTS`, `ROOT`, `OPENROWSET`, `OPENDATASOURCE`, `OPENQUERY`, `OPENXML`, `OPENJSON`, `CONTAINS`, `FREETEXT`, `READONLY`, `OUT`...

2. **Kiểu dữ liệu T-SQL (37 kiểu):**
   - `INT`, `INTEGER`, `BIGINT`, `SMALLINT`, `TINYINT`, `BIT`
   - `DECIMAL`, `NUMERIC`, `MONEY`, `SMALLMONEY`, `FLOAT`, `REAL`
   - `DATE`, `DATETIME`, `DATETIME2`, `DATETIMEOFFSET`, `SMALLDATETIME`, `TIME`
   - `CHAR`, `CHARACTER`, `VARCHAR`, `NCHAR`, `NVARCHAR`, `TEXT`, `NTEXT`
   - `BINARY`, `VARBINARY`, `IMAGE`
   - `UNIQUEIDENTIFIER`, `XML`, `SQL_VARIANT`, `GEOMETRY`, `GEOGRAPHY`, `HIERARCHYID`, `ROWVERSION`, `TIMESTAMP`, `SYSNAME`, `JSON`, `VECTOR`

3. **Hàm tích hợp & Hệ thống (180+ hàm):**
   - **Tập hợp & Cửa sổ:** `COUNT`, `COUNT_BIG`, `SUM`, `AVG`, `MIN`, `MAX`, `STRING_AGG`, `APPROX_COUNT_DISTINCT`, `CHECKSUM_AGG`, `STDEV`, `VAR`, `GROUPING`, `ROW_NUMBER`, `RANK`, `DENSE_RANK`, `NTILE`, `LEAD`, `LAG`, `FIRST_VALUE`, `LAST_VALUE`, `PERCENT_RANK`, `CUME_DIST`...
   - **Xử lý chuỗi:** `ASCII`, `CHAR`, `CHARINDEX`, `CONCAT`, `CONCAT_WS`, `DIFFERENCE`, `FORMAT`, `LEFT`, `LEN`, `LOWER`, `LTRIM`, `NCHAR`, `PATINDEX`, `QUOTENAME`, `REPLACE`, `REPLICATE`, `REVERSE`, `RIGHT`, `RTRIM`, `SOUNDEX`, `SPACE`, `STR`, `STRING_ESCAPE`, `STRING_SPLIT`, `STUFF`, `SUBSTRING`, `TRANSLATE`, `TRIM`, `UNICODE`, `UPPER`, `DATALENGTH`...
   - **Ngày giờ:** `GETDATE`, `GETUTCDATE`, `SYSDATETIME`, `SYSUTCDATETIME`, `SYSDATETIMEOFFSET`, `DATEADD`, `DATEDIFF`, `DATEDIFF_BIG`, `DATEPART`, `DATENAME`, `DATEFROMPARTS`, `EOMONTH`, `ISDATE`, `YEAR`, `MONTH`, `DAY`...
   - **Chuyển đổi & Điều kiện:** `CAST`, `CONVERT`, `PARSE`, `TRY_CAST`, `TRY_CONVERT`, `TRY_PARSE`, `ISNULL`, `COALESCE`, `NULLIF`, `IIF`, `CHOOSE`, `GREATEST`, `LEAST`...
   - **Toán học:** `ABS`, `ACOS`, `ASIN`, `ATAN`, `CEILING`, `COS`, `EXP`, `FLOOR`, `LOG`, `LOG10`, `PI`, `POWER`, `RAND`, `ROUND`, `SIGN`, `SIN`, `SQRT`, `SQUARE`, `TAN`...
   - **Metadata & Đối tượng:** `SCOPE_IDENTITY`, `IDENT_CURRENT`, `OBJECT_ID`, `OBJECT_NAME`, `OBJECT_SCHEMA_NAME`, `OBJECT_DEFINITION`, `OBJECTPROPERTY`, `COLUMNPROPERTY`, `COL_LENGTH`, `INDEXPROPERTY`, `DATABASEPROPERTYEX`, `DB_ID`, `DB_NAME`, `SCHEMA_ID`, `SCHEMA_NAME`, `TYPE_ID`, `SERVERPROPERTY`, `USER_ID`, `USER_NAME`, `APP_NAME`, `NEWID`, `NEWSEQUENTIALID`, `ERROR_MESSAGE`, `ROWCOUNT`, `TRANCOUNT`, `SPID`...

---

## 4. QUẢN LÝ & SỬ DỤNG SNIPPETS (SMART SNIPPET MANAGER)

### 4.1. Thư mục Snippets tùy biến & Định dạng JSON độc lập
Bạn có thể trỏ thư mục lưu trữ snippet đến bất kỳ đâu (ví dụ: `D:\Work\MySnippets` hoặc thư mục đồng bộ OneDrive/Git).

Mỗi snippet được lưu độc lập dưới dạng một file `{prefix}.json`:
```json
{
  "id": "1dd28f3b-f3ef-4868-b3db-b502a682f22f",
  "prefix": "adrop",
  "description": "Drop toàn bộ bảng tạm khi debug",
  "body": "-- Drop bảng tạm khi debug\ndeclare @zsql nvarchar(max); select @zsql = isnull(@zsql + ';', '') + 'drop table ' + quotename(name) from tempdb..sysobjects where name like '#%' and OBJECT_ID('tempdb..' + name) is not null; exec (@zsql)"
}
```

- **`prefix`**: Từ viết tắt để kích hoạt snippet (ví dụ: `ssf`, `adrop`, `ct`, `ii`).
- **`description`**: Mô tả chức năng hiển thị trong suggestion popup.
- **`body`**: Đoạn mã mẫu. Hỗ trợ biến `$CURSOR$` xác định vị trí con trỏ chuột dừng lại sau khi bung mã.

---

### 4.2. Gợi ý tự động khi gõ (IntelliSense Suggestion Popup)
1. Trong Query Editor của SSMS, gõ các ký tự đầu của prefix (ví dụ: gõ `a` hoặc `s`).
2. Một popup gợi ý nổi lên ngay dưới con trỏ:
   - Cột trái: Danh sách các snippet phù hợp kèm badge shortcut nổi bật.
   - Cột phải: Khung xem trước code template hoàn chỉnh.
   - Dùng phím **`Up` / `Down`** để chọn snippet.
   - Nhấn **`Tab` hoặc `Enter`** để chèn toàn bộ đoạn code vào editor.
   - Nhấn **`Esc`** để đóng popup gợi ý.

---

### 4.3. Tạo nhanh Snippet từ Editor (`Ctrl + Alt + S`)
1. Trong Query Editor, bôi đen đoạn mã SQL bạn muốn lưu làm template.
2. Nhấn tổ hợp phím **`Ctrl + Alt + S`**.
3. Cửa sổ **Tạo Snippet - Lumi SQL** sẽ mở ra với đoạn code đã được điền sẵn:
   - Nhập **Prefix** (ví dụ: `myquery`).
   - Nhập **Mô tả**.
   - Bấm nút **Chèn $CURSOR$** tại vị trí con trỏ mong muốn.
   - Nhấn **`Enter`** hoặc bấm **Lưu Snippet**.

---

### 4.4. Trình quản lý chi tiết trong Tools -> Options
Vào menu **Tools** -> **Options** -> **Lumi SQL Formatter** -> tab **Snippet Manager**:

- **Khắc phục triệt để lỗi nhập liệu:** Sử dụng `UIElementDialogPage` cùng bộ hook `WM_GETDLGCODE` chuyên sâu, cho phép gõ phím trực tiếp vào các ô Prefix, Mô tả và Body mà không bị chặn bởi Win32 Modal Dialog của SSMS.
- **Lưới hiển thị cao hơn & Thanh phân tách tùy biến (Resizable DataGrid):** Ô lưới snippets có chiều cao mở rộng mặc định 190px (nhìn rõ 6-7 dòng cùng lúc), tích hợp thanh kéo **GridSplitter** cho phép người dùng tùy ý kéo giãn chiều cao lưới để xem được nhiều snippets hơn khi cần.
- **Chèn biến `$CURSOR$` chính xác tại vị trí con trỏ:** Bấm nút **Chèn $CURSOR$** sẽ chèn ngay tại vị trí trỏ chuột / con trỏ văn bản đang đặt (hoặc thay thế đoạn bôi đen), thay vì tự động đẩy về cuối văn bản như trước.
- **Tùy chọn Theme Sáng / Tối (Theme Mode):** Cho phép chọn chế độ **Light Mode** hoặc **Dark Mode** với độ tương phản cao, chữ sắc nét, dễ nhìn.
- **Nút Làm mới:** Bấm nút **Làm mới** để quét lại toàn bộ thư mục snippet nếu bạn vừa pull code từ Git hoặc chỉnh sửa file từ bên ngoài.
- **Mặc định không cài đặt snippet mẫu:** Hệ thống để danh sách trống để bạn hoàn toàn chủ động xây dựng bộ snippet cá nhân hoặc trỏ đến kho snippet của riêng mình.

---

## 5. HƯỚNG DẪN GỠ CÀI ĐẶT (UNINSTALL)

Bạn có thể gỡ cài đặt extension theo một trong hai cách chuẩn của SSMS:

### Cách 1: Gỡ bỏ từ hộp thoại Tools -> Options (Nhanh nhất)
1. Trong SSMS, mở menu **Tools** -> **Options**.
2. Chọn nhánh **Lumi SQL Formatter** -> tab **Formatting & Casing**.
3. Nhấn nút **Gỡ Cài Đặt Extension**.
4. Xác nhận gỡ bỏ và lựa chọn có giữ lại thư mục Snippets cá nhân hay không. Hệ thống sẽ tự động dọn sạch thư viện và làm mới bộ nhớ đệm cache của SSMS.

### Cách 2: Gỡ bỏ qua trình quản lý Extensions của SSMS
1. Trong SSMS, mở menu **Extensions** -> **Manage Extensions**.
2. Tìm **Lumi SQL Formatter & Snippets (PHONGNT)** trong danh sách đã cài đặt (Installed).
3. Nhấn **Uninstall**.
4. Đóng SSMS để VSIX Installer tiến hành gỡ bỏ tự động.

---

## 6. CẤU TRÚC KỸ THUẬT CỦA DỰ ÁN

```text
d:\LumiSqlFormatter\
├── LumiSqlFormatter.sln                        # Visual Studio Solution
├── HUONG_DAN_SU_DUNG.md                        # Tài liệu hướng dẫn sử dụng toàn diện
├── README.md                                   # Giới thiệu tổng quan dự án
└── LumiSqlFormatter\
    ├── LumiSqlFormatter.csproj                 # Cấu hình VSSDK, SSMS References
    ├── source.extension.vsixmanifest           # VSIX Manifest (v1.1.4 - PHONGNT)
    ├── ExtensionPackage.cs                     # AsyncPackage đăng ký Menu, ToolWindow, OptionsPage
    ├── ExtensionPackage.vsct                   # Khai báo Top-Level Menu "Lumi SQL"
    ├── PostProcessVsix.ps1                     # Script đóng gói chuẩn SSMS VsixV2
    ├── Models/
    │   ├── CasingStyle.cs                      # Enum: Uppercase, Lowercase, PascalCase
    │   ├── FormatterSettings.cs                # Model cài đặt Casing & Snippets
    │   └── SnippetItem.cs                      # Model Snippet (Id, Prefix, Description, Body)
    │   └── SnippetTheme.cs                     # Enum: Dark, Light
    ├── Services/
    │   ├── SettingsService.cs                  # Quản lý đọc/ghi JSON tại folder tùy biến
    │   ├── ShortcutHelper.cs                   # Xử lý phân tích và bắt phím tắt linh hoạt
    │   ├── SqlTokenParsingService.cs           # Lexical State Scanner & 510+ tokens dictionary
    │   └── SnippetService.cs                   # Mở rộng template, tính toán $CURSOR$, prefix match
    ├── Editor/
    │   ├── TextViewCreationListener.cs         # MEF Component gắn KeyCommandFilter vào Query Editor
    │   └── KeyCommandFilter.cs                 # IOleCommandTarget bắt phím Space, Enter, Tab, ), v.v.
    ├── UI/
    │   ├── SettingsToolWindow.cs               # ToolWindow nhúng SettingsView
    │   ├── OptionsPage.cs                      # UIElementDialogPage trong Tools -> Options
    │   ├── Views/
    │   │   ├── SettingsView.xaml (.cs)         # Giao diện chính (Lưới mở rộng, GridSplitter, Theme Dark/Light)
    │   │   ├── QuickSnippetWindow.xaml (.cs)   # Modal tạo nhanh snippet từ editor (Ctrl+Alt+S)
    │   │   └── SnippetSuggestionPopup.xaml     # Popup gợi ý snippet cạnh con trỏ
    │   └── ViewModels/
    │       ├── ViewModelBase.cs / RelayCommand.cs
    │       ├── QuickSnippetViewModel.cs
    │       └── SettingsViewModel.cs            # Binding logic, CRUD snippets, Theme logic
    └── Properties/
        └── AssemblyInfo.cs                     # Version 1.1.5.0
```

---

## 7. NHẬT KÝ CẬP NHẬT CÁC PHIÊN BẢN (CHANGELOG)

### Phiên bản 1.1.5 (Bản hiện tại)
- **Sửa lỗi đếm sai số lượng Stored Procedures & Functions:** Truy vấn trực tiếp sys.objects thay vì sys.all_objects để lấy chính xác các đối tượng của riêng database hiện tại, loại trừ hơn 2.000 đối tượng hệ thống tĩnh.
- **Phân giải Database active đa tầng:** Tự động nhận diện chính xác database active theo cửa sổ Query Editor (qua Window Caption, ISqlEditorService, SMO ServerConnection) ngay khi chuyển dropdown hoặc tab.

### Phiên bản 1.1.4
- **Bổ sung tính năng định dạng & quản lý Cache trong Context Menu (Chuột phải trong Editor) & Menu chính "Lumi SQL":**
  - **Lumi Format Uppercase (`Ctrl+Alt+U`):** Quét toàn bộ khối mã được bôi đen (hoặc toàn bộ file nếu không bôi đen) và chuyển toàn bộ từ khóa, kiểu dữ liệu, hàm T-SQL sang IN HOA giống như tính năng auto-capitalization, bảo vệ an toàn chuỗi ký tự, chú thích, biến `@Year` và bảng tạm `#Temp`.
  - **Lumi Format Lowercase (`Ctrl+Alt+L`):** Chuyển toàn bộ mã sang chữ thường, đồng thời giữ nguyên định dạng gốc của `@variables`, biến bảng tạm `Temp variable table` (`@TempTable`), `#temp` tables (`#TempTable`), chuỗi ký tự dữ liệu `'...'` và chú thích.
  - **Lumi: Refresh Database Objects Cache (`Ctrl+Shift+R`):** Quét và nạp lại ngầm toàn bộ danh mục thủ tục và hàm của database hiện tại.
  - **Diễn giải phím tắt đầy đủ trên Context Menu:** Mọi mục menu chuột phải đều thể hiện rõ ràng và đầy đủ phím tắt đi kèm ngay cạnh tên lệnh: `Lumi Format Uppercase (Ctrl+Alt+U)`, `Lumi Format Lowercase (Ctrl+Alt+L)` và `Lumi: Refresh Database Objects Cache (Ctrl+Shift+R)`, tự động cập nhật ngay khi người dùng đổi phím tắt.
- **Quét & Nạp sẵn Stored Procedures & Functions & Database Metadata Cache chủ động:**
  - **Nạp sẵn danh mục chuẩn hệ thống:** Nạp sẵn hơn 150 thủ tục lưu trữ hệ thống (`sp_executesql`, `sp_send_dbmail`, `sp_helptext`, `sp_who`, `sp_rename`...), thủ tục mở rộng (`xp_cmdshell`, `xp_fixeddrives`...) và hàm hệ thống (`fn_my_permissions`, `fn_virtualfilestats`, `fn_trace_gettable`...). Các đối tượng này luôn giữ nguyên format chuẩn trong định nghĩa của hệ thống và không bị tác động sai lệch bởi Upper hay Lower.
  - **Bộ quét động hàm và thủ tục (Script Scanner):** Tự động bóc tách và loại trừ an toàn chuỗi ký tự và chú thích trước khi quét cú pháp; nhận diện chính xác định dạng gốc trong các câu lệnh khai báo (`CREATE/ALTER PROCEDURE`, `CREATE/ALTER FUNCTION`, `EXEC/EXECUTE`, `dbo.Func(...)`) để giữ nguyên định dạng trong định nghĩa trên toàn bộ kịch bản.
  - **Cơ chế Cache metadata Database active (Database Metadata Cache):** Tự động truy vấn ngầm `sys.all_objects` / `sys.objects` từ phiên kết nối SSMS hiện tại để nạp toàn bộ danh mục thủ tục và hàm của database vào bộ nhớ cache in-memory.
  - **Tự động nhận diện thay đổi Database (Context Switch Re-cache):** Khi người dùng chuyển sang kết nối hoặc database khác, cache cũ sẽ tự động được xóa và quét lại (Re-cache) cho phù hợp với database mới.
  - **Làm mới Cache chủ động (Manual Refresh):** Bổ sung nút **"Làm Mới Cache Database"** trong **Tools -> Options** và lệnh **"Lumi: Refresh Database Objects Cache"** trên Menu chuột phải (Context Menu) cũng như Menu chính "Lumi SQL" để người dùng chủ động nạp lại metadata ngầm bất cứ lúc nào mà không gián đoạn thao tác soạn thảo.
- **Tùy biến phím tắt thao tác (Hotkeys & Shortcuts Configuration):** Tích hợp khung cấu hình và bộ ghi nhận phím thông minh (Key recorder) trực tiếp trong **Tools -> Options**, cho phép người dùng tùy ý gán và đổi tổ hợp phím tắt cho cả 4 thao tác: Format Uppercase (`Ctrl+Alt+U`), Format Lowercase (`Ctrl+Alt+L`), Refresh DB Cache (`Ctrl+Shift+R`) và Tạo nhanh Snippet (`Ctrl+Alt+S`).
- **Sửa triệt để hiện tượng tràn và cắt chữ (Text Clipping) trên giao diện SSMS:** Tối ưu hóa các nhãn, combobox theme, thanh công cụ thư mục snippet và bổ sung cuộn dọc mượt mà trên mọi tỷ lệ màn hình và DPI scaling.

---

### Phiên bản 1.1.3
- **Live Preview đồng bộ Theme Light/Dark & Tô màu cú pháp (Syntax Highlighting):** Khung xem trước Live Preview nay tự động chuyển đổi nền và màu sắc theo Theme Light/Dark đã chọn, hỗ trợ tô màu cú pháp chuẩn xác (Keywords xanh dương, Data Types xanh ngọc, Functions vàng đất, Comments xanh lá, Strings đỏ tươi) giúp kiểm tra trực quan, sinh động.
- **Hiện đại hóa giao diện cấu hình chuẩn Enterprise (Không AI, không emoji):** Thiết kế lại thanh tiêu đề với thanh chỉ thị thương hiệu và huy hiệu phiên bản sắc nét; bổ sung các thẻ badge phân loại màu sắc (`KEYWORD`, `DATA TYPE`, `FUNCTION`), phím kích hoạt dạng chip hiện đại và nút bấm đồng bộ màu sắc chuẩn mực.
- **Mở rộng ô lưới Snippets & Hỗ trợ GridSplitter:** Chiều cao ô lưới snippets được tăng lên 190px (hiển thị 6-7 snippet cùng lúc), bổ sung thanh kéo phân tách linh hoạt giúp xem được nhiều snippets theo kích thước màn hình.
- **Chèn `$CURSOR$` ngay tại vị trí trỏ chuột:** Sửa nút bấm "Chèn $CURSOR$" để chèn chính xác tại con trỏ văn bản / vùng bôi đen của ô soạn thảo thay vì luôn dồn xuống cuối.
- **Loại bỏ file batch (`.bat`), hỗ trợ cài đè trực tiếp:** Toàn bộ quy trình cài đặt và nâng cấp sử dụng chuẩn VSIX Installer (`.vsix`) độc lập.

---

### Phiên bản 1.1.2
- **Bổ sung từ khóa `COLUMN`:** Đã thêm vào từ điển cú pháp DDL (`ALTER TABLE ... ADD COLUMN ...`).
- **Xử lý triệt để biến T-SQL (`@Year`):**
  - Bảo vệ các biến có tiền tố `@` (như `@Year`, `@Date`, `@Status`, `@Month`, `@Type`...) không bị tự động viết hoa nhầm thành từ khóa SQL.
  - Bảo vệ tên bảng tạm dạng `#Year`.
  - Giữ nguyên tính năng viết hoa cho hàm chuẩn `YEAR(...)`.
- **Mở rộng bộ từ điển lên hơn 510 từ khóa & hàm:** Bao gồm đầy đủ DQL, DML, DDL, Windowing, Pivot, Transaction, Query Hints, Lock Hints, SET Options, 37 kiểu dữ liệu và 180+ hàm tích hợp.
- **Bổ sung dấu đóng ngoặc `)` vào danh sách phím kích hoạt AutoUpperCase:** Gõ `CAST(col AS int)` rồi gõ `)` sẽ tự động chuyển `int` thành `INT`.
- **Cập nhật hiển thị:** Bổ sung nhãn **`(PHONGNT)`** vào `DisplayName` gói VSIX, tiêu đề cửa sổ và giao diện cấu hình.

---

### Phiên bản 1.1.1
- **Khắc phục lỗi AutoUpperCase trong chuỗi ký tự nhiều dòng (Multi-line String):**
  - Thay thế cơ chế kiểm tra từng dòng đơn lẻ bằng bộ quét **Lexical State Machine** đa dòng quét trên Buffer Snapshot.
  - Tự động nhận diện chuỗi mở kéo dài qua các ký tự xuống dòng (`\r\n`) cho đến khi gặp dấu nháy đóng hợp lệ.
  - Xử lý chính xác cặp nháy đơn thoát (`''`) bên trong chuỗi.
  - Hỗ trợ đầy đủ khối chú thích nhiều dòng lồng nhau (`/* ... */`).
  - Tốc độ quét cực cao: < 1ms cho tài liệu 500.000 ký tự.

---

### Phiên bản 1.1.0
- **Khắc phục dứt điểm lỗi không nhập liệu trong hộp thoại Options:**
  - Chuyển `OptionsPage` sang kế thừa `UIElementDialogPage` tích hợp chuẩn `IKeyboardInputSink`.
  - Xử lý thông điệp `WM_GETDLGCODE` chuyên sâu, khôi phục `TranslateMessage` để WPF TextBoxes nhận thông điệp `WM_CHAR`.
  - Tự động kích hoạt Win32 focus và bôi đen ô `Prefix` khi bấm Thêm mới.
- **Tối ưu hóa giao diện Snippet:**
  - Thu gọn lưới danh sách Snippet xuống đúng 3 dòng (`Height="108"`), nhường toàn bộ không gian cho khung soạn thảo chi tiết.
  - Bổ sung tùy chọn giao diện **Light Mode / Dark Mode** với màu chữ tương phản cao, sắc nét.
  - Đổi tên nút thành **"Làm mới"** để tải lại thư mục snippet.
  - Xóa toàn bộ snippet mặc định, cho phép người dùng tự quản lý danh mục riêng.

---

### Phiên bản 1.0.0
- Khởi tạo dự án Lumi SQL Formatter cho SSMS 22 (x64).
- Tự động viết hoa thời gian thực các từ khóa cơ bản.
- Hỗ trợ lưu trữ snippet theo định dạng file JSON riêng lẻ (Redgate style).
- Tích hợp popup gợi ý snippet và tạo nhanh qua `Ctrl + Alt + S`.
