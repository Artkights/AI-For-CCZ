using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Text;
using CCZModStudio.Models;

namespace CCZModStudio.Core;

public sealed class RSceneDialoguePreviewService
{
    private static readonly DialoguePreviewProfile DefaultProfile = new(
        BoxBounds: new Rectangle(164, 312, 330, 84),
        FaceBounds: new Rectangle(508, 286, 120, 112),
        NameBounds: new Rectangle(176, 320, 300, 20),
        TextBounds: new Rectangle(176, 342, 306, 42),
        NoFaceTextBounds: new Rectangle(176, 328, 306, 50),
        BorderColor: Color.FromArgb(255, 218, 202, 168),
        InnerBorderColor: Color.FromArgb(255, 248, 246, 238),
        FillColor: Color.FromArgb(246, 248, 246, 238),
        HighlightColor: Color.FromArgb(255, 28, 64, 232),
        TextColor: Color.FromArgb(255, 8, 8, 8),
        ShadowColor: Color.FromArgb(0, 0, 0, 0),
        FontFamily: "SimSun",
        NameFontSize: 14.5f,
        TextFontSize: 14.5f,
        MaxTextLines: 2,
        TextLineHeight: 24,
        MapTextBounds: new Rectangle(132, 36, 376, 42));

    private readonly ImageAssignmentPreviewService _imageAssignmentPreviewService;

    public RSceneDialoguePreviewService()
        : this(new ImageAssignmentPreviewService())
    {
    }

    public RSceneDialoguePreviewService(ImageAssignmentPreviewService imageAssignmentPreviewService)
    {
        _imageAssignmentPreviewService = imageAssignmentPreviewService;
    }

    public RSceneDialoguePreviewResult DrawPreview(Graphics graphics, CczProject project, LegacyScenarioCommandNode? command, IReadOnlyDictionary<int, RSceneDialoguePreviewPerson> people)
    {
        if (command == null)
        {
            return RSceneDialoguePreviewResult.CreateNotApplied("未选择 R 剧本命令。");
        }

        var model = BuildPreviewModel(command, people);
        if (model == null)
        {
            return RSceneDialoguePreviewResult.CreateNotApplied($"命令 {command.CommandIdHex} 不是对白预览命令。");
        }

        graphics.SmoothingMode = SmoothingMode.None;
        graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
        graphics.PixelOffsetMode = PixelOffsetMode.Half;

        if (model.Kind == RSceneDialoguePreviewKind.MapText)
        {
            DrawMapText(graphics, model);
            return RSceneDialoguePreviewResult.CreateApplied(model.Detail);
        }

        DrawDialogueBox(graphics, project, model);
        return RSceneDialoguePreviewResult.CreateApplied(model.Detail);
    }

    public RSceneDialoguePreviewResult RenderPreviewOnImage(Bitmap image, CczProject project, LegacyScenarioCommandNode? command, IReadOnlyDictionary<int, RSceneDialoguePreviewPerson> people)
    {
        using var graphics = Graphics.FromImage(image);
        return DrawPreview(graphics, project, command, people);
    }

    public RSceneDialoguePreviewModel? BuildPreviewModel(LegacyScenarioCommandNode command, IReadOnlyDictionary<int, RSceneDialoguePreviewPerson> people)
    {
        var text = NormalizeText(command.TextParameters.FirstOrDefault()?.Text ?? string.Empty);
        if (string.IsNullOrWhiteSpace(text) && command.CommandId != 0x2C)
        {
            return null;
        }

        var firstValue = GetParameterValue(command, 0);
        return command.CommandId switch
        {
            0x14 or 0x15 or 0x7A => BuildTalkModel(command, text, firstValue, people),
            0x16 => new RSceneDialoguePreviewModel(
                RSceneDialoguePreviewKind.Information,
                SpeakerId: null,
                FaceId: null,
                SpeakerName: "信息",
                Text: text,
                HasFace: false,
                Detail: $"{command.CommandIdHex} {command.CommandName}：无头像信息框"),
            0x69 => new RSceneDialoguePreviewModel(
                RSceneDialoguePreviewKind.Narration,
                SpeakerId: null,
                FaceId: null,
                SpeakerName: "旁白",
                Text: text,
                HasFace: false,
                Detail: $"{command.CommandIdHex} {command.CommandName}：无头像旁白"),
            0x2C => new RSceneDialoguePreviewModel(
                RSceneDialoguePreviewKind.MapText,
                SpeakerId: null,
                FaceId: null,
                SpeakerName: string.Empty,
                Text: text,
                HasFace: false,
                Detail: $"{command.CommandIdHex} {command.CommandName}：地图文字提示"),
            _ => null
        };
    }

