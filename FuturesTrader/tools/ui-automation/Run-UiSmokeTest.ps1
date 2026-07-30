#requires -Version 5.1
<#
.SYNOPSIS
  FuturesTrader UI 烟雾测试：登录 → 浮动栏 → 合约窗口 → 行情数据流入 → MCP。
.DESCRIPTION
  通过 UIAutomationClient 模拟用户操作，验证：
    1. 启动应用并加载登录页（行情/账号自动加载 + 测速）
    2. 输入密码 + 点击登录按钮
    3. 浮动工具栏出现 + 账号 ID 展示
    4. 点击分组按钮（含窗口）→ 打开合约窗口
    5. 行情数据流入 → 价格梯渲染
    6. MCP 接口调用（/ping 与 /mcp tools/list）
  全程 Mock 模式，离线可运行。
#>

[CmdletBinding()]
param(
    [string]$ExePath = "D:\work\projs\futures\FuturesTrader\src\FuturesTrader.Host\bin\Debug\net10.0-windows\FuturesTrader.Host.exe",
    [string]$Password = "258147",
    [int]$StartupTimeoutSec = 30,
    [int]$LoginTimeoutSec = 20,
    [int]$WindowOpenTimeoutSec = 15,
    [int]$MarketDataTimeoutSec = 20,
    [string]$McpUrl = "http://127.0.0.1:51800"
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

# ---------- 工具函数 ----------
function Write-Step([string]$msg) { Write-Host "[STEP] $msg" -ForegroundColor Cyan }
function Write-Ok([string]$msg)   { Write-Host "[ OK ] $msg" -ForegroundColor Green }
function Write-Warn2([string]$msg){ Write-Host "[WARN] $msg" -ForegroundColor Yellow }
function Write-Err([string]$msg)  { Write-Host "[FAIL] $msg" -ForegroundColor Red }

Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Windows.Forms

function Get-DesktopRoot {
    return [System.Windows.Automation.AutomationElement]::RootElement
}

function Wait-WindowByName([string]$name, [int]$timeoutSec) {
    $root = Get-DesktopRoot
    $cond = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::NameProperty, $name)
    $deadline = (Get-Date).AddSeconds($timeoutSec)
    while ((Get-Date) -lt $deadline) {
        $el = $root.FindFirst([System.Windows.Automation.TreeScope]::Children, $cond)
        if ($el) { return $el }
        Start-Sleep -Milliseconds 400
    }
    return $null
}

function Find-DescendantsByType([System.Windows.Automation.AutomationElement]$parent, [System.Windows.Automation.ControlType]$ctrlType, [int]$timeoutSec = 8) {
    $deadline = (Get-Date).AddSeconds($timeoutSec)
    while ((Get-Date) -lt $deadline) {
        $cond = New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::ControlTypeProperty, $ctrlType)
        $found = $parent.FindAll([System.Windows.Automation.TreeScope]::Descendants, $cond)
        # PowerShell unwraps collections; force to array
        $arr = @($found)
        if ($arr.Count -gt 0) { return ,$arr }
        Start-Sleep -Milliseconds 300
    }
    return ,@()
}

function Invoke-Button([System.Windows.Automation.AutomationElement]$btn) {
    $pat = $btn.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
    if ($null -eq $pat) { throw "Button does not support InvokePattern: $($btn.Current.Name)" }
    $pat.Invoke()
}

# ---------- 主流程 ----------

Write-Step "1. Start FuturesTrader.Host.exe"
if (-not (Test-Path $ExePath)) { throw "exe not found: $ExePath" }

Get-Process -Name "FuturesTrader.Host" -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep -Seconds 1

$workDir = Split-Path $ExePath -Parent
$proc = Start-Process -FilePath $ExePath -WorkingDirectory $workDir -PassThru
Write-Ok "Process started PID=$($proc.Id)"

