using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using SExpressionSharp;

namespace KiCadSharp.Documents
{
    /// <summary>
    /// Represents a KiCad symbol library
    /// </summary>
    public class KiCadSymbolLibrary
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
        /// Gets the list of symbols in this library
        /// </summary>
        public List<KiCadSymbol> Symbols { get; } = new List<KiCadSymbol>();

        /// <summary>
        /// Create a new empty KiCad symbol library
        /// </summary>
        /// <param name="generator">Name of the generator creating this library</param>
        /// <param name="version">Version string</param>
        public KiCadSymbolLibrary(string generator = "KiCad Library Importer", string version = "20211014")
        {
            _rootExpression = new SExpression("kicad_symbol_lib");
            _rootExpression.CreateChild("version", version);
            _rootExpression.CreateChild("generator", generator);
            
            Version = version;
            Generator = generator;
        }

        /// <summary>
        /// Create a KiCad symbol library from an existing S-expression
        /// </summary>
        /// <param name="expression">Root S-expression for the library</param>
        public KiCadSymbolLibrary(SExpression expression)
        {
            _rootExpression = expression;
            
            // Extract metadata
            var versionExp = expression.GetChild("version");
            Version = versionExp?.GetValueAsString() ?? "20211014";
            
            var generatorExp = expression.GetChild("generator");
            Generator = generatorExp?.GetValueAsString() ?? "KiCad Library Importer";
            
            // Extract all symbols
            foreach (var symbolExp in expression.GetChildren("symbol"))
            {
                Symbols.Add(new KiCadSymbol(symbolExp));
            }
        }

        /// <summary>
        /// Load a KiCad symbol library from a file
        /// </summary>
        /// <param name="filePath">Path to the .kicad_sym file</param>
        /// <returns>The loaded symbol library</returns>
        public static KiCadSymbolLibrary Load(string filePath)
        {
            var parser = new SExpressionParser();
            var expression = parser.ParseFile(filePath);
            return new KiCadSymbolLibrary(expression);
        }

        /// <summary>
        /// Add a symbol to the library
        /// </summary>
        /// <param name="symbol">Symbol to add</param>
        public void AddSymbol(KiCadSymbol symbol)
        {
            Symbols.Add(symbol);
            _rootExpression.AddChild(symbol.ToSExpression());
        }

        /// <summary>
        /// Save the library to a file
        /// </summary>
        /// <param name="filePath">Path to the output .kicad_sym file</param>
        public void Save(string filePath)
        {
            // Clear existing symbols and re-add them
            // This ensures the root expression has the current state of all symbols
            var symbolExpressions = _rootExpression.GetChildren("symbol").ToList();
            foreach (var symbolExp in symbolExpressions)
            {
                _rootExpression.Children.Remove(symbolExp);
            }
            
            foreach (var symbol in Symbols)
            {
                _rootExpression.AddChild(symbol.ToSExpression());
            }
            
            // Write to file
            var writer = new SExpressionWriter();
            writer.WriteToFile(_rootExpression, filePath);
        }