    public static bool IsPreviewCommand(int commandId)
        => commandId is 0x14 or 0x15 or 0x16 or 0x69 or 0x7A or 0x2C;

    private static RSceneDialoguePreviewModel BuildTalkModel(
        LegacyScenarioCommandNode command,
        string text,
        int? speakerId,
        IReadOnlyDictionary<int, RSceneDialoguePreviewPerson> people)
    {
        RSceneDialoguePreviewPerson? person = null;
        if (speakerId.HasValue)
        {
            people.TryGetValue(speakerId.Value, out person);
        }

        var speakerName = !string.IsNullOrWhiteSpace(person?.Name)
            ? person.Name
            : speakerId.HasValue
                ? $"人物 {speakerId.Value}"
                : "说话人";
        var faceId = person?.FaceId;
        var speakerDetail = speakerId.HasValue
            ? $"人物={speakerId.Value}"
            : "人物=未读取";
        var faceDetail = faceId.HasValue
            ? $"头像={faceId.Value}"
            : "头像=未读取";
        return new RSceneDialoguePreviewModel(
            RSceneDialoguePreviewKind.Talk,
            SpeakerId: speakerId,
            FaceId: faceId,
            SpeakerName: speakerName,
            Text: text,
            HasFace: speakerId.HasValue && speakerId.Value >= 0,
            Detail: $"{command.CommandIdHex} {command.CommandName}：{speakerName}（{speakerDetail}，{faceDetail}）");
    }

    private void DrawDialogueBox(Graphics graphics, CczProject project, RSceneDialoguePreviewModel model)
    {
        var profile = DefaultProfile;
        using var fill = new SolidBrush(profile.FillColor);
        using var shadow = new SolidBrush(Color.FromArgb(70, Color.Black));
        using var border = new Pen(profile.BorderColor, 1);
        var box = profile.BoxBounds;
        FillRoundedRectangle(graphics, shadow, new Rectangle(box.Left + 3, box.Top + 3, box.Width, box.Height), 6);
        FillRoundedRectangle(graphics, fill, box, 6);
        DrawRoundedRectangle(graphics, border, box, 6);

        if (model.HasFace)
        {
            DrawFace(graphics, project, model.FaceId, profile.FaceBounds);
            DrawTextWithShadow(graphics, model.SpeakerName, profile.NameBounds, profile.NameFont, profile.HighlightColor, ContentAlignment.MiddleLeft, trim: true);
            DrawBodyText(graphics, model.Text, profile.TextBounds, profile);
            return;
        }

        DrawBodyText(graphics, model.Text, profile.NoFaceTextBounds, profile);
    }

    private void DrawFace(Graphics graphics, CczProject project, int? dataFaceId, Rectangle bounds)
    {
        using var faceBackground = new SolidBrush(Color.FromArgb(255, 12, 12, 14));
        using var faceBorder = new Pen(Color.FromArgb(255, 188, 166, 0), 2);
        graphics.FillRectangle(faceBackground, bounds);
        graphics.DrawRectangle(faceBorder, bounds);

        if (!dataFaceId.HasValue || dataFaceId.Value < 0)
        {
            DrawTextWithShadow(graphics, "头像\n未定", bounds, DefaultProfile.TextFont, Color.White, ContentAlignment.MiddleCenter);
            return;
        }

        using var face = _imageAssignmentPreviewService.TryRenderFaceImage(project, dataFaceId.Value);
        if (face == null)
        {
            DrawTextWithShadow(graphics, "头像\n缺失", bounds, DefaultProfile.TextFont, Color.White, ContentAlignment.MiddleCenter);
            return;
        }

        graphics.DrawImage(face, bounds);
        graphics.DrawRectangle(faceBorder, bounds);
    }

