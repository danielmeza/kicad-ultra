using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using SExpressionSharp;

namespace KiCadSharp.Documents
{
    /// <summary>
    /// Represents a KiCad footprint library
    /// </summary>
    public class KiCadFootprintLibrary
    {
        private readonly SExpression _rootExpression;
        
        /// <summary>
        /// Gets the library version
        /// </summary>
        public string Version { get; }
        
        /// <summary>
        /// Gets the generator used to create the library
        /// </summary>
        public string Generator { get; }

        /// <summary>
        /// Gets the list of footprints in this library
        /// </summary>
        public List<KiCadFootprint> Footprints { get; } = new List<KiCadFootprint>();

        /// <summary>
        /// Create a new empty KiCad footprint library
        /// </summary>
        /// <param name="generator">Name of the generator creating this library</param>
        /// <param name="version">Version string</param>
        public KiCadFootprintLibrary(string generator = "KiCad Library Importer", string version = "20211014")
        {
            _rootExpression = new SExpression("kicad_pcb");
            _rootExpression.CreateChild("version", version);
            _rootExpression.CreateChild("generator", generator);
            
            Version = version;
            Generator = generator;
            
            // Add common elements
            _rootExpression.CreateChild("general");
            _rootExpression.CreateChild("paper", "A4");
            _rootExpression.CreateChild("layers");
        }

        /// <summary>
        /// Create a KiCad footprint library from an existing S-expression
        /// </summary>
        /// <param name="expression">Root S-expression for the library</param>
        public KiCadFootprintLibrary(SExpression expression)
        {
            _rootExpression = expression;
            
            // Extract metadata
            var versionExp = expression.GetChild("version");
            Version = versionExp?.GetValueAsString() ?? "20211014";
            
            var generatorExp = expression.GetChild("generator");
            Generator = generatorExp?.GetValueAsString() ?? "KiCad Library Importer";
            
            // Extract all footprints
            foreach (var footprintExp in expression.GetChildren("footprint"))
            {
                Footprints.Add(new KiCadFootprint(footprintExp));
            }
            
            // Handle the special case where a .kicad_mod file contains a single module directly
            if (Footprints.Count == 0 && expression.Token == "module")
            {
                Footprints.Add(new KiCadFootprint(expression));
            }
        }

        /// <summary>
        /// Load a KiCad footprint library from a file
        /// </summary>
        /// <param name="filePath">Path to the .kicad_pcb or .kicad_mod file</param>
        /// <returns>The loaded footprint library</returns>
        public static KiCadFootprintLibrary Load(string filePath)
        {
            var parser = new SExpressionParser();
            var expression = parser.ParseFile(filePath);
            return new KiCadFootprintLibrary(expression);
        }

        /// <summary>
        /// Add a footprint to the library
        /// </summary>
        /// <param name="footprint">Footprint to add</param>
        public void AddFootprint(KiCadFootprint footprint)
        {
            Footprints.Add(footprint);
            _rootExpression.AddChild(footprint.ToSExpression());
        }

        /// <summary>
        /// Save the library to a file
        /// </summary>
        /// <param name="filePath">Path to the output file</param>
        public void Save(string filePath)
        {
            // Clear existing footprints and re-add them
            // This ensures the root expression has the current state of all footprints
            var footprintExpressions = _rootExpression.GetChildren("footprint").ToList();
            foreach (var footprintExp in footprintExpressions)
            {
                _rootExpression.Children.Remove(footprintExp);
            }
            
            foreach (var footprint in Footprints)
            {
                _rootExpression.AddChild(footprint.ToSExpression());
            }
            
            // Write to file
            var writer = new SExpressionWriter();
            writer.WriteToFile(_rootExpression, filePath);
        }

        /// <summary>
        /// Save a single footprint to a .kicad_mod file
        /// </summary>
        /// <param name="footprint">Footprint to save</param>
        /// <param name="filePath">Path to the output .kicad_mod file</param>
        public static void SaveFootprint(KiCadFootprint footprint, string filePath)
        {
            var writer = new SExpressionWriter();
            writer.WriteToFile(footprint.ToSExpression(), filePath);
        }

        /// <summary>
        /// Gets a footprint by ID from the library
        /// </summary>
        /// <param name="id">Footprint ID to find</param>
        /// <returns>The footprint, or null if not found</returns>
        public KiCadFootprint? GetFootprint(string id)
        {
            return Footprints.FirstOrDefault(s => s.Id == id);
        }
    }

    /// <summary>
    /// Represents a KiCad footprint
    /// </summary>
    public class KiCadFootprint
    {
        /// <summary>
        /// Gets the identifier of this footprint
        /// </summary>
        public string Id { get; set; }
        
        /// <summary>
        /// Gets or sets the layer on which this footprint is placed
        /// </summary>
        public string Layer { get; set; } = "F.Cu";
        
        /// <summary>
        /// Gets or sets optional tedit timestamp
        /// </summary>
        public long Tedit { get; set; }
        
