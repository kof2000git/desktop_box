<#
.SYNOPSIS
    DesktopBox 一键发布脚本:编译 -> 测试 -> 发布单文件 -> 打包 DLL -> 清理。
    可选:打 tag + 创建 GitHub Release 并上传 zip(需 -Publish 开关,且已 gh auth login)。

.DESCRIPTION
    版本号从 src/DesktopBox/DesktopBox.csproj 的 <Version> 自动读取,无需在脚本里硬编码。
    产出:
      publish/DesktopBox.exe              单文件绿色版(~71MB,self-contained)
      publish/DesktopBox.ShellMenu.dll    原生右键菜单 DLL
      release/DesktopBox-<ver>-win-x64.zip  发布包(上述两个文件)

.PARAMETER Publish
    加上此开关才会打 git tag 并创建 GitHub Release 上传资产。不带则只本地构建打包。

.PARAMETER Notes
    GitHub Release 的发行说明。省略时用 csproj 版本号生成默认说明。

.EXAMPLE
    # 只本地构建打包(不碰 git/Release)
    .\release.ps1

    # 构建并发布到 GitHub
    .\release.ps1 -Publish -Notes "修复 XYZ"
#>
[CmdletBinding()]
param(
    [switch]$Publish,
    [string]$Notes
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $repoRoot
Write-Host "==> 仓库根: $repoRoot" -ForegroundColor Cyan

# ---------- 0. 读取版本号 ----------
$csproj = Join-Path $repoRoot 'src/DesktopBox/DesktopBox.csproj'
if (-not (Test-Path $csproj)) { throw "找不到 csproj: $csproj" }
$csprojXml = [xml](Get-Content $csproj)
$version = $csprojXml.Project.PropertyGroup.Version
if (-not $version) { throw "csproj 里没找到 <Version>" }
$tagName = "v$version"
Write-Host "==> 版本: $version (tag: $tagName)" -ForegroundColor Cyan

# 发布前确认 git 工作区干净(避免把未提交的改动打进 Release)
$gitDirty = git status --porcelain
if ($gitDirty) {
    Write-Host "==> git 工作区有未提交改动,请先提交:" -ForegroundColor Yellow
    Write-Host $gitDirty
    exit 1
}

# 关闭可能在跑的进程(避免 exe 文件占用)
$proc = Get-Process -Name DesktopBox -ErrorAction SilentlyContinue
if ($proc) {
    Write-Host "==> 关闭运行中的 DesktopBox..." -ForegroundColor Yellow
    $proc | Stop-Process -Force
    Start-Sleep -Seconds 1
}

# ---------- 1. 编译 ----------
Write-Host "`n==> [1/5] dotnet build (Release)..." -ForegroundColor Green
dotnet build DesktopBox.sln -c Release --nologo
if ($LASTEXITCODE -ne 0) { throw "编译失败" }

# ---------- 2. 测试 ----------
Write-Host "`n==> [2/5] dotnet test..." -ForegroundColor Green
dotnet test DesktopBox.sln -c Release --nologo --verbosity quiet
if ($LASTEXITCODE -ne 0) { throw "测试失败" }

# ---------- 3. 发布单文件 ----------
Write-Host "`n==> [3/5] dotnet publish (单文件)..." -ForegroundColor Green
Remove-Item -Recurse -Force (Join-Path $repoRoot 'publish') -ErrorAction SilentlyContinue
dotnet publish src/DesktopBox/DesktopBox.csproj `
    -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -o publish --nologo
if ($LASTEXITCODE -ne 0) { throw "发布失败" }

# ---------- 4. 原生右键菜单 DLL ----------
Write-Host "`n==> [4/5] 构建 DesktopBox.ShellMenu.dll..." -ForegroundColor Green
& (Join-Path $repoRoot 'src/DesktopBox.ShellMenu/build_dll.bat')
if ($LASTEXITCODE -ne 0) { throw "ShellMenu DLL 构建失败" }

# 清理副产物(pdb/exp/lib 对终端用户无用)
foreach ($junk in 'DesktopBox.pdb', 'DesktopBox.ShellMenu.exp', 'DesktopBox.ShellMenu.lib') {
    Remove-Item -Force (Join-Path $repoRoot "publish/$junk") -ErrorAction SilentlyContinue
}

# 快速冒烟:exe 能启动
$exe = Join-Path $repoRoot 'publish/DesktopBox.exe'
if (-not (Test-Path $exe)) { throw "exe 未生成: $exe" }
Write-Host "==> 启动冒烟测试..." -ForegroundColor Green
$p = Start-Process $exe -PassThru
Start-Sleep -Seconds 4
if ($p.HasExited) { throw "exe 启动后立即退出,ExitCode=$($p.ExitCode)" }
Stop-Process -Id $p.Id -Force
Write-Host "    exe 运行正常 (PID $($p.Id))" -ForegroundColor DarkGray

# ---------- 5. 打包 ----------
Write-Host "`n==> [5/5] 打包 zip..." -ForegroundColor Green
$releaseDir = Join-Path $repoRoot 'release'
New-Item -ItemType Directory -Force -Path $releaseDir | Out-Null
$zipName = "DesktopBox-$version-win-x64.zip"
$zipPath = Join-Path $releaseDir $zipName
Remove-Item -Force $zipPath -ErrorAction SilentlyContinue
Compress-Archive -Path `
    (Join-Path $repoRoot 'publish/DesktopBox.exe'), `
    (Join-Path $repoRoot 'publish/DesktopBox.ShellMenu.dll') `
    -DestinationPath $zipPath
$zipSize = [math]::Round((Get-Item $zipPath).Length / 1MB, 1)
Write-Host "==> 产出: $zipPath ($zipSize MB)" -ForegroundColor Cyan

Write-Host "`n========== 本地构建打包完成 ==========" -ForegroundColor Green
Write-Host "zip: $zipPath"
if (-not $Publish) {
    Write-Host "`n(本地构建完成。如需发布到 GitHub,加 -Publish 开关)" -ForegroundColor DarkGray
    return
}

# ---------- 6. GitHub Release ----------
Write-Host "`n==> [6] 发布到 GitHub..." -ForegroundColor Green

$gh = Get-Command gh -ErrorAction SilentlyContinue
if (-not $gh) {
    $ghExe = 'C:\Program Files\GitHub CLI\gh.exe'
    if (-not (Test-Path $ghExe)) { throw "找不到 gh CLI,请安装(winget install GitHub.cli)" }
} else { $ghExe = $gh.Source }

# tag 已存在则跳过创建
$existingTag = git tag -l $tagName
if ($existingTag) {
    Write-Host "    tag $tagName 已存在,跳过创建" -ForegroundColor DarkGray
} else {
    git tag -a $tagName -m "Release $tagName"
    git push origin $tagName
    if ($LASTEXITCODE -ne 0) { throw "推送 tag 失败" }
    Write-Host "    已创建并推送 tag $tagName" -ForegroundColor DarkGray
}

# notes 优先级:-Notes 参数 > release/v<version>-notes.md > 内置默认
if (-not $Notes) {
    $notesFilePath = Join-Path $releaseDir "v$version-notes.md"
    if (Test-Path $notesFilePath) {
        $Notes = Get-Content -Raw -Encoding UTF8 $notesFilePath
        Write-Host "    使用 notes 文件: release\v$version-notes.md" -ForegroundColor DarkGray
    } else {
        $Notes = "DesktopBox $tagName 发布包.`n`n绿色版,解压即用(含主程序 + 原生右键菜单 DLL)。`n运行环境:Windows 10/11 (64-bit),无需安装 .NET。"
    }
}

# Release 已存在则只补充上传资产,否则新建
& $ghExe release view $tagName --repo kof2000git/desktop_box *> $null
if ($LASTEXITCODE -eq 0) {
    Write-Host "    Release $tagName 已存在,上传/覆盖资产..." -ForegroundColor DarkGray
    & $ghExe release upload $tagName $zipPath --clobber --repo kof2000git/desktop_box
} else {
    # notes 写临时文件传给 gh:--notes 多行字符串易被 PowerShell 参数解析拆散,--notes-file 更稳
    $notesTmp = [System.IO.Path]::GetTempFileName()
    try {
        [System.IO.File]::WriteAllText($notesTmp, $Notes, [System.Text.UTF8Encoding]::new($false))
        & $ghExe release create $tagName $zipPath `
            --title $tagName --notes-file $notesTmp --repo kof2000git/desktop_box
    }
    finally { Remove-Item $notesTmp -ErrorAction SilentlyContinue }
}
if ($LASTEXITCODE -ne 0) { throw "GitHub Release 创建/上传失败" }

$relUrl = "https://github.com/kof2000git/desktop_box/releases/tag/$tagName"
Write-Host "`n========== 发布完成 ==========" -ForegroundColor Green
Write-Host "Release: $relUrl" -ForegroundColor Cyan
Write-Host "下载:    https://github.com/kof2000git/desktop_box/releases/download/$tagName/$zipName" -ForegroundColor Cyan