    private static void DrawBodyText(Graphics graphics, string text, Rectangle bounds, DialoguePreviewProfile profile)
    {
        var lines = WrapText(graphics, text, profile.TextFont, bounds.Width, profile.MaxTextLines);
        for (var i = 0; i < lines.Count; i++)
        {
            var lineRect = new Rectangle(bounds.Left, bounds.Top + i * profile.TextLineHeight, bounds.Width, profile.TextLineHeight);
            DrawTextWithShadow(graphics, lines[i], lineRect, profile.TextFont, profile.TextColor, ContentAlignment.MiddleLeft);
        }
    }

    private static void DrawMapText(Graphics graphics, RSceneDialoguePreviewModel model)
    {
        var profile = DefaultProfile;
        using var fill = new SolidBrush(Color.FromArgb(214, 0, 0, 0));
        using var border = new Pen(profile.BorderColor, 1);
        var box = profile.MapTextBounds;
        graphics.FillRectangle(fill, box);
        graphics.DrawRectangle(border, box);
        DrawTextWithShadow(graphics, model.Text, Rectangle.Inflate(box, -10, -4), profile.TextFont, profile.TextColor, ContentAlignment.MiddleCenter, trim: true);
    }

    private static void DrawTextWithShadow(Graphics graphics, string text, Rectangle bounds, Font font, Color color, ContentAlignment alignment, bool trim = false)
    {
        if (trim) text = TrimToFit(graphics, text, font, bounds.Width);
        using var textBrush = new SolidBrush(color);
        using var format = BuildStringFormat(alignment);
        if (DefaultProfile.ShadowColor.A > 0)
        {
            using var shadowBrush = new SolidBrush(DefaultProfile.ShadowColor);
            graphics.DrawString(text, font, shadowBrush, new Rectangle(bounds.Left + 1, bounds.Top + 1, bounds.Width, bounds.Height), format);
        }

        graphics.DrawString(text, font, textBrush, bounds, format);
    }

    private static void FillRoundedRectangle(Graphics graphics, Brush brush, Rectangle bounds, int radius)
    {
        using var path = BuildRoundedRectanglePath(bounds, radius);
        graphics.FillPath(brush, path);
    }

    private static void DrawRoundedRectangle(Graphics graphics, Pen pen, Rectangle bounds, int radius)
    {
        using var path = BuildRoundedRectanglePath(bounds, radius);
        graphics.DrawPath(pen, path);
    }

    private static GraphicsPath BuildRoundedRectanglePath(Rectangle bounds, int radius)
    {
        var path = new GraphicsPath();
        var diameter = Math.Max(1, radius * 2);
        var arc = new Rectangle(bounds.Left, bounds.Top, diameter, diameter);
        path.AddArc(arc, 180, 90);
        arc.X = bounds.Right - diameter;
        path.AddArc(arc, 270, 90);
        arc.Y = bounds.Bottom - diameter;
        path.AddArc(arc, 0, 90);
        arc.X = bounds.Left;
        path.AddArc(arc, 90, 90);
        path.CloseFigure();
        return path;
    }

    private static List<string> WrapText(Graphics graphics, string text, Font font, int maxWidth, int maxLines)
    {
        var result = new List<string>();
        foreach (var paragraph in text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            AppendWrappedParagraph(graphics, paragraph, font, maxWidth, maxLines, result);
            if (result.Count >= maxLines) break;
        }

        return result.Count == 0 ? [string.Empty] : result;
    }

    private static void AppendWrappedParagraph(Graphics graphics, string paragraph, Font font, int maxWidth, int maxLines, List<string> result)
    {
        var current = string.Empty;
        foreach (var rune in paragraph.EnumerateRunes())
        {
            var next = current + rune.ToString();
            if (current.Length > 0 && MeasureTextWidth(graphics, next, font) > maxWidth)
            {
                result.Add(current);
                if (result.Count >= maxLines) return;
                current = rune.ToString();
            }
            else
            {
                current = next;
            }
        }

        if (result.Count < maxLines && current.Length > 0)
        {
            result.Add(current);
        }
    }

