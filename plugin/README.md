# KiCad UltraLibrarian Importer

A KiCad v9 plugin that allows you to easily import schematic symbols, footprints, and 3D models from UltraLibrarian directly into your KiCad projects.

## Features

- **Web-based UltraLibrarian Integration**: Browse and download components from UltraLibrarian directly within KiCad
- **Seamless Import**: Automatically import downloaded components into KiCad
- **Multiple Component Types**: Support for schematic symbols, footprints, and 3D models
- **KiCad v9 Compatible**: Designed for the latest KiCad version using the new IPC API
- **Native wxWidgets UI**: Uses KiCad's native UI framework for seamless integration

## Installation

### Prerequisites

- KiCad v9.0 or later
- Python 3.8 or later
- wxPython package

### Method 1: Installation via Plugin Manager (Recommended)

1. In KiCad, go to **Plugin and Content Manager**
2. Search for "UltraLibrarian Importer"
3. Click **Install**
4. Restart KiCad

### Method 2: Manual Installation

1. Download the latest release from [GitHub](https://github.com/yourusername/kicad-ultralibrarian-importer/releases)
2. Extract the ZIP file
3. Copy the extracted folder to your KiCad plugins directory:
   - Windows: `%APPDATA%\kicad\9.0\plugins\`
   - macOS: `~/Library/Application Support/kicad/9.0/plugins/`
   - Linux: `~/.local/share/kicad/9.0/plugins/`
4. Install dependencies:
   ```
   pip install -r requirements.txt
   ```
5. Restart KiCad

## Usage

1. In KiCad, go to **Tools** > **UltraLibrarian Importer**
2. The plugin will open with the UltraLibrarian website in a browser window
3. Search for and select the component you need on UltraLibrarian
4. Download the component (the plugin will automatically detect downloads)
5. In the plugin window, select the import options (Symbol, Footprint, 3D Model)
6. Click **Import** to add the component to your KiCad libraries

## Configuration

You can configure the plugin settings by clicking the **Settings** button in the main plugin window:

- **KiCad Paths**: Specify custom paths for KiCad libraries
- **UltraLibrarian Options**: Configure download directory
- **Import Options**: Set preferences for how components are imported

## Troubleshooting

### Common Issues

#### Plugin doesn't appear in KiCad
- Make sure KiCad is restarted after installation
- Check that all dependencies are installed correctly

#### Downloads not detected
- Check that you have a working internet connection
- Ensure your browser doesn't block downloads

#### Import fails
- Verify that the KiCad paths in settings are correct
- Check the KiCad IPC API token is configured

### Reporting Issues

If you encounter problems, please report them on the [GitHub issue tracker](https://github.com/yourusername/kicad-ultralibrarian-importer/issues) with:

1. Description of the problem
2. Steps to reproduce
3. Error messages (if any)
4. KiCad and plugin version

## License

This project is licensed under the MIT License - see the LICENSE file for details.

## Acknowledgements

- [KiCad](https://www.kicad.org/) for their excellent PCB design software
- [UltraLibrarian](https://www.ultralibrarian.com/) for their component library service
- Inspired by [Octopart KiCad Integration](https://github.com/slugspark/Octopart_KiCad_Integration)