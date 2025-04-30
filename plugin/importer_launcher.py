
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
    exe_path = os.path.join(script_dir, "bin", "UltralibrarianImporter.exe")
    
    # Check if the executable exists
    if not os.path.exists(exe_path):
        print(f"Error: Executable not found at {exe_path}")
        sys.exit(1)
    
    # Launch the executable with the current environment variables
    process = subprocess.Popen(exe_path, env=os.environ)
    
    # Wait for the process to complete
    return_code = process.wait()
    sys.exit(return_code)
    os.environ.update(os.environ)

launch_importer()
# The above code is a Python script that launches an external executable (UltralibrarianImporter.exe) from a specified directory.