        /// <summary>
        /// Gets or sets optional tstamp timestamp
        /// </summary>
        public long Tstamp { get; set; }
        
        /// <summary>
        /// Gets the list of attributes for this footprint
        /// </summary>
        public List<string> Attributes { get; } = new List<string>();
        
        /// <summary>
        /// Gets the list of model 3D references for this footprint
        /// </summary>
        public List<KiCadModel> Models { get; } = new List<KiCadModel>();
        
        /// <summary>
        /// Gets the list of text items for this footprint
        /// </summary>
        public List<KiCadFpText> TextItems { get; } = new List<KiCadFpText>();
        
        /// <summary>
        /// Gets the list of pads for this footprint
        /// </summary>
        public List<KiCadPad> Pads { get; } = new List<KiCadPad>();
        
        /// <summary>
        /// Gets the list of lines for this footprint
        /// </summary>
        public List<KiCadFpLine> Lines { get; } = new List<KiCadFpLine>();
        
        /// <summary>
        /// Gets the list of circles for this footprint
        /// </summary>
        public List<KiCadFpCircle> Circles { get; } = new List<KiCadFpCircle>();
        
        /// <summary>
        /// Gets the list of arcs for this footprint
        /// </summary>
        public List<KiCadFpArc> Arcs { get; } = new List<KiCadFpArc>();
        
        /// <summary>
        /// Gets the list of polygons for this footprint
        /// </summary>
        public List<KiCadFpPoly> Polygons { get; } = new List<KiCadFpPoly>();

        /// <summary>
        /// Create a new KiCad footprint
        /// </summary>
        /// <param name="id">Footprint identifier</param>
        public KiCadFootprint(string id)
        {
            Id = id;
            Tedit = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            Tstamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            
            // Add mandatory reference and value text
            AddFpText("reference", "REF**", 0, 0, "F.SilkS");
            AddFpText("value", id, 0, 1.27, "F.Fab");
        }

        /// <summary>
        /// Create a KiCad footprint from an S-expression
        /// </summary>
        /// <param name="expression">S-expression node for the footprint</param>
        public KiCadFootprint(SExpression expression)
        {
            // The token could be either 'footprint' or 'module' depending on the file format version
            // Extract ID, which is the first value
            Id = expression.GetValue(0) ?? "Unknown";
            
            // Extract layer
            var layerExp = expression.GetChild("layer");
            Layer = layerExp?.GetValueAsString() ?? "F.Cu";
            
            // Extract timestamps if present
            var teditExp = expression.GetChild("tedit");
            if (teditExp != null)
            {
                // The timestamp is a hexadecimal string
                string teditHex = teditExp.GetValueAsString() ?? "0";
                if (teditHex.StartsWith("0x"))
                    teditHex = teditHex.Substring(2);
                
                if (long.TryParse(teditHex, System.Globalization.NumberStyles.HexNumber, null, out long tedit))
                    Tedit = tedit;
            }
            
            var tstampExp = expression.GetChild("tstamp");
            if (tstampExp != null)
            {
                // The timestamp is a hexadecimal string
                string tstampHex = tstampExp.GetValueAsString() ?? "0";
                if (tstampHex.StartsWith("0x"))
                    tstampHex = tstampHex.Substring(2);
                
                if (long.TryParse(tstampHex, System.Globalization.NumberStyles.HexNumber, null, out long tstamp))
                    Tstamp = tstamp;
            }
            
            // Extract attributes
            var attrsExp = expression.GetChild("attr");
            if (attrsExp != null)
            {
                foreach (var attr in attrsExp.Values)
                {
                    Attributes.Add(attr);
                }
            }
            
            // Extract 3D models
            foreach (var modelExp in expression.GetChildren("model"))
            {
                Models.Add(new KiCadModel(modelExp));
            }
            
            // Extract text items
            foreach (var textExp in expression.GetChildren("fp_text"))
            {
                TextItems.Add(new KiCadFpText(textExp));
            }
            
            // Extract pads
            foreach (var padExp in expression.GetChildren("pad"))
            {
                Pads.Add(new KiCadPad(padExp));
            }
            
            // Extract lines
            foreach (var lineExp in expression.GetChildren("fp_line"))
            {
                Lines.Add(new KiCadFpLine(lineExp));
            }
            
            // Extract circles
            foreach (var circleExp in expression.GetChildren("fp_circle"))
            {
                Circles.Add(new KiCadFpCircle(circleExp));
            }
            
            // Extract arcs
            foreach (var arcExp in expression.GetChildren("fp_arc"))
            {
                Arcs.Add(new KiCadFpArc(arcExp));
            }
            
            // Extract polygons
            foreach (var polyExp in expression.GetChildren("fp_poly"))
            {
                Polygons.Add(new KiCadFpPoly(polyExp));
            }
        }

