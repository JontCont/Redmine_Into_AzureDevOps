# Redmine 到 Azure DevOps 遷移工具

一個完整的工具，用於將 Redmine Issues 遷移到 Azure DevOps Work Items，包含附件遷移和狀態同步功能。

## 功能特色

### ✅ 已解決的問題

1. **重複檢查機制** - 自動檢測 Azure DevOps 中是否已存在對應的 Work Item
2. **狀態同步** - 支援 Redmine 狀態與 Azure DevOps 狀態的自動對應和更新
3. **附件遷移** - 完整支援圖片、文件等附件的下載和上傳
4. **進度追蹤** - 詳細的遷移記錄和錯誤追蹤
5. **批次處理** - 高效的批次遷移，避免 API 限制

### 🚀 核心功能

- **完整遷移**: 遷移 Issues 的標題、描述、狀態、指派人、優先權等
- **附件支援**: 自動下載 Redmine 附件並上傳到 Azure DevOps
- **狀態對應**: 可設定的狀態對應表
- **重複檢查**: 避免重複建立已存在的 Work Items
- **批次操作**: 支援大量 Issues 的批次遷移
- **錯誤處理**: 完善的錯誤處理和重試機制
- **進度報告**: 詳細的遷移摘要和統計

## 設定檔說明

### appsettings.json

```json
{
  "Redmine": {
    "Host": "https://your-redmine-server.com/redmine",
    "ApiKey": "your-redmine-api-key",
    "ProjectIdentifier": "your-project-id"
  },
  "AzureDevOps": {
    "OrganizationUrl": "https://dev.azure.com/your-org/",
    "PersonalAccessToken": "your-azure-devops-pat",
    "ProjectName": "YourProjectName"
  },
  "Migration": {
    "BatchSize": 10,
    "EnableAttachmentMigration": true,
    "StatusMapping": {
      "新建": "New",
      "進行中": "Active",
      "已解決": "Resolved",
      "已關閉": "Closed"
    },
    "ReverseStatusMapping": {
      "New": "新建",
      "Active": "進行中",
      "Resolved": "已解決",
      "Closed": "已關閉"
    },
    "WorkItemTypeMapping": {
      "錯誤": "Bug",
      "功能": "User Story",
      "任務": "Task"
    }
  }
}
```

## 使用方式

### 1. 環境準備

1. 確保已安裝 .NET 8.0 SDK
2. 設定 `appsettings.json` 中的連線資訊
3. 確保 Redmine 和 Azure DevOps 的 API 權限正確

### 2. 執行程式

```bash
dotnet run
```

### 3. 選擇操作

程式提供互動式選單：

- **遷移所有 Issues**: 遷移專案中的所有 Issues
- **遷移指定數量**: 遷移指定數量的 Issues（測試用）
- **強制重新遷移**: 重新遷移所有 Issues（會重複建立）
- **同步狀態**: 同步現有 Work Items 的狀態
- **查看遷移摘要**: 顯示遷移統計資訊
- **查看 Issues 列表**: 預覽 Redmine Issues

## 遷移記錄

程式會自動建立以下檔案來追蹤遷移狀態：

- `migration_records.json`: 主要遷移記錄
- `attachment_records.json`: 附件遷移記錄

## 架構說明

### 主要類別

- **RedmineFactory**: 處理 Redmine API 互動
- **AzureDevopsFactory**: 處理 Azure DevOps API 互動
- **MigrationService**: 核心遷移邏輯
- **MigrationTrackingService**: 遷移狀態追蹤

### 資料流程

1. 從 Redmine 取得 Issues 清單
2. 檢查 Azure DevOps 是否已存在對應 Work Item
3. 建立或更新 Work Item
4. 遷移附件（如果啟用）
5. 記錄遷移狀態
6. 產生遷移報告

## 狀態對應

程式支援自訂狀態對應，將 Redmine 狀態對應到 Azure DevOps 狀態：

| Redmine 狀態 | Azure DevOps 狀態 |
|-------------|-------------------|
| 新建 | New |
| 指派 | Active |
| 進行中 | Active |
| 已解決 | Resolved |
| 已結案 | Closed |
| 已關閉 | Closed |

## Work Item 類型對應

| Redmine 追蹤器 | Azure DevOps 類型 |
|---------------|-------------------|
| 錯誤 | Bug |
| 功能 | User Story |
| 任務 | Task |
| 支援 | Task |

## 注意事項

1. **API 限制**: 程式內建批次處理和延遲機制，避免觸發 API 限制
2. **權限要求**: 確保 API 金鑰有足夠權限讀取 Redmine 和寫入 Azure DevOps
3. **附件大小**: 大型附件可能需要較長時間處理
4. **重複執行**: 程式具備重複檢查機制，但強制遷移會重複建立 Work Items

## 疑難排解

### 常見問題

1. **連線失敗**: 檢查網路連線和 API 金鑰
2. **權限錯誤**: 確認 API 金鑰權限設定
3. **附件下載失敗**: 檢查 Redmine 附件存取權限
4. **狀態對應錯誤**: 確認 Azure DevOps 專案支援設定的狀態

### 日誌檔案

程式會在 Console 中顯示詳細的執行日誌，包括：
- 遷移進度
- 錯誤訊息
- 成功/失敗統計

## 開發說明

### 相依套件

- Microsoft.TeamFoundationServer.Client
- redmine-api
- Microsoft.Extensions.Configuration
- Microsoft.Extensions.DependencyInjection
- Microsoft.Extensions.Logging

### 擴展功能

程式採用依賴注入和介面設計，易於擴展：

- 實作 `IMigrationTrackingService` 以支援資料庫儲存
- 實作 `IMigrationService` 以支援其他遷移邏輯
- 添加更多狀態和類型對應

## 版本記錄

### v2.0.0 (最新)
- ✅ 新增重複檢查機制
- ✅ 新增狀態同步功能  
- ✅ 新增附件遷移支援
- ✅ 新增進度追蹤和錯誤處理
- ✅ 新增互動式選單
- ✅ 新增遷移摘要報告
- ✅ 改進架構和可維護性

### v1.0.0 (原始版本)
- 基本 Issues 列表查詢
- 簡單的 Work Items 顯示

### 🔧 新增功能 (問題解決)

#### 1. 工作項目類型修正
- **問題**: 已經生成的 work type 在同步時候要可以修正回來
- **解決方案**: 新增 `CorrectWorkItemTypeAsync` 方法
- **功能**: 
  - 可以修正已遷移的工作項目類型 (Bug、Task、Feature 等)
  - 自動更新遷移記錄
  - 保持資料一致性

```csharp
// 修正工作項目類型
await migrationService.CorrectWorkItemTypeAsync(redmineIssueId: 123, correctWorkItemType: "Bug");
```

#### 2. 限制性 Redmine 同步
- **問題**: 同步給 redmine 只能修改狀態以及回寫 commit 訊息
- **解決方案**: 新增 `SyncToRedmineAsync` 方法，限制同步範圍
- **功能**:
  - 僅允許狀態更新和 commit 訊息添加
  - 防止覆蓋 Redmine 中的其他重要資料
  - 支援反向狀態對應

```csharp
// 同步狀態和 commit 訊息到 Redmine
await migrationService.SyncToRedmineAsync(azureWorkItemId: 456, commitMessage: "修正關鍵 bug");

// 僅同步狀態
await migrationService.SyncToRedmineAsync(azureWorkItemId: 789);
```
