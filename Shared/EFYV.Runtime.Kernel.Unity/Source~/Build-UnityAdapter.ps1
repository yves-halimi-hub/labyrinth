[CmdletBinding()]
param(
    [ValidateSet('Auto', 'MSVC', 'Docker')]
    [string] $NativeToolchain = 'Auto',

    [switch] $Check
)

$ErrorActionPreference = 'Stop'

$packageVersion = '0.2.0'
$dockerImage =
    'gcc@sha256:5e927c284bf55a7dc796262e311a0703344f62f41f5621eb56843111b1d37e15'
$sourceDirectory = Split-Path -Parent $PSCommandPath
$packageDirectory = Split-Path -Parent $sourceDirectory
$labyrinthRepository = Resolve-Path (Join-Path $packageDirectory '..\..')
$runtimeRepository = Resolve-Path (Join-Path $labyrinthRepository '..\..\..\EFYV-runtime-kernel')
$managedProject = Join-Path $sourceDirectory 'Efyv.RuntimeKernel.Unity.csproj'
$managedDestination = Join-Path $packageDirectory 'Runtime\Managed\Efyv.RuntimeKernel.dll'
$nativeDestination =
    Join-Path $packageDirectory 'Runtime\Plugins\x86_64\efyv_runtime_kernel.dll'
$provenanceDestination =
    Join-Path $packageDirectory `
        'Runtime\Plugins\x86_64\efyv_runtime_kernel.provenance.json'
$officialBindingSource =
    Join-Path $runtimeRepository.Path `
        'bindings\dotnet\Efyv.RuntimeKernel\RuntimeKernel.cs'
$msvcBuildDirectory =
    Join-Path $sourceDirectory 'BuildOutput~\native-windows-x64-msvc'
$mingwBuildDirectory =
    Join-Path $sourceDirectory 'BuildOutput~\native-windows-x64-mingw'

function Resolve-NativeToolchain {
    param(
        [Parameter(Mandatory)]
        [ValidateSet('Auto', 'MSVC', 'Docker')]
        [string] $Requested,

        [Parameter(Mandatory)]
        [bool] $HasHostCmake,

        [Parameter(Mandatory)]
        [bool] $HasDocker
    )

    if ($Requested -eq 'MSVC') {
        if (-not $HasHostCmake) {
            throw 'MSVC mode requires host CMake.'
        }
        return 'MSVC'
    }

    if ($Requested -eq 'Docker') {
        if (-not $HasDocker) {
            throw 'Docker mode requires the Docker CLI.'
        }
        return 'Docker'
    }

    if ($HasHostCmake) {
        return 'MSVC'
    }
    if ($HasDocker) {
        return 'Docker'
    }
    throw 'Auto mode requires either host CMake or Docker.'
}

