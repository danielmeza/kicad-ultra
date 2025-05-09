using System;
using System.IO;

using KiCadSharp;
using KiCadSharp.Documents;

using SExpressionSharp;

namespace SampleConsole
{
    class KiCadTest
    {
        public static void TestKiCadParser()
        {
            Console.WriteLine("=== KiCad Document Parser Test ===");
            
            // Test Symbol Library Parsing
            TestSymbolParsing();
            
            // Test creating a new symbol
            TestCreateSymbol();
            
            // Test creating a new footprint
            TestCreateFootprint();
            
            Console.WriteLine("=== Tests Complete ===");
        }
        
        private static void TestSymbolParsing()
        {
            Console.WriteLine("\nTesting Symbol Library Parsing:");
            string symbolFilePath = @"c:\Users\danie\AppData\Roaming\UltralibrarianKicad\ul_STUSB4500LQTR\KiCADv6\2025-05-01_22-48-51.kicad_sym";
            
            if (File.Exists(symbolFilePath))
            {
                try
                {
                    // Parse the example file
                    var parser = new SExpressionParser();
                    var expression = parser.ParseFile(symbolFilePath);
                    var library = new KiCadSymbolLibrary(expression);
                    
                    // Display basic information
                    Console.WriteLine($"Library version: {library.Version}");
                    Console.WriteLine($"Generator: {library.Generator}");
                    Console.WriteLine($"Number of symbols: {library.Symbols.Count}");
                    
                    // Show details of the first symbol
                    if (library.Symbols.Count > 0)
                    {
                        var symbol = library.Symbols[0];
                        Console.WriteLine($"\nSymbol Id: {symbol.Id}");
                        Console.WriteLine("Properties:");
                        foreach (var property in symbol.Properties)
                        {
                            Console.WriteLine($"  {property.Key}: {property.Value}");
                        }
                        
                        Console.WriteLine($"Pins: {symbol.Pins.Count}");
                        Console.WriteLine($"Graphical items: {symbol.GraphicalItems.Count}");
                        
                        // Save a modified copy of the symbol
                        string outputPath = Path.Combine(Path.GetTempPath(), "test_symbol.kicad_sym");
                        
                        // Create a clone with a new name
                        var clonedSymbol = KiCadUtils.CloneSymbol(symbol, "STUSB4500_CLONE");
                        var newLibrary = new KiCadSymbolLibrary();
                        newLibrary.AddSymbol(clonedSymbol);
                        newLibrary.Save(outputPath);
                        
                        Console.WriteLine($"Modified symbol saved to: {outputPath}");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error parsing symbol library: {ex.Message}");
                }
            }
            else
            {
                Console.WriteLine($"Symbol file not found: {symbolFilePath}");
                Console.WriteLine("Example will use a newly created symbol instead.");
            }
        }
        
        private static void TestCreateSymbol()
        {
            Console.WriteLine("\nTesting Symbol Creation:");
            
            try
            {
                // Create a new symbol library
                var library = new KiCadSymbolLibrary("UltraLibrarian Test Generator");
                
                // Create a new symbol
                var symbol = new KiCadSymbol("TEST_COMPONENT");
                
                // Set properties
                symbol.AddProperty("Manufacturer", "Test Manufacturer");
                symbol.AddProperty("Manufacturer_Part_Number", "TEST123");
                symbol.AddProperty("Description", "Test Component for Parser Demo");
                
                // Add pins
                symbol.AddPin(new KiCadPin("input", "line", 
                    new KiCadPosition(0, 0, 0), 2.54, "VCC", "1"));
                
                symbol.AddPin(new KiCadPin("output", "line", 
                    new KiCadPosition(0, -2.54, 0), 2.54, "OUT", "2"));
                
                symbol.AddPin(new KiCadPin("power_in", "line", 
                    new KiCadPosition(0, -5.08, 0), 2.54, "GND", "3"));
                
                // Add a rectangle to represent the symbol body
                var rect = new KiCadRectangle(2.54, 2.54, 12.7, -7.62);
                symbol.AddGraphicalItem(rect);
                
                // Add the symbol to the library
                library.AddSymbol(symbol);
                
                // Save the library
                string outputPath = Path.Combine(Path.GetTempPath(), "test_created_symbol.kicad_sym");
                library.Save(outputPath);
                
                Console.WriteLine($"Created new symbol and saved to: {outputPath}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error creating symbol: {ex.Message}");
            }
        }
        
        private static void TestCreateFootprint()
        {
            Console.WriteLine("\nTesting Footprint Creation:");
            
            try
            {
                // Create a new footprint
                var footprint = new KiCadFootprint("TEST_FOOTPRINT");
                
                // Add attributes for SMD
                footprint.Attributes.Add("smd");
                
                // Add SMD pads
                footprint.AddPad("1", "smd", "rect", -2.0, 0.0, 1.0, 0.5, 
                    new System.Collections.Generic.List<string> { "F.Cu", "F.Paste", "F.Mask" });
                
                footprint.AddPad("2", "smd", "rect", 2.0, 0.0, 1.0, 0.5, 
                    new System.Collections.Generic.List<string> { "F.Cu", "F.Paste", "F.Mask" });
                
                // Add silkscreen outline
                footprint.AddLine(-3.0, -1.0, 3.0, -1.0, "F.SilkS");
                footprint.AddLine(3.0, -1.0, 3.0, 1.0, "F.SilkS");
                footprint.AddLine(3.0, 1.0, -3.0, 1.0, "F.SilkS");
                footprint.AddLine(-3.0, 1.0, -3.0, -1.0, "F.SilkS");
                
                // Add pin 1 marker
                footprint.AddLine(-3.0, -1.0, -3.0, -1.5, "F.SilkS");
                footprint.AddLine(-3.0, -1.5, -2.5, -1.5, "F.SilkS");
                
                // Add a courtyard
                footprint.AddLine(-3.5, -1.5, 3.5, -1.5, "F.CrtYd");
                footprint.AddLine(3.5, -1.5, 3.5, 1.5, "F.CrtYd");
                footprint.AddLine(3.5, 1.5, -3.5, 1.5, "F.CrtYd");
                footprint.AddLine(-3.5, 1.5, -3.5, -1.5, "F.CrtYd");
                
                // Add a 3D model (example path)
                var model = footprint.AddModel("${KICAD6_3DMODEL_DIR}/Package_SO.3dshapes/SOIC-8_3.9x4.9mm_P1.27mm.wrl");
                
                // Save the footprint
                string outputPath = Path.Combine(Path.GetTempPath(), "test_created_footprint.kicad_mod");
                KiCadFootprintLibrary.SaveFootprint(footprint, outputPath);
                
                Console.WriteLine($"Created new footprint and saved to: {outputPath}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error creating footprint: {ex.Message}");
            }
        }
    }
}