        /// <summary>
        /// Add a text item to the footprint
        /// </summary>
        /// <param name="type">Text type (reference, value, user)</param>
        /// <param name="text">Text content</param>
        /// <param name="x">X position</param>
        /// <param name="y">Y position</param>
        /// <param name="layer">Layer name</param>
        /// <returns>The created text item</returns>
        public KiCadFpText AddFpText(string type, string text, double x, double y, string layer)
        {
            var fpText = new KiCadFpText
            {
                Type = type,
                Text = text,
                Position = new KiCadPosition(x, y),
                Layer = layer
            };
            
            TextItems.Add(fpText);
            return fpText;
        }

        /// <summary>
        /// Add a pad to the footprint
        /// </summary>
        /// <param name="number">Pad number or name</param>
        /// <param name="type">Pad type (smd, thru_hole, etc.)</param>
        /// <param name="shape">Pad shape (rect, circle, etc.)</param>
        /// <param name="x">X position</param>
        /// <param name="y">Y position</param>
        /// <param name="width">Pad width</param>
        /// <param name="height">Pad height</param>
        /// <param name="layers">List of layers</param>
        /// <returns>The created pad</returns>
        public KiCadPad AddPad(string number, string type, string shape, double x, double y, double width, double height, List<string> layers)
        {
            var pad = new KiCadPad
            {
                Number = number,
                Type = type,
                Shape = shape,
                Position = new KiCadPosition(x, y),
                Size = new KiCadSize(width, height),
                Layers = layers
            };
            
            Pads.Add(pad);
            return pad;
        }

        /// <summary>
        /// Add a line to the footprint
        /// </summary>
        /// <param name="startX">Start X position</param>
        /// <param name="startY">Start Y position</param>
        /// <param name="endX">End X position</param>
        /// <param name="endY">End Y position</param>
        /// <param name="layer">Layer name</param>
        /// <param name="width">Line width</param>
        /// <returns>The created line</returns>
        public KiCadFpLine AddLine(double startX, double startY, double endX, double endY, string layer, double width = 0.12)
        {
            var line = new KiCadFpLine
            {
                Start = new KiCadPosition(startX, startY),
                End = new KiCadPosition(endX, endY),
                Layer = layer,
                Width = width
            };
            
            Lines.Add(line);
            return line;
        }

        /// <summary>
        /// Add a circle to the footprint
        /// </summary>
        /// <param name="centerX">Center X position</param>
        /// <param name="centerY">Center Y position</param>
        /// <param name="endX">End X position (defines radius)</param>
        /// <param name="endY">End Y position (defines radius)</param>
        /// <param name="layer">Layer name</param>
        /// <param name="width">Line width</param>
        /// <returns>The created circle</returns>
        public KiCadFpCircle AddCircle(double centerX, double centerY, double endX, double endY, string layer, double width = 0.12)
        {
            var circle = new KiCadFpCircle
            {
                Center = new KiCadPosition(centerX, centerY),
                End = new KiCadPosition(endX, endY),
                Layer = layer,
                Width = width
            };
            
            Circles.Add(circle);
            return circle;
        }

        /// <summary>
        /// Add a 3D model to the footprint
        /// </summary>
        /// <param name="path">Path to the 3D model file</param>
        /// <returns>The created model reference</returns>
        public KiCadModel AddModel(string path)
        {
            var model = new KiCadModel
            {
                Path = path,
                Offset = new KiCadOffset(0, 0, 0),
                Scale = new KiCadScale(1, 1, 1),
                Rotation = new KiCadRotation(0, 0, 0)
            };
            
            Models.Add(model);
            return model;
        }

        /// <summary>
        /// Convert the footprint to an S-expression
        /// </summary>
        /// <returns>The S-expression representing this footprint</returns>
        public SExpression ToSExpression()
        {
            var expression = new SExpression("footprint", Id);
            
            // Add layer
            expression.CreateChild("layer", Layer);
            
            // Add tedit/tstamp
            if (Tedit > 0)
            {
                expression.CreateChild("tedit", $"0x{Tedit:X}");
            }
            
            if (Tstamp > 0)
            {
                expression.CreateChild("tstamp", $"0x{Tstamp:X}");
            }
            
            // Add attributes
            if (Attributes.Count > 0)
            {
                var attrsExp = expression.CreateChild("attr");
                foreach (var attr in Attributes)
                {
                    attrsExp.Values.Add(attr);
                }
            }
            
            // Add text items
            foreach (var text in TextItems)
            {
                expression.AddChild(text.ToSExpression());
            }
            
            // Add pads
            foreach (var pad in Pads)
            {
                expression.AddChild(pad.ToSExpression());
            }
            
            // Add lines
            foreach (var line in Lines)
            {
                expression.AddChild(line.ToSExpression());
            }
            
            // Add circles
            foreach (var circle in Circles)
            {
                expression.AddChild(circle.ToSExpression());
            }
            
            // Add arcs
            foreach (var arc in Arcs)
            {
                expression.AddChild(arc.ToSExpression());
            }
            
            // Add polygons
            foreach (var poly in Polygons)
            {
                expression.AddChild(poly.ToSExpression());
            }
            
            // Add 3D models
            foreach (var model in Models)
            {
                expression.AddChild(model.ToSExpression());
            }
            
            return expression;
        }
    }