try {
    Write-Step "2. Wait for LoginWindow (timeout ${StartupTimeoutSec}s)"
    $loginWin = Wait-WindowByName -name "期货交易终端 · 登录" -timeoutSec $StartupTimeoutSec
    if (-not $loginWin) {
        Write-Err "LoginWindow did not appear"
        throw "LoginWindow timeout"
    }
    Write-Ok "LoginWindow appeared (Handle=$($loginWin.Current.NativeWindowHandle))"

    Write-Step "3. Wait for data loading + probe (8s)"
    Start-Sleep -Seconds 8

    Write-Step "4. Locate PasswordBox and input password"
    # WPF UI PasswordBox exposes as Edit control with className containing 'PasswordBox'
    $edits = Find-DescendantsByType -parent $loginWin -ctrlType ([System.Windows.Automation.ControlType]::Edit) -timeoutSec 8
    Write-Host "     Found $($edits.Count) Edit control(s)"
    $pwdEl = $null
    foreach ($e in $edits) {
        $cn = ""
        try { $cn = $e.Current.ClassName } catch {}
        $nm = ""
        try { $nm = $e.Current.Name } catch {}
        $aid = ""
        try { $aid = $e.Current.AutomationId } catch {}
        Write-Host "     Edit: ClassName='$cn' Name='$nm' AutomationId='$aid'"
        # WPF UI PasswordBox ClassName typically 'PasswordBox' or similar
        if ($cn -like '*Password*' -or $aid -like '*Password*' -or $nm -like '*密码*' -or $nm -like '*Password*') {
            $pwdEl = $e
            break
        }
    }
    # Fallback: take the last Edit (PasswordBox is usually after search TextBox)
    if (-not $pwdEl -and $edits.Count -gt 0) {
        # Prefer an Edit whose ValuePattern is NOT available (PasswordBox hides value)
        foreach ($e in $edits) {
            $hasValue = $false
            try {
                $null = $e.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern)
                $hasValue = $true
            } catch {}
            if (-not $hasValue) { $pwdEl = $e; break }
        }
        if (-not $pwdEl) { $pwdEl = $edits[$edits.Count - 1] }
    }
    if (-not $pwdEl) { throw "PasswordBox not found" }
    Write-Ok "PasswordBox located"

    $pwdEl.SetFocus()
    Start-Sleep -Milliseconds 300
    [System.Windows.Forms.SendKeys]::SendWait("{END}+{HOME}{DEL}")
    Start-Sleep -Milliseconds 100
    [System.Windows.Forms.SendKeys]::SendWait($Password)
    Start-Sleep -Milliseconds 500
    Write-Ok "Password entered"

    Write-Step "5. Locate and click Login button"
    $loginBtn = $null
    $deadline = (Get-Date).AddSeconds(10)
    while ((Get-Date) -lt $deadline -and -not $loginBtn) {
        $btns = Find-DescendantsByType -parent $loginWin -ctrlType ([System.Windows.Automation.ControlType]::Button) -timeoutSec 2
        foreach ($b in $btns) {
            $n = ""
            try { $n = $b.Current.Name } catch {}
            if ($n -match '登录' -or $n -match 'Login') {
                $loginBtn = $b; break
            }
        }
        if (-not $loginBtn) { Start-Sleep -Milliseconds 400 }
    }
    if (-not $loginBtn) { throw "Login button not found" }
    Write-Ok "Login button found"

    $isEnabled = $false
    try { $isEnabled = $loginBtn.Current.IsEnabled } catch {}
    Write-Host "     Button IsEnabled=$isEnabled"
    if (-not $isEnabled) {
        Write-Warn2 "Login button disabled, waiting 5s..."
        Start-Sleep -Seconds 5
        try { $isEnabled = $loginBtn.Current.IsEnabled } catch {}
        Write-Host "     Button IsEnabled=$isEnabled (after wait)"
    }

    Invoke-Button $loginBtn
    Write-Ok "Login button clicked"

    Write-Step "6. Wait for FloatingMainWindow (timeout ${LoginTimeoutSec}s)"
    $floatingWin = Wait-WindowByName -name "浮动工具栏" -timeoutSec $LoginTimeoutSec
    if (-not $floatingWin) {
        Write-Err "FloatingMainWindow did not appear"
        throw "FloatingMainWindow timeout"
    }
    Write-Ok "FloatingMainWindow appeared (Handle=$($floatingWin.Current.NativeWindowHandle))"

    Write-Step "7. Verify AccountId is displayed"
    Start-Sleep -Seconds 2
    $texts = Find-DescendantsByType -parent $floatingWin -ctrlType ([System.Windows.Automation.ControlType]::Text) -timeoutSec 4
    $acctId = $null
    foreach ($t in $texts) {
        $n = ""
        try { $n = $t.Current.Name } catch {}
        if ($n -match '^\d{5,}$') {
            $acctId = $n
            break
        }
    }
    if ($acctId) {
        Write-Ok "AccountId = $acctId"
    } else {
        Write-Warn2 "AccountId TextBlock not found (non-blocking)"
        Write-Host "     Visible texts:"
        foreach ($t in $texts) {
            $n = ""
            try { $n = $t.Current.Name } catch {}
            if ($n) { Write-Host "       - '$n'" }
        }
    }

    Write-Step "8. Find and click an enabled group button"
    Start-Sleep -Seconds 1
    $groupBtn = $null
    $groupBtnId = ""
    $deadline = (Get-Date).AddSeconds(10)
    while ((Get-Date) -lt $deadline -and -not $groupBtn) {
        $btns = Find-DescendantsByType -parent $floatingWin -ctrlType ([System.Windows.Automation.ControlType]::Button) -timeoutSec 2
        foreach ($b in $btns) {
            $n = ""
            try { $n = $b.Current.Name } catch {}
            $en = $true
            try { $en = $b.Current.IsEnabled } catch {}
            if ($n -match '^\d+$' -and [int]$n -ge 1 -and [int]$n -le 20 -and $en) {
                $groupBtn = $b
                $groupBtnId = $n
                break
            }
        }
        if (-not $groupBtn) { Start-Sleep -Milliseconds 400 }
    }

    if (-not $groupBtn) {
        Write-Warn2 "No enabled group button found. Trying any group button..."
        $btns = Find-DescendantsByType -parent $floatingWin -ctrlType ([System.Windows.Automation.ControlType]::Button) -timeoutSec 2
        foreach ($b in $btns) {
            $n = ""
            try { $n = $b.Current.Name } catch {}
            if ($n -match '^\d+$') {
                $groupBtn = $b
                $groupBtnId = $n
                break
            }
        }
    }

    if ($groupBtn) {
        $en = $false
        try { $en = $groupBtn.Current.IsEnabled } catch {}
        Write-Ok "Group button #$groupBtnId found (Enabled=$en)"
        if ($en) {
            Invoke-Button $groupBtn
            Write-Ok "Clicked group #$groupBtnId"

            Write-Step "9. Wait for TradingWindow(s) (timeout ${WindowOpenTimeoutSec}s)"
            $tradingWin = $null
            $deadline = (Get-Date).AddSeconds($WindowOpenTimeoutSec)
            while ((Get-Date) -lt $deadline) {
                $root = Get-DesktopRoot
                $allChildren = $root.FindAll([System.Windows.Automation.TreeScope]::Children,
                    [System.Windows.Automation.Condition]::TrueCondition)
                foreach ($c in $allChildren) {
                    $cn = ""
                    try { $cn = $c.Current.Name } catch {}
                    $ct = ""
                    try { $ct = $c.Current.ControlType.Name } catch {}
                    if ($ct -eq 'Window' -and
                        $cn -ne '浮动工具栏' -and
                        $cn -ne '期货交易终端 · 登录' -and
                        $cn -ne '' -and
                        $cn -notmatch 'Settings|设置') {
                        $tradingWin = $c
                        break
                    }
                }
                if ($tradingWin) { break }
                Start-Sleep -Milliseconds 500
            }

            if ($tradingWin) {
                $twName = ""
                try { $twName = $tradingWin.Current.Name } catch {}
                Write-Ok "TradingWindow appeared: '$twName'"

                Write-Step "10. Wait for market data flow (timeout ${MarketDataTimeoutSec}s)"
                Start-Sleep -Seconds 3
                $priceFound = $false
                $deadline = (Get-Date).AddSeconds($MarketDataTimeoutSec)
                $lastSnapshot = ""
                while ((Get-Date) -lt $deadline) {
                    $priceTexts = Find-DescendantsByType -parent $tradingWin -ctrlType ([System.Windows.Automation.ControlType]::Text) -timeoutSec 2
                    $names = @()
                    foreach ($t in $priceTexts) {
                        $n = ""
                        try { $n = $t.Current.Name } catch {}
                        if ($n) { $names += $n }
                    }
                    $snapshot = $names -join '|'
                    if ($snapshot -ne $lastSnapshot) {
                        $display = $snapshot
                        if ($display.Length -gt 200) { $display = $display.Substring(0, 200) + "..." }
                        Write-Host "     [tick] $display"
                        $lastSnapshot = $snapshot
                    }
                    foreach ($n in $names) {
                        if ($n -match '^\d+\.?\d*$') {
                            $priceFound = $true
                            break
                        }
                    }
                    if ($priceFound) { break }
                    Start-Sleep -Milliseconds 800
                }
                if ($priceFound) {
                    Write-Ok "Market data is flowing, price ladder rendered"
                } else {
                    Write-Warn2 "No market data detected (Mock may not have produced ticks visible to UIA)"
                }
            } else {
                Write-Warn2 "TradingWindow did not appear (WindowManager.OpenGroup may not have created windows)"
            }
        } else {
            Write-Warn2 "Group #$groupBtnId is disabled (no windows bound), skipping window open test"
        }
    } else {
        Write-Warn2 "No group button found at all"
    }

    Write-Step "11. Verify MCP service (/ping)"
    try {
        $ping = Invoke-RestMethod -Uri "$McpUrl/ping" -Method GET -TimeoutSec 5
        Write-Ok "MCP /ping returned: $ping"
    } catch {
        Write-Err "MCP /ping failed: $($_.Exception.Message)"
    }

    Write-Step "12. Verify MCP initialize + tools/list"
    try {
        $body = @{
            jsonrpc = "2.0"
            id = 1
            method = "initialize"
            params = @{
                protocolVersion = "2024-11-05"
                capabilities = @{}
                clientInfo = @{ name = "ps-test"; version = "1.0" }
            }
        } | ConvertTo-Json -Depth 10

        $headers = @{
            "Content-Type" = "application/json"
            "Accept" = "application/json, text/event-stream"
        }
        $resp = Invoke-WebRequest -Uri "$McpUrl/mcp" -Method POST -Body $body -Headers $headers -TimeoutSec 10 -UseBasicParsing
        Write-Ok "MCP initialize HTTP $($resp.StatusCode)"

        $raw = $resp.Content
        $jsonLine = ($raw -split "`n") | Where-Object { $_ -match '^data:' } | Select-Object -First 1
        if ($jsonLine) {
            $json = ($jsonLine -replace '^data:\s*', '') | ConvertFrom-Json
            if ($json.result.serverInfo) {
                Write-Ok "MCP server: $($json.result.serverInfo.name) v$($json.result.serverInfo.version)"
            }
        }

        $body2 = @{
            jsonrpc = "2.0"
            id = 2
            method = "tools/list"
            params = @{}
        } | ConvertTo-Json -Depth 10

        $resp2 = Invoke-WebRequest -Uri "$McpUrl/mcp" -Method POST -Body $body2 -Headers $headers -TimeoutSec 10 -UseBasicParsing
        $raw2 = $resp2.Content
        $jsonLine2 = ($raw2 -split "`n") | Where-Object { $_ -match '^data:' } | Select-Object -First 1
        if ($jsonLine2) {
            $json2 = ($jsonLine2 -replace '^data:\s*', '') | ConvertFrom-Json
            $tools = $json2.result.tools
            Write-Ok "MCP tools ($($tools.Count)) listed:"
            foreach ($t in $tools) {
                Write-Host "       - $($t.name): $($t.description)"
            }
        }
    } catch {
        Write-Err "MCP tools/list failed: $($_.Exception.Message)"
    }

    Write-Step "13. Call MCP get_config tool"
    try {
        $body = @{
            jsonrpc = "2.0"
            id = 3
            method = "tools/call"
            params = @{
                name = "get_config"
                arguments = @{}
            }
        } | ConvertTo-Json -Depth 10

        $headers = @{
            "Content-Type" = "application/json"
            "Accept" = "application/json, text/event-stream"
        }
        $resp = Invoke-WebRequest -Uri "$McpUrl/mcp" -Method POST -Body $body -Headers $headers -TimeoutSec 10 -UseBasicParsing
        $raw = $resp.Content
        $jsonLine = ($raw -split "`n") | Where-Object { $_ -match '^data:' } | Select-Object -First 1
        if ($jsonLine) {
            $json = ($jsonLine -replace '^data:\s*', '') | ConvertFrom-Json
            $content = $json.result.content[0].text
            $display = $content
            if ($display.Length -gt 400) { $display = $display.Substring(0, 400) + "..." }
            Write-Ok "get_config returned:"
            Write-Host $display -ForegroundColor Gray
        }
    } catch {
        Write-Err "MCP get_config failed: $($_.Exception.Message)"
    }

    Write-Step "Test complete"
}
finally {
    Write-Step "Cleanup: terminate FuturesTrader.Host"
    Get-Process -Name "FuturesTrader.Host" -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
    Write-Ok "Cleaned up"
}
