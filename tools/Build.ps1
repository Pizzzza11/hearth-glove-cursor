param([string]$Version = '1.0.0')
$ErrorActionPreference = 'Stop'
if ($Version -notmatch '^\d+\.\d+\.\d+([-.][A-Za-z0-9.-]+)?$') { throw 'Invalid version.' }
$root = Split-Path $PSScriptRoot -Parent
$bin = Join-Path $root 'bin'
$dist = Join-Path $root 'dist'
$packageName = "HearthGloveCursor-$Version-windows"
$stage = Join-Path $dist $packageName
if (Test-Path -LiteralPath $stage) { throw "Build stage exists: $stage. Use a fresh version or move the old stage aside." }
New-Item -ItemType Directory -Path $bin,$dist,$stage -Force | Out-Null
$compiler = Join-Path $env:SystemRoot 'Microsoft.NET\Framework\v3.5\csc.exe'
$references = @('/reference:System.Xml.dll')
if (-not (Test-Path -LiteralPath $compiler)) {
    $compiler = Join-Path $env:SystemRoot 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
    $referenceRoot = Join-Path $bin 'net35-reference'
    if (-not (Test-Path -LiteralPath $referenceRoot)) {
        [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
        $download = Join-Path $bin 'net35-reference.zip'
        Invoke-WebRequest 'https://api.nuget.org/v3-flatcontainer/microsoft.netframework.referenceassemblies.net35/1.0.3/microsoft.netframework.referenceassemblies.net35.1.0.3.nupkg' -OutFile $download -UseBasicParsing
        Expand-Archive -LiteralPath $download -DestinationPath $referenceRoot
    }
    $referenceDll = Get-ChildItem -LiteralPath $referenceRoot -Filter mscorlib.dll -Recurse | Select-Object -First 1
    if (-not $referenceDll) { throw '.NET 3.5 reference assemblies were not found.' }
    $framework = $referenceDll.DirectoryName
    $references = @('/noconfig','/nostdlib+',('/reference:'+(Join-Path $framework 'mscorlib.dll')),('/reference:'+(Join-Path $framework 'System.dll')),('/reference:'+(Join-Path $framework 'System.Xml.dll')))
}
if (-not (Test-Path -LiteralPath $compiler)) { throw 'The .NET Framework C# compiler was not found.' }
$binary = Join-Path $bin 'HearthGloveCursor.exe'
& $compiler /nologo /target:winexe /optimize+ /platform:anycpu "/out:$binary" @references (Join-Path $root 'src\HearthGloveCursor.cs') (Join-Path $root 'src\CursorSetup.cs')
if ($LASTEXITCODE -ne 0) { throw 'C# build failed.' }
Copy-Item -LiteralPath $binary -Destination $stage
Copy-Item -LiteralPath (Join-Path $root 'src\HearthGloveCursor.exe.config') -Destination $stage
Copy-Item -LiteralPath (Join-Path $root 'src\HearthGloveCursor.exe.config') -Destination $bin -Force
foreach ($file in @('Install.cmd','Uninstall.cmd','Start.cmd','Stop.cmd','Status.cmd','README.md','README.en.md','LICENSE','NOTICE.md','CHANGELOG.md')) {
    Copy-Item -LiteralPath (Join-Path $root $file) -Destination $stage
}
Copy-Item -LiteralPath (Join-Path $root 'cursor-pack') -Destination $stage -Recurse
# Keep assets beside the staged EXE as well as in cursor-pack so the real
# Windows loader can verify the executable without installing the package.
Get-ChildItem -LiteralPath (Join-Path $stage 'cursor-pack') -File | Where-Object { $_.Extension -in @('.cur','.ani') } |
    Copy-Item -Destination $stage
$check = Start-Process -FilePath (Join-Path $stage 'HearthGloveCursor.exe') -ArgumentList '--verify-assets' -Wait -PassThru -WindowStyle Hidden
if ($check.ExitCode -ne 0) { throw 'Windows cursor-loader validation failed.' }
# Stage is retained, including these duplicate assets, so the EXE can also be verified by users.
$zip = Join-Path $dist ($packageName+'.zip')
Compress-Archive -LiteralPath $stage -DestinationPath $zip -CompressionLevel Optimal
$hash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
($hash+'  '+[IO.Path]::GetFileName($zip)) | Set-Content -LiteralPath (Join-Path $dist 'SHA256SUMS.txt') -Encoding ASCII
Write-Host "Release ZIP: $zip"
Write-Host "SHA256: $hash"