    /// <summary>
    /// Represents a text element in a KiCad footprint
    /// </summary>
    public class KiCadFpText
    {
        /// <summary>
        /// Gets or sets the type of text (reference, value, user)
        /// </summary>
        public string Type { get; set; } = "user";
        
        /// <summary>
        /// Gets or sets the text content
        /// </summary>
        public string Text { get; set; } = "";
        
        /// <summary>
        /// Gets or sets the position
        /// </summary>
        public KiCadPosition Position { get; set; } = new KiCadPosition(0, 0);
        
        /// <summary>
        /// Gets or sets the layer
        /// </summary>
        public string Layer { get; set; } = "F.SilkS";
        
        /// <summary>
        /// Gets or sets the text size
        /// </summary>
        public KiCadSize Size { get; set; } = new KiCadSize(1, 1);
        
        /// <summary>
        /// Gets or sets the text thickness
        /// </summary>
        public double Thickness { get; set; } = 0.15;
        
        /// <summary>
        /// Gets or sets whether the text is italic
        /// </summary>
        public bool Italic { get; set; } = false;
        
        /// <summary>
        /// Gets or sets whether the text is hidden
        /// </summary>
        public bool Hide { get; set; } = false;

        /// <summary>
        /// Create a KiCad footprint text from an S-expression
        /// </summary>
        /// <param name="expression">S-expression node for the text</param>
        public KiCadFpText(SExpression expression)
        {
            Type = expression.GetValue(0) ?? "user";
            Text = expression.GetValue(1) ?? "";
            
            var atExp = expression.GetChild("at");
            if (atExp != null)
            {
                Position = new KiCadPosition(
                    atExp.GetValueAsDouble(0),
                    atExp.GetValueAsDouble(1),
                    atExp.Values.Count > 2 ? atExp.GetValueAsDouble(2) : 0);
            }
            
            var layerExp = expression.GetChild("layer");
            Layer = layerExp?.GetValueAsString() ?? "F.SilkS";
            
            var effectsExp = expression.GetChild("effects");
            if (effectsExp != null)
            {
                var fontExp = effectsExp.GetChild("font");
                if (fontExp != null)
                {
                    var sizeExp = fontExp.GetChild("size");
                    if (sizeExp != null)
                    {
                        Size = new KiCadSize(
                            sizeExp.GetValueAsDouble(0),
                            sizeExp.GetValueAsDouble(1));
                    }
                    
                    var thicknessExp = fontExp.GetChild("thickness");
                    if (thicknessExp != null)
                    {
                        Thickness = thicknessExp.GetValueAsDouble();
                    }
                    
                    Italic = fontExp.GetChild("italic") != null;
                }
                
                Hide = effectsExp.GetChild("hide") != null;
            }
        }

        /// <summary>
        /// Create a new KiCad footprint text
        /// </summary>
        public KiCadFpText()
        {
        }

        /// <summary>
        /// Convert the text to an S-expression
        /// </summary>
        /// <returns>The S-expression representing this text</returns>
        public SExpression ToSExpression()
        {
            var expression = new SExpression("fp_text", Type, Text);
            
            // Add position
            var atExp = expression.CreateChild("at", 
                Position.X.ToString(), 
                Position.Y.ToString());
            
            if (Position.Rotation != 0)
            {
                atExp.Values.Add(Position.Rotation.ToString());
            }
            
            // Add layer
            expression.CreateChild("layer", Layer);
            
            // Add effects
            var effectsExp = expression.CreateChild("effects");
            var fontExp = effectsExp.CreateChild("font");
            
            fontExp.CreateChild("size", 
                Size.Width.ToString(), 
                Size.Height.ToString());
            
            fontExp.CreateChild("thickness", Thickness.ToString());
            
            if (Italic)
            {
                fontExp.CreateChild("italic");
            }
            
            if (Hide)
            {
                effectsExp.CreateChild("hide");
            }
            
            return expression;
        }
    }

    /// <summary>
    /// Represents a pad in a KiCad footprint
    /// </summary>
    public class KiCadPad
    {
        /// <summary>
        /// Gets or sets the pad number or name
        /// </summary>
        public string Number { get; set; } = "1";
        
        /// <summary>
        /// Gets or sets the pad type (smd, thru_hole, npth)
        /// </summary>
        public string Type { get; set; } = "smd";
        
        /// <summary>
        /// Gets or sets the pad shape (rect, circle, oval, etc.)
        /// </summary>
        public string Shape { get; set; } = "rect";
        
        /// <summary>
        /// Gets or sets the position
        /// </summary>
        public KiCadPosition Position { get; set; } = new KiCadPosition(0, 0);
        
        /// <summary>
        /// Gets or sets the size
        /// </summary>
        public KiCadSize Size { get; set; } = new KiCadSize(1, 1);
        
