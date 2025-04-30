# KiCad UltraLibrarian Importer

A KiCad plugin for importing components from UltraLibrarian.

## Architecture

This plugin consists of two main components:

1. A KiCad Plugin (Python) - Acts as a launcher for the UI application
2. Avalonia UI Application (.NET) - Provides the user interface and core functionality

## Building the Plugin

### Prerequisites

- .NET SDK 9.0 or later
- Python 3.7 or later

### Build Steps

The project uses [Nuke](https://nuke.build/) for build automation:

1. Clone the repository
2. Navigate to the repository root
3. Run the build script:

```bash
# On Windows
.\UltraLibrarianImporter\build\build.cmd

# On macOS/Linux
./UltraLibrarianImporter/build/build.sh
```

This will:
- Build the .NET solution
- Publish the Avalonia UI application for Windows, macOS, and Linux
- Create a KiCad plugin package (ZIP file) in the output directory

## Installation

1. Open KiCad
2. Go to "Plugin and Content Manager"
3. Click "Install from File"
4. Select the generated ZIP file from the `output` directory
5. Restart KiCad

## Development

### Project Structure

- `kicad-ultralibrarian-importer/` - Python KiCad plugin
- `UltraLibrarianImporter/` - .NET solution containing:
  - `UltraLibrarianImporter.UI` - Avalonia UI application
  - `UltraLibrarianImporter.KiCadBindings` - Library for KiCad API communication

### Making Changes

1. Make changes to the code
2. Run the build script to create an updated package
3. Install the updated package in KiCad for testing

## License

MIT