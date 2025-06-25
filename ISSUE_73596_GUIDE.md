# Issue #73596 修正指南

## 問題描述
- **Redmine Issue ID**: #73596
- **作者**: 謝 安琪
- **指派給**: 子雯 陳
- **狀態**: 已結束
- **優先權**: 普通(p4)
- **問題**: 在單子上押錯，要怎樣同步 Work Type / Status

## 解決方案

### 方法 1: 使用專用修正程式

```bash
# 進入專案目錄
cd d:\10.GitHub\Redmine_Into_AzureDevOps

# 執行專門針對此 Issue 的修正程式
dotnet run --project . -- IssueCorrection73596
```

### 方法 2: 使用批次修正工具

```bash
# 執行批次修正工具
dotnet run --project . -- BatchCorrection
```

然後選擇選項 1 來修正單一 Issue 的工作項目類型。

### 方法 3: 程式化修正

```csharp
// 建立服務
var host = CreateHostBuilder(args).Build();
var migrationService = host.Services.GetRequiredService<IMigrationService>();

// 修正工作項目類型
int redmineIssueId = 73596;
string correctType = "Task"; // 根據需求選擇: Bug, Feature, Task

var result = await migrationService.CorrectWorkItemTypeAsync(redmineIssueId, correctType);
if (result)
{
    Console.WriteLine("✅ 工作項目類型修正成功");
}

// 如果需要同步狀態到 Redmine
int azureWorkItemId = 12345; // 替換為實際的 Azure Work Item ID
string commitMessage = "修正 Issue #73596 - 作者: 謝安琪";

var syncResult = await migrationService.SyncToRedmineAsync(azureWorkItemId, commitMessage);
```

## 步驟說明

### 1. 確認目前狀態
首先確認 Issue #73596 是否已經遷移到 Azure DevOps：
- 檢查 `migration_records.json` 檔案
- 或使用批次工具的選項 3 查看遷移摘要

### 2. 修正工作項目類型
根據 Issue 的性質選擇正確的類型：
- **Bug**: 如果是錯誤或臭蟲
- **Feature**: 如果是新需求
- **Task**: 如果是調整或一般任務

### 3. 同步狀態
由於 Redmine 狀態是「已結束」，對應的 Azure DevOps 狀態應該是「Closed」。
如果需要同步，使用 `SyncToRedmineAsync` 方法。

### 4. 驗證結果
- 檢查 Azure DevOps 中的工作項目類型是否正確
- 確認狀態對應是否正確
- 查看遷移記錄是否更新

## 狀態對應表

根據您的 appsettings.json 設定：

| Redmine 狀態 | Azure DevOps 狀態 |
|-------------|------------------|
| 新建立       | New             |
| 實作中       | Active          |
| 待測試       | Resolved        |
| 已結束       | Closed          |
| 已關閉       | Closed          |

## 工作項目類型對應

| Redmine 類型 | Azure DevOps 類型 |
|-------------|------------------|
| 錯誤/臭蟲    | Bug             |
| 新需求       | Feature         |
| 調整        | Task            |

## 故障排除

如果修正失敗，請檢查：

1. **Issue 是否已遷移**: 確認 Issue #73596 已經存在於 Azure DevOps
2. **連線設定**: 檢查 appsettings.json 中的 API 金鑰和 URL
3. **權限**: 確認 Azure DevOps PAT 有足夠權限修改工作項目
4. **工作項目類型**: 確認目標專案支援所選的工作項目類型

## 聯絡資訊

如果遇到問題，請提供：
- 錯誤訊息
- Issue ID
- 嘗試的修正類型
- Azure DevOps Work Item ID (如果已知)