    private static string NormalizeText(string text)
        => string.IsNullOrEmpty(text) ? string.Empty : text.Replace("\r\n", "\n").Replace('\r', '\n').Trim();

    private static int MeasureTextWidth(Graphics graphics, string text, Font font)
        => (int)Math.Ceiling(graphics.MeasureString(text, font, int.MaxValue, StringFormat.GenericTypographic).Width);

    private static string TrimToFit(Graphics graphics, string text, Font font, int maxWidth)
    {
        if (MeasureTextWidth(graphics, text, font) <= maxWidth) return text;
        const string ellipsis = "...";
        var builder = new StringBuilder();
        foreach (var rune in text.EnumerateRunes())
        {
            var next = builder + rune.ToString();
            if (MeasureTextWidth(graphics, next + ellipsis, font) > maxWidth)
            {
                break;
            }

            builder.Append(rune.ToString());
        }

        return builder.Length == 0 ? ellipsis : builder + ellipsis;
    }

    private static StringFormat BuildStringFormat(ContentAlignment alignment)
    {
        var format = (StringFormat)StringFormat.GenericTypographic.Clone();
        format.FormatFlags &= ~StringFormatFlags.LineLimit;
        format.Trimming = StringTrimming.None;
        format.Alignment = alignment switch
        {
            ContentAlignment.TopCenter or ContentAlignment.MiddleCenter or ContentAlignment.BottomCenter => StringAlignment.Center,
            ContentAlignment.TopRight or ContentAlignment.MiddleRight or ContentAlignment.BottomRight => StringAlignment.Far,
            _ => StringAlignment.Near
        };
        format.LineAlignment = alignment switch
        {
            ContentAlignment.MiddleLeft or ContentAlignment.MiddleCenter or ContentAlignment.MiddleRight => StringAlignment.Center,
            ContentAlignment.BottomLeft or ContentAlignment.BottomCenter or ContentAlignment.BottomRight => StringAlignment.Far,
            _ => StringAlignment.Near
        };
        return format;
    }

    private static int? GetParameterValue(LegacyScenarioCommandNode command, int index)
    {
        var parameter = command.Parameters.FirstOrDefault(parameter => parameter.Index == index && parameter.Kind is LegacyScenarioParameterKind.Word16 or LegacyScenarioParameterKind.Dword32);
        return parameter?.IntValue;
    }

    private sealed record DialoguePreviewProfile(
        Rectangle BoxBounds,
        Rectangle FaceBounds,
        Rectangle NameBounds,
        Rectangle TextBounds,
        Rectangle NoFaceTextBounds,
        Color BorderColor,
        Color InnerBorderColor,
        Color FillColor,
        Color HighlightColor,
        Color TextColor,
        Color ShadowColor,
        string FontFamily,
        float NameFontSize,
        float TextFontSize,
        int MaxTextLines,
        int TextLineHeight,
        Rectangle MapTextBounds)
    {
        public Font NameFont { get; } = CreateFont(FontFamily, NameFontSize, FontStyle.Bold);
        public Font TextFont { get; } = CreateFont(FontFamily, TextFontSize, FontStyle.Regular);

        private static Font CreateFont(string familyName, float size, FontStyle style)
        {
            try
            {
                return new Font(familyName, size, style, GraphicsUnit.Point);
            }
            catch
            {
                return new Font(global::System.Drawing.FontFamily.GenericSansSerif, size, style, GraphicsUnit.Point);
            }
        }
    }
}

public sealed record RSceneDialoguePreviewResult(bool Applied, string Message)
{
    public static RSceneDialoguePreviewResult CreateApplied(string message) => new(true, message);
    public static RSceneDialoguePreviewResult CreateNotApplied(string message) => new(false, message);
}

public sealed record RSceneDialoguePreviewModel(
    RSceneDialoguePreviewKind Kind,
    int? SpeakerId,
    int? FaceId,
    string SpeakerName,
    string Text,
    bool HasFace,
    string Detail);

public sealed record RSceneDialoguePreviewPerson(string Name, int? FaceId);

public enum RSceneDialoguePreviewKind
{
    Talk,
    Information,
    Narration,
    MapText
}
