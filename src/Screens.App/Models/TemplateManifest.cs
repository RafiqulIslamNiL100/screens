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

    /// <summary>True for templates saved under %APPDATA%\Screens\Templates (e.g. duplicates the
    /// user made) — these show a Delete affordance in the gallery; bundled templates never do.</summary>
    [JsonIgnore] public bool IsCustom { get; set; }

    /// <summary>True for templates downloaded into %APPDATA%\Screens\PremiumTemplates — admin-shipped
    /// templates unlocked by a premium_templates access key. TemplateService force-clears every
    /// field's UserEditableColor/UserEditableSize when loading from that directory, regardless of
    /// what the manifest itself says, so these can never be restyled by the end user — the whole
    /// point of the feature is that the admin's font/size choices are what ships.</summary>
    [JsonIgnore] public bool IsPremium { get; set; }
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
    /// <summary>"text" (default) or "image" — a user-chosen photo drawn to fill the box instead of text.</summary>
    [JsonPropertyName("type")] public string Type { get; set; } = "text";
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
    /// <summary>Unlike UserEditableColor/Size (which default to locked, and only the handful of
    /// fields that explicitly opt in show a picker), this defaults to *unlocked* — every existing
    /// template's manifest JSON predates this property, so a missing key here means "on," letting
    /// the font-family/bold picker light up across already-shipped templates without editing a
    /// single one of them. Premium Templates still force this false at load time exactly like the
    /// other two (see TemplateService.LoadFrom), so an admin's font choice still ships locked
    /// there — this default only affects everything else.</summary>
    [JsonPropertyName("userEditableFont")] public bool UserEditableFont { get; set; } = true;
    [JsonPropertyName("opacity")] public double Opacity { get; set; } = 1.0;
    [JsonPropertyName("shadow")] public bool Shadow { get; set; }

    /// <summary>Runtime-only overrides (drag-to-reposition, per-field color/size/font pickers).
    /// Never written back to the manifest file — these reset when the app restarts or the
    /// template reloads.</summary>
    [JsonIgnore] public string? RuntimeColorOverride { get; set; }
    [JsonIgnore] public double? RuntimeSizeOverride { get; set; }
    [JsonIgnore] public string? RuntimeFamilyOverride { get; set; }
    [JsonIgnore] public string? RuntimeWeightOverride { get; set; }
}
