# 快速執行指南 - Issue #73596

## 立即解決方案

### 1. 修正 Issue #73596 的工作項目類型

```bash
# 直接執行專用修正程式
dotnet run --project d:\10.GitHub\Redmine_Into_AzureDevOps\IssueCorrection73596.cs
```

### 2. 使用互動式批次工具

```bash
# 執行批次修正工具
dotnet run --project d:\10.GitHub\Redmine_Into_AzureDevOps\BatchCorrection.cs
```

## 操作步驟

### 使用專用修正程式 (推薦)

1. 開啟命令提示字元或 PowerShell
2. 執行以下命令：

```powershell
cd d:\10.GitHub\Redmine_Into_AzureDevOps
dotnet run -- IssueCorrection73596
```

3. 按照程式指示選擇正確的工作項目類型
4. 如果有對應的 Azure Work Item ID，輸入以進行狀態同步

### 使用批次工具

1. 執行批次工具：

```powershell
cd d:\10.GitHub\Redmine_Into_AzureDevOps
dotnet run -- BatchCorrection
```

2. 選擇選項 1 (修正單一 Issue 的工作項目類型)
3. 輸入 Issue ID: `73596`
4. 選擇適當的工作項目類型

## 工作項目類型建議

根據 Issue #73596 的描述「在單子上押錯」，建議選擇：

- **Option 3 (Task)**: 如果這是一個調整或修正作業
- **Option 1 (Bug)**: 如果這是一個需要修正的錯誤
- **Option 2 (Feature)**: 如果這是一個新的需求

## 即時執行 (一行指令)

```powershell
# 直接進入目錄並執行
cd d:\10.GitHub\Redmine_Into_AzureDevOps; dotnet run -- IssueCorrection73596
```

## 驗證結果

執行完成後，您可以：

1. 檢查 Azure DevOps 中的工作項目是否已更新
2. 查看 `migration_records.json` 檔案中的記錄
3. 使用批次工具的選項 3 查看遷移摘要

## 如果遇到問題

常見問題解決：

1. **找不到 Issue**: 確認 Issue #73596 已經遷移到 Azure DevOps
2. **連線失敗**: 檢查 `appsettings.json` 中的設定
3. **權限不足**: 確認 Azure DevOps PAT 有修改工作項目的權限

立即執行這個命令來開始修正：

```bash
cd d:\10.GitHub\Redmine_Into_AzureDevOps && dotnet run -- IssueCorrection73596
```
