任何回答都用 正體中文回應

# Copilot Instructions for Redmine_Into_AzureDevOps

## 專案架構與核心元件
- **RedmineFactory**：負責與 Redmine API 溝通，取得 Issues 與附件。
- **AzureDevopsFactory**：負責與 Azure DevOps API 溝通，建立/更新 Work Items。
- **MigrationService**：主遷移流程，包含批次處理、狀態/類型對應、附件遷移、重複檢查。
- **MigrationTrackingService**：追蹤遷移進度與記錄，維護 `migration_records.json`、`attachment_records.json`。
- **Models/**：定義設定與遷移資料結構。
- **Services/**：遷移與追蹤服務實作。

## 主要資料流程
1. 由 Redmine 取得 Issues 清單
2. 檢查 Azure DevOps 是否已存在對應 Work Item（避免重複）
3. 建立/更新 Work Item，並遷移附件（如啟用）
4. 記錄遷移狀態於 json 檔案
5. 產生遷移摘要與統計

## 關鍵設定與檔案
- `appsettings.json`：Redmine/Azure DevOps 連線資訊、狀態/類型對應、批次大小等
- `migration_records.json`、`attachment_records.json`：遷移與附件追蹤

## 開發/執行流程
- **建置/執行**：
  - 需 .NET 8.0 SDK
  - `dotnet run` 啟動互動式選單
- **常用選單功能**：
  - 遷移所有 Issues、指定數量、強制重新遷移、同步狀態、查看摘要/列表
- **測試/除錯**：
  - 主要以互動式選單操作，日誌於 Console 輸出
  - 重要錯誤與進度會記錄於 json 檔案

## 專案慣例與擴展
- 狀態/類型對應皆可於 `appsettings.json` 客製
- 依賴注入設計，介面如 `IMigrationService`、`IMigrationTrackingService` 可擴充
- 批次處理與 API 延遲已內建，避免觸發限制
- 僅允許同步 Redmine 狀態與 commit 訊息，防止資料覆蓋

## 進階功能範例
```csharp
// 修正已遷移工作項目類型
await migrationService.CorrectWorkItemTypeAsync(redmineIssueId: 123, correctWorkItemType: "Bug");

// 僅同步狀態與 commit 訊息到 Redmine
await migrationService.SyncToRedmineAsync(azureWorkItemId: 456, commitMessage: "修正關鍵 bug");
```

## 參考檔案
- `README.md`：完整功能、設定、架構與常見問題
- `appsettings.json`：所有對應與連線設定
- `Models/`, `Services/`：擴展與自訂服務實作

---
如需進一步說明，請參考 `README.md` 或現有程式碼範例。
