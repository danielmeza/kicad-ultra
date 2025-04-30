# Convert-SvgToPng.ps1
# This script converts SVG files to PNG format

# Install required NuGet package if not already installed
if (-not (Get-Package -Name Svg -ErrorAction SilentlyContinue)) {
    Write-Host "Installing Svg NuGet package..."
    Install-Package Svg -Scope CurrentUser -Force
}

# Load the Svg assembly
Add-Type -Path "$HOME\.nuget\packages\svg\3.4.4\lib\net462\Svg.dll"
Add-Type -AssemblyName System.Drawing

# Function to convert SVG to PNG
function Convert-SvgToPng {
    param (
        [string]$SvgPath,
        [string]$PngPath,
        [int]$Width = 48,
        [int]$Height = 48
    )

    try {
        Write-Host "Converting $SvgPath to $PngPath..."
        
        # Load the SVG document
        $svgDocument = [Svg.SvgDocument]::Open($SvgPath)
        
        # Create a bitmap to render the SVG
        $bitmap = New-Object System.Drawing.Bitmap $Width, $Height
        $bitmap.SetResolution(96, 96)
        
        # Create graphics object from the bitmap
        $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
        $graphics.Clear([System.Drawing.Color]::Transparent)
        
        # Render the SVG to the bitmap
        $svgDocument.Draw($graphics)
        
        # Save the bitmap as PNG
        $bitmap.Save($PngPath, [System.Drawing.Imaging.ImageFormat]::Png)
        
        # Dispose resources
        $graphics.Dispose()
        $bitmap.Dispose()
        
        Write-Host "Conversion completed successfully!"
    }
    catch {
        Write-Host "Error converting SVG to PNG: $_"
    }
}

# Get the base directory of the script
$baseDir = "e:\src\spn\kikad-ultra\kicad-ultralibrarian-importer\resources"

# Convert all SVG files
$svgFiles = @(
    "icon.svg",
    "icon-dark.svg",
    "icon-light.svg"
)

foreach ($svgFile in $svgFiles) {
    $svgPath = Join-Path $baseDir $svgFile
    $pngPath = Join-Path $baseDir ($svgFile -replace '\.svg$', '.png')
    Convert-SvgToPng -SvgPath $svgPath -PngPath $pngPath -Width 48 -Height 48
}

Write-Host "All conversions completed!"