"""
KiCad Component Explorer & Importer Plugin
"""

import os
import sys

__version__ = "0.2.0"
__author__ = "Daniel Meza, Fanefo"
__license__ = "MIT"

try:
    import pcbnew
    import wx
    from .importer_launcher import launch_importer

    class UltraLibrarianActionPlugin(pcbnew.ActionPlugin):
        def defaults(self):
            self.name = "Component Explorer & Importer"
            self.category = "Import"
            self.description = "Browse and import components from UltraLibrarian, SnapEDA, Octopart, EasyEDA, etc."
            self.show_toolbar_button = True
            icon_path = os.path.join(os.path.dirname(__file__), "resources", "icon-dark.png")
            if os.path.exists(icon_path):
                self.icon_file_name = icon_path

        def Run(self):
            launch_importer()

    exe_name = os.path.basename(sys.executable).lower()
    is_standalone_cli = exe_name.startswith("python") and wx.GetApp() is None

    if not is_standalone_cli:
        UltraLibrarianActionPlugin().register()
except Exception as e:
    print(f"[ComponentExplorer] Error during plugin registration: {e}")

