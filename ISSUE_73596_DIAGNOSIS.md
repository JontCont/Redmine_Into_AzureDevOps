# Issue #73596 修正問題診斷和解決方案

## 問題診斷

根據執行結果，發現以下問題：

### 1. Issue 詳細資訊 ✅
- **Issue ID**: #73596
- **標題**: 【合庫回報1219】刷卡機/週邊設備派工管理，新增POS需求單，複製端末機號有誤＿子雯
- **狀態**: 已結束
- **類型**: 臭蟲 (建議修正為 Bug 類型)
- **作者**: 謝 安琪
- **指派給**: 子雯 陳

### 2. Azure DevOps 連線問題 ❌
- **錯誤**: `VS30063: You are not authorized to access https://dev.azure.com`
- **原因**: Personal Access Token (PAT) 權限不足或已過期

## 解決方案

### 方案 1: 更新 Azure DevOps PAT (推薦)

1. **登入 Azure DevOps**:
   - 前往 https://dev.azure.com/hendry0676/
   - 使用您的帳號登入

2. **生成新的 PAT**:
   - 點擊右上角用戶頭像 → Personal access tokens
   - 點擊 "New Token"
   - 設定權限範圍 (Scopes):
     - ✅ **Work Items** (Read & write)
     - ✅ **Project and team** (Read)
     - ✅ **Code** (Read) - 如果需要
   - 設定過期時間 (建議 90 天或更長)
   - 複製生成的 Token

3. **更新 appsettings.json**:
   ```json
   {
     "AzureDevOps": {
       "OrganizationUrl": "https://dev.azure.com/hendry0676/",
       "PersonalAccessToken": "您的新PAT",
       "ProjectName": "tcbbank_estore_pIII"
     }
   }
   ```

### 方案 2: 檢查現有 PAT 權限

如果 PAT 沒有過期，請確認包含以下權限：
- Work Items (Read & write)
- Project and team (Read)

### 方案 3: 檢查組織 URL

確認組織 URL 正確：
- 當前: `https://dev.azure.com/hendry0676/`
- 請確認 "hendry0676" 是正確的組織名稱

## 修正後的執行步驟

### 1. 更新 PAT 後重新執行

```bash
cd d:\10.GitHub\Redmine_Into_AzureDevOps
dotnet run
```

### 2. 如果成功，您將看到：

```
✅ 成功遷移 Issue #73596 到 Azure DevOps
根據 Issue 內容，建議類型: Bug  # 因為類型是"臭蟲"
請選擇正確的工作項目類型:
1. Bug (錯誤/臭蟲)
2. Feature (新需求)
3. Task (調整/任務)
```

**建議選擇**: 選擇 `1` (Bug)，因為 Redmine 中的類型是"臭蟲"

### 3. 驗證結果

程式執行完成後：
- 檢查 Azure DevOps 專案中是否出現新的 Bug 工作項目
- 標題應該包含 "[Bug][73596]" 前綴
- 狀態應該對應為 "Closed" (因為 Redmine 狀態是"已結束")

## 故障排除 Checklist

- [ ] Azure DevOps PAT 已更新且有效
- [ ] PAT 包含 Work Items (Read & write) 權限
- [ ] 組織 URL 正確
- [ ] 專案名稱 "tcbbank_estore_pIII" 存在
- [ ] 網路連線正常
- [ ] Redmine API 連線正常

## 快速測試連線

更新 PAT 後，可以先執行簡單測試：

```bash
# 測試 Azure DevOps 連線
dotnet run --project AzureDevOpsTest.cs
```

如果連線成功，再執行完整修正程式。
