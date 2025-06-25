# 如何使用工作項目修正和同步功能

## 前置需求

1. 確保 `appsettings.json` 已正確設定
2. 確保已安裝所需的 NuGet 套件

## 執行方式

### 1. 執行示範程式

```bash
cd d:\10.GitHub\Redmine_Into_AzureDevOps
dotnet run --project WorkItemCorrectionDemo.cs
```

### 2. 程式化使用

```csharp
// 設定服務
var host = CreateHostBuilder(args).Build();
var migrationService = host.Services.GetRequiredService<IMigrationService>();

// 修正工作項目類型
int redmineIssueId = 123;
string correctType = "Bug";
var result = await migrationService.CorrectWorkItemTypeAsync(redmineIssueId, correctType);

// 同步到 Redmine (含 commit 訊息)
int azureWorkItemId = 456;
string commitMessage = "修正了關鍵 bug - commit: abc123";
var syncResult = await migrationService.SyncToRedmineAsync(azureWorkItemId, commitMessage);

// 僅同步狀態
var statusOnlyResult = await migrationService.SyncToRedmineAsync(789);
```

## 設定說明

### appsettings.json 範例

```json
{
  "Redmine": {
    "Host": "https://your-redmine-server.com",
    "ApiKey": "your-api-key",
    "ProjectIdentifier": "your-project"
  },
  "AzureDevOps": {
    "OrganizationUrl": "https://dev.azure.com/your-org/",
    "PersonalAccessToken": "your-pat",
    "ProjectName": "YourProject"
  },
  "Migration": {
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
      "功能": "Feature",
      "任務": "Task"
    }
  }
}
```

## 功能說明

### 1. 工作項目類型修正
- 修正已遷移但類型錯誤的工作項目
- 自動更新追蹤記錄
- 支援所有 Azure DevOps 工作項目類型

### 2. 限制性 Redmine 同步
- 僅更新狀態和添加註釋
- 防止覆蓋重要資料
- 支援選擇性同步

## 錯誤處理

程式包含完整的錯誤處理機制：
- 無效的 Issue ID 檢查
- 網路連線問題處理
- API 權限驗證
- 詳細的錯誤訊息輸出

## 安全性考量

- 限制 Redmine 更新範圍
- API 金鑰安全保護
- 狀態對應驗證
- 操作日誌記錄
