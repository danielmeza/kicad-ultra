
import sys
import os
import subprocess

def launch_importer():
    """
    Launches the importer module.
    """
    # The valonia app is a .exe application shipped with the kicad plugin
    # Determine the path to the executable
    script_dir = os.path.dirname(os.path.abspath(__file__))
    candidate_paths = [
        os.path.join(script_dir, "bin", "UltralibrarianImporter.exe"),
        os.path.join(script_dir, "bin", "UltraLibrarianImporter.UI.exe"),
        os.path.join(script_dir, "..", "src", "importer", "UltraLibrarianImporter.UI", "bin", "Debug", "net10.0", "UltraLibrarianImporter.UI.exe"),
        os.path.join(script_dir, "..", "src", "importer", "UltraLibrarianImporter.UI", "bin", "Release", "net10.0", "UltraLibrarianImporter.UI.exe"),
    ]
    
    exe_path = next((p for p in candidate_paths if os.path.exists(p)), None)
    
    # Check if the executable exists
    if not exe_path:
        print(f"Error: Executable not found in any of: {candidate_paths}")
        sys.exit(1)
    
    # Launch the executable with the current environment variables
    process = subprocess.Popen(exe_path, env=os.environ)
    
    # Wait for the process to complete
    return_code = process.wait()
    sys.exit(return_code)
    os.environ.update(os.environ)

launch_importer()
# The above code is a Python script that launches an external executable (UltralibrarianImporter.exe) from a specified directory.