        /// <summary>
        /// Gets or sets the drill (for thru_hole pads)
        /// </summary>
        public KiCadDrill? Drill { get; set; }
        
        /// <summary>
        /// Gets or sets the layers this pad is on
        /// </summary>
        public List<string> Layers { get; set; } = new List<string>();

        /// <summary>
        /// Create a KiCad pad from an S-expression
        /// </summary>
        /// <param name="expression">S-expression node for the pad</param>
        public KiCadPad(SExpression expression)
        {
            Number = expression.GetValue(0) ?? "1";
            Type = expression.GetValue(1) ?? "smd";
            Shape = expression.GetValue(2) ?? "rect";
            
            var atExp = expression.GetChild("at");
            if (atExp != null)
            {
                Position = new KiCadPosition(
                    atExp.GetValueAsDouble(0),
                    atExp.GetValueAsDouble(1),
                    atExp.Values.Count > 2 ? atExp.GetValueAsDouble(2) : 0);
            }
            
            var sizeExp = expression.GetChild("size");
            if (sizeExp != null)
            {
                Size = new KiCadSize(
                    sizeExp.GetValueAsDouble(0),
                    sizeExp.GetValueAsDouble(1));
            }
            
            var drillExp = expression.GetChild("drill");
            if (drillExp != null)
            {
                double drillSize = drillExp.GetValueAsDouble(0);
                if (drillExp.Values.Count > 1)
                {
                    // Oval drill
                    Drill = new KiCadDrill(
                        drillExp.GetValueAsDouble(0),
                        drillExp.GetValueAsDouble(1));
                }
                else
                {
                    // Round drill
                    Drill = new KiCadDrill(drillSize);
                }
                
                // Check for offset
                var drillOffsetExp = drillExp.GetChild("offset");
                if (drillOffsetExp != null)
                {
                    Drill.Offset = new KiCadPosition(
                        drillOffsetExp.GetValueAsDouble(0),
                        drillOffsetExp.GetValueAsDouble(1));
                }
            }
            
            var layersExp = expression.GetChild("layers");
            if (layersExp != null)
            {
                Layers = layersExp.Values.ToList();
            }
        }

        /// <summary>
        /// Create a new KiCad pad
        /// </summary>
        public KiCadPad()
        {
        }

        /// <summary>
        /// Convert the pad to an S-expression
        /// </summary>
        /// <returns>The S-expression representing this pad</returns>
        public SExpression ToSExpression()
        {
            var expression = new SExpression("pad", Number, Type, Shape);
            
            // Add position
            var atExp = expression.CreateChild("at", 
                Position.X.ToString(), 
                Position.Y.ToString());
            
            if (Position.Rotation != 0)
            {
                atExp.Values.Add(Position.Rotation.ToString());
            }
            
            // Add size
            expression.CreateChild("size", 
                Size.Width.ToString(), 
                Size.Height.ToString());
            
            // Add drill for thru_hole pads
            if (Drill != null && Type == "thru_hole")
            {
                var drillExp = Drill.IsRound
                    ? expression.CreateChild("drill", Drill.Size.ToString())
                    : expression.CreateChild("drill", Drill.Width.ToString(), Drill.Height.ToString());
                
                if (Drill.Offset != null && (Drill.Offset.X != 0 || Drill.Offset.Y != 0))
                {
                    drillExp.CreateChild("offset", 
                        Drill.Offset.X.ToString(), 
                        Drill.Offset.Y.ToString());
                }
            }
            
            // Add layers
            var layersExp = expression.CreateChild("layers");
            foreach (var layer in Layers)
            {
                layersExp.Values.Add(layer);
            }
            
            return expression;
        }
    }

    /// <summary>
    /// Represents a line in a KiCad footprint
    /// </summary>
    public class KiCadFpLine
    {
        /// <summary>
        /// Gets or sets the start position
        /// </summary>
        public KiCadPosition Start { get; set; } = new KiCadPosition(0, 0);
        
        /// <summary>
        /// Gets or sets the end position
        /// </summary>
        public KiCadPosition End { get; set; } = new KiCadPosition(0, 0);
        
        /// <summary>
        /// Gets or sets the layer
        /// </summary>
        public string Layer { get; set; } = "F.SilkS";
        
        /// <summary>
        /// Gets or sets the line width
        /// </summary>
        public double Width { get; set; } = 0.12;

        /// <summary>
        /// Create a KiCad footprint line from an S-expression
        /// </summary>
        /// <param name="expression">S-expression node for the line</param>
        public KiCadFpLine(SExpression expression)
        {
            var startExp = expression.GetChild("start");
            if (startExp != null)
            {
                Start = new KiCadPosition(
                    startExp.GetValueAsDouble(0),
                    startExp.GetValueAsDouble(1));
            }
            
            var endExp = expression.GetChild("end");
            if (endExp != null)
            {
                End = new KiCadPosition(
                    endExp.GetValueAsDouble(0),
                    endExp.GetValueAsDouble(1));
            }
            
            var layerExp = expression.GetChild("layer");
            Layer = layerExp?.GetValueAsString() ?? "F.SilkS";
            
            var widthExp = expression.GetChild("width");
            Width = widthExp?.GetValueAsDouble() ?? 0.12;
        }

