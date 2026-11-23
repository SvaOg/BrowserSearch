set windows-shell := ["pwsh", "-NoLogo", "-NoProfile", "-NonInteractive", "-Command"]
set script-interpreter := ["pwsh", "-NoLogo", "-NoProfile", "-NonInteractive", "-File"]

project := "BrowserSearch\\BrowserSearch.csproj"
configuration := "Release"
libs_dir := "BrowserSearch\\libs"
publish_dir := "PublishOutput"
powertoys_min_version := "0.102.0"
powertoys_user_dir := env("LOCALAPPDATA") + "\\PowerToys"
powertoys_dir := if path_exists(powertoys_user_dir + "\\PowerToys.exe") == "true" { powertoys_user_dir } else { env("ProgramFiles") + "\\PowerToys" }
plugin_dir := env("LOCALAPPDATA") + "\\Microsoft\\PowerToys\\PowerToys Run\\Plugins\\BrowserSearch"

# List available recipes
default:
    @just --list

# Remove build output and copied PowerToys DLLs
[script]
[extension(".ps1")]
clean:
    $ErrorActionPreference = 'Stop'
    foreach ($dir in 'BrowserSearch\bin', 'BrowserSearch\obj', '{{ libs_dir }}', '{{ publish_dir }}') {
        if (Test-Path $dir) {
            Remove-Item $dir -Recurse -Force
            Write-Host "Removed $dir"
        }
    }

# Check that a compatible PowerToys is installed
[script]
[extension(".ps1")]
check:
    $ErrorActionPreference = 'Stop'
    $exe = Join-Path '{{ powertoys_dir }}' 'PowerToys.exe'
    if (-not (Test-Path $exe)) {
        throw "PowerToys not found at '{{ powertoys_dir }}'"
    }

    $version = [version](Get-Item $exe).VersionInfo.FileVersion
    if ($version -lt [version]'{{ powertoys_min_version }}') {
        throw "PowerToys $version is too old, {{ powertoys_min_version }} or newer is required"
    }

    # The plugin runs inside PowerToys Run, so both must target the same .NET version
    $runtimeConfig = Get-Content (Join-Path '{{ powertoys_dir }}' 'PowerToys.PowerLauncher.runtimeconfig.json') -Raw | ConvertFrom-Json
    $powertoysTfm = $runtimeConfig.runtimeOptions.tfm
    $projectTfm = ([xml](Get-Content '{{ project }}' -Raw)).Project.PropertyGroup.TargetFramework -replace '-windows.*$'
    if ($powertoysTfm -ne $projectTfm) {
        throw "PowerToys $version targets $powertoysTfm but the project targets $projectTfm"
    }
    Write-Host "Found PowerToys $version ($powertoysTfm) at {{ powertoys_dir }}"

# Copy the required PowerToys DLLs to the libs folder
[script]
[extension(".ps1")]
libs: check
    $ErrorActionPreference = 'Stop'
    New-Item -ItemType Directory -Force '{{ libs_dir }}' > $null
    foreach ($dll in 'Wox.Plugin.dll', 'Wox.Infrastructure.dll', 'Microsoft.Data.Sqlite.dll', 'PowerToys.Settings.UI.Lib.dll') {
        Copy-Item (Join-Path '{{ powertoys_dir }}' $dll) '{{ libs_dir }}' -Force
    }

# Build the plugin
build: libs
    dotnet build {{ project }} -c {{ configuration }}

# Publish the plugin to the output folder
[script]
[extension(".ps1")]
publish: build
    $ErrorActionPreference = 'Stop'
    if (Test-Path '{{ publish_dir }}') {
        Remove-Item '{{ publish_dir }}' -Recurse -Force
    }

    dotnet publish {{ project }} -c {{ configuration }} -o '{{ publish_dir }}' --no-build
    if ($LASTEXITCODE -ne 0) {
        exit $LASTEXITCODE
    }

    # PowerToys already ships these
    foreach ($dll in 'Microsoft.Windows.SDK.NET.dll', 'WinRT.Runtime.dll') {
        Remove-Item (Join-Path '{{ publish_dir }}' $dll) -ErrorAction SilentlyContinue
    }

# Install the plugin, restarting PowerToys if it's running
[script]
[extension(".ps1")]
install: publish
    $ErrorActionPreference = 'Stop'
    $installDir = [IO.Path]::GetFullPath('{{ powertoys_dir }}').TrimEnd('\') + '\'
    $processes = @(Get-Process -Name 'PowerToys*' -ErrorAction SilentlyContinue)

    # The path of an elevated process can't be read from a non-elevated shell
    if ($processes | Where-Object { -not $_.Path }) {
        throw 'PowerToys is running as administrator, run this recipe from an elevated shell'
    }

    $processes = @($processes | Where-Object { $_.Path.StartsWith($installDir, [StringComparison]::OrdinalIgnoreCase) })
    $runnerPath = ($processes | Where-Object Name -eq 'PowerToys' | Select-Object -First 1).Path

    try {
        # PowerToys Run locks the plugin's DLL. Stop the runner first so it doesn't relaunch its modules
        if ($processes) {
            Write-Host 'Stopping PowerToys...'
            $processes | Sort-Object { $_.Name -ne 'PowerToys' } | Stop-Process -Force -ErrorAction SilentlyContinue
            $processes | Wait-Process -Timeout 15
        }

        # Windows can keep the DLL locked briefly after PowerToys exits. Moving the
        # folder is all-or-nothing, so a locked file can't leave a partial install
        $pluginDir = '{{ plugin_dir }}'
        $backupDir = Join-Path $env:TEMP "BrowserSearch-$([guid]::NewGuid().ToString('N'))"
        $hasBackup = $false
        if (Test-Path $pluginDir) {
            $deadline = (Get-Date).AddSeconds(15)
            while (-not $hasBackup) {
                try {
                    [IO.Directory]::Move($pluginDir, $backupDir)
                    $hasBackup = $true
                }
                catch {
                    if ((Get-Date) -ge $deadline) {
                        throw
                    }
                    Start-Sleep -Milliseconds 250
                }
            }
        }

        try {
            New-Item -ItemType Directory -Path $pluginDir > $null
            Copy-Item '{{ publish_dir }}\*' $pluginDir -Recurse
        }
        catch {
            Remove-Item $pluginDir -Recurse -Force -ErrorAction SilentlyContinue
            if ($hasBackup) {
                [IO.Directory]::Move($backupDir, $pluginDir)
            }
            throw
        }

        if ($hasBackup) {
            Remove-Item $backupDir -Recurse -Force -ErrorAction SilentlyContinue
        }
        Write-Host "Plugin installed to $pluginDir"
    }
    finally {
        if ($runnerPath) {
            Write-Host 'Starting PowerToys...'
            Start-Process $runnerPath
        }
    }
