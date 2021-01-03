# Create a temporary folder to download to.
$tempFolder = Join-Path $env:TEMP "cypher"
New-Item $tempFolder -ItemType Directory -Force

# Get the latest release
$latestRelease = Invoke-WebRequest "https://api.github.com/repos/cypher-network/cypher/releases/latest" |
ConvertFrom-Json |
Select-Object tag_name
$tag_name =  $latestRelease.tag_name

# Download the zip
Write-Host "Downloading latest version ($tag_name)"
$client = New-Object "System.Net.WebClient"
$url = "https://github.com/cypher-network/cypher/releases/download/$tag_name/cypher.$tag_name.zip"
$zipFile = Join-Path $tempFolder "cypher.zip"
$client.DownloadFile($url,$zipFile)

$installationFolder = Join-Path $env:USERPROFILE ".cypher"
Microsoft.PowerShell.Archive\Expand-Archive $zipFile -DestinationPath $installationFolder -Force
Remove-Item $tempFolder -Recurse -Force

$binFolder = Join-Path $installationFolder "bin"
$runner = Join-Path $installationFolder "Runners"
$runner = Join-Path $runner "cypnode.cmd"

If (!(Test-Path $binFolder))
{
    New-Item -Path $binFolder -ItemType "directory"
}
Copy-Item $runner -Destination $binFolder -Force

$path = [System.Environment]::GetEnvironmentVariable("path", [System.EnvironmentVariableTarget]::User);
$paths = $path.Split(";") -inotlike $binFolder 
# Add the bin folder to the path
$paths += $binFolder
# Create the new path string
$path = $paths -join ";"

[System.Environment]::SetEnvironmentVariable("path", $path, [System.EnvironmentVariableTarget]::User)
Write-Host "cypher was installed successfully!"