        /// <summary>
        /// Create a new KiCad footprint line
        /// </summary>
        public KiCadFpLine()
        {
        }

        /// <summary>
        /// Convert the line to an S-expression
        /// </summary>
        /// <returns>The S-expression representing this line</returns>
        public SExpression ToSExpression()
        {
            var expression = new SExpression("fp_line");
            
            // Add start and end points
            expression.CreateChild("start", Start.X.ToString(), Start.Y.ToString());
            expression.CreateChild("end", End.X.ToString(), End.Y.ToString());
            
            // Add layer
            expression.CreateChild("layer", Layer);
            
            // Add width
            expression.CreateChild("width", Width.ToString());
            
            return expression;
        }
    }

    /// <summary>
    /// Represents a circle in a KiCad footprint
    /// </summary>
    public class KiCadFpCircle
    {
        /// <summary>
        /// Gets or sets the center position
        /// </summary>
        public KiCadPosition Center { get; set; } = new KiCadPosition(0, 0);
        
        /// <summary>
        /// Gets or sets the end position (defines radius)
        /// </summary>
        public KiCadPosition End { get; set; } = new KiCadPosition(0, 0);
        
        /// <summary>
        /// Gets or sets the layer
        /// </summary>
        public string Layer { get; set; } = "F.SilkS";
        
        /// <summary>
        /// Gets or sets the line width
        /// </summary>
        public double Width { get; set; } = 0.12;

        /// <summary>
        /// Create a KiCad footprint circle from an S-expression
        /// </summary>
        /// <param name="expression">S-expression node for the circle</param>
        public KiCadFpCircle(SExpression expression)
        {
            var centerExp = expression.GetChild("center");
            if (centerExp != null)
            {
                Center = new KiCadPosition(
                    centerExp.GetValueAsDouble(0),
                    centerExp.GetValueAsDouble(1));
            }
            
            var endExp = expression.GetChild("end");
            if (endExp != null)
            {
                End = new KiCadPosition(
                    endExp.GetValueAsDouble(0),
                    endExp.GetValueAsDouble(1));
            }
            
            var layerExp = expression.GetChild("layer");
            Layer = layerExp?.GetValueAsString() ?? "F.SilkS";
            
            var widthExp = expression.GetChild("width");
            Width = widthExp?.GetValueAsDouble() ?? 0.12;
        }

        /// <summary>
        /// Create a new KiCad footprint circle
        /// </summary>
        public KiCadFpCircle()
        {
        }

        /// <summary>
        /// Convert the circle to an S-expression
        /// </summary>
        /// <returns>The S-expression representing this circle</returns>
        public SExpression ToSExpression()
        {
            var expression = new SExpression("fp_circle");
            
            // Add center and end points
            expression.CreateChild("center", Center.X.ToString(), Center.Y.ToString());
            expression.CreateChild("end", End.X.ToString(), End.Y.ToString());
            
            // Add layer
            expression.CreateChild("layer", Layer);
            
            // Add width
            expression.CreateChild("width", Width.ToString());
            
            return expression;
        }
    }

    /// <summary>
    /// Represents an arc in a KiCad footprint
    /// </summary>
    public class KiCadFpArc
    {
        /// <summary>
        /// Gets or sets the start position
        /// </summary>
        public KiCadPosition Start { get; set; } = new KiCadPosition(0, 0);
        
        /// <summary>
        /// Gets or sets the end position
        /// </summary>
        public KiCadPosition End { get; set; } = new KiCadPosition(0, 0);
        
        /// <summary>
        /// Gets or sets the angle in degrees
        /// </summary>
        public double Angle { get; set; } = 0;
        
        /// <summary>
        /// Gets or sets the layer
        /// </summary>
        public string Layer { get; set; } = "F.SilkS";
        
        /// <summary>
        /// Gets or sets the line width
        /// </summary>
        public double Width { get; set; } = 0.12;

        /// <summary>
        /// Create a KiCad footprint arc from an S-expression
        /// </summary>
        /// <param name="expression">S-expression node for the arc</param>
        public KiCadFpArc(SExpression expression)
        {
            var startExp = expression.GetChild("start");
            if (startExp != null)
            {
                Start = new KiCadPosition(
                    startExp.GetValueAsDouble(0),
                    startExp.GetValueAsDouble(1));
            }
            
            var endExp = expression.GetChild("end");
            if (endExp != null)
            {
                End = new KiCadPosition(
                    endExp.GetValueAsDouble(0),
                    endExp.GetValueAsDouble(1));
            }
            
            var angleExp = expression.GetChild("angle");
            if (angleExp != null)
            {
                Angle = angleExp.GetValueAsDouble();
            }
            
            var layerExp = expression.GetChild("layer");
            Layer = layerExp?.GetValueAsString() ?? "F.SilkS";
            
            var widthExp = expression.GetChild("width");
            Width = widthExp?.GetValueAsDouble() ?? 0.12;
        }

