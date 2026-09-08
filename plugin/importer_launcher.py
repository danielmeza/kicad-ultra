import sys
import os
import subprocess

def launch_importer():
    """
    Launches the Avalonia importer desktop application.
    """
    script_dir = os.path.dirname(os.path.abspath(__file__))
    candidate_paths = [
        os.path.join(script_dir, "bin", "UltraLibrarianImporter.UI.exe"),
        os.path.join(script_dir, "bin", "UltralibrarianImporter.exe"),
        r"d:\proiecte Programare\KiCad-pluggin-discord-try\kicad-ultra-master\src\importer\UltraLibrarianImporter.UI\bin\Debug\net10.0\UltraLibrarianImporter.UI.exe",
        os.path.join(script_dir, "..", "src", "importer", "UltraLibrarianImporter.UI", "bin", "Debug", "net10.0", "UltraLibrarianImporter.UI.exe"),
        os.path.join(script_dir, "..", "src", "importer", "UltraLibrarianImporter.UI", "bin", "Release", "net10.0", "UltraLibrarianImporter.UI.exe"),
    ]
    
    exe_path = next((p for p in candidate_paths if os.path.exists(p)), None)
    
    if not exe_path:
        print(f"Error: Executable not found in any of: {candidate_paths}")
        return
    
    env = os.environ.copy()
    subprocess.Popen([exe_path], env=env)

if __name__ == "__main__":
    launch_importer()