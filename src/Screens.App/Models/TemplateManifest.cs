using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Screens.App.Models;

public sealed class TemplateManifest
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("category")] public string Category { get; set; } = "";
    [JsonPropertyName("image")] public string Image { get; set; } = "";
    [JsonPropertyName("canvas")] public CanvasSize Canvas { get; set; } = new();
    [JsonPropertyName("fields")] public List<TemplateField> Fields { get; set; } = new();

    [JsonIgnore] public string? SourceDirectory { get; set; }
}

public sealed class CanvasSize
{
    [JsonPropertyName("width")] public int Width { get; set; }
    [JsonPropertyName("height")] public int Height { get; set; }
}

public sealed class FieldBox
{
    [JsonPropertyName("x")] public double X { get; set; }
    [JsonPropertyName("y")] public double Y { get; set; }
    [JsonPropertyName("width")] public double Width { get; set; }
    [JsonPropertyName("height")] public double Height { get; set; }
}

public sealed class FieldFont
{
    [JsonPropertyName("family")] public string Family { get; set; } = "Inter";
    [JsonPropertyName("size")] public double Size { get; set; } = 32;
    [JsonPropertyName("weight")] public string Weight { get; set; } = "Regular";
    [JsonPropertyName("italic")] public bool Italic { get; set; }
}

public sealed class TemplateField
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("label")] public string Label { get; set; } = "";
    [JsonPropertyName("default")] public string Default { get; set; } = "";
    [JsonPropertyName("placeholder")] public string Placeholder { get; set; } = "";
    [JsonPropertyName("maxLength")] public int? MaxLength { get; set; }
    [JsonPropertyName("multiline")] public bool Multiline { get; set; }
    [JsonPropertyName("box")] public FieldBox Box { get; set; } = new();
    [JsonPropertyName("align")] public string Align { get; set; } = "left";
    [JsonPropertyName("verticalAlign")] public string VerticalAlign { get; set; } = "top";
    [JsonPropertyName("font")] public FieldFont Font { get; set; } = new();
    [JsonPropertyName("color")] public string Color { get; set; } = "#1A1028";
    [JsonPropertyName("lineHeight")] public double LineHeight { get; set; } = 1.2;
    [JsonPropertyName("letterSpacing")] public double LetterSpacing { get; set; }
    [JsonPropertyName("uppercase")] public bool Uppercase { get; set; }
    [JsonPropertyName("autoShrink")] public bool AutoShrink { get; set; } = true;
    [JsonPropertyName("userEditableColor")] public bool UserEditableColor { get; set; }
    [JsonPropertyName("userEditableSize")] public bool UserEditableSize { get; set; }
}