        /// <summary>
        /// Create a new KiCad footprint arc
        /// </summary>
        public KiCadFpArc()
        {
        }

        /// <summary>
        /// Convert the arc to an S-expression
        /// </summary>
        /// <returns>The S-expression representing this arc</returns>
        public SExpression ToSExpression()
        {
            var expression = new SExpression("fp_arc");
            
            // Add start and end points
            expression.CreateChild("start", Start.X.ToString(), Start.Y.ToString());
            expression.CreateChild("end", End.X.ToString(), End.Y.ToString());
            
            // Add angle
            expression.CreateChild("angle", Angle.ToString());
            
            // Add layer
            expression.CreateChild("layer", Layer);
            
            // Add width
            expression.CreateChild("width", Width.ToString());
            
            return expression;
        }
    }

    /// <summary>
    /// Represents a polygon in a KiCad footprint
    /// </summary>
    public class KiCadFpPoly
    {
        /// <summary>
        /// Gets or sets the points in the polygon
        /// </summary>
        public List<KiCadPosition> Points { get; set; } = new List<KiCadPosition>();
        
        /// <summary>
        /// Gets or sets the layer
        /// </summary>
        public string Layer { get; set; } = "F.SilkS";
        
        /// <summary>
        /// Gets or sets the line width
        /// </summary>
        public double Width { get; set; } = 0.12;

        /// <summary>
        /// Create a KiCad footprint polygon from an S-expression
        /// </summary>
        /// <param name="expression">S-expression node for the polygon</param>
        public KiCadFpPoly(SExpression expression)
        {
            var ptsExp = expression.GetChild("pts");
            if (ptsExp != null)
            {
                foreach (var xyExp in ptsExp.GetChildren("xy"))
                {
                    Points.Add(new KiCadPosition(
                        xyExp.GetValueAsDouble(0),
                        xyExp.GetValueAsDouble(1)));
                }
            }
            
            var layerExp = expression.GetChild("layer");
            Layer = layerExp?.GetValueAsString() ?? "F.SilkS";
            
            var widthExp = expression.GetChild("width");
            Width = widthExp?.GetValueAsDouble() ?? 0.12;
        }

        /// <summary>
        /// Create a new KiCad footprint polygon
        /// </summary>
        public KiCadFpPoly()
        {
        }

        /// <summary>
        /// Convert the polygon to an S-expression
        /// </summary>
        /// <returns>The S-expression representing this polygon</returns>
        public SExpression ToSExpression()
        {
            var expression = new SExpression("fp_poly");
            
            // Add points
            var ptsExp = expression.CreateChild("pts");
            foreach (var point in Points)
            {
                ptsExp.CreateChild("xy", point.X.ToString(), point.Y.ToString());
            }
            
            // Add layer
            expression.CreateChild("layer", Layer);
            
            // Add width
            expression.CreateChild("width", Width.ToString());
            
            return expression;
        }
    }

    /// <summary>
    /// Represents a 3D model reference in a KiCad footprint
    /// </summary>
    public class KiCadModel
    {
        /// <summary>
        /// Gets or sets the path to the 3D model
        /// </summary>
        public string Path { get; set; } = "";
        
        /// <summary>
        /// Gets or sets the offset
        /// </summary>
        public KiCadOffset Offset { get; set; } = new KiCadOffset(0, 0, 0);
        
        /// <summary>
        /// Gets or sets the scale
        /// </summary>
        public KiCadScale Scale { get; set; } = new KiCadScale(1, 1, 1);
        
        /// <summary>
        /// Gets or sets the rotation
        /// </summary>
        public KiCadRotation Rotation { get; set; } = new KiCadRotation(0, 0, 0);

        /// <summary>
        /// Create a KiCad model from an S-expression
        /// </summary>
        /// <param name="expression">S-expression node for the model</param>
        public KiCadModel(SExpression expression)
        {
            Path = expression.GetValue(0) ?? "";
            
            var offsetExp = expression.GetChild("offset");
            if (offsetExp != null)
            {
                Offset = new KiCadOffset(
                    offsetExp.GetChild("xyz")?.GetValueAsDouble(0) ?? 0,
                    offsetExp.GetChild("xyz")?.GetValueAsDouble(1) ?? 0,
                    offsetExp.GetChild("xyz")?.GetValueAsDouble(2) ?? 0);
            }
            
            var scaleExp = expression.GetChild("scale");
            if (scaleExp != null)
            {
                Scale = new KiCadScale(
                    scaleExp.GetChild("xyz")?.GetValueAsDouble(0) ?? 1,
                    scaleExp.GetChild("xyz")?.GetValueAsDouble(1) ?? 1,
                    scaleExp.GetChild("xyz")?.GetValueAsDouble(2) ?? 1);
            }
            
            var rotateExp = expression.GetChild("rotate");
            if (rotateExp != null)
            {
                Rotation = new KiCadRotation(
                    rotateExp.GetChild("xyz")?.GetValueAsDouble(0) ?? 0,
                    rotateExp.GetChild("xyz")?.GetValueAsDouble(1) ?? 0,
                    rotateExp.GetChild("xyz")?.GetValueAsDouble(2) ?? 0);
            }
        }