function Get-RuntimeBuildInputTreeHash {
    param(
        [Parameter(Mandatory)]
        [string] $RuntimeRoot
    )

    $files = @(
        Get-Item -LiteralPath (Join-Path $RuntimeRoot 'CMakeLists.txt')
        Get-ChildItem -LiteralPath (Join-Path $RuntimeRoot 'include') -Recurse -File |
            Where-Object { $_.Extension -in '.h', '.hpp' }
        Get-ChildItem -LiteralPath (Join-Path $RuntimeRoot 'src') -Recurse -File |
            Where-Object { $_.Extension -in '.c', '.cpp', '.h', '.hpp', '.inc' }
    )
    $entries = foreach ($file in $files) {
        [PSCustomObject]@{
            File = $file
            Relative = [System.IO.Path]::GetRelativePath(
                $RuntimeRoot,
                $file.FullName).Replace('\', '/')
        }
    }

    $manifest = [System.Text.StringBuilder]::new()
    foreach ($entry in ($entries | Sort-Object -Property Relative)) {
        $fileHash =
            (Get-FileHash -LiteralPath $entry.File.FullName -Algorithm SHA256).Hash.
                ToLowerInvariant()
        [void] $manifest.Append($fileHash).
            Append('  ').
            Append($entry.Relative).
            Append("`n")
    }

    $hasher = [System.Security.Cryptography.SHA256]::Create()
    try {
        $bytes = [System.Text.Encoding]::UTF8.GetBytes($manifest.ToString())
        $digest = $hasher.ComputeHash($bytes)
        return [System.BitConverter]::ToString($digest).Replace('-', '').ToLowerInvariant()
    }
    finally {
        $hasher.Dispose()
    }
}

function Get-CmakeCompilerIdentity {
    param(
        [Parameter(Mandatory)]
        [string] $BuildDirectory
    )

    $compilerFile =
        Get-ChildItem -LiteralPath (Join-Path $BuildDirectory 'CMakeFiles') `
            -Filter 'CMakeCXXCompiler.cmake' -Recurse -File |
        Select-Object -First 1
    if ($null -eq $compilerFile) {
        return 'MSVC (version unavailable)'
    }

    $contents = Get-Content -LiteralPath $compilerFile.FullName -Raw
    $id = [regex]::Match(
        $contents,
        'set\(CMAKE_CXX_COMPILER_ID "([^"]+)"\)').Groups[1].Value
    $version = [regex]::Match(
        $contents,
        'set\(CMAKE_CXX_COMPILER_VERSION "([^"]+)"\)').Groups[1].Value
    return "$id $version".Trim()
}

$hostCmake =
    Get-Command cmake -CommandType Application -ErrorAction SilentlyContinue |
    Select-Object -First 1
$dockerExecutable =
    Get-Command docker -CommandType Application -ErrorAction SilentlyContinue |
    Select-Object -First 1
$gitExecutable =
    Get-Command git -CommandType Application -ErrorAction SilentlyContinue |
    Select-Object -First 1
$resolvedNativeToolchain = Resolve-NativeToolchain `
    -Requested $NativeToolchain `
    -HasHostCmake ($null -ne $hostCmake) `
    -HasDocker ($null -ne $dockerExecutable)

if ($Check) {
    $noHostCmakeResolution = Resolve-NativeToolchain `
        -Requested Auto `
        -HasHostCmake $false `
        -HasDocker $true
    if ($noHostCmakeResolution -ne 'Docker') {
        throw 'Auto must resolve to Docker when host CMake is unavailable.'
    }
    Write-Host "Adapter paths and selection logic are valid."
    Write-Host "Requested=$NativeToolchain Resolved=$resolvedNativeToolchain"
    Write-Host 'No-host-CMake Auto resolution=Docker'
    return
}

if ($null -eq $gitExecutable) {
    throw 'Git is required to record Runtime source provenance.'
}
$runtimeCommitBefore =
    (& $gitExecutable.Source -C $runtimeRepository.Path rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0) {
    throw 'Unable to identify the Runtime source commit before building.'
}
$runtimeInputHashBefore =
    Get-RuntimeBuildInputTreeHash -RuntimeRoot $runtimeRepository.Path
$bindingHashBefore =
    (Get-FileHash -LiteralPath $officialBindingSource -Algorithm SHA256).Hash.
        ToLowerInvariant()

dotnet build $managedProject --configuration Release --nologo
if ($LASTEXITCODE -ne 0) {
    throw 'The managed Unity adapter build failed.'
}

$builtNative = $null
$usedNativeToolchain = $null
$cmakeIdentity = $null
$compilerIdentity = $null
$containerImageIdentity = $null

if ($resolvedNativeToolchain -eq 'MSVC') {
    & $hostCmake.Source -S $runtimeRepository.Path -B $msvcBuildDirectory `
        -G 'Visual Studio 18 2026' -A x64 `
        -DBUILD_SHARED_LIBS=ON `
        -DEFYV_RUNTIME_BUILD_TESTS=OFF `
        -DEFYV_RUNTIME_BUILD_BENCHMARKS=OFF

    if ($LASTEXITCODE -eq 0) {
        & $hostCmake.Source --build $msvcBuildDirectory `
            --config Release --target efyv_runtime_kernel
        $candidate =
            Join-Path $msvcBuildDirectory 'Release\efyv_runtime_kernel.dll'
        if ($LASTEXITCODE -eq 0 -and
            (Test-Path -LiteralPath $candidate -PathType Leaf)) {
            $builtNative = $candidate
            $usedNativeToolchain = 'MSVC'
            $cmakeIdentity =
                (& $hostCmake.Source --version | Select-Object -First 1).Trim()
            $compilerIdentity =
                Get-CmakeCompilerIdentity -BuildDirectory $msvcBuildDirectory
        }
    }

    if ($null -eq $builtNative -and $NativeToolchain -eq 'MSVC') {
        throw 'The Runtime Kernel MSVC build failed. Install the Windows SDK or use -NativeToolchain Docker.'
    }
}

if ($null -eq $builtNative) {
    if ($null -eq $dockerExecutable) {
        throw 'Docker is required because the local MSVC path did not produce the native library.'
    }

    New-Item -ItemType Directory -Force -Path $mingwBuildDirectory | Out-Null
    $dockerBuildScript = @'
set -eu
apt-get update >/dev/null
DEBIAN_FRONTEND=noninteractive apt-get install -y --no-install-recommends cmake ninja-build mingw-w64 >/dev/null
cmake --version | sed -n '1p' > /build/efyv-cmake-version.txt
x86_64-w64-mingw32-g++-posix --version | sed -n '1p' > /build/efyv-cxx-version.txt
cmake -S /source -B /build -G Ninja \
  -DCMAKE_SYSTEM_NAME=Windows \
  -DCMAKE_C_COMPILER=x86_64-w64-mingw32-gcc-posix \
  -DCMAKE_CXX_COMPILER=x86_64-w64-mingw32-g++-posix \
  -DCMAKE_RC_COMPILER=x86_64-w64-mingw32-windres \
  -DCMAKE_BUILD_TYPE=Release \
  '-DCMAKE_SHARED_LINKER_FLAGS=-static-libgcc -static-libstdc++ -static' \
  -DBUILD_SHARED_LIBS=ON \
  -DEFYV_RUNTIME_BUILD_TESTS=OFF \
  -DEFYV_RUNTIME_BUILD_BENCHMARKS=OFF
cmake --build /build --target efyv_runtime_kernel --parallel
'@
    $dockerArguments = @(
        'run',
        '--rm',
        '--volume', "$($runtimeRepository.Path):/source:ro",
        '--volume', "${mingwBuildDirectory}:/build",
        $dockerImage,
        'sh',
        '-lc',
        $dockerBuildScript
    )
    & $dockerExecutable.Source @dockerArguments
    $candidate = Join-Path $mingwBuildDirectory 'libefyv_runtime_kernel.dll'
    if ($LASTEXITCODE -ne 0 -or
        -not (Test-Path -LiteralPath $candidate -PathType Leaf)) {
        throw 'The Runtime Kernel Docker/MinGW build failed.'
    }
    $builtNative = $candidate
    $usedNativeToolchain = 'Docker/MinGW'
    $containerImageIdentity = $dockerImage
    $cmakeIdentity =
        (Get-Content -LiteralPath (
            Join-Path $mingwBuildDirectory 'efyv-cmake-version.txt') -Raw).Trim()
    $compilerIdentity =
        (Get-Content -LiteralPath (
            Join-Path $mingwBuildDirectory 'efyv-cxx-version.txt') -Raw).Trim()
}

$runtimeCommitAfter =
    (& $gitExecutable.Source -C $runtimeRepository.Path rev-parse HEAD).Trim()
$runtimeInputHashAfter =
    Get-RuntimeBuildInputTreeHash -RuntimeRoot $runtimeRepository.Path
$bindingHashAfter =
    (Get-FileHash -LiteralPath $officialBindingSource -Algorithm SHA256).Hash.
        ToLowerInvariant()
if ($runtimeCommitAfter -ne $runtimeCommitBefore -or
    $runtimeInputHashAfter -ne $runtimeInputHashBefore -or
    $bindingHashAfter -ne $bindingHashBefore) {
    throw 'Runtime build inputs changed during the adapter build; the generated native artifact was not published.'
}

New-Item -ItemType Directory -Force `
    -Path (Split-Path -Parent $nativeDestination) | Out-Null
Copy-Item -LiteralPath $builtNative -Destination $nativeDestination -Force

$runtimeStatus =
    @(& $gitExecutable.Source -C $runtimeRepository.Path `
        status --porcelain=v1 --untracked-files=normal)