        /// <summary>
        /// Gets a symbol by ID from the library
        /// </summary>
        /// <param name="id">Symbol ID to find</param>
        /// <returns>The symbol, or null if not found</returns>
        public KiCadSymbol? GetSymbol(string id)
        {
            return Symbols.FirstOrDefault(s => s.Id == id);
        }
    }

    /// <summary>
    /// Represents a KiCad symbol
    /// </summary>
    public class KiCadSymbol
    {
        /// <summary>
        /// Gets the identifier of this symbol
        /// </summary>
        public string Id { get; set; }
        
        /// <summary>
        /// Gets the list of properties for this symbol
        /// </summary>
        public List<KiCadProperty> Properties { get; } = new List<KiCadProperty>();
        
        /// <summary>
        /// Gets the list of pins for this symbol
        /// </summary>
        public List<KiCadPin> Pins { get; } = new List<KiCadPin>();
        
        /// <summary>
        /// Gets the list of graphical items for this symbol
        /// </summary>
        public List<KiCadGraphicalItem> GraphicalItems { get; } = new List<KiCadGraphicalItem>();
        
        /// <summary>
        /// Gets or sets whether to hide pin numbers
        /// </summary>
        public bool HidePinNumbers { get; set; }
        
        /// <summary>
        /// Gets or sets whether to hide pin names
        /// </summary>
        public bool HidePinNames { get; set; }
        
        /// <summary>
        /// Gets or sets whether the symbol is included in the Bill of Materials
        /// </summary>
        public bool InBom { get; set; } = true;
        
        /// <summary>
        /// Gets or sets whether the symbol is placed on the board
        /// </summary>
        public bool OnBoard { get; set; } = true;

        /// <summary>
        /// Create a new KiCad symbol
        /// </summary>
        /// <param name="id">Symbol identifier</param>
        public KiCadSymbol(string id)
        {
            Id = id;
            
            // Add mandatory properties
            AddProperty("Reference", "U");
            AddProperty("Value", id);
            AddProperty("Footprint", "");
            AddProperty("Datasheet", "");
        }

        /// <summary>
        /// Create a KiCad symbol from an S-expression
        /// </summary>
        /// <param name="expression">S-expression node for the symbol</param>
        public KiCadSymbol(SExpression expression)
        {
            // Extract ID
            Id = expression.GetValue(0) ?? "Unknown";
            
            // Extract options
            var pinNumbersExp = expression.GetChild("pin_numbers");
            HidePinNumbers = pinNumbersExp?.GetChild("hide") != null;
            
            var pinNamesExp = expression.GetChild("pin_names");
            HidePinNames = pinNamesExp?.GetChild("hide") != null;
            
            var inBomExp = expression.GetChild("in_bom");
            InBom = inBomExp?.GetValueAsString() == "yes";
            
            var onBoardExp = expression.GetChild("on_board");
            OnBoard = onBoardExp?.GetValueAsString() == "yes";
            
            // Extract properties
            foreach (var propExp in expression.GetChildren("property"))
            {
                Properties.Add(new KiCadProperty(propExp));
            }
            
            // Extract pins
            foreach (var pinExp in expression.GetChildren("pin"))
            {
                Pins.Add(new KiCadPin(pinExp));
            }
            
            // Extract graphical items
            foreach (var polylineExp in expression.GetChildren("polyline"))
            {
                GraphicalItems.Add(new KiCadPolyline(polylineExp));
            }
            
            foreach (var rectangleExp in expression.GetChildren("rectangle"))
            {
                GraphicalItems.Add(new KiCadRectangle(rectangleExp));
            }
            
            foreach (var circleExp in expression.GetChildren("circle"))
            {
                GraphicalItems.Add(new KiCadCircle(circleExp));
            }
            
            foreach (var arcExp in expression.GetChildren("arc"))
            {
                GraphicalItems.Add(new KiCadArc(arcExp));
            }
            
            foreach (var textExp in expression.GetChildren("text"))
            {
                GraphicalItems.Add(new KiCadText(textExp));
            }
        }

        /// <summary>
        /// Add a property to the symbol
        /// </summary>
        /// <param name="key">Property key</param>
        /// <param name="value">Property value</param>
        /// <returns>The newly created property</returns>
        public KiCadProperty AddProperty(string key, string value)
        {
            var property = new KiCadProperty(key, value, Properties.Count);
            Properties.Add(property);
            return property;
        }

        /// <summary>
        /// Add a pin to the symbol
        /// </summary>
        /// <param name="pin">Pin to add</param>
        public void AddPin(KiCadPin pin)
        {
            Pins.Add(pin);
        }

        /// <summary>
        /// Add a graphical item to the symbol
        /// </summary>
        /// <param name="item">Graphical item to add</param>
        public void AddGraphicalItem(KiCadGraphicalItem item)
        {
            GraphicalItems.Add(item);
        }

        /// <summary>
        /// Convert the symbol to an S-expression
        /// </summary>
        /// <returns>The S-expression representing this symbol</returns>
        public SExpression ToSExpression()
        {
            var expression = new SExpression("symbol", Id);
            
            // Add options
            if (HidePinNumbers)
            {
                expression.CreateChild("pin_numbers").CreateChild("hide");
            }
            
            if (HidePinNames)
            {
                expression.CreateChild("pin_names").CreateChild("hide");
            }
            
            expression.CreateChild("in_bom", InBom ? "yes" : "no");
            expression.CreateChild("on_board", OnBoard ? "yes" : "no");
            
            // Add properties
            foreach (var property in Properties)
            {
                expression.AddChild(property.ToSExpression());
            }
            
            // Add pins
            foreach (var pin in Pins)
            {
                expression.AddChild(pin.ToSExpression());
            }
            
            // Add graphical items
            foreach (var item in GraphicalItems)
            {
                expression.AddChild(item.ToSExpression());
            }
            
            return expression;
        }
    }

    /// <summary>
    /// Represents a KiCad symbol property
    /// </summary>
    public class KiCadProperty
    {
        /// <summary>
        /// Gets or sets the property key
        /// </summary>
        public string Key { get; set; }
        
        /// <summary>
        /// Gets or sets the property value
        /// </summary>
        public string Value { get; set; }
        
        /// <summary>
        /// Gets or sets the property ID
        /// </summary>
        public int Id { get; set; }
        
        /// <summary>
        /// Gets or sets the property position
        /// </summary>
        public KiCadPosition Position { get; set; } = new KiCadPosition(0, 0);
        
        /// <summary>
        /// Gets or sets the font effects
        /// </summary>
        public KiCadFontEffects FontEffects { get; set; } = new KiCadFontEffects();

        /// <summary>
        /// Create a new KiCad property
        /// </summary>
        /// <param name="key">Property key</param>
        /// <param name="value">Property value</param>
        /// <param name="id">Property ID</param>
        public KiCadProperty(string key, string value, int id)
        {
            Key = key;
            Value = value;
            Id = id;
        }

        /// <summary>
        /// Create a KiCad property from an S-expression
        /// </summary>
        /// <param name="expression">S-expression node for the property</param>
        public KiCadProperty(SExpression expression)
        {
            Key = expression.GetValue(0) ?? "Unknown";
            Value = expression.GetValue(1) ?? "";
            
            var idExp = expression.GetChild("id");
            Id = idExp?.GetValueAsInt() ?? 0;
            
            var atExp = expression.GetChild("at");
            if (atExp != null)
            {
                Position = new KiCadPosition(
                    atExp.GetValueAsDouble(0),
                    atExp.GetValueAsDouble(1),
                    atExp.GetValueAsDouble(2));
            }
            
            var effectsExp = expression.GetChild("effects");
            if (effectsExp != null)
            {
                var fontExp = effectsExp.GetChild("font");
                if (fontExp != null)
                {
                    FontEffects = new KiCadFontEffects
                    {
                        Size = new KiCadSize(
                            fontExp.GetChild("size")?.GetValueAsDouble(0) ?? 1.27,
                            fontExp.GetChild("size")?.GetValueAsDouble(1) ?? 1.27),
                        Thickness = fontExp.GetChild("thickness")?.GetValueAsDouble() ?? 0.25,
                        Bold = fontExp.GetChild("bold") != null,
                        Italic = fontExp.GetChild("italic") != null
                    };
                }
            }
        }

        /// <summary>
        /// Convert the property to an S-expression
        /// </summary>
        /// <returns>The S-expression representing this property</returns>
        public SExpression ToSExpression()
        {
            var expression = new SExpression("property", Key, Value);
            
            // Add ID
            expression.CreateChild("id", Id.ToString());
            
            // Add position
            var posExp = expression.CreateChild("at", 
                Position.X.ToString(), 
                Position.Y.ToString());
            
            if (Position.Rotation != 0)
            {
                posExp.Values.Add(Position.Rotation.ToString());
            }
            
            // Add font effects
            var effectsExp = expression.CreateChild("effects");
            var fontExp = effectsExp.CreateChild("font");
            
            fontExp.CreateChild("size", 
                FontEffects.Size.Width.ToString(), 
                FontEffects.Size.Height.ToString());
            
            fontExp.CreateChild("thickness", FontEffects.Thickness.ToString());
            
            if (FontEffects.Bold)
            {
                fontExp.CreateChild("bold");
            }
            
            if (FontEffects.Italic)
            {
                fontExp.CreateChild("italic");
            }
            
            return expression;
        }
    }

    /// <summary>
    /// Represents a KiCad pin
    /// </summary>
    public class KiCadPin
    {
        /// <summary>
        /// Gets or sets the pin type
        /// </summary>
        public string Type { get; set; }
        
        /// <summary>
        /// Gets or sets the pin style
        /// </summary>
        public string Style { get; set; }
        
        /// <summary>
        /// Gets or sets the pin position
        /// </summary>
        public KiCadPosition Position { get; set; }
        
        /// <summary>
        /// Gets or sets the pin length
        /// </summary>
        public double Length { get; set; }
        
        /// <summary>
        /// Gets or sets the pin name position
        /// </summary>
        public string NamePosition { get; set; }
        
        /// <summary>
        /// Gets or sets the pin number position
        /// </summary>
        public string NumberPosition { get; set; }
        
        /// <summary>
        /// Gets or sets the pin name
        /// </summary>
        public string Name { get; set; }
        
        /// <summary>
        /// Gets or sets the pin number
        /// </summary>
        public string Number { get; set; }

        /// <summary>
        /// Create a new KiCad pin
        /// </summary>
        /// <param name="type">Pin type</param>
        /// <param name="style">Pin style</param>
        /// <param name="position">Pin position</param>
        /// <param name="length">Pin length</param>
        /// <param name="name">Pin name</param>
        /// <param name="number">Pin number</param>
        public KiCadPin(string type, string style, KiCadPosition position, double length,
            string name, string number)
        {
            Type = type;
            Style = style;
            Position = position;
            Length = length;
            Name = name;
            Number = number;
            NamePosition = "middle";
            NumberPosition = "middle";
        }

        /// <summary>
        /// Create a KiCad pin from an S-expression
        /// </summary>
        /// <param name="expression">S-expression node for the pin</param>
        public KiCadPin(SExpression expression)
        {
            Type = expression.GetValue(0) ?? "input";
            Style = expression.GetValue(1) ?? "line";
            
            var atExp = expression.GetChild("at");
            if (atExp != null)
            {
                Position = new KiCadPosition(
                    atExp.GetValueAsDouble(0),
                    atExp.GetValueAsDouble(1),
                    atExp.GetValueAsDouble(2));
            }
            else
            {
                Position = new KiCadPosition(0, 0);
            }
            
            var lengthExp = expression.GetChild("length");
            Length = lengthExp?.GetValueAsDouble() ?? 2.54;
            
            var nameEffectsExp = expression.GetChild("name_effects");
            if (nameEffectsExp != null)
            {
                NamePosition = nameEffectsExp.GetChild("position")?.GetValueAsString() ?? "middle";
            }
            else
            {
                NamePosition = "middle";
            }
            
            var numberEffectsExp = expression.GetChild("number_effects");
            if (numberEffectsExp != null)
            {
                NumberPosition = numberEffectsExp.GetChild("position")?.GetValueAsString() ?? "middle";
            }
            else
            {
                NumberPosition = "middle";
            }
            
            var nameExp = expression.GetChild("name");
            Name = nameExp?.GetValueAsString() ?? "Pin";
            
            var numberExp = expression.GetChild("number");
            Number = numberExp?.GetValueAsString() ?? "1";
        }

        /// <summary>
        /// Convert the pin to an S-expression
        /// </summary>
        /// <returns>The S-expression representing this pin</returns>
        public SExpression ToSExpression()
        {
            var expression = new SExpression("pin", Type, Style);
            
            // Add position
            expression.CreateChild("at", 
                Position.X.ToString(), 
                Position.Y.ToString(), 
                Position.Rotation.ToString());
            
            // Add length
            expression.CreateChild("length", Length.ToString());
            
            // Add name & effects
            var nameExp = expression.CreateChild("name", Name);
            var nameEffectsExp = nameExp.CreateChild("effects");
            nameEffectsExp.CreateChild("position", NamePosition);
            
            // Add number & effects
            var numberExp = expression.CreateChild("number", Number);
            var numberEffectsExp = numberExp.CreateChild("effects");
            numberEffectsExp.CreateChild("position", NumberPosition);
            
            return expression;
        }
    }

    /// <summary>
    /// Base class for KiCad graphical items
    /// </summary>
    public abstract class KiCadGraphicalItem
    {
        /// <summary>
        /// Convert the graphical item to an S-expression
        /// </summary>
        /// <returns>The S-expression representing this graphical item</returns>
        public abstract SExpression ToSExpression();
    }

    /// <summary>
    /// Represents a KiCad polyline (line segment or polygon)
    /// </summary>
    public class KiCadPolyline : KiCadGraphicalItem
    {
        /// <summary>
        /// Gets or sets the list of points in the polyline
        /// </summary>
        public List<KiCadPosition> Points { get; } = new List<KiCadPosition>();
        
        /// <summary>
        /// Gets or sets the stroke definition
        /// </summary>
        public KiCadStroke Stroke { get; set; } = new KiCadStroke();
        
        /// <summary>
        /// Gets or sets the fill definition
        /// </summary>
        public KiCadFill Fill { get; set; } = new KiCadFill();

        /// <summary>
        /// Create a new KiCad polyline
        /// </summary>
        public KiCadPolyline()
        {
        }

        /// <summary>
        /// Create a KiCad polyline from an S-expression
        /// </summary>
        /// <param name="expression">S-expression node for the polyline</param>
        public KiCadPolyline(SExpression expression)
        {
            var pointsExp = expression.GetChild("pts");
            if (pointsExp != null)
            {
                foreach (var xyExp in pointsExp.GetChildren("xy"))
                {
                    Points.Add(new KiCadPosition(
                        xyExp.GetValueAsDouble(0),
                        xyExp.GetValueAsDouble(1)));
                }
            }
            
            var strokeExp = expression.GetChild("stroke");
            if (strokeExp != null)
            {
                Stroke = new KiCadStroke
                {
                    Width = strokeExp.GetChild("width")?.GetValueAsDouble() ?? 0.25,
                    Type = strokeExp.GetChild("type")?.GetValueAsString() ?? "default",
                    Color = strokeExp.GetChild("color")?.Values.ToList() 
                            ?? new List<string> { "0", "0", "0", "0" }
                };
            }
            
            var fillExp = expression.GetChild("fill");
            if (fillExp != null)
            {
                Fill = new KiCadFill
                {
                    Type = fillExp.GetValueAsString() ?? "none",
                    Color = fillExp.GetChild("color")?.Values.ToList()
                            ?? new List<string> { "0", "0", "0", "0" }
                };
            }
        }

        /// <summary>
        /// Add a point to the polyline
        /// </summary>
        /// <param name="x">X coordinate</param>
        /// <param name="y">Y coordinate</param>
        public void AddPoint(double x, double y)
        {
            Points.Add(new KiCadPosition(x, y));
        }

        /// <summary>
        /// Convert the polyline to an S-expression
        /// </summary>
        /// <returns>The S-expression representing this polyline</returns>
        public override SExpression ToSExpression()
        {
            var expression = new SExpression("polyline");
            
            // Add points
            var pointsExp = expression.CreateChild("pts");
            foreach (var point in Points)
            {
                pointsExp.CreateChild("xy", point.X.ToString(), point.Y.ToString());
            }
            
            // Add stroke
            var strokeExp = expression.CreateChild("stroke");
            strokeExp.CreateChild("width", Stroke.Width.ToString());
            strokeExp.CreateChild("type", Stroke.Type);
            var strokeColorExp = strokeExp.CreateChild("color");
            foreach (var color in Stroke.Color)
            {
                strokeColorExp.Values.Add(color);
            }
            
            // Add fill
            var fillExp = expression.CreateChild("fill", Fill.Type);
            if (Fill.Type != "none")
            {
                var fillColorExp = fillExp.CreateChild("color");
                foreach (var color in Fill.Color)
                {
                    fillColorExp.Values.Add(color);
                }
            }
            
            return expression;
        }
    }

    /// <summary>
    /// Represents a KiCad rectangle
    /// </summary>
    public class KiCadRectangle : KiCadGraphicalItem
    {
        /// <summary>
        /// Gets or sets the start position (top-left corner)
        /// </summary>
        public KiCadPosition Start { get; set; } = new KiCadPosition(0, 0);
        
        /// <summary>
        /// Gets or sets the end position (bottom-right corner)
        /// </summary>
        public KiCadPosition End { get; set; } = new KiCadPosition(0, 0);
        
        /// <summary>
        /// Gets or sets the stroke definition
        /// </summary>
        public KiCadStroke Stroke { get; set; } = new KiCadStroke();
        
        /// <summary>
        /// Gets or sets the fill definition
        /// </summary>
        public KiCadFill Fill { get; set; } = new KiCadFill();

        /// <summary>
        /// Create a new KiCad rectangle
        /// </summary>
        /// <param name="startX">Start X coordinate</param>
        /// <param name="startY">Start Y coordinate</param>
        /// <param name="endX">End X coordinate</param>
        /// <param name="endY">End Y coordinate</param>
        public KiCadRectangle(double startX, double startY, double endX, double endY)
        {
            Start = new KiCadPosition(startX, startY);
            End = new KiCadPosition(endX, endY);
        }

        /// <summary>
        /// Create a KiCad rectangle from an S-expression
        /// </summary>
        /// <param name="expression">S-expression node for the rectangle</param>
        public KiCadRectangle(SExpression expression)
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
            
            var strokeExp = expression.GetChild("stroke");
            if (strokeExp != null)
            {
                Stroke = new KiCadStroke
                {
                    Width = strokeExp.GetChild("width")?.GetValueAsDouble() ?? 0.25,
                    Type = strokeExp.GetChild("type")?.GetValueAsString() ?? "default",
                    Color = strokeExp.GetChild("color")?.Values.ToList() 
                            ?? new List<string> { "0", "0", "0", "0" }
                };
            }
            
            var fillExp = expression.GetChild("fill");
            if (fillExp != null)
            {
                Fill = new KiCadFill
                {
                    Type = fillExp.GetValueAsString() ?? "none",
                    Color = fillExp.GetChild("color")?.Values.ToList()
                            ?? new List<string> { "0", "0", "0", "0" }
                };
            }
        }

        /// <summary>
        /// Convert the rectangle to an S-expression
        /// </summary>
        /// <returns>The S-expression representing this rectangle</returns>
        public override SExpression ToSExpression()
        {
            var expression = new SExpression("rectangle");
            
            // Add start and end points
            expression.CreateChild("start", Start.X.ToString(), Start.Y.ToString());
            expression.CreateChild("end", End.X.ToString(), End.Y.ToString());
            
            // Add stroke
            var strokeExp = expression.CreateChild("stroke");
            strokeExp.CreateChild("width", Stroke.Width.ToString());
            strokeExp.CreateChild("type", Stroke.Type);
            var strokeColorExp = strokeExp.CreateChild("color");
            foreach (var color in Stroke.Color)
            {
                strokeColorExp.Values.Add(color);
            }
            
            // Add fill
            var fillExp = expression.CreateChild("fill", Fill.Type);
            if (Fill.Type != "none")
            {
                var fillColorExp = fillExp.CreateChild("color");
                foreach (var color in Fill.Color)
                {
                    fillColorExp.Values.Add(color);
                }
            }
            
            return expression;
        }
    }

    /// <summary>
    /// Represents a KiCad circle
    /// </summary>
    public class KiCadCircle : KiCadGraphicalItem
    {
        /// <summary>
        /// Gets or sets the center position
        /// </summary>
        public KiCadPosition Center { get; set; } = new KiCadPosition(0, 0);
        
        /// <summary>
        /// Gets or sets the radius
        /// </summary>
        public double Radius { get; set; }
        
        /// <summary>
        /// Gets or sets the stroke definition
        /// </summary>
        public KiCadStroke Stroke { get; set; } = new KiCadStroke();
        
        /// <summary>
        /// Gets or sets the fill definition
        /// </summary>
        public KiCadFill Fill { get; set; } = new KiCadFill();

        /// <summary>
        /// Create a new KiCad circle
        /// </summary>
        /// <param name="centerX">Center X coordinate</param>
        /// <param name="centerY">Center Y coordinate</param>
        /// <param name="radius">Radius</param>
        public KiCadCircle(double centerX, double centerY, double radius)
        {
            Center = new KiCadPosition(centerX, centerY);
            Radius = radius;
        }

        /// <summary>
        /// Create a KiCad circle from an S-expression
        /// </summary>
        /// <param name="expression">S-expression node for the circle</param>
        public KiCadCircle(SExpression expression)
        {
            var centerExp = expression.GetChild("center");
            if (centerExp != null)
            {
                Center = new KiCadPosition(
                    centerExp.GetValueAsDouble(0),
                    centerExp.GetValueAsDouble(1));
            }
            
            var radiusExp = expression.GetChild("radius");
            Radius = radiusExp?.GetValueAsDouble() ?? 0;
            
            var strokeExp = expression.GetChild("stroke");
            if (strokeExp != null)
            {
                Stroke = new KiCadStroke
                {
                    Width = strokeExp.GetChild("width")?.GetValueAsDouble() ?? 0.25,
                    Type = strokeExp.GetChild("type")?.GetValueAsString() ?? "default",
                    Color = strokeExp.GetChild("color")?.Values.ToList() 
                            ?? new List<string> { "0", "0", "0", "0" }
                };
            }
            
            var fillExp = expression.GetChild("fill");
            if (fillExp != null)
            {
                Fill = new KiCadFill
                {
                    Type = fillExp.GetValueAsString() ?? "none",
                    Color = fillExp.GetChild("color")?.Values.ToList()
                            ?? new List<string> { "0", "0", "0", "0" }
                };
            }
        }

        /// <summary>
        /// Convert the circle to an S-expression
        /// </summary>
        /// <returns>The S-expression representing this circle</returns>
        public override SExpression ToSExpression()
        {
            var expression = new SExpression("circle");
            
            // Add center and radius
            expression.CreateChild("center", Center.X.ToString(), Center.Y.ToString());
            expression.CreateChild("radius", Radius.ToString());
            
            // Add stroke
            var strokeExp = expression.CreateChild("stroke");
            strokeExp.CreateChild("width", Stroke.Width.ToString());
            strokeExp.CreateChild("type", Stroke.Type);
            var strokeColorExp = strokeExp.CreateChild("color");
            foreach (var color in Stroke.Color)
            {
                strokeColorExp.Values.Add(color);
            }
            
            // Add fill
            var fillExp = expression.CreateChild("fill", Fill.Type);
            if (Fill.Type != "none")
            {
                var fillColorExp = fillExp.CreateChild("color");
                foreach (var color in Fill.Color)
                {
                    fillColorExp.Values.Add(color);
                }
            }
            
            return expression;
        }
    }

    /// <summary>
    /// Represents a KiCad arc
    /// </summary>
    public class KiCadArc : KiCadGraphicalItem
    {
        /// <summary>
        /// Gets or sets the start position
        /// </summary>
        public KiCadPosition Start { get; set; } = new KiCadPosition(0, 0);
        
        /// <summary>
        /// Gets or sets the mid position
        /// </summary>
        public KiCadPosition Mid { get; set; } = new KiCadPosition(0, 0);
        
        /// <summary>
        /// Gets or sets the end position
        /// </summary>
        public KiCadPosition End { get; set; } = new KiCadPosition(0, 0);
        
        /// <summary>
        /// Gets or sets the stroke definition
        /// </summary>
        public KiCadStroke Stroke { get; set; } = new KiCadStroke();
        
        /// <summary>
        /// Gets or sets the fill definition
        /// </summary>
        public KiCadFill Fill { get; set; } = new KiCadFill();

        /// <summary>
        /// Create a new KiCad arc
        /// </summary>
        /// <param name="startX">Start X coordinate</param>
        /// <param name="startY">Start Y coordinate</param>
        /// <param name="midX">Mid X coordinate</param>
        /// <param name="midY">Mid Y coordinate</param>
        /// <param name="endX">End X coordinate</param>
        /// <param name="endY">End Y coordinate</param>
        public KiCadArc(double startX, double startY, double midX, double midY, double endX, double endY)
        {
            Start = new KiCadPosition(startX, startY);
            Mid = new KiCadPosition(midX, midY);
            End = new KiCadPosition(endX, endY);
        }

        /// <summary>
        /// Create a KiCad arc from an S-expression
        /// </summary>
        /// <param name="expression">S-expression node for the arc</param>
        public KiCadArc(SExpression expression)
        {
            var startExp = expression.GetChild("start");
            if (startExp != null)
            {
                Start = new KiCadPosition(
                    startExp.GetValueAsDouble(0),
                    startExp.GetValueAsDouble(1));
            }
            
            var midExp = expression.GetChild("mid");
            if (midExp != null)
            {
                Mid = new KiCadPosition(
                    midExp.GetValueAsDouble(0),
                    midExp.GetValueAsDouble(1));
            }
            
            var endExp = expression.GetChild("end");
            if (endExp != null)
            {
                End = new KiCadPosition(
                    endExp.GetValueAsDouble(0),
                    endExp.GetValueAsDouble(1));
            }
            
            var strokeExp = expression.GetChild("stroke");
            if (strokeExp != null)
            {
                Stroke = new KiCadStroke
                {
                    Width = strokeExp.GetChild("width")?.GetValueAsDouble() ?? 0.25,
                    Type = strokeExp.GetChild("type")?.GetValueAsString() ?? "default",
                    Color = strokeExp.GetChild("color")?.Values.ToList() 
                            ?? new List<string> { "0", "0", "0", "0" }
                };
            }
            
            var fillExp = expression.GetChild("fill");
            if (fillExp != null)
            {
                Fill = new KiCadFill
                {
                    Type = fillExp.GetValueAsString() ?? "none",
                    Color = fillExp.GetChild("color")?.Values.ToList()
                            ?? new List<string> { "0", "0", "0", "0" }
                };
            }
        }

        /// <summary>
        /// Convert the arc to an S-expression
        /// </summary>
        /// <returns>The S-expression representing this arc</returns>
        public override SExpression ToSExpression()
        {
            var expression = new SExpression("arc");
            
            // Add start, mid, and end points
            expression.CreateChild("start", Start.X.ToString(), Start.Y.ToString());
            expression.CreateChild("mid", Mid.X.ToString(), Mid.Y.ToString());
            expression.CreateChild("end", End.X.ToString(), End.Y.ToString());
            
            // Add stroke
            var strokeExp = expression.CreateChild("stroke");
            strokeExp.CreateChild("width", Stroke.Width.ToString());
            strokeExp.CreateChild("type", Stroke.Type);
            var strokeColorExp = strokeExp.CreateChild("color");
            foreach (var color in Stroke.Color)
            {
                strokeColorExp.Values.Add(color);
            }
            
            // Add fill
            var fillExp = expression.CreateChild("fill", Fill.Type);
            if (Fill.Type != "none")
            {
                var fillColorExp = fillExp.CreateChild("color");
                foreach (var color in Fill.Color)
                {
                    fillColorExp.Values.Add(color);
                }
            }
            
            return expression;
        }
    }

    /// <summary>
    /// Represents KiCad text
    /// </summary>
    public class KiCadText : KiCadGraphicalItem
    {
        /// <summary>
        /// Gets or sets the text content
        /// </summary>
        public string Text { get; set; }
        
        /// <summary>
        /// Gets or sets the position
        /// </summary>
        public KiCadPosition Position { get; set; } = new KiCadPosition(0, 0);
        
        /// <summary>
        /// Gets or sets the font effects
        /// </summary>
        public KiCadFontEffects FontEffects { get; set; } = new KiCadFontEffects();

        /// <summary>
        /// Create a new KiCad text
        /// </summary>
        /// <param name="text">Text content</param>
        /// <param name="x">X coordinate</param>
        /// <param name="y">Y coordinate</param>
        /// <param name="rotation">Rotation angle</param>
        public KiCadText(string text, double x, double y, double rotation = 0)
        {
            Text = text;
            Position = new KiCadPosition(x, y, rotation);
        }

        /// <summary>
        /// Create KiCad text from an S-expression
        /// </summary>
        /// <param name="expression">S-expression node for the text</param>
        public KiCadText(SExpression expression)
        {
            Text = expression.GetValue(0) ?? "";
            
            var atExp = expression.GetChild("at");
            if (atExp != null)
            {
                Position = new KiCadPosition(
                    atExp.GetValueAsDouble(0),
                    atExp.GetValueAsDouble(1),
                    atExp.GetValueAsDouble(2));
            }
            
            var effectsExp = expression.GetChild("effects");
            if (effectsExp != null)
            {
                var fontExp = effectsExp.GetChild("font");
                if (fontExp != null)
                {
                    FontEffects = new KiCadFontEffects
                    {
                        Size = new KiCadSize(
                            fontExp.GetChild("size")?.GetValueAsDouble(0) ?? 1.27,
                            fontExp.GetChild("size")?.GetValueAsDouble(1) ?? 1.27),
                        Thickness = fontExp.GetChild("thickness")?.GetValueAsDouble() ?? 0.25,
                        Bold = fontExp.GetChild("bold") != null,
                        Italic = fontExp.GetChild("italic") != null
                    };
                }
            }
        }

        /// <summary>
        /// Convert the text to an S-expression
        /// </summary>
        /// <returns>The S-expression representing this text</returns>
        public override SExpression ToSExpression()
        {
            var expression = new SExpression("text", Text);
            
            // Add position
            expression.CreateChild("at", 
                Position.X.ToString(), 
                Position.Y.ToString(), 
                Position.Rotation.ToString());
            
            // Add font effects
            var effectsExp = expression.CreateChild("effects");
            var fontExp = effectsExp.CreateChild("font");
            
            fontExp.CreateChild("size", 
                FontEffects.Size.Width.ToString(), 
                FontEffects.Size.Height.ToString());
            
            fontExp.CreateChild("thickness", FontEffects.Thickness.ToString());
            
            if (FontEffects.Bold)
            {
                fontExp.CreateChild("bold");
            }
            
            if (FontEffects.Italic)
            {
                fontExp.CreateChild("italic");
            }
            
            return expression;
        }
    }

    #region Helper Structures

    /// <summary>
    /// Represents a position in KiCad coordinates
    /// </summary>
    public class KiCadPosition
    {
        /// <summary>
        /// Gets or sets the X coordinate
        /// </summary>
        public double X { get; set; }
        
        /// <summary>
        /// Gets or sets the Y coordinate
        /// </summary>
        public double Y { get; set; }
        
        /// <summary>
        /// Gets or sets the rotation angle
        /// </summary>
        public double Rotation { get; set; }

        /// <summary>
        /// Create a new KiCad position
        /// </summary>
        /// <param name="x">X coordinate</param>
        /// <param name="y">Y coordinate</param>
        /// <param name="rotation">Rotation angle</param>
        public KiCadPosition(double x, double y, double rotation = 0)
        {
            X = x;
            Y = y;
            Rotation = rotation;
        }
    }

    /// <summary>
    /// Represents a size in KiCad
    /// </summary>
    public class KiCadSize
    {
        /// <summary>
        /// Gets or sets the width
        /// </summary>
        public double Width { get; set; }
        
        /// <summary>
        /// Gets or sets the height
        /// </summary>
        public double Height { get; set; }

        /// <summary>
        /// Create a new KiCad size
        /// </summary>
        /// <param name="width">Width</param>
        /// <param name="height">Height</param>
        public KiCadSize(double width, double height)
        {
            Width = width;
            Height = height;
        }
    }

    /// <summary>
    /// Represents font effects in KiCad
    /// </summary>
    public class KiCadFontEffects
    {
        /// <summary>
        /// Gets or sets the font size
        /// </summary>
        public KiCadSize Size { get; set; } = new KiCadSize(1.27, 1.27);
        
        /// <summary>
        /// Gets or sets the font thickness
        /// </summary>
        public double Thickness { get; set; } = 0.25;
        
        /// <summary>
        /// Gets or sets whether the font is bold
        /// </summary>
        public bool Bold { get; set; }
        
        /// <summary>
        /// Gets or sets whether the font is italic
        /// </summary>
        public bool Italic { get; set; }
    }

    /// <summary>
    /// Represents a stroke in KiCad
    /// </summary>
    public class KiCadStroke
    {
        /// <summary>
        /// Gets or sets the stroke width
        /// </summary>
        public double Width { get; set; } = 0.25;
        
        /// <summary>
        /// Gets or sets the stroke type
        /// </summary>
        public string Type { get; set; } = "default";
        
        /// <summary>
        /// Gets or sets the stroke color (in RGBA format)
        /// </summary>
        public List<string> Color { get; set; } = new List<string> { "0", "0", "0", "0" };
    }

    /// <summary>
    /// Represents a fill in KiCad
    /// </summary>
    public class KiCadFill
    {
        /// <summary>
        /// Gets or sets the fill type
        /// </summary>
        public string Type { get; set; } = "none";
        
        /// <summary>
        /// Gets or sets the fill color (in RGBA format)
        /// </summary>
        public List<string> Color { get; set; } = new List<string> { "0", "0", "0", "0" };
    }

    #endregion
}