        /// <summary>
        /// Create a new KiCad model
        /// </summary>
        public KiCadModel()
        {
        }

        /// <summary>
        /// Convert the model to an S-expression
        /// </summary>
        /// <returns>The S-expression representing this model</returns>
        public SExpression ToSExpression()
        {
            var expression = new SExpression("model", Path);
            
            // Add offset
            var offsetExp = expression.CreateChild("offset");
            offsetExp.CreateChild("xyz", Offset.X.ToString(), Offset.Y.ToString(), Offset.Z.ToString());
            
            // Add scale
            var scaleExp = expression.CreateChild("scale");
            scaleExp.CreateChild("xyz", Scale.X.ToString(), Scale.Y.ToString(), Scale.Z.ToString());
            
            // Add rotation
            var rotateExp = expression.CreateChild("rotate");
            rotateExp.CreateChild("xyz", Rotation.X.ToString(), Rotation.Y.ToString(), Rotation.Z.ToString());
            
            return expression;
        }
    }

    #region Helper Structures

    /// <summary>
    /// Represents a drill definition for a thru_hole pad
    /// </summary>
    public class KiCadDrill
    {
        /// <summary>
        /// Gets the drill size for a round hole
        /// </summary>
        public double Size { get; }
        
        /// <summary>
        /// Gets the drill width for an oval hole
        /// </summary>
        public double Width { get; }
        
        /// <summary>
        /// Gets the drill height for an oval hole
        /// </summary>
        public double Height { get; }
        
        /// <summary>
        /// Gets whether the drill is round
        /// </summary>
        public bool IsRound { get; }
        
        /// <summary>
        /// Gets or sets the drill offset
        /// </summary>
        public KiCadPosition? Offset { get; set; }

        /// <summary>
        /// Create a round drill
        /// </summary>
        /// <param name="size">Drill diameter</param>
        public KiCadDrill(double size)
        {
            Size = size;
            Width = size;
            Height = size;
            IsRound = true;
        }

        /// <summary>
        /// Create an oval drill
        /// </summary>
        /// <param name="width">Drill width</param>
        /// <param name="height">Drill height</param>
        public KiCadDrill(double width, double height)
        {
            Size = Math.Min(width, height);
            Width = width;
            Height = height;
            IsRound = false;
        }
    }

    /// <summary>
    /// Represents a 3D offset
    /// </summary>
    public class KiCadOffset
    {
        /// <summary>
        /// Gets or sets the X offset
        /// </summary>
        public double X { get; set; }
        
        /// <summary>
        /// Gets or sets the Y offset
        /// </summary>
        public double Y { get; set; }
        
        /// <summary>
        /// Gets or sets the Z offset
        /// </summary>
        public double Z { get; set; }

        /// <summary>
        /// Create a new 3D offset
        /// </summary>
        /// <param name="x">X offset</param>
        /// <param name="y">Y offset</param>
        /// <param name="z">Z offset</param>
        public KiCadOffset(double x, double y, double z)
        {
            X = x;
            Y = y;
            Z = z;
        }
    }

    /// <summary>
    /// Represents a 3D scale
    /// </summary>
    public class KiCadScale
    {
        /// <summary>
        /// Gets or sets the X scale
        /// </summary>
        public double X { get; set; }
        
        /// <summary>
        /// Gets or sets the Y scale
        /// </summary>
        public double Y { get; set; }
        
        /// <summary>
        /// Gets or sets the Z scale
        /// </summary>
        public double Z { get; set; }

        /// <summary>
        /// Create a new 3D scale
        /// </summary>
        /// <param name="x">X scale</param>
        /// <param name="y">Y scale</param>
        /// <param name="z">Z scale</param>
        public KiCadScale(double x, double y, double z)
        {
            X = x;
            Y = y;
            Z = z;
        }
    }

    /// <summary>
    /// Represents a 3D rotation
    /// </summary>
    public class KiCadRotation
    {
        /// <summary>
        /// Gets or sets the X rotation
        /// </summary>
        public double X { get; set; }
        
        /// <summary>
        /// Gets or sets the Y rotation
        /// </summary>
        public double Y { get; set; }
        
        /// <summary>
        /// Gets or sets the Z rotation
        /// </summary>
        public double Z { get; set; }

        /// <summary>
        /// Create a new 3D rotation
        /// </summary>
        /// <param name="x">X rotation</param>
        /// <param name="y">Y rotation</param>
        /// <param name="z">Z rotation</param>
        public KiCadRotation(double x, double y, double z)
        {
            X = x;
            Y = y;
            Z = z;
        }
    }

    #endregion
}