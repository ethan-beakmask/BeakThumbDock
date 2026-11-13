# 免安裝使用 .NET 10 SDK

不執行安裝程式、不需要系統管理員權限，把 SDK 解壓到使用者目錄就能用。

## 1. 下載並解壓（PowerShell）

```powershell
Invoke-WebRequest https://dot.net/v1/dotnet-install.ps1 -OutFile "$env:TEMP\dotnet-install.ps1"
& "$env:TEMP\dotnet-install.ps1" -Channel 10.0 -InstallDir "$env:LOCALAPPDATA\Microsoft\dotnet" -NoPath
```

- `-Channel 10.0`：取 .NET 10（LTS）最新的 SDK。
- `-NoPath`：不修改 PATH，也不影響系統原有的 `C:\Program Files\dotnet`。
- 下載約 300 MB，解壓需要幾分鐘。

## 2. 建置

直接呼叫完整路徑：

```powershell
& "$env:LOCALAPPDATA\Microsoft\dotnet\dotnet.exe" build
```

## 3. 執行建置出來的 exe（framework-dependent）

exe 預設只會到 `C:\Program Files\dotnet` 找執行階段，所以要先告訴它 .NET 在哪裡：

```powershell
$env:DOTNET_ROOT = "$env:LOCALAPPDATA\Microsoft\dotnet"
.\ThumbDockSpike.exe --selftest
```

沒設 `DOTNET_ROOT` 時，會出現「You must install or update .NET」錯誤，Windows 可能還會開瀏覽器下載 `windowsdesktop-runtime-*.exe`。那個安裝檔不是必要的。

## 4. 移除

直接刪除 `%LOCALAPPDATA%\Microsoft\dotnet` 資料夾即可，沒有登錄檔殘留。

## 備註

- 正式版用 `dotnet publish --self-contained` 打包，產物不需要目標電腦裝任何 .NET。
