# 問題解決方案總結

## 問題 1: 已經生成 work type 在同步時候要可以修正回來

### 問題描述
當 Redmine Issues 遷移到 Azure DevOps 時，有時候工作項目類型 (Work Item Type) 可能會被錯誤地設定。例如，一個 Bug 可能被遷移成 Task，需要在後續同步時修正回來。

### 解決方案

#### 1. 新增 WorkItemType 追蹤
- 在 `MigrationRecord` 模型中新增 `WorkItemType` 欄位
- 在遷移過程中記錄工作項目類型

#### 2. 實作工作項目類型修正功能
```csharp
public async Task<bool> CorrectWorkItemTypeAsync(int redmineIssueId, string correctWorkItemType)
```

#### 3. Azure DevOps 端修正方法
```csharp
public async Task<WorkItem?> CorrectWorkItemTypeAsync(int workItemId, string newWorkItemType)
```

### 使用方式
```csharp
// 修正 Redmine Issue #123 對應的 Work Item 類型為 Bug
await migrationService.CorrectWorkItemTypeAsync(123, "Bug");
```

---

## 問題 2: 同步給 redmine 只能修改狀態以及回寫 commit 訊息

### 問題描述
當從 Azure DevOps 同步資料回 Redmine 時，需要限制同步的範圍，只允許更新狀態和添加 commit 訊息，避免覆蓋 Redmine 中的其他重要資料。

### 解決方案

#### 1. 限制性同步方法
```csharp
public async Task<bool> SyncToRedmineAsync(int azureWorkItemId, string? commitMessage = null)
```

#### 2. Redmine 端安全更新
- 僅允許狀態更新：`UpdateIssueStatusAsync`
- 僅允許添加註釋：`AddCommitMessageAsync`
- 使用 HTTP PUT 請求直接控制更新欄位

#### 3. 反向狀態對應
- 新增 `ReverseStatusMapping` 設定
- 將 Azure DevOps 狀態正確對應回 Redmine 狀態

### 設定範例
```json
"ReverseStatusMapping": {
  "New": "新建立",
  "Active": "實作中",
  "Resolved": "待測試",
  "Closed": "已結束"
}
```

### 使用方式
```csharp
// 同步狀態和 commit 訊息
await migrationService.SyncToRedmineAsync(456, "修正了關鍵 bug - commit: abc123");

// 僅同步狀態
await migrationService.SyncToRedmineAsync(789);
```

---

## 核心改進

### 1. 安全性提升
- 限制 Redmine 同步範圍，防止意外覆蓋
- 明確區分哪些欄位可以更新

### 2. 靈活性增強
- 支援工作項目類型後續修正
- 支援選擇性同步 (狀態 + commit 訊息)

### 3. 追蹤完整性
- 記錄工作項目類型變更
- 追蹤同步時間和狀態

### 4. 設定友善性
- 透過設定檔控制狀態對應
- 支援雙向狀態對應

---

## 檔案變更摘要

### 新增檔案
- `WorkItemCorrectionTest.cs` - 測試程式
- `WorkItemCorrectionDemo.cs` - 示範程式

### 修改檔案
- `Services/MigrationService.cs` - 新增修正和同步方法
- `Services/MigrationTrackingService.cs` - 新增按 Azure ID 查詢
- `Models/MigrationModels.cs` - 新增 WorkItemType 欄位
- `Models/AppConfig.cs` - 新增反向狀態對應
- `AzureDevopsFactory.cs` - 新增工作項目修正方法
- `RedmineFactory.cs` - 新增限制性更新方法
- `appsettings.json` - 新增反向狀態對應設定
- `README.md` - 新增功能說明

這些改進確保了系統的安全性、靈活性和可維護性，解決了工作項目類型修正和限制性同步的需求。
