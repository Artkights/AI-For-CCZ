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
        TextBounds: new Rectangle(176, 342, 306, 50),
        NoFaceTextBounds: new Rectangle(176, 328, 306, 50),
        BorderColor: Color.FromArgb(255, 218, 202, 168),
        InnerBorderColor: Color.FromArgb(255, 248, 246, 238),
        FillColor: Color.FromArgb(246, 248, 246, 238),
        HighlightColor: Color.FromArgb(255, 28, 64, 232),
        TextColor: Color.FromArgb(255, 8, 8, 8),
        ShadowColor: Color.FromArgb(0, 0, 0, 0),
        FontFamily: "SimSun",
        NameFontSize: 17f,
        TextFontSize: 15f,
        MaxTextLines: 3,
        TextLineHeight: 16);

    private static readonly MapTextPreviewProfile DefaultMapTextProfile = new(
        PanelBounds: new Rectangle(56, 338, 560, 62),
        TextBounds: new Rectangle(60, 346, 552, 46),
        PanelColor: Color.FromArgb(150, 0, 0, 0),
        TextColor: Color.White,
        ShadowColor: Color.FromArgb(180, 0, 0, 0),
        FontFamily: "SimSun",
        TextFontSize: 15f,
        MaxTextLines: 3,
        TextLineHeight: 18);

    private readonly ImageAssignmentPreviewService _imageAssignmentPreviewService;

    public RSceneDialoguePreviewService()
        : this(new ImageAssignmentPreviewService())
    {
    }

    public RSceneDialoguePreviewService(ImageAssignmentPreviewService imageAssignmentPreviewService)
    {
        _imageAssignmentPreviewService = imageAssignmentPreviewService;
    }

    public RSceneDialoguePreviewResult DrawPreview(
        Graphics graphics,
        CczProject project,
        LegacyScenarioCommandNode? command,
        IReadOnlyDictionary<int, RSceneDialoguePreviewPerson> people,
        Func<LegacyScenarioCommandNode, int, int?>? personReferenceResolver = null,
        LegacyTextWrapOptions? textWrapOptions = null)
    {
        if (command == null)
        {
            return RSceneDialoguePreviewResult.CreateNotApplied("未选择 R 剧本命令。");
        }

        var model = BuildPreviewModel(command, people, personReferenceResolver, textWrapOptions);
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

    public RSceneDialoguePreviewResult RenderPreviewOnImage(
        Bitmap image,
        CczProject project,
        LegacyScenarioCommandNode? command,
        IReadOnlyDictionary<int, RSceneDialoguePreviewPerson> people,
        Func<LegacyScenarioCommandNode, int, int?>? personReferenceResolver = null,
        LegacyTextWrapOptions? textWrapOptions = null)
    {
        using var graphics = Graphics.FromImage(image);
        return DrawPreview(graphics, project, command, people, personReferenceResolver, textWrapOptions);
    }

    public RSceneDialoguePreviewModel? BuildPreviewModel(
        LegacyScenarioCommandNode command,
        IReadOnlyDictionary<int, RSceneDialoguePreviewPerson> people,
        Func<LegacyScenarioCommandNode, int, int?>? personReferenceResolver = null,
        LegacyTextWrapOptions? textWrapOptions = null)
    {
        var text = NormalizeText(command.TextParameters.FirstOrDefault()?.Text ?? string.Empty);
        var wrapResult = LegacyTextWrapService.Wrap(
            text,
            textWrapOptions ?? LegacyTextWrapService.CreateOptions(
                command.CommandId,
                LegacyTextWrapService.GetDefaultLineLimit(command.CommandId)));
        text = NormalizeText(wrapResult.Text);
        var lineLimit = textWrapOptions?.LineLimit ?? LegacyTextWrapService.GetDefaultLineLimit(command.CommandId);
        var previewLineCount = GetRenderLines(text, DefaultProfile.MaxTextLines).Count;
        var wrapSummary = $"；每行上限={lineLimit}；正文预览 {previewLineCount} 行";
        var warningSuffix = wrapResult.HasWarnings
            ? wrapSummary + "；" + LegacyTextWrapService.FormatDiagnostics(wrapResult.Diagnostics).Replace(Environment.NewLine, "；", StringComparison.Ordinal)
            : wrapSummary;
        if (string.IsNullOrWhiteSpace(text) && command.CommandId != 0x2C)
        {
            return null;
        }

        return command.CommandId switch
        {
            0x14 or 0x15 or 0x7A => BuildTalkModel(
                command,
                text,
                GetParameterValue(command, 0),
                ResolveSpeakerId(command, GetParameterValue(command, 0), personReferenceResolver),
                people,
                warningSuffix),
            0x16 => new RSceneDialoguePreviewModel(
                RSceneDialoguePreviewKind.Information,
                SpeakerId: null,
                FaceId: null,
                SpeakerName: "信息",
                Text: text,
                HasFace: false,
                Detail: $"{command.CommandIdHex} {command.CommandName}：无头像信息框{warningSuffix}"),
            0x69 => new RSceneDialoguePreviewModel(
                RSceneDialoguePreviewKind.Narration,
                SpeakerId: null,
                FaceId: null,
                SpeakerName: "旁白",
                Text: text,
                HasFace: false,
                Detail: $"{command.CommandIdHex} {command.CommandName}：无头像旁白{warningSuffix}"),
            0x2C => new RSceneDialoguePreviewModel(
                RSceneDialoguePreviewKind.MapText,
                SpeakerId: null,
                FaceId: null,
                SpeakerName: string.Empty,
                Text: text,
                HasFace: false,
                Detail: $"{command.CommandIdHex} {command.CommandName}：地图文字横板{warningSuffix}"),
            _ => null
        };
    }

    public static bool IsPreviewCommand(int commandId)
        => commandId is 0x14 or 0x15 or 0x16 or 0x69 or 0x7A or 0x2C;

    private static RSceneDialoguePreviewModel BuildTalkModel(
        LegacyScenarioCommandNode command,
        string text,
        int? rawSpeakerValue,
        int? speakerId,
        IReadOnlyDictionary<int, RSceneDialoguePreviewPerson> people,
        string warningSuffix)
    {
        var textSpeaker = ResolveSpeakerFromText(text, people);
        var resolvedSpeakerId = textSpeaker?.SpeakerId ?? speakerId;
        var speakerSource = string.Empty;
        if (textSpeaker?.SpeakerId.HasValue == true)
        {
            speakerSource = $"text&{textSpeaker.SpeakerName}";
        }

        RSceneDialoguePreviewPerson? person = null;
        if (resolvedSpeakerId.HasValue)
        {
            people.TryGetValue(resolvedSpeakerId.Value, out person);
        }

        var speakerName = !string.IsNullOrWhiteSpace(person?.Name)
            ? person.Name
            : resolvedSpeakerId.HasValue
                ? $"人物 {resolvedSpeakerId.Value}"
                : "说话人";
        var faceId = person?.FaceId ?? resolvedSpeakerId;
        var speakerDetail = resolvedSpeakerId.HasValue
            ? rawSpeakerValue.HasValue && rawSpeakerValue.Value != resolvedSpeakerId.Value
                ? $"人物={resolvedSpeakerId.Value}（参数={rawSpeakerValue.Value}）"
                : $"人物={resolvedSpeakerId.Value}"
            : "人物=未读取";
        var faceDetail = faceId.HasValue
            ? $"头像={faceId.Value}"
            : "头像=未读取";
        if (textSpeaker?.SpeakerId.HasValue == true)
        {
            var textSpeakerId = textSpeaker.SpeakerId.Value;
            speakerDetail = rawSpeakerValue.HasValue && rawSpeakerValue.Value != textSpeakerId
                ? $"人物={textSpeakerId}（{speakerSource}，参数={rawSpeakerValue.Value}）"
                : $"人物={textSpeakerId}（{speakerSource}）";
        }

        return new RSceneDialoguePreviewModel(
            RSceneDialoguePreviewKind.Talk,
            SpeakerId: resolvedSpeakerId,
            FaceId: faceId,
            SpeakerName: speakerName,
            Text: textSpeaker?.BodyText ?? text,
            HasFace: true,
            Detail: $"{command.CommandIdHex} {command.CommandName}：{speakerName}（{speakerDetail}，{faceDetail}）{warningSuffix}");
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

        if (model.Kind == RSceneDialoguePreviewKind.Talk)
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
        var lines = GetRenderLines(text, profile.MaxTextLines);
        for (var i = 0; i < lines.Count; i++)
        {
            var lineRect = new Rectangle(bounds.Left, bounds.Top + i * profile.TextLineHeight, bounds.Width, profile.TextLineHeight);
            DrawBodyLine(graphics, lines[i], lineRect, profile.TextFont, profile.TextColor);
        }
    }

    private static void DrawMapText(Graphics graphics, RSceneDialoguePreviewModel model)
    {
        var profile = DefaultMapTextProfile;
        using var panelBrush = new SolidBrush(profile.PanelColor);
        graphics.FillRectangle(panelBrush, profile.PanelBounds);

        var lines = GetRenderLines(model.Text, profile.MaxTextLines);
        for (var i = 0; i < lines.Count; i++)
        {
            var lineRect = new Rectangle(
                profile.TextBounds.Left,
                profile.TextBounds.Top + i * profile.TextLineHeight,
                profile.TextBounds.Width,
                profile.TextLineHeight);
            DrawMapTextLine(graphics, lines[i], lineRect, profile);
        }
    }

    private static List<string> GetRenderLines(string text, int maxLines)
    {
        if (maxLines <= 0)
        {
            return [];
        }

        var lines = NormalizeNewLines(text).Split('\n').Take(maxLines).ToList();
        return lines.Count == 0 ? [string.Empty] : lines;
    }

    private static void DrawBodyLine(Graphics graphics, string text, Rectangle bounds, Font font, Color color)
    {
        var displayText = TrimToFit(graphics, text, font, bounds.Width);
        DrawTextWithShadow(graphics, displayText, bounds, font, color, ContentAlignment.MiddleLeft);
    }

    private static void DrawMapTextLine(Graphics graphics, string text, Rectangle bounds, MapTextPreviewProfile profile)
    {
        var displayText = TrimToFit(graphics, text, profile.TextFont, bounds.Width);
        using var shadowBrush = new SolidBrush(profile.ShadowColor);
        using var textBrush = new SolidBrush(profile.TextColor);
        using var format = BuildStringFormat(ContentAlignment.MiddleLeft);
        graphics.DrawString(
            displayText,
            profile.TextFont,
            shadowBrush,
            new Rectangle(bounds.Left + 1, bounds.Top + 1, bounds.Width, bounds.Height),
            format);
        graphics.DrawString(displayText, profile.TextFont, textBrush, bounds, format);
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

    private static string NormalizeText(string text)
        => string.IsNullOrEmpty(text) ? string.Empty : NormalizeNewLines(text).Trim();

    private static string NormalizeNewLines(string text)
        => string.IsNullOrEmpty(text) ? string.Empty : text.Replace("\r\n", "\n").Replace('\r', '\n');

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

    private static int? ResolveSpeakerId(
        LegacyScenarioCommandNode command,
        int? rawSpeakerValue,
        Func<LegacyScenarioCommandNode, int, int?>? personReferenceResolver)
    {
        if (personReferenceResolver != null)
        {
            var resolved = personReferenceResolver(command, 0);
            if (resolved.HasValue)
            {
                return resolved;
            }
        }

        return rawSpeakerValue is >= 0 and <= 1023 ? rawSpeakerValue : null;
    }

    private static TextSpeakerCandidate? ResolveSpeakerFromText(
        string text,
        IReadOnlyDictionary<int, RSceneDialoguePreviewPerson> people)
    {
        var segments = ExtractSpeakerSegments(text, people);
        return segments.Count == 0 ? null : ResolveSpeakerSegment(segments[0], people, 1, segments.Count);
    }

    private static IReadOnlyList<RawSpeakerSegment> ExtractSpeakerSegments(
        string text,
        IReadOnlyDictionary<int, RSceneDialoguePreviewPerson> people)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return Array.Empty<RawSpeakerSegment>();
        }

        var normalized = text.Replace("\r\n", "\n").Replace('\r', '\n');
        var markerIndexes = normalized
            .Select((character, index) => (character, index))
            .Where(x => x.character is '&' or '＆')
            .Select(x => x.index)
            .ToList();
        if (markerIndexes.Count == 0)
        {
            return Array.Empty<RawSpeakerSegment>();
        }

        var segments = new List<RawSpeakerSegment>();
        for (var i = 0; i < markerIndexes.Count; i++)
        {
            var contentStart = markerIndexes[i] + 1;
            var contentEnd = i + 1 < markerIndexes.Count ? markerIndexes[i + 1] : normalized.Length;
            if (contentStart >= contentEnd)
            {
                continue;
            }

            var content = normalized[contentStart..contentEnd].Trim();
            if (content.Length == 0)
            {
                continue;
            }

            if (TryBuildKnownSpeakerSegment(content, people, out var knownSegment))
            {
                segments.Add(knownSegment);
                continue;
            }

            var lines = content.Split('\n').ToList();
            var firstLineIndex = lines.FindIndex(line => line.Trim().Length > 0);
            if (firstLineIndex < 0)
            {
                continue;
            }

            var speakerName = lines[firstLineIndex].Trim();
            lines.RemoveAt(firstLineIndex);
            segments.Add(new RawSpeakerSegment(speakerName, NormalizeText(string.Join("\n", lines))));
        }

        return segments;
    }

    private static bool TryBuildKnownSpeakerSegment(
        string content,
        IReadOnlyDictionary<int, RSceneDialoguePreviewPerson> people,
        out RawSpeakerSegment segment)
    {
        var trimmed = content.TrimStart();
        foreach (var name in people.Values
                     .Select(person => person.Name?.Trim() ?? string.Empty)
                     .Where(name => name.Length > 0)
                     .Distinct(StringComparer.Ordinal)
                     .OrderByDescending(name => name.Length))
        {
            if (!trimmed.StartsWith(name, StringComparison.Ordinal))
            {
                continue;
            }

            var body = trimmed[name.Length..].TrimStart();
            body = TrimDialogueBodyPrefix(body);
            segment = new RawSpeakerSegment(name, NormalizeText(body));
            return true;
        }

        segment = default;
        return false;
    }

    private static TextSpeakerCandidate ResolveSpeakerSegment(
        RawSpeakerSegment segment,
        IReadOnlyDictionary<int, RSceneDialoguePreviewPerson> people,
        int segmentIndex,
        int segmentCount)
    {
        var normalizedSpeakerName = NormalizeSpeakerName(segment.SpeakerName);
        if (normalizedSpeakerName.Length == 0)
        {
            return new TextSpeakerCandidate(segment.SpeakerName, segment.BodyText, SpeakerId: null, segmentIndex, segmentCount);
        }

        var matches = people
            .Where(pair => string.Equals(NormalizeSpeakerName(pair.Value.Name), normalizedSpeakerName, StringComparison.Ordinal))
            .Take(2)
            .ToList();
        return matches.Count == 1
            ? new TextSpeakerCandidate(segment.SpeakerName, segment.BodyText, matches[0].Key, segmentIndex, segmentCount)
            : new TextSpeakerCandidate(segment.SpeakerName, segment.BodyText, SpeakerId: null, segmentIndex, segmentCount);
    }

    private static string TrimDialogueBodyPrefix(string body)
    {
        body = body.TrimStart();
        while (body.Length > 0 && body[0] is ':' or '：' or '，' or ',' or '。')
        {
            body = body[1..].TrimStart();
        }

        return body;
    }

    private static string NormalizeSpeakerName(string name)
    {
        var builder = new StringBuilder();
        foreach (var character in name.Trim())
        {
            if (!char.IsWhiteSpace(character))
            {
                builder.Append(character);
            }
        }

        return builder.ToString();
    }

    private readonly record struct RawSpeakerSegment(string SpeakerName, string BodyText);

    private sealed record TextSpeakerCandidate(
        string SpeakerName,
        string BodyText,
        int? SpeakerId,
        int SegmentIndex,
        int SegmentCount);

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
        int TextLineHeight)
    {
        public Font NameFont { get; } = CreateFont(FontFamily, NameFontSize, FontStyle.Bold);
        public Font TextFont { get; } = CreateFont(FontFamily, TextFontSize, FontStyle.Regular);

        private static Font CreateFont(string familyName, float size, FontStyle style)
        {
            try
            {
                return new Font(familyName, size, style, GraphicsUnit.Pixel);
            }
            catch
            {
                return new Font(global::System.Drawing.FontFamily.GenericSansSerif, size, style, GraphicsUnit.Pixel);
            }
        }
    }

    private sealed record MapTextPreviewProfile(
        Rectangle PanelBounds,
        Rectangle TextBounds,
        Color PanelColor,
        Color TextColor,
        Color ShadowColor,
        string FontFamily,
        float TextFontSize,
        int MaxTextLines,
        int TextLineHeight)
    {
        public Font TextFont { get; } = CreateFont(FontFamily, TextFontSize, FontStyle.Regular);

        private static Font CreateFont(string familyName, float size, FontStyle style)
        {
            try
            {
                return new Font(familyName, size, style, GraphicsUnit.Pixel);
            }
            catch
            {
                return new Font(global::System.Drawing.FontFamily.GenericSansSerif, size, style, GraphicsUnit.Pixel);
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