if ($LASTEXITCODE -ne 0) {
    throw 'Unable to inspect the Runtime working tree.'
}

$provenance = [ordered]@{
    schemaVersion = 1
    artifact = 'efyv_runtime_kernel.dll'
    packageVersion = $packageVersion
    status = 'verified-build'
    generatedAtUtc = [DateTime]::UtcNow.ToString('o')
    runtimeCommit = $runtimeCommitAfter
    runtimeWorkingTreeDirty = $runtimeStatus.Count -ne 0
    runtimeBuildInputTreeSha256 = $runtimeInputHashAfter
    officialBindingSourceSha256 = $bindingHashAfter
    managedAssemblyVersion =
        [System.Reflection.AssemblyName]::GetAssemblyName(
            $managedDestination).Version.ToString()
    nativeArtifactSha256 =
        (Get-FileHash -LiteralPath $nativeDestination -Algorithm SHA256).Hash.
            ToLowerInvariant()
    nativeToolchain = $usedNativeToolchain
    containerImage = $containerImageIdentity
    cmake = $cmakeIdentity
    cxxCompiler = $compilerIdentity
    buildConfiguration = 'Release'
    sharedLibrary = $true
}
$provenanceTemporary = "$provenanceDestination.tmp"
$provenance |
    ConvertTo-Json -Depth 3 |
    Set-Content -LiteralPath $provenanceTemporary -Encoding utf8NoBOM
[System.IO.File]::Move($provenanceTemporary, $provenanceDestination, $true)

Write-Host "Generated managed/native 0.2.0 artifacts via $usedNativeToolchain."
Write-Host "Recorded build provenance in $provenanceDestination."
