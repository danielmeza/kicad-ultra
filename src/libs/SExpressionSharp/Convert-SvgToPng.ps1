# Convert SVG to PNG using Inkscape
# This script creates PNG versions of the icon at different sizes

param (
    [string]$SvgPath = ".\icon.svg",
    [string]$OutputDir = "."
)

$sizes = @(16, 32, 64, 128, 256, 512)

# Check if Inkscape is installed
$inkscapePath = "C:\Program Files\Inkscape\bin\inkscape.exe"
if (-not (Test-Path $inkscapePath)) {
    Write-Host "Inkscape not found at $inkscapePath. Please install Inkscape or correct the path."
    exit 1
}

# Create PNGs at different sizes
foreach ($size in $sizes) {
    $outputPath = Join-Path $OutputDir "icon-$size.png"
    
    Write-Host "Creating $size x $size PNG at $outputPath..."
    
    & $inkscapePath --export-filename="$outputPath" -w $size -h $size "$SvgPath"
    
    if ($LASTEXITCODE -eq 0) {
        Write-Host "Successfully created $outputPath" -ForegroundColor Green
    } else {
        Write-Host "Failed to create $outputPath" -ForegroundColor Red
    }
}

Write-Host "PNG conversion complete!"
