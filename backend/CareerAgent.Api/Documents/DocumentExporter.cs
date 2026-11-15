using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace CareerAgent.Api.Documents;

public static class DocumentExporter
{
    public static byte[] Export(DocumentContent content, string kind)
    {
        if (kind == "Cv") return ExportCv(content);
        using var stream = new MemoryStream();
        using (var word = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document))
        {
            var main = word.AddMainDocumentPart();
            var body = new Body(); main.Document = new Document(body);
            var styles = main.AddNewPart<StyleDefinitionsPart>();
            styles.Styles = new Styles(
                new DocDefaults(new RunPropertiesDefault(new RunPropertiesBaseStyle(new RunFonts { Ascii = "Arial", HighAnsi = "Arial" }, new Color { Val = "000000" }, new FontSize { Val = "22" }))),
                new Style(new StyleName { Val = "Normal" }, new StyleParagraphProperties(new SpacingBetweenLines { After = "120", Line = "276", LineRule = LineSpacingRuleValues.Auto })) { Type = StyleValues.Paragraph, StyleId = "Normal", Default = true },
                HeadingStyle("Title", "Title", "40"), HeadingStyle("Heading1", "Heading 1", "24"));
            var numbering = main.AddNewPart<NumberingDefinitionsPart>();
            numbering.Numbering = new Numbering(new AbstractNum(new Level(new StartNumberingValue { Val = 1 },
                new NumberingFormat { Val = NumberFormatValues.Bullet }, new LevelText { Val = "•" },
                new LevelJustification { Val = LevelJustificationValues.Left },
                new PreviousParagraphProperties(new Indentation { Left = "260", Hanging = "220" })) { LevelIndex = 0 }) { AbstractNumberId = 1 },
                new NumberingInstance(new AbstractNumId { Val = 1 }) { NumberID = 1 });
            var identity = content.Identity;
            if (!string.IsNullOrWhiteSpace(identity?.FullName)) body.Append(Paragraph(identity.FullName, "Title", "40", true));
            var contacts = identity is null ? [] : new[] { identity.Location, identity.Email, identity.Phone, identity.Links }.Where(s => !string.IsNullOrWhiteSpace(s)).ToArray();
            if (contacts.Length > 0) body.Append(Paragraph(string.Join("  ·  ", contacts), "Normal", "19", after: "200"));
            body.Append(Paragraph(content.Title, identity?.FullName.Length > 0 ? "Normal" : "Title", "28", true, after: "100"));
            if (!string.IsNullOrWhiteSpace(content.Subtitle)) body.Append(Paragraph(content.Subtitle, "Normal", "22", after: "220"));
            var letter = kind == "Application";
            if (letter && content.Language is not null) body.Append(Paragraph(content.Language == "English" ? "Dear hiring team" : "Til rekrutteringsteamet", "Normal", "22", after: "180"));
            foreach (var section in content.Sections)
            {
                if (!letter) body.Append(Paragraph(section.Heading, "Heading1", "24", true, before: "240", after: "100"));
                foreach (var block in section.Blocks)
                {
                    var paragraph = Paragraph(block.Text, "Normal", "22", after: letter ? "180" : "100");
                    if (block.Type == "bullet") paragraph.ParagraphProperties!.InsertBefore(new NumberingProperties(new NumberingLevelReference { Val = 0 }, new NumberingId { Val = 1 }), paragraph.ParagraphProperties.GetFirstChild<SpacingBetweenLines>());
                    body.Append(paragraph);
                }
            }
            if (letter && content.Language is not null)
            {
                body.Append(Paragraph(content.Language == "English" ? "Kind regards" : "Med venlig hilsen", "Normal", "22", before: "140", after: "40"));
                if (!string.IsNullOrWhiteSpace(identity?.FullName)) body.Append(Paragraph(identity.FullName, "Normal", "22"));
            }
            body.Append(new SectionProperties(new PageSize { Width = 12240, Height = 15840 },
                new PageMargin { Top = 1080, Bottom = 1080, Left = 1152, Right = 1152 }));
            main.Document.Save();
        }
        return stream.ToArray();
    }
    public static int CvPage(DocumentSection section)
    {
        if (section.Page is 1 or 2) return section.Page.Value;
        var heading = section.Heading.ToLowerInvariant();
        if (heading.Contains("uddannelse") || heading.Contains("education")) return 1;
        return new[] { "projekt", "project", "kompetenc", "skill", "suppler", "additional", "sprog", "language", "praktisk" }.Any(heading.Contains) ? 2 : 1;
    }
    private static byte[] ExportCv(DocumentContent content)
    {
        using var stream = new MemoryStream();
        using (var word = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document))
        {
            var main = word.AddMainDocumentPart(); var body = new Body(); main.Document = new Document(body);
            var styles = main.AddNewPart<StyleDefinitionsPart>();
            styles.Styles = new Styles(new Style(new StyleName { Val = "Normal" },
                new StyleRunProperties(new RunFonts { Ascii = "Helvetica", HighAnsi = "Helvetica" }, new Color { Val = "19344A" }, new FontSize { Val = "21" })) { Type = StyleValues.Paragraph, StyleId = "Normal", Default = true },
                new Style(new StyleName { Val = "Title" }, new BasedOn { Val = "Normal" }) { Type = StyleValues.Paragraph, StyleId = "Title" });
            var numbering = main.AddNewPart<NumberingDefinitionsPart>();
            numbering.Numbering = new Numbering(new AbstractNum(new Level(new StartNumberingValue { Val = 1 }, new NumberingFormat { Val = NumberFormatValues.Bullet },
                new LevelText { Val = "•" }, new LevelJustification { Val = LevelJustificationValues.Left },
                new PreviousParagraphProperties(new Indentation { Left = "220", Hanging = "180" })) { LevelIndex = 0 }) { AbstractNumberId = 1 },
                new NumberingInstance(new AbstractNumId { Val = 1 }) { NumberID = 1 });
            var name = content.Identity?.FullName;
            body.Append(CvParagraph((string.IsNullOrWhiteSpace(name) ? content.Title : name).ToUpperInvariant(), "50", bold: true, after: "140", style: "Title"));
            if (!string.IsNullOrWhiteSpace(name)) body.Append(CvParagraph(content.Title.ToUpperInvariant(), "24", "267C85", true, after: "100"));
            if (content.Identity is { } identity)
            {
                var contacts = new[] { identity.Location, identity.Phone, identity.Email, identity.Links }.Where(s => !string.IsNullOrWhiteSpace(s));
                if (contacts.Any()) body.Append(CvParagraph(string.Join(" | ", contacts), "20", after: "220"));
            }
            for (var page = 1; page <= 2; page++)
            {
                if (page == 2)
                {
                    body.Append(CvParagraph(content.Language == "English" ? "PROJECTS & SKILLS" : "PROJEKTER & KOMPETENCER", "36", bold: true, pageBreak: true, after: "120"));
                    body.Append(CvParagraph(string.Join(" · ", new[] { name, content.Title }.Where(s => !string.IsNullOrWhiteSpace(s))), "20", "5B6773", after: "200"));
                }
                foreach (var section in content.Sections.Where(s => CvPage(s) == page))
                {
                    var heading = CvParagraph(section.Heading.ToUpperInvariant(), "22", "267C85", true, before: "200", after: "140", keep: true);
                    heading.ParagraphProperties!.InsertBefore(new ParagraphBorders(new BottomBorder { Val = BorderValues.Single, Color = "D8E3E8", Size = 4, Space = 5 }), heading.ParagraphProperties.GetFirstChild<SpacingBetweenLines>());
                    body.Append(heading);
                    foreach (var block in section.Blocks)
                    {
                        if (block.Type == "entry")
                        {
                            var entry = CvParagraph(block.Label ?? "", "22", bold: true, keep: true, after: "60");
                            entry.ParagraphProperties!.InsertBefore(new Tabs(new TabStop { Val = TabStopValues.Right, Position = 9860 }), entry.ParagraphProperties.GetFirstChild<SpacingBetweenLines>());
                            if (!string.IsNullOrWhiteSpace(block.Period)) entry.Append(new Run(new RunProperties(new RunFonts { Ascii = "Helvetica", HighAnsi = "Helvetica" }, new Color { Val = "5B6773" }, new FontSize { Val = "20" }), new TabChar(), new Text(block.Period)));
                            body.Append(entry, CvParagraph(block.Text, "22", bold: true, keep: true, after: "60"));
                            if (!string.IsNullOrWhiteSpace(block.Technologies)) body.Append(CvParagraph(block.Technologies, "19", "267C85", italic: true, keep: true, after: "120"));
                        }
                        else
                        {
                            var paragraph = CvParagraph(block.Text, block.Type == "subheading" ? "22" : "21", bold: block.Type == "subheading", keep: block.Type == "subheading", after: block.Type == "bullet" ? "20" : "100");
                            if (block.Type == "bullet") paragraph.ParagraphProperties!.InsertBefore(new NumberingProperties(new NumberingLevelReference { Val = 0 }, new NumberingId { Val = 1 }), paragraph.ParagraphProperties.GetFirstChild<SpacingBetweenLines>());
                            body.Append(paragraph);
                        }
                    }
                }
            }
            var footer = main.AddNewPart<FooterPart>();
            var foot = CvParagraph(string.IsNullOrWhiteSpace(name) ? "CV" : name + " · CV", "18", "5B6773");
            foot.ParagraphProperties!.InsertBefore(new Tabs(new TabStop { Val = TabStopValues.Center, Position = 4930 }), foot.ParagraphProperties.GetFirstChild<SpacingBetweenLines>());
            foot.Append(new Run(new TabChar()), new SimpleField(new Run(new Text("1"))) { Instruction = "PAGE" }, new Run(new Text(" / 2")));
            footer.Footer = new Footer(foot);
            body.Append(new SectionProperties(new FooterReference { Type = HeaderFooterValues.Default, Id = main.GetIdOfPart(footer) },
                new PageSize { Width = 11906, Height = 16838 }, new PageMargin { Top = 960, Bottom = 720, Left = 1020, Right = 1020, Footer = 360 }));
            main.Document.Save();
        }
        return stream.ToArray();
    }
    private static Paragraph CvParagraph(string text, string size, string color = "19344A", bool bold = false, bool italic = false,
        string before = "0", string after = "80", bool keep = false, bool pageBreak = false, string style = "Normal")
    {
        var properties = new ParagraphProperties(new ParagraphStyleId { Val = style });
        if (keep) properties.Append(new KeepNext(), new KeepLines());
        if (pageBreak) properties.Append(new PageBreakBefore());
        properties.Append(new WidowControl(), new SpacingBetweenLines { Before = before, After = after, Line = "280", LineRule = LineSpacingRuleValues.AtLeast });
        var runProperties = new RunProperties(new RunFonts { Ascii = "Helvetica", HighAnsi = "Helvetica" });
        if (bold) runProperties.Append(new Bold());
        if (italic) runProperties.Append(new Italic());
        runProperties.Append(new Color { Val = color }, new FontSize { Val = size });
        var run = new Run(runProperties);
        var pieces = text.Split('\t');
        for (var i = 0; i < pieces.Length; i++) { if (i > 0) run.Append(new TabChar()); run.Append(new Text(pieces[i]) { Space = SpaceProcessingModeValues.Preserve }); }
        return new Paragraph(properties, run);
    }
    private static Style HeadingStyle(string id, string name, string size) => new(new StyleName { Val = name },
        new BasedOn { Val = "Normal" }, new NextParagraphStyle { Val = "Normal" },
        new StyleParagraphProperties(new KeepNext(), new KeepLines()),
        new StyleRunProperties(new Bold(), new Color { Val = "000000" }, new FontSize { Val = size }))
        { Type = StyleValues.Paragraph, StyleId = id };
    private static Paragraph Paragraph(string text, string style, string size, bool bold = false, string before = "0", string after = "120")
    {
        var properties = new ParagraphProperties(new ParagraphStyleId { Val = style });
        if (style is "Title" or "Heading1") properties.Append(new KeepNext(), new KeepLines());
        properties.Append(new WidowControl(), new SpacingBetweenLines { Before = before, After = after, Line = "276", LineRule = LineSpacingRuleValues.Auto });
        var run = new RunProperties(new RunFonts { Ascii = "Arial", HighAnsi = "Arial" });
        if (bold) run.Append(new Bold());
        run.Append(new Color { Val = "000000" }, new FontSize { Val = size });
        return new Paragraph(properties, new Run(run, new Text(text) { Space = SpaceProcessingModeValues.Preserve }));
